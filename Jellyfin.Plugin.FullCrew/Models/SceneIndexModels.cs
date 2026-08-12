using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.FullCrew.Models;

/// <summary>
/// One timed scene identify result stored in the local scene index.
/// </summary>
public class SceneIndexEntry
{
    /// <summary>
    /// Gets or sets playback position in ticks when identified.
    /// </summary>
    public long PositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the bucket key (seconds from start, floored to bucket size).
    /// </summary>
    public long BucketSeconds { get; set; }

    /// <summary>
    /// Gets or sets matched people for this moment.
    /// </summary>
    public List<SceneIdentifyMatch> Matches { get; set; } = [];

    /// <summary>
    /// Gets or sets when this entry was stored (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets an optional note from identify.
    /// </summary>
    public string? Note { get; set; }
}

/// <summary>
/// On-disk index document for one library item.
/// </summary>
public class SceneIndexDocument
{
    /// <summary>
    /// Gets or sets the Jellyfin item id (N format).
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets timed identify entries.
    /// </summary>
    public List<SceneIndexEntry> Entries { get; set; } = [];
}

/// <summary>
/// Playback companion payload: title cast + nearby indexed scene (no OpenAI required).
/// </summary>
public class PlaybackSceneResponse
{
    /// <summary>
    /// Gets or sets the Jellyfin item id.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item display name when known.
    /// </summary>
    public string? ItemName { get; set; }

    /// <summary>
    /// Gets or sets top billed cast for this title/episode.
    /// </summary>
    public IReadOnlyList<SceneIdentifyMatch> Cast { get; set; } = [];

    /// <summary>
    /// Gets or sets a nearby scene-index hit for the current position, if any.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SceneIdentifyResponse? Scene { get; set; }

    /// <summary>
    /// Gets or sets how many indexed moments exist for this item.
    /// </summary>
    public int IndexedSceneCount { get; set; }

    /// <summary>
    /// Gets or sets whether OpenAI vision identify is currently available.
    /// </summary>
    public bool VisionEnabled { get; set; }

    /// <summary>
    /// Gets or sets why Vision is unavailable when <see cref="VisionEnabled"/> is false.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VisionReason { get; set; }

    /// <summary>
    /// Gets or sets a credits error when cast could not load.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}
