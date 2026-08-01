using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.FullCrew.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Fetches and categorizes full cast/crew credits from TMDB.
/// </summary>
public class CreditsService
{
    /// <summary>
    /// Same shared TMDB API key Jellyfin's official TheMovieDb provider uses
    /// (<c>MediaBrowser.Providers.Plugins.Tmdb.TmdbUtils.ApiKey</c>) when no custom key is configured.
    /// </summary>
    private const string JellyfinSharedTmdbApiKey = "4219e299c89411838049ab0dab19ebd5";

    private static readonly string[] DepartmentOrder =
    [
        "Cast",
        "Directing",
        "Writing",
        "Production",
        "Camera",
        "Editing",
        "Sound",
        "Art",
        "Costume & Make-Up",
        "Visual Effects",
        "Lighting",
        "Crew",
        "Other"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILibraryManager _libraryManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CreditsService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreditsService"/> class.
    /// </summary>
    public CreditsService(
        ILibraryManager libraryManager,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        ILogger<CreditsService> logger)
    {
        _libraryManager = libraryManager;
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    /// <summary>
    /// Gets categorized cast and crew for a Jellyfin item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Categorized credits response.</returns>
    public async Task<FullCrewResponse> GetCreditsAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return new FullCrewResponse
            {
                ItemId = itemId.ToString("N", CultureInfo.InvariantCulture),
                Error = "Item not found."
            };
        }

        var config = Plugin.Instance?.Configuration;
        var apiKey = ResolveApiKey(config?.TmdbApiKey);

        var lookup = ResolveTmdbLookup(item);
        if (lookup is null)
        {
            return new FullCrewResponse
            {
                ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                ItemName = item.Name,
                MediaType = item.GetBaseItemKind().ToString(),
                Error = "No TMDB id found for this item. Identify the item with TheMovieDb metadata."
            };
        }

        var cacheKey = $"fullcrew-{lookup.MediaKind}-{lookup.TmdbId}";
        if (_memoryCache.TryGetValue(cacheKey, out FullCrewResponse? cached) && cached is not null)
        {
            return CloneForItem(cached, item);
        }

        try
        {
            var response = await FetchFromTmdbAsync(item, lookup, apiKey, cancellationToken).ConfigureAwait(false);
            var cacheHours = Math.Clamp(config?.CacheHours ?? 12, 1, 168);
            _memoryCache.Set(cacheKey, response, TimeSpan.FromHours(cacheHours));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load TMDB credits for item {ItemId} (TMDB {TmdbId})", item.Id, lookup.TmdbId);
            return new FullCrewResponse
            {
                ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                ItemName = item.Name,
                TmdbId = lookup.TmdbId,
                MediaType = item.GetBaseItemKind().ToString(),
                Error = "Failed to load credits from TMDB."
            };
        }
    }

    private async Task<FullCrewResponse> FetchFromTmdbAsync(
        BaseItem item,
        TmdbLookup lookup,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-Plugin-FullCrew/1.0");

        var path = lookup.MediaKind == TmdbMediaKind.Movie
            ? $"https://api.themoviedb.org/3/movie/{lookup.TmdbId}/credits?api_key={Uri.EscapeDataString(apiKey)}"
            : $"https://api.themoviedb.org/3/tv/{lookup.TmdbId}/aggregate_credits?api_key={Uri.EscapeDataString(apiKey)}";

        using var httpResponse = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "TMDB credits request failed ({StatusCode}) for {Path}: {Body}",
                (int)httpResponse.StatusCode,
                path.Split('?')[0],
                body);

            return new FullCrewResponse
            {
                ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                ItemName = item.Name,
                TmdbId = lookup.TmdbId,
                MediaType = item.GetBaseItemKind().ToString(),
                Error = $"TMDB returned {(int)httpResponse.StatusCode}."
            };
        }

        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<TmdbCreditsPayload>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var departments = BuildDepartments(payload, Plugin.Instance?.Configuration);
        return new FullCrewResponse
        {
            ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
            ItemName = item.Name,
            TmdbId = lookup.TmdbId,
            MediaType = item.GetBaseItemKind().ToString(),
            Departments = departments
        };
    }

    private static IReadOnlyList<CrewDepartment> BuildDepartments(TmdbCreditsPayload? payload, Configuration.PluginConfiguration? config)
    {
        var enabled = new HashSet<string>(
            config?.EnabledDepartments ?? DepartmentOrder,
            StringComparer.OrdinalIgnoreCase);
        var maxPerDept = Math.Clamp(config?.MaxPeoplePerDepartment ?? 100, 1, 1000);

        var buckets = new Dictionary<string, List<CrewPerson>>(StringComparer.OrdinalIgnoreCase);

        void AddPerson(string department, CrewPerson person)
        {
            var key = NormalizeDepartment(department);
            if (!enabled.Contains(key))
            {
                return;
            }

            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }

            // Deduplicate by TMDB id + role within department
            if (list.Any(p =>
                    p.TmdbPersonId == person.TmdbPersonId
                    && string.Equals(p.Role, person.Role, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Name, person.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            list.Add(person);
        }

        if (payload?.Cast is not null)
        {
            foreach (var member in payload.Cast.OrderBy(c => c.Order ?? int.MaxValue))
            {
                if (string.IsNullOrWhiteSpace(member.Name))
                {
                    continue;
                }

                var role = !string.IsNullOrWhiteSpace(member.Character)
                    ? member.Character!
                    : FirstRole(member.Roles) ?? "Actor";

                AddPerson(
                    "Cast",
                    new CrewPerson
                    {
                        Name = member.Name.Trim(),
                        Role = role.Trim(),
                        TmdbPersonId = member.Id,
                        ProfileUrl = ToProfileUrl(member.ProfilePath),
                        Order = member.Order
                    });
            }
        }

        if (payload?.Crew is not null)
        {
            foreach (var member in payload.Crew)
            {
                if (string.IsNullOrWhiteSpace(member.Name))
                {
                    continue;
                }

                if (member.Jobs is { Count: > 0 })
                {
                    foreach (var job in member.Jobs)
                    {
                        var jobTitle = string.IsNullOrWhiteSpace(job.Job) ? "Crew" : job.Job!;
                        AddPerson(
                            string.IsNullOrWhiteSpace(member.Department) ? "Crew" : member.Department!,
                            new CrewPerson
                            {
                                Name = member.Name.Trim(),
                                Role = jobTitle.Trim(),
                                TmdbPersonId = member.Id,
                                ProfileUrl = ToProfileUrl(member.ProfilePath)
                            });
                    }
                }
                else
                {
                    var jobTitle = string.IsNullOrWhiteSpace(member.Job) ? "Crew" : member.Job!;
                    AddPerson(
                        string.IsNullOrWhiteSpace(member.Department) ? "Crew" : member.Department!,
                        new CrewPerson
                        {
                            Name = member.Name.Trim(),
                            Role = jobTitle.Trim(),
                            TmdbPersonId = member.Id,
                            ProfileUrl = ToProfileUrl(member.ProfilePath)
                        });
                }
            }
        }

        var result = new List<CrewDepartment>();
        foreach (var deptName in DepartmentOrder)
        {
            if (!buckets.TryGetValue(deptName, out var people) || people.Count == 0)
            {
                continue;
            }

            IEnumerable<CrewPerson> ordered = string.Equals(deptName, "Cast", StringComparison.OrdinalIgnoreCase)
                ? people.OrderBy(p => p.Order ?? int.MaxValue).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                : people.OrderBy(p => p.Role, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase);

            result.Add(new CrewDepartment
            {
                Name = deptName,
                People = ordered.Take(maxPerDept).ToList()
            });
        }

        // Any unexpected department names that slipped through normalization
        foreach (var orphan in buckets.Keys
                     .Where(k => !DepartmentOrder.Contains(k, StringComparer.OrdinalIgnoreCase))
                     .OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            if (!enabled.Contains(orphan))
            {
                continue;
            }

            result.Add(new CrewDepartment
            {
                Name = orphan,
                People = buckets[orphan]
                    .OrderBy(p => p.Role, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(maxPerDept)
                    .ToList()
            });
        }

        return result;
    }

    private static string NormalizeDepartment(string department)
    {
        if (string.IsNullOrWhiteSpace(department))
        {
            return "Other";
        }

        var trimmed = department.Trim();
        foreach (var known in DepartmentOrder)
        {
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        // Common TMDB variants
        if (trimmed.Contains("make-up", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("makeup", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("costume", StringComparison.OrdinalIgnoreCase))
        {
            return "Costume & Make-Up";
        }

        if (trimmed.Contains("visual effect", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "VFX", StringComparison.OrdinalIgnoreCase))
        {
            return "Visual Effects";
        }

        return "Other";
    }

    private static string? FirstRole(List<TmdbRole>? roles)
    {
        if (roles is null || roles.Count == 0)
        {
            return null;
        }

        return roles
            .Select(r => r.Character)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
    }

    private static string? ToProfileUrl(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            return null;
        }

        return "https://image.tmdb.org/t/p/w185" + profilePath;
    }

    private static FullCrewResponse CloneForItem(FullCrewResponse cached, BaseItem item)
    {
        return new FullCrewResponse
        {
            ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
            ItemName = item.Name,
            TmdbId = cached.TmdbId,
            MediaType = item.GetBaseItemKind().ToString(),
            Error = cached.Error,
            Departments = cached.Departments
        };
    }

    private static TmdbLookup? ResolveTmdbLookup(BaseItem item)
    {
        if (item is Episode episode)
        {
            var series = episode.Series;
            if (series is not null)
            {
                var seriesId = GetProviderId(series, "Tmdb") ?? GetProviderId(series, "TmdbSeries");
                if (!string.IsNullOrWhiteSpace(seriesId))
                {
                    return new TmdbLookup(seriesId, TmdbMediaKind.Tv);
                }
            }
        }

        if (item is Series)
        {
            var seriesId = GetProviderId(item, "Tmdb") ?? GetProviderId(item, "TmdbSeries");
            if (!string.IsNullOrWhiteSpace(seriesId))
            {
                return new TmdbLookup(seriesId, TmdbMediaKind.Tv);
            }
        }

        if (item is Movie || item.GetBaseItemKind() == BaseItemKind.Movie)
        {
            var movieId = GetProviderId(item, "Tmdb");
            if (!string.IsNullOrWhiteSpace(movieId))
            {
                return new TmdbLookup(movieId, TmdbMediaKind.Movie);
            }
        }

        // Fallback: treat as movie if only Tmdb is present, else TV if TmdbSeries
        var tmdb = GetProviderId(item, "Tmdb");
        if (!string.IsNullOrWhiteSpace(tmdb))
        {
            var kind = item is Series or Episode ? TmdbMediaKind.Tv : TmdbMediaKind.Movie;
            return new TmdbLookup(tmdb, kind);
        }

        var tmdbSeries = GetProviderId(item, "TmdbSeries");
        if (!string.IsNullOrWhiteSpace(tmdbSeries))
        {
            return new TmdbLookup(tmdbSeries, TmdbMediaKind.Tv);
        }

        return null;
    }

    private static string? GetProviderId(BaseItem item, string key)
    {
        if (item.ProviderIds is null)
        {
            return null;
        }

        return item.ProviderIds.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    /// <summary>
    /// Uses a configured key when set; otherwise Jellyfin's shared TMDB provider key.
    /// </summary>
    private static string ResolveApiKey(string? configuredKey)
    {
        var trimmed = configuredKey?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? JellyfinSharedTmdbApiKey : trimmed;
    }

    private sealed record TmdbLookup(string TmdbId, TmdbMediaKind MediaKind);

    private enum TmdbMediaKind
    {
        Movie,
        Tv
    }

    private sealed class TmdbCreditsPayload
    {
        [JsonPropertyName("cast")]
        public List<TmdbPerson>? Cast { get; set; }

        [JsonPropertyName("crew")]
        public List<TmdbPerson>? Crew { get; set; }
    }

    private sealed class TmdbPerson
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("character")]
        public string? Character { get; set; }

        [JsonPropertyName("job")]
        public string? Job { get; set; }

        [JsonPropertyName("department")]
        public string? Department { get; set; }

        [JsonPropertyName("profile_path")]
        public string? ProfilePath { get; set; }

        [JsonPropertyName("order")]
        public int? Order { get; set; }

        [JsonPropertyName("roles")]
        public List<TmdbRole>? Roles { get; set; }

        [JsonPropertyName("jobs")]
        public List<TmdbJob>? Jobs { get; set; }
    }

    private sealed class TmdbRole
    {
        [JsonPropertyName("character")]
        public string? Character { get; set; }
    }

    private sealed class TmdbJob
    {
        [JsonPropertyName("job")]
        public string? Job { get; set; }
    }
}
