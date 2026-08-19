using System.Collections.Generic;

namespace Jellyfin.Plugin.FullCrew.Models;

/// <summary>
/// A nostalgia break-bumper suggestion for an item.
/// </summary>
public class BumperResponse
{
    /// <summary>
    /// Gets or sets the Jellyfin item id this bumper was resolved for.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the playback source: Local, YouTube, or Search.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a display title for the bumper.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the network/studio hint used for matching.
    /// </summary>
    public string? Network { get; set; }

    /// <summary>
    /// Gets or sets a local Jellyfin item id when <see cref="Source"/> is Local.
    /// </summary>
    public string? LocalItemId { get; set; }

    /// <summary>
    /// Gets or sets a direct YouTube watch URL when available.
    /// </summary>
    public string? YouTubeUrl { get; set; }

    /// <summary>
    /// Gets or sets a YouTube video id for in-app embed playback.
    /// </summary>
    public string? YouTubeVideoId { get; set; }

    /// <summary>
    /// Gets or sets a YouTube search URL fallback.
    /// </summary>
    public string? SearchUrl { get; set; }

    /// <summary>
    /// Gets or sets an error when nothing could be resolved.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets network hints considered for this item.
    /// </summary>
    public IReadOnlyList<string> NetworkHints { get; set; } = [];

    /// <summary>
    /// Gets or sets a stable bumper key used for rotation history (local:… or yt:…).
    /// </summary>
    public string? BumperKey { get; set; }

    /// <summary>
    /// Gets or sets how many other unseen bumpers remain for this show.
    /// </summary>
    public int AlternatesAvailable { get; set; }
}
