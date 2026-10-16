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
/// Fetches production facts (budget, box office, runtime, status, …) from TMDB movie/series details.
/// </summary>
public class TitleFactsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILibraryManager _libraryManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<TitleFactsService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleFactsService"/> class.
    /// </summary>
    public TitleFactsService(
        ILibraryManager libraryManager,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        ILogger<TitleFactsService> logger)
    {
        _libraryManager = libraryManager;
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    /// <summary>
    /// Gets production facts for a Jellyfin movie or series item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The facts response.</returns>
    public async Task<TitleFactsResponse> GetFactsAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return new TitleFactsResponse
            {
                ItemId = itemId.ToString("N", CultureInfo.InvariantCulture),
                Error = "Item not found."
            };
        }

        var kind = item.GetBaseItemKind();
        if (kind is not (BaseItemKind.Movie or BaseItemKind.Series or BaseItemKind.Season or BaseItemKind.Episode))
        {
            return new TitleFactsResponse
            {
                ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                ItemName = item.Name,
                MediaType = kind.ToString(),
                Error = "Title facts are only available for movies and series."
            };
        }

        var lookup = ResolveTmdbLookup(item);
        if (lookup is null)
        {
            return new TitleFactsResponse
            {
                ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                ItemName = item.Name,
                MediaType = kind.ToString(),
                Error = "No TMDB id found for this item. Identify the item with TheMovieDb metadata."
            };
        }

        var config = Plugin.Instance?.Configuration;
        var apiKey = TmdbDefaults.ResolveApiKey(config?.TmdbApiKey);
        var cacheKey = $"titlefacts-v1-{lookup.IsMovie}-{lookup.TmdbId}";
        if (_memoryCache.TryGetValue(cacheKey, out TitleFactsResponse? cached) && cached is not null)
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
            _logger.LogWarning(ex, "Failed to load TMDB title facts for item {ItemId} (TMDB {TmdbId})", item.Id, lookup.TmdbId);
            return new TitleFactsResponse
            {
                ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                ItemName = item.Name,
                TmdbId = lookup.TmdbId,
                MediaType = kind.ToString(),
                Error = "Failed to load title facts from TMDB."
            };
        }
    }

    private async Task<TitleFactsResponse> FetchFromTmdbAsync(
        BaseItem item,
        TmdbLookup lookup,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        var url = lookup.IsMovie
            ? $"https://api.themoviedb.org/3/movie/{lookup.TmdbId}?api_key={Uri.EscapeDataString(apiKey)}"
            : $"https://api.themoviedb.org/3/tv/{lookup.TmdbId}?api_key={Uri.EscapeDataString(apiKey)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", PluginInfo.UserAgent);
        using var httpResponse = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "TMDB details request failed ({StatusCode}): {Body}",
                (int)httpResponse.StatusCode,
                body);

            return new TitleFactsResponse
            {
                ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                ItemName = item.Name,
                TmdbId = lookup.TmdbId,
                MediaType = item.GetBaseItemKind().ToString(),
                Error = $"TMDB returned {(int)httpResponse.StatusCode}."
            };
        }

        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<TmdbDetailsPayload>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var data = ToData(payload, lookup.IsMovie);
        var (tagline, homepage, facts) = TitleFactsFormatter.Build(data);

        return new TitleFactsResponse
        {
            ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
            ItemName = item.Name,
            TmdbId = lookup.TmdbId,
            MediaType = item.GetBaseItemKind().ToString(),
            Tagline = tagline,
            Homepage = homepage,
            Facts = facts
        };
    }

    private static TitleFactsData ToData(TmdbDetailsPayload? p, bool isMovie)
    {
        if (p is null)
        {
            return new TitleFactsData { IsMovie = isMovie };
        }

        return new TitleFactsData
        {
            IsMovie = isMovie,
            Status = p.Status,
            Runtime = isMovie ? p.Runtime : p.EpisodeRunTime?.FirstOrDefault(r => r > 0),
            Budget = p.Budget,
            Revenue = p.Revenue,
            ReleaseDate = p.ReleaseDate,
            FirstAirDate = p.FirstAirDate,
            LastAirDate = p.LastAirDate,
            InProduction = p.InProduction ?? false,
            NumberOfSeasons = p.NumberOfSeasons,
            NumberOfEpisodes = p.NumberOfEpisodes,
            OriginalLanguage = p.OriginalLanguage,
            Genres = Names(p.Genres),
            ProductionCompanies = Names(p.ProductionCompanies),
            Networks = Names(p.Networks),
            ProductionCountries = Names(p.ProductionCountries),
            SpokenLanguages = p.SpokenLanguages?
                .Select(s => new SpokenLanguage(s.Iso6391, s.EnglishName ?? s.Name))
                .ToList() ?? [],
            VoteAverage = p.VoteAverage,
            VoteCount = p.VoteCount,
            Tagline = p.Tagline,
            Homepage = p.Homepage
        };
    }

    private static IReadOnlyList<string> Names(IEnumerable<TmdbNamed>? items)
        => items?.Select(i => i.Name ?? string.Empty).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? [];

    private static TmdbLookup? ResolveTmdbLookup(BaseItem item)
    {
        if (item is Movie || item.GetBaseItemKind() == BaseItemKind.Movie)
        {
            var movieId = GetProviderId(item, "Tmdb");
            return string.IsNullOrWhiteSpace(movieId) ? null : new TmdbLookup(movieId, true);
        }

        BaseItem? seriesSource = item switch
        {
            Series => item,
            Season season => season.Series ?? item,
            Episode episode => episode.Series ?? item,
            _ => item
        };

        var seriesId = GetProviderId(seriesSource, "Tmdb")
            ?? GetProviderId(seriesSource, "TmdbSeries")
            ?? GetProviderId(item, "Tmdb")
            ?? GetProviderId(item, "TmdbSeries");

        return string.IsNullOrWhiteSpace(seriesId) ? null : new TmdbLookup(seriesId, false);
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

    private static TitleFactsResponse CloneForItem(TitleFactsResponse cached, BaseItem item)
    {
        return new TitleFactsResponse
        {
            ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
            ItemName = item.Name,
            TmdbId = cached.TmdbId,
            MediaType = item.GetBaseItemKind().ToString(),
            Error = cached.Error,
            Tagline = cached.Tagline,
            Homepage = cached.Homepage,
            Facts = cached.Facts
        };
    }

    private sealed record TmdbLookup(string TmdbId, bool IsMovie);

    private sealed class TmdbDetailsPayload
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("runtime")]
        public int? Runtime { get; set; }

        [JsonPropertyName("episode_run_time")]
        public List<int>? EpisodeRunTime { get; set; }

        [JsonPropertyName("budget")]
        public long? Budget { get; set; }

        [JsonPropertyName("revenue")]
        public long? Revenue { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }

        [JsonPropertyName("last_air_date")]
        public string? LastAirDate { get; set; }

        [JsonPropertyName("in_production")]
        public bool? InProduction { get; set; }

        [JsonPropertyName("number_of_seasons")]
        public int? NumberOfSeasons { get; set; }

        [JsonPropertyName("number_of_episodes")]
        public int? NumberOfEpisodes { get; set; }

        [JsonPropertyName("original_language")]
        public string? OriginalLanguage { get; set; }

        [JsonPropertyName("genres")]
        public List<TmdbNamed>? Genres { get; set; }

        [JsonPropertyName("production_companies")]
        public List<TmdbNamed>? ProductionCompanies { get; set; }

        [JsonPropertyName("networks")]
        public List<TmdbNamed>? Networks { get; set; }

        [JsonPropertyName("production_countries")]
        public List<TmdbNamed>? ProductionCountries { get; set; }

        [JsonPropertyName("spoken_languages")]
        public List<TmdbSpokenLanguage>? SpokenLanguages { get; set; }

        [JsonPropertyName("vote_average")]
        public double? VoteAverage { get; set; }

        [JsonPropertyName("vote_count")]
        public int? VoteCount { get; set; }

        [JsonPropertyName("tagline")]
        public string? Tagline { get; set; }

        [JsonPropertyName("homepage")]
        public string? Homepage { get; set; }
    }

    private sealed class TmdbNamed
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed class TmdbSpokenLanguage
    {
        [JsonPropertyName("iso_639_1")]
        public string? Iso6391 { get; set; }

        [JsonPropertyName("english_name")]
        public string? EnglishName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
