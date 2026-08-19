using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Picks an in-title still from TMDB tagged images (character-in-show photo),
/// falling back to nothing so callers can use the actor profile.
/// </summary>
internal static class TmdbCharacterStills
{
    /// <summary>How many billed people to look up stills for (playback rail).</summary>
    public const int MaxLookups = 16;

    /// <summary>Parallel tagged-image requests.</summary>
    public const int LookupConcurrency = 4;

    /// <summary>Maps a Jellyfin item kind to TMDB tagged-image media_type.</summary>
    public static string? ExpectedMediaType(string? jellyfinMediaType)
    {
        if (string.IsNullOrWhiteSpace(jellyfinMediaType))
        {
            return null;
        }

        return string.Equals(jellyfinMediaType, "Movie", StringComparison.OrdinalIgnoreCase)
            ? "movie"
            : "tv";
    }

    /// <summary>Builds a CDN URL for a TMDB still file path.</summary>
    public static string? ToStillUrl(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var path = filePath.Trim();
        if (path[0] != '/')
        {
            path = "/" + path;
        }

        return TmdbDefaults.ImageBaseW500 + path;
    }

    /// <summary>
    /// Chooses the best tagged still that belongs to this title.
    /// Prefers landscape backdrops/stills over portraits.
    /// </summary>
    public static string? PickTaggedStillPath(
        IReadOnlyList<TmdbTaggedImage> images,
        int mediaTmdbId,
        string? expectedMediaType)
    {
        if (images.Count == 0 || mediaTmdbId <= 0)
        {
            return null;
        }

        var matching = images
            .Where(img => BelongsToTitle(img, mediaTmdbId, expectedMediaType))
            .Where(img => !string.IsNullOrWhiteSpace(img.FilePath))
            .ToList();

        if (matching.Count == 0)
        {
            return null;
        }

        var landscape = matching.Where(img => img.AspectRatio >= 1.2).ToList();
        var pool = landscape.Count > 0 ? landscape : matching;

        return pool
            .OrderByDescending(PreferInShowStill)
            .ThenByDescending(img => img.VoteAverage)
            .ThenByDescending(img => img.VoteCount)
            .Select(img => img.FilePath)
            .FirstOrDefault();
    }

    private static bool BelongsToTitle(TmdbTaggedImage img, int mediaTmdbId, string? expectedMediaType)
    {
        var id = img.Media?.Id ?? 0;
        if (id != mediaTmdbId)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedMediaType))
        {
            return true;
        }

        var type = img.MediaType ?? img.Media?.MediaType;
        return string.IsNullOrWhiteSpace(type)
            || string.Equals(type, expectedMediaType, StringComparison.OrdinalIgnoreCase);
    }

    private static int PreferInShowStill(TmdbTaggedImage img)
    {
        var type = img.ImageType?.Trim();
        if (string.Equals(type, "backdrop", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "still", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(type, "poster", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "profile", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return 1;
    }
}

/// <summary>TMDB <c>/person/{id}/tagged_images</c> payload.</summary>
internal sealed class TmdbTaggedImagesPayload
{
    /// <summary>Gets or sets tagged image results.</summary>
    [JsonPropertyName("results")]
    public List<TmdbTaggedImage>? Results { get; set; }
}

/// <summary>One TMDB tagged image (still/poster of a person in a title).</summary>
internal sealed class TmdbTaggedImage
{
    /// <summary>Gets or sets the image file path on image.tmdb.org.</summary>
    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    /// <summary>Gets or sets width / height.</summary>
    [JsonPropertyName("aspect_ratio")]
    public double AspectRatio { get; set; }

    /// <summary>Gets or sets TMDB image_type when present (backdrop, poster, …).</summary>
    [JsonPropertyName("image_type")]
    public string? ImageType { get; set; }

    /// <summary>Gets or sets movie or tv.</summary>
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    /// <summary>Gets or sets the title this image is tagged on.</summary>
    [JsonPropertyName("media")]
    public TmdbTaggedMedia? Media { get; set; }

    /// <summary>Gets or sets community vote average.</summary>
    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    /// <summary>Gets or sets community vote count.</summary>
    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }
}

/// <summary>The movie/TV the tagged image belongs to.</summary>
internal sealed class TmdbTaggedMedia
{
    /// <summary>Gets or sets the TMDB movie or series id.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets movie or tv when nested on media.</summary>
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }
}
