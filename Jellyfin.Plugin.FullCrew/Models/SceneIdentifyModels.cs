using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.FullCrew.Models;

/// <summary>
/// Client-facing status for scene identify (no secrets).
/// </summary>
public class SceneIdentifyStatusResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether identify is enabled and an API key is configured.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a short reason when disabled.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets the configured vision model name (for UI display).
    /// </summary>
    public string? Model { get; set; }
}

/// <summary>
/// Request body for frame identification.
/// </summary>
public class SceneIdentifyRequest
{
    /// <summary>
    /// Gets or sets a data-URL or raw base64 JPEG/PNG of the captured frame.
    /// </summary>
    public string ImageBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional playback position in ticks (for scene index).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? PositionTicks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to skip the scene index and call Vision again.
    /// </summary>
    public bool ForceRefresh { get; set; }
}

/// <summary>
/// Response from frame identification.
/// </summary>
public class SceneIdentifyResponse
{
    /// <summary>
    /// Gets or sets the Jellyfin item id.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets people matched from this title's cast list.
    /// </summary>
    public IReadOnlyList<SceneIdentifyMatch> Matches { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the result came from the short frame-hash cache.
    /// </summary>
    public bool FromCache { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the result came from the persistent scene index.
    /// </summary>
    public bool FromSceneIndex { get; set; }

    /// <summary>
    /// Gets or sets playback position ticks associated with this result.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? PositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the result source: vision, memory, or index.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets a non-fatal note (e.g. uncertain / empty).
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Gets or sets an error message when identify failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// One cast member matched in the frame.
/// </summary>
public class SceneIdentifyMatch
{
    /// <summary>
    /// Gets or sets the person display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the character / role when known.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Gets or sets the TMDB person id.
    /// </summary>
    public int? TmdbPersonId { get; set; }

    /// <summary>
    /// Gets or sets the TMDB profile image URL.
    /// </summary>
    public string? ProfileUrl { get; set; }

    /// <summary>
    /// Gets or sets model confidence 0–1 when provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Confidence { get; set; }
}
