using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.FullCrew.Models;

/// <summary>
/// Full Crew studio detail page payload (TMDB company + library titles + stats).
/// </summary>
public class StudioPageResponse
{
    /// <summary>Display name (cluster label or exact studio).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Jellyfin Studio item id when known.</summary>
    public string? ItemId { get; set; }

    /// <summary>TMDB company overview / description when available.</summary>
    public string? Overview { get; set; }

    /// <summary>Absolute logo URL (TMDB) when available.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Official homepage when available.</summary>
    public string? Homepage { get; set; }

    /// <summary>TMDB company id when resolved.</summary>
    public int? TmdbCompanyId { get; set; }

    /// <summary>TMDB company page URL when resolved.</summary>
    public string? TmdbUrl { get; set; }

    /// <summary>Headquarters string from TMDB when present.</summary>
    public string? Headquarters { get; set; }

    /// <summary>Origin country code from TMDB when present.</summary>
    public string? OriginCountry { get; set; }

    /// <summary>Parent company name from TMDB when present.</summary>
    public string? ParentCompany { get; set; }

    /// <summary>Exact studio credit strings included in this page (cluster branches).</summary>
    public IReadOnlyList<string> Branches { get; set; } = [];

    /// <summary>Movies/Series in the library credited to any branch.</summary>
    public IReadOnlyList<StudioLibraryTitle> Titles { get; set; } = [];

    /// <summary>Aggregated library statistics for this studio.</summary>
    public StudioLibraryStats? Stats { get; set; }

    /// <summary>Well-known TMDB titles from this company not found in the library (bounded).</summary>
    public IReadOnlyList<StudioMissingTitle> MissingPopular { get; set; } = [];
}

/// <summary>
/// Library-derived studio statistics.
/// </summary>
public class StudioLibraryStats
{
    /// <summary>Total Movie + Series count.</summary>
    public int TotalCount { get; set; }

    /// <summary>Movie count.</summary>
    public int MovieCount { get; set; }

    /// <summary>Series count.</summary>
    public int SeriesCount { get; set; }

    /// <summary>Oldest production year in the library set.</summary>
    public int? FirstReleaseYear { get; set; }

    /// <summary>Newest production year in the library set.</summary>
    public int? NewestReleaseYear { get; set; }

    /// <summary>Average community rating across rated titles (0–10).</summary>
    public double? AverageCommunityRating { get; set; }

    /// <summary>Count of titles that contributed to the average.</summary>
    public int RatedTitleCount { get; set; }

    /// <summary>Highest-rated title name when available.</summary>
    public string? HighestRatedTitle { get; set; }

    /// <summary>Highest-rated title id when available.</summary>
    public string? HighestRatedTitleId { get; set; }

    /// <summary>Highest rating value.</summary>
    public double? HighestRatedValue { get; set; }

    /// <summary>Oldest title name when available.</summary>
    public string? OldestTitle { get; set; }

    /// <summary>Oldest title id when available.</summary>
    public string? OldestTitleId { get; set; }

    /// <summary>Newest title name when available.</summary>
    public string? NewestTitle { get; set; }

    /// <summary>Newest title id when available.</summary>
    public string? NewestTitleId { get; set; }
}

/// <summary>
/// A library Movie/Series credited to the studio (or cluster).
/// </summary>
public class StudioLibraryTitle
{
    /// <summary>Jellyfin item id (N format).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Movie or Series.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Production year when known.</summary>
    public int? ProductionYear { get; set; }

    /// <summary>Community rating when known.</summary>
    public float? CommunityRating { get; set; }

    /// <summary>Primary image tag when the item has one.</summary>
    public string? ImageTag { get; set; }
}

/// <summary>
/// A popular TMDB title credited to the company that is not in the library.
/// </summary>
public class StudioMissingTitle
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Release year when known.</summary>
    public int? Year { get; set; }

    /// <summary>TMDB media type (movie / tv).</summary>
    public string MediaType { get; set; } = "movie";

    /// <summary>TMDB id.</summary>
    public int TmdbId { get; set; }

    /// <summary>TMDB page URL.</summary>
    public string? TmdbUrl { get; set; }
}
