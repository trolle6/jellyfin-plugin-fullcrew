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

    /// <summary>
    /// Preferred display order when one person has multiple stacked jobs.
    /// Unknown roles sort after these, alphabetically.
    /// </summary>
    private static readonly string[] RolePriorityOrder =
    [
        "Creator",
        "Executive Producer",
        "Co-Executive Producer",
        "Producer",
        "Co-Producer",
        "Associate Producer",
        "Line Producer",
        "Director",
        "Co-Director",
        "Writer",
        "Screenplay",
        "Story",
        "Characters",
        "Editor",
        "Supervising Editor",
        "Director of Photography",
        "Cinematography",
        "Original Music Composer",
        "Music",
        "Actor",
        "Self"
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

        var cacheKey = lookup.SeasonNumber is int seasonNumber
            ? $"fullcrew-v2-{lookup.MediaKind}-{lookup.TmdbId}-s{seasonNumber}"
            : $"fullcrew-v2-{lookup.MediaKind}-{lookup.TmdbId}";
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

        var path = BuildTmdbCreditsUrl(lookup, apiKey);
        using var httpResponse = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode && lookup.SeasonNumber is not null)
        {
            // Season credits missing — fall back to full-series aggregate credits.
            _logger.LogDebug(
                "TMDB season credits unavailable for {TmdbId} S{Season}; falling back to series aggregate credits.",
                lookup.TmdbId,
                lookup.SeasonNumber);

            var fallbackPath = BuildTmdbCreditsUrl(lookup with { SeasonNumber = null }, apiKey);
            using var fallbackResponse = await client.GetAsync(fallbackPath, cancellationToken).ConfigureAwait(false);
            return await ParseCreditsResponseAsync(item, lookup, fallbackResponse, cancellationToken).ConfigureAwait(false);
        }

        return await ParseCreditsResponseAsync(item, lookup, httpResponse, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildTmdbCreditsUrl(TmdbLookup lookup, string apiKey)
    {
        if (lookup.MediaKind == TmdbMediaKind.Movie)
        {
            return $"https://api.themoviedb.org/3/movie/{lookup.TmdbId}/credits?api_key={Uri.EscapeDataString(apiKey)}";
        }

        if (lookup.SeasonNumber is int seasonNumber)
        {
            return $"https://api.themoviedb.org/3/tv/{lookup.TmdbId}/season/{seasonNumber.ToString(CultureInfo.InvariantCulture)}/credits?api_key={Uri.EscapeDataString(apiKey)}";
        }

        return $"https://api.themoviedb.org/3/tv/{lookup.TmdbId}/aggregate_credits?api_key={Uri.EscapeDataString(apiKey)}";
    }

    private async Task<FullCrewResponse> ParseCreditsResponseAsync(
        BaseItem item,
        TmdbLookup lookup,
        HttpResponseMessage httpResponse,
        CancellationToken cancellationToken)
    {
        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "TMDB credits request failed ({StatusCode}): {Body}",
                (int)httpResponse.StatusCode,
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

        var rawCredits = new List<(string Department, CrewPerson Person)>();

        void AddRaw(string department, CrewPerson person)
        {
            var key = NormalizeDepartment(department);
            if (!enabled.Contains(key))
            {
                return;
            }

            rawCredits.Add((key, person));
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

                // Aggregate credits can list several characters for one person.
                if (member.Roles is { Count: > 0 } && string.IsNullOrWhiteSpace(member.Character))
                {
                    foreach (var character in member.Roles
                                 .Select(r => r.Character)
                                 .Where(c => !string.IsNullOrWhiteSpace(c))
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        AddRaw(
                            "Cast",
                            new CrewPerson
                            {
                                Name = member.Name.Trim(),
                                Role = character!.Trim(),
                                TmdbPersonId = member.Id,
                                ProfileUrl = ToProfileUrl(member.ProfilePath),
                                Order = member.Order
                            });
                    }

                    continue;
                }

                AddRaw(
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
                        AddRaw(
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
                    AddRaw(
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

        // One card per person: stack roles, park them in their primary department
        // (earliest match in DepartmentOrder — e.g. Production before Editing).
        var buckets = new Dictionary<string, List<CrewPerson>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in rawCredits.GroupBy(PersonKey, StringComparer.OrdinalIgnoreCase))
        {
            var credits = group.ToList();
            var primaryDepartment = credits
                .Select(c => c.Department)
                .OrderBy(DepartmentSortIndex)
                .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
                .First();

            var stackedRoles = credits
                .Select(c => c.Person.Role)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(RoleSortIndex)
                .ThenBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var best = credits
                .OrderBy(c => string.IsNullOrWhiteSpace(c.Person.ProfileUrl) ? 1 : 0)
                .ThenBy(c => c.Person.Order ?? int.MaxValue)
                .Select(c => c.Person)
                .First();

            var billingOrders = credits
                .Select(c => c.Person.Order)
                .Where(o => o.HasValue)
                .Select(o => o!.Value)
                .ToList();

            var stacked = new CrewPerson
            {
                Name = best.Name,
                Role = string.Join(" · ", stackedRoles),
                TmdbPersonId = best.TmdbPersonId,
                ProfileUrl = best.ProfileUrl ?? credits.Select(c => c.Person.ProfileUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
                Order = billingOrders.Count > 0 ? billingOrders.Min() : null
            };

            if (!buckets.TryGetValue(primaryDepartment, out var list))
            {
                list = [];
                buckets[primaryDepartment] = list;
            }

            list.Add(stacked);
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
                : people.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Role, StringComparer.OrdinalIgnoreCase);

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
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Role, StringComparer.OrdinalIgnoreCase)
                    .Take(maxPerDept)
                    .ToList()
            });
        }

        return result;
    }

    private static string PersonKey((string Department, CrewPerson Person) credit)
    {
        if (credit.Person.TmdbPersonId is int id and > 0)
        {
            return "id:" + id.ToString(CultureInfo.InvariantCulture);
        }

        return "name:" + credit.Person.Name.Trim().ToLowerInvariant();
    }

    private static int DepartmentSortIndex(string department)
    {
        for (var i = 0; i < DepartmentOrder.Length; i++)
        {
            if (string.Equals(DepartmentOrder[i], department, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return DepartmentOrder.Length + 1;
    }

    private static int RoleSortIndex(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return RolePriorityOrder.Length + 2;
        }

        for (var i = 0; i < RolePriorityOrder.Length; i++)
        {
            if (string.Equals(RolePriorityOrder[i], role, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        // Fuzzy: "Executive Producer (uncredited)" still ranks with Executive Producer.
        for (var i = 0; i < RolePriorityOrder.Length; i++)
        {
            if (role.StartsWith(RolePriorityOrder[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return RolePriorityOrder.Length + 1;
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

        return "https://image.tmdb.org/t/p/w342" + profilePath;
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
        if (item is Season season)
        {
            var series = season.Series;
            if (series is not null)
            {
                var seriesId = GetProviderId(series, "Tmdb") ?? GetProviderId(series, "TmdbSeries");
                if (!string.IsNullOrWhiteSpace(seriesId))
                {
                    return new TmdbLookup(seriesId, TmdbMediaKind.Tv, season.IndexNumber);
                }
            }

            // Some libraries store the series TMDB id on the season itself.
            var seasonSeriesId = GetProviderId(item, "Tmdb") ?? GetProviderId(item, "TmdbSeries");
            if (!string.IsNullOrWhiteSpace(seasonSeriesId))
            {
                return new TmdbLookup(seasonSeriesId, TmdbMediaKind.Tv, season.IndexNumber);
            }
        }

        if (item is Episode episode)
        {
            var series = episode.Series;
            if (series is not null)
            {
                var seriesId = GetProviderId(series, "Tmdb") ?? GetProviderId(series, "TmdbSeries");
                if (!string.IsNullOrWhiteSpace(seriesId))
                {
                    return new TmdbLookup(seriesId, TmdbMediaKind.Tv, episode.ParentIndexNumber);
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
            var kind = item is Series or Season or Episode ? TmdbMediaKind.Tv : TmdbMediaKind.Movie;
            int? seasonNumber = item is Season s ? s.IndexNumber
                : item is Episode e ? e.ParentIndexNumber
                : null;
            return new TmdbLookup(tmdb, kind, seasonNumber);
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

    private sealed record TmdbLookup(string TmdbId, TmdbMediaKind MediaKind, int? SeasonNumber = null);

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
