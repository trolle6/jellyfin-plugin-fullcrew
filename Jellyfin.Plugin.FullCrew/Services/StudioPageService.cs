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
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.FullCrew.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Builds studio detail pages: TMDB company metadata + library Movie/Series grid.
/// </summary>
public class StudioPageService
{
    private const string JellyfinSharedTmdbApiKey = "4219e299c89411838049ab0dab19ebd5";
    private const string TmdbImageBase = "https://image.tmdb.org/t/p/w300";
    private const int MaxTitles = 400;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILibraryManager _libraryManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<StudioPageService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StudioPageService"/> class.
    /// </summary>
    public StudioPageService(
        ILibraryManager libraryManager,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        ILogger<StudioPageService> logger)
    {
        _libraryManager = libraryManager;
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    /// <summary>
    /// Builds a studio page for a Jellyfin Studio item id and/or studio name (+ optional branches).
    /// </summary>
    public async Task<StudioPageResponse> GetStudioPageAsync(
        User? user,
        Guid? itemId,
        string? name,
        IReadOnlyList<string>? branches,
        CancellationToken cancellationToken)
    {
        string? itemIdN = null;
        var displayName = (name ?? string.Empty).Trim();

        if (itemId is Guid id && id != Guid.Empty)
        {
            var item = _libraryManager.GetItemById(id);
            if (item is not null)
            {
                itemIdN = item.Id.ToString("N", CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = item.Name?.Trim() ?? string.Empty;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(displayName) && branches is { Count: > 0 })
        {
            displayName = branches[0].Trim();
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return new StudioPageResponse
            {
                Name = "Unknown studio",
                Overview = null,
                Titles = []
            };
        }

        var (_, clusterLabel) = StudioNameClustering.ResolveRoot(displayName);
        var matchNames = ResolveMatchNames(displayName, branches);
        if (matchNames.Count > 1)
        {
            // Prefer cluster label for multi-branch pages (e.g. Disney).
            displayName = clusterLabel;
        }

        var titles = FindLibraryTitles(user, matchNames);
        var tmdb = await TryFetchTmdbCompanyAsync(displayName, matchNames, cancellationToken).ConfigureAwait(false);

        return new StudioPageResponse
        {
            Name = displayName,
            ItemId = itemIdN,
            Overview = NullIfEmpty(tmdb?.Description),
            LogoUrl = string.IsNullOrWhiteSpace(tmdb?.LogoPath)
                ? null
                : TmdbImageBase + tmdb!.LogoPath,
            Homepage = NullIfEmpty(tmdb?.Homepage),
            TmdbCompanyId = tmdb?.Id > 0 ? tmdb.Id : null,
            TmdbUrl = tmdb?.Id > 0
                ? "https://www.themoviedb.org/company/" + tmdb.Id.ToString(CultureInfo.InvariantCulture)
                : null,
            Headquarters = NullIfEmpty(tmdb?.Headquarters),
            OriginCountry = NullIfEmpty(tmdb?.OriginCountry),
            Branches = matchNames,
            Titles = titles
        };
    }

    private IReadOnlyList<string> ResolveMatchNames(string seed, IReadOnlyList<string>? branches)
    {
        var explicitBranches = (branches ?? [])
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (explicitBranches.Count > 0)
        {
            if (!explicitBranches.Contains(seed, StringComparer.OrdinalIgnoreCase)
                && !StudioNameClustering.ResolveRoot(seed).Label.Equals(seed, StringComparison.OrdinalIgnoreCase))
            {
                // seed may be cluster label ("Disney") — keep explicit branches only.
            }

            // Always ensure we search for every provided branch.
            return explicitBranches
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Expand via name-root against studios seen on library Movie/Series.
        var libraryStudios = CollectLibraryStudioNames();
        return StudioNameClustering.ExpandBranches(seed, libraryStudios);
    }

    private IReadOnlyList<string> CollectLibraryStudioNames()
    {
        const string cacheKey = "fullcrew:studio-names:v1";
        if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached is not null)
        {
            return cached;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var query = new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
                IsVirtualItem = false
            };
            foreach (var item in _libraryManager.GetItemList(query))
            {
                if (item?.Studios is null)
                {
                    continue;
                }

                foreach (var studio in item.Studios)
                {
                    if (!string.IsNullOrWhiteSpace(studio))
                    {
                        names.Add(studio.Trim());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to collect library studio names");
        }

        var list = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        _memoryCache.Set(cacheKey, (IReadOnlyList<string>)list, TimeSpan.FromMinutes(10));
        return list;
    }

    private IReadOnlyList<StudioLibraryTitle> FindLibraryTitles(User? user, IReadOnlyList<string> matchNames)
    {
        if (matchNames.Count == 0)
        {
            return [];
        }

        var wanted = new HashSet<string>(matchNames, StringComparer.OrdinalIgnoreCase);
        try
        {
            var query = user is null
                ? new InternalItemsQuery()
                : new InternalItemsQuery(user);

            query.Recursive = true;
            query.IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series];
            query.IsVirtualItem = false;

            // Prefer server-side studio filter when a single name is exact.
            if (matchNames.Count == 1)
            {
                var studioIds = TryResolveStudioIds(matchNames);
                if (studioIds is { Length: > 0 })
                {
                    query.StudioIds = studioIds;
                }
            }

            var results = new List<StudioLibraryTitle>();
            foreach (var item in _libraryManager.GetItemList(query))
            {
                if (item is null || item.IsVirtualItem)
                {
                    continue;
                }

                var studios = item.Studios ?? [];
                var hit = studios.Any(s => !string.IsNullOrWhiteSpace(s) && wanted.Contains(s.Trim()));
                if (!hit)
                {
                    continue;
                }

                var kind = item.GetBaseItemKind();
                results.Add(new StudioLibraryTitle
                {
                    Id = item.Id.ToString("N", CultureInfo.InvariantCulture),
                    Name = item.Name ?? string.Empty,
                    Type = kind == BaseItemKind.Series ? "Series" : "Movie",
                    ProductionYear = item.ProductionYear,
                    ImageTag = item.HasImage(ImageType.Primary) ? "primary" : null
                });

                if (results.Count >= MaxTitles)
                {
                    break;
                }
            }

            return results
                .OrderByDescending(t => t.ProductionYear ?? 0)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to find library titles for studio page");
            return [];
        }
    }

    private Guid[]? TryResolveStudioIds(IReadOnlyList<string> names)
    {
        var ids = new List<Guid>();
        foreach (var name in names)
        {
            try
            {
                var studio = _libraryManager.GetStudio(name);
                if (studio is not null && studio.Id != Guid.Empty)
                {
                    ids.Add(studio.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetStudio failed for {Name}", name);
            }
        }

        return ids.Count > 0 ? ids.ToArray() : null;
    }

    private async Task<TmdbCompanyDetails?> TryFetchTmdbCompanyAsync(
        string displayName,
        IReadOnlyList<string> matchNames,
        CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey(Plugin.Instance?.Configuration.TmdbApiKey);
        var searchNames = new List<string> { displayName };
        foreach (var n in matchNames)
        {
            if (!searchNames.Contains(n, StringComparer.OrdinalIgnoreCase))
            {
                searchNames.Add(n);
            }
        }

        foreach (var queryName in searchNames.Take(6))
        {
            var cacheKey = "fullcrew:tmdb-company:" + queryName.ToLowerInvariant();
            if (_memoryCache.TryGetValue(cacheKey, out TmdbCompanyDetails? cached))
            {
                if (cached is not null)
                {
                    return cached;
                }

                continue;
            }

            try
            {
                var companyId = await SearchCompanyIdAsync(queryName, apiKey, cancellationToken).ConfigureAwait(false);
                if (companyId is null or <= 0)
                {
                    _memoryCache.Set(cacheKey, (TmdbCompanyDetails?)null, TimeSpan.FromMinutes(30));
                    continue;
                }

                var details = await GetCompanyDetailsAsync(companyId.Value, apiKey, cancellationToken).ConfigureAwait(false);
                _memoryCache.Set(cacheKey, details, TimeSpan.FromHours(6));
                if (details is not null)
                {
                    return details;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "TMDB company lookup failed for {Name}", queryName);
            }
        }

        return null;
    }

    private async Task<int?> SearchCompanyIdAsync(string query, string apiKey, CancellationToken cancellationToken)
    {
        var url =
            "https://api.themoviedb.org/3/search/company?api_key="
            + Uri.EscapeDataString(apiKey)
            + "&query="
            + Uri.EscapeDataString(query);

        var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<TmdbCompanySearchPayload>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        var results = payload?.Results;
        if (results is null || results.Count == 0)
        {
            return null;
        }

        // Prefer exact (case-insensitive) name match, else first result.
        var exact = results.FirstOrDefault(r =>
            string.Equals(r.Name, query, StringComparison.OrdinalIgnoreCase));
        return (exact ?? results[0]).Id;
    }

    private async Task<TmdbCompanyDetails?> GetCompanyDetailsAsync(int companyId, string apiKey, CancellationToken cancellationToken)
    {
        var url =
            "https://api.themoviedb.org/3/company/"
            + companyId.ToString(CultureInfo.InvariantCulture)
            + "?api_key="
            + Uri.EscapeDataString(apiKey);

        var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<TmdbCompanyDetails>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ResolveApiKey(string? configuredKey)
    {
        var trimmed = configuredKey?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? JellyfinSharedTmdbApiKey : trimmed;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class TmdbCompanySearchPayload
    {
        public List<TmdbCompanySearchResult>? Results { get; set; }
    }

    private sealed class TmdbCompanySearchResult
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }

    private sealed class TmdbCompanyDetails
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Description { get; set; }

        public string? Headquarters { get; set; }

        public string? Homepage { get; set; }

        [JsonPropertyName("logo_path")]
        public string? LogoPath { get; set; }

        [JsonPropertyName("origin_country")]
        public string? OriginCountry { get; set; }
    }
}
