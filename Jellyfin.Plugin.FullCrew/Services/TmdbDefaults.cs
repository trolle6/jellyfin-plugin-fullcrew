using System;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Shared TMDB defaults used by credits, studio pages, and related lookups.
/// </summary>
internal static class TmdbDefaults
{
    /// <summary>
    /// Same shared TMDB API key Jellyfin's official TheMovieDb provider uses
    /// (<c>MediaBrowser.Providers.Plugins.Tmdb.TmdbUtils.ApiKey</c>) when no custom key is configured.
    /// </summary>
    public const string SharedApiKey = "4219e299c89411838049ab0dab19ebd5";

    /// <summary>TMDB image CDN base for w500 assets.</summary>
    public const string ImageBaseW500 = "https://image.tmdb.org/t/p/w500";

    /// <summary>
    /// Uses a configured key when set; otherwise Jellyfin's shared TMDB provider key.
    /// </summary>
    public static string ResolveApiKey(string? configuredKey)
    {
        var trimmed = configuredKey?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? SharedApiKey : trimmed;
    }

    /// <summary>Trims and returns null for blank strings.</summary>
    public static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
