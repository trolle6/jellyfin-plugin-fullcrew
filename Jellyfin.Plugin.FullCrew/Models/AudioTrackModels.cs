using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.FullCrew.Models;

/// <summary>
/// Summary of one audio-kind bucket (commentary, dub, etc.).
/// </summary>
public class AudioTypeSummary
{
    /// <summary>
    /// Gets or sets the stable type id (e.g. <c>commentary</c>).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a short description of the type.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how many distinct items have at least one track of this type.
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Gets or sets how many individual audio streams matched this type.
    /// </summary>
    public int TrackCount { get; set; }
}

/// <summary>
/// Library-wide audio type overview.
/// </summary>
public class AudioTypesResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the audio browser is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a full reindex is running.
    /// </summary>
    public bool IsIndexing { get; set; }

    /// <summary>
    /// Gets or sets when the in-memory index was last built.
    /// </summary>
    public DateTime? IndexedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct items in the index.
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Gets or sets the number of special audio streams in the index.
    /// </summary>
    public int TrackCount { get; set; }

    /// <summary>
    /// Gets or sets type buckets with counts.
    /// </summary>
    public IReadOnlyList<AudioTypeSummary> Types { get; set; } = [];
}

/// <summary>
/// One classified audio stream on an item.
/// </summary>
public class AudioTrackInfo
{
    /// <summary>
    /// Gets or sets the stream index within the item.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the classified type id.
    /// </summary>
    public string TypeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the classified type display name.
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the raw stream title from the file.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the ISO language code when present.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets a friendly language label.
    /// </summary>
    public string? LanguageName { get; set; }

    /// <summary>
    /// Gets or sets the audio codec.
    /// </summary>
    public string? Codec { get; set; }

    /// <summary>
    /// Gets or sets the channel count.
    /// </summary>
    public int? Channels { get; set; }

    /// <summary>
    /// Gets or sets a compact display string for the stream.
    /// </summary>
    public string DisplayTitle { get; set; } = string.Empty;
}

/// <summary>
/// A movie or episode that carries one or more special audio tracks.
/// </summary>
public class AudioTrackItem
{
    /// <summary>
    /// Gets or sets the Jellyfin item id.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item name (episode or movie title).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media kind (Movie, Episode, Video).
    /// </summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the series name for episodes.
    /// </summary>
    public string? SeriesName { get; set; }

    /// <summary>
    /// Gets or sets the series id for episodes.
    /// </summary>
    public string? SeriesId { get; set; }

    /// <summary>
    /// Gets or sets the season number for episodes.
    /// </summary>
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode number.
    /// </summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    public int? ProductionYear { get; set; }

    /// <summary>
    /// Gets or sets the item id to use for the primary image (episode, else series, else self).
    /// </summary>
    public string ImageItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets matching tracks on this item (already filtered to the requested type when applicable).
    /// </summary>
    public IReadOnlyList<AudioTrackInfo> Tracks { get; set; } = [];
}

/// <summary>
/// Paged list of items that have a given audio type.
/// </summary>
public class AudioTypeItemsResponse
{
    /// <summary>
    /// Gets or sets the requested type id, or <c>all</c>.
    /// </summary>
    public string TypeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the requested type display name.
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether a full reindex is running.
    /// </summary>
    public bool IsIndexing { get; set; }

    /// <summary>
    /// Gets or sets the total matching items before paging.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the page start index.
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// Gets or sets languages present in the unpaged match set.
    /// </summary>
    public IReadOnlyList<string> Languages { get; set; } = [];

    /// <summary>
    /// Gets or sets the page of items.
    /// </summary>
    public IReadOnlyList<AudioTrackItem> Items { get; set; } = [];
}

/// <summary>
/// Special audio tracks found on a single item.
/// </summary>
public class ItemAudioTracksResponse
{
    /// <summary>
    /// Gets or sets the Jellyfin item id.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets classified special audio tracks. Empty when none were found.
    /// </summary>
    public IReadOnlyList<AudioTrackInfo> Tracks { get; set; } = [];
}
