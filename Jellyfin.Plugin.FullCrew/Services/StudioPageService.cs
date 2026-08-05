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

        var (titles, coCredit) = FindLibraryTitles(user, matchNames);
        var stats = BuildLibraryStats(titles);
        var tmdb = await TryFetchTmdbCompanyAsync(displayName, matchNames, cancellationToken).ConfigureAwait(false);
        var missing = tmdb?.Id > 0
            ? await TryFetchMissingPopularAsync(tmdb.Id, titles, cancellationToken).ConfigureAwait(false)
            : [];

        return new StudioPageResponse
        {
            Name = displayName,
            ItemId = itemIdN,
            Overview = TmdbDefaults.NullIfEmpty(tmdb?.Description),
            LogoUrl = string.IsNullOrWhiteSpace(tmdb?.LogoPath)
                ? null
                : TmdbDefaults.ImageBaseW500 + tmdb!.LogoPath,
            Homepage = TmdbDefaults.NullIfEmpty(tmdb?.Homepage),
            TmdbCompanyId = tmdb?.Id > 0 ? tmdb.Id : null,
            TmdbUrl = tmdb?.Id > 0
                ? "https://www.themoviedb.org/company/" + tmdb.Id.ToString(CultureInfo.InvariantCulture)
                : null,
            Headquarters = TmdbDefaults.NullIfEmpty(tmdb?.Headquarters),
            OriginCountry = TmdbDefaults.NullIfEmpty(tmdb?.OriginCountry),
            ParentCompany = TmdbDefaults.NullIfEmpty(tmdb?.ParentCompany?.Name),
            Branches = matchNames,
            Titles = titles,
            Stats = stats,
            MissingPopular = missing,
            CoCreditHint = coCredit
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
            // Prefer caller-provided cluster branches (exact credit strings).
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

    private (IReadOnlyList<StudioLibraryTitle> Titles, StudioCoCreditHint? CoCredit) FindLibraryTitles(
        User? user,
        IReadOnlyList<string> matchNames)
    {
        if (matchNames.Count == 0)
        {
            return ([], null);
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
            var coCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
                    CommunityRating = item.CommunityRating is > 0 ? item.CommunityRating : null,
                    ImageTag = item.HasImage(ImageType.Primary) ? "primary" : null
                });

                foreach (var studio in studios)
                {
                    if (string.IsNullOrWhiteSpace(studio))
                    {
                        continue;
                    }

                    var other = studio.Trim();
                    if (wanted.Contains(other))
                    {
                        continue;
                    }

                    coCounts[other] = coCounts.TryGetValue(other, out var n) ? n + 1 : 1;
                }
            }

            var ordered = results
                .OrderByDescending(t => t.ProductionYear ?? 0)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxTitles)
                .ToList();

            return (ordered, BuildCoCreditHint(results.Count, coCounts));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to find library titles for studio page");
            return ([], null);
        }
    }

    private static StudioCoCreditHint? BuildCoCreditHint(int totalTitles, Dictionary<string, int> coCounts)
    {
        if (totalTitles < 2 || coCounts.Count == 0)
        {
            return null;
        }

        var best = coCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .First();

        // Soft note only when the other studio appears on most titles (financier/partner pattern).
        if (best.Value < 2 || best.Value * 2 < totalTitles)
        {
            return null;
        }

        return new StudioCoCreditHint
        {
            Name = best.Key,
            SharedTitleCount = best.Value,
            TotalTitleCount = totalTitles
        };
    }

    private static StudioLibraryStats BuildLibraryStats(IReadOnlyList<StudioLibraryTitle> titles)
    {
        var stats = new StudioLibraryStats
        {
            TotalCount = titles.Count,
            MovieCount = titles.Count(t => string.Equals(t.Type, "Movie", StringComparison.OrdinalIgnoreCase)),
            SeriesCount = titles.Count(t => string.Equals(t.Type, "Series", StringComparison.OrdinalIgnoreCase))
        };

        if (titles.Count == 0)
        {
            return stats;
        }

        var withYear = titles.Where(t => t.ProductionYear is > 0).ToList();
        if (withYear.Count > 0)
        {
            var oldest = withYear.OrderBy(t => t.ProductionYear).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).First();
            var newest = withYear.OrderByDescending(t => t.ProductionYear).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).First();
            stats.FirstReleaseYear = oldest.ProductionYear;
            stats.NewestReleaseYear = newest.ProductionYear;
            stats.OldestTitle = oldest.Name;
            stats.OldestTitleId = oldest.Id;
            stats.NewestTitle = newest.Name;
            stats.NewestTitleId = newest.Id;
        }

        var rated = titles.Where(t => t.CommunityRating is > 0).ToList();
        if (rated.Count > 0)
        {
            stats.RatedTitleCount = rated.Count;
            stats.AverageCommunityRating = Math.Round(rated.Average(t => t.CommunityRating!.Value), 1);
            var best = rated.OrderByDescending(t => t.CommunityRating).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).First();
            stats.HighestRatedTitle = best.Name;
            stats.HighestRatedTitleId = best.Id;
            stats.HighestRatedValue = Math.Round(best.CommunityRating!.Value, 1);
        }

        return stats;
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
        var apiKey = TmdbDefaults.ResolveApiKey(Plugin.Instance?.Configuration.TmdbApiKey);
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

        // Prefer exact name; otherwise require a strong prefix/containment match.
        // Avoid grabbing unrelated first hits (e.g. "Mitsubishi" → car company).
        var exact = results.FirstOrDefault(r =>
            string.Equals(r.Name, query, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact.Id;
        }

        var q = NormalizeCompanyKey(query);
        var ranked = results
            .Select(r => (Result: r, Score: CompanyNameScore(q, r.Name)))
            .Where(x => x.Score >= 70)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => (x.Result.Name ?? string.Empty).Length)
            .ToList();

        return ranked.Count > 0 ? ranked[0].Result.Id : null;
    }

    private static string NormalizeCompanyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Trim().ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int CompanyNameScore(string queryKey, string? candidateName)
    {
        var candidate = NormalizeCompanyKey(candidateName);
        if (queryKey.Length == 0 || candidate.Length == 0)
        {
            return 0;
        }

        if (string.Equals(queryKey, candidate, StringComparison.Ordinal))
        {
            return 100;
        }

        if (candidate.StartsWith(queryKey, StringComparison.Ordinal)
            || queryKey.StartsWith(candidate, StringComparison.Ordinal))
        {
            return 90;
        }

        if (candidate.Contains(queryKey, StringComparison.Ordinal)
            || queryKey.Contains(candidate, StringComparison.Ordinal))
        {
            // Require meaningful overlap so short tokens don't latch onto random companies.
            var shorter = Math.Min(queryKey.Length, candidate.Length);
            return shorter >= 6 ? 75 : 40;
        }

        return 0;
    }

    private async Task<IReadOnlyList<StudioMissingTitle>> TryFetchMissingPopularAsync(
        int companyId,
        IReadOnlyList<StudioLibraryTitle> owned,
        CancellationToken cancellationToken)
    {
        try
        {
            var apiKey = TmdbDefaults.ResolveApiKey(Plugin.Instance?.Configuration.TmdbApiKey);
            var ownedKeys = new HashSet<string>(
                owned.Select(t => NormalizeTitleKey(t.Name)),
                StringComparer.OrdinalIgnoreCase);

            var movies = await DiscoverCompanyMediaAsync(companyId, "movie", apiKey, cancellationToken).ConfigureAwait(false);
            var shows = await DiscoverCompanyMediaAsync(companyId, "tv", apiKey, cancellationToken).ConfigureAwait(false);

            var missing = new List<StudioMissingTitle>();
            foreach (var hit in movies.Concat(shows))
            {
                var title = TmdbDefaults.NullIfEmpty(hit.Title) ?? TmdbDefaults.NullIfEmpty(hit.Name);
                if (title is null)
                {
                    continue;
                }

                if (ownedKeys.Contains(NormalizeTitleKey(title)))
                {
                    continue;
                }

                var year = ParseYear(hit.ReleaseDate) ?? ParseYear(hit.FirstAirDate);
                var mediaType = !string.IsNullOrWhiteSpace(hit.Title) ? "movie" : "tv";
                missing.Add(new StudioMissingTitle
                {
                    Name = title,
                    Year = year,
                    MediaType = mediaType,
                    TmdbId = hit.Id,
                    TmdbUrl = mediaType == "tv"
                        ? "https://www.themoviedb.org/tv/" + hit.Id.ToString(CultureInfo.InvariantCulture)
                        : "https://www.themoviedb.org/movie/" + hit.Id.ToString(CultureInfo.InvariantCulture)
                });

                if (missing.Count >= 8)
                {
                    break;
                }
            }

            return missing;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Missing-popular lookup failed for company {CompanyId}", companyId);
            return [];
        }
    }

    private async Task<IReadOnlyList<TmdbDiscoverHit>> DiscoverCompanyMediaAsync(
        int companyId,
        string mediaType,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var path = mediaType == "tv" ? "discover/tv" : "discover/movie";
        var url =
            "https://api.themoviedb.org/3/"
            + path
            + "?api_key="
            + Uri.EscapeDataString(apiKey)
            + "&with_companies="
            + companyId.ToString(CultureInfo.InvariantCulture)
            + "&sort_by=popularity.desc&include_adult=false&page=1";

        var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<TmdbDiscoverPayload>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return payload?.Results ?? [];
    }

    private static string NormalizeTitleKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var chars = name.Trim().ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int? ParseYear(string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || date.Length < 4)
        {
            return null;
        }

        return int.TryParse(date.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
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

        [JsonPropertyName("parent_company")]
        public TmdbParentCompany? ParentCompany { get; set; }
    }

    private sealed class TmdbParentCompany
    {
        public string? Name { get; set; }
    }

    private sealed class TmdbDiscoverPayload
    {
        public List<TmdbDiscoverHit>? Results { get; set; }
    }

    private sealed class TmdbDiscoverHit
    {
        public int Id { get; set; }

        public string? Title { get; set; }

        public string? Name { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }
    }
}
