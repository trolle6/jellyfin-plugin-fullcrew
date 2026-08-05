using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.FullCrew.Models;

/// <summary>
/// Full Crew studio detail page payload (TMDB company + library titles).
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

    /// <summary>Exact studio credit strings included in this page (cluster branches).</summary>
    public IReadOnlyList<string> Branches { get; set; } = [];

    /// <summary>Movies/Series in the library credited to any branch.</summary>
    public IReadOnlyList<StudioLibraryTitle> Titles { get; set; } = [];
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

    /// <summary>Primary image tag when the item has one.</summary>
    public string? ImageTag { get; set; }
}
