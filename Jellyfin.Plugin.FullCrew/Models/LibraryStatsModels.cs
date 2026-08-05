using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.FullCrew.Models;

/// <summary>
/// Aggregated library statistics for movies and series.
/// </summary>
public class LibraryStatsResponse
{
    /// <summary>
    /// Gets or sets when these stats were generated (UTC).
    /// </summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of movies counted.
    /// </summary>
    public int MovieCount { get; set; }

    /// <summary>
    /// Gets or sets the number of series counted.
    /// </summary>
    public int SeriesCount { get; set; }

    /// <summary>
    /// Gets or sets the total movie + series count.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the percent of items whose genres include Animation.
    /// </summary>
    public double AnimationPercent { get; set; }

    /// <summary>
    /// Gets or sets total runtime ticks across movies that have RunTimeTicks.
    /// </summary>
    public long MovieRuntimeTicksTotal { get; set; }

    /// <summary>
    /// Gets or sets average runtime ticks for movies with known runtime.
    /// </summary>
    public long MovieRuntimeTicksAverage { get; set; }

    /// <summary>
    /// Gets or sets how many movies contributed to the runtime average.
    /// </summary>
    public int MovieRuntimeSampleCount { get; set; }

    /// <summary>
    /// Gets or sets total runtime ticks across series that have RunTimeTicks.
    /// </summary>
    public long SeriesRuntimeTicksTotal { get; set; }

    /// <summary>
    /// Gets or sets average runtime ticks for series with known runtime.
    /// </summary>
    public long SeriesRuntimeTicksAverage { get; set; }

    /// <summary>
    /// Gets or sets how many series contributed to the runtime average.
    /// </summary>
    public int SeriesRuntimeSampleCount { get; set; }

    /// <summary>
    /// Gets or sets short auto-generated insight lines.
    /// </summary>
    public IReadOnlyList<string> Insights { get; set; } = [];

    /// <summary>
    /// Gets or sets the Movie vs Series type mix.
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> Types { get; set; } = [];

    /// <summary>
    /// Gets or sets genre frequency buckets.
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> Genres { get; set; } = [];

    /// <summary>
    /// Gets or sets studio / network frequency buckets.
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> Studios { get; set; } = [];

    /// <summary>
    /// Gets or sets official rating frequency buckets.
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> OfficialRatings { get; set; } = [];

    /// <summary>
    /// Gets or sets production decade frequency buckets.
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> Decades { get; set; } = [];

    /// <summary>
    /// Gets or sets community rating distribution buckets.
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> CommunityRatings { get; set; } = [];

    /// <summary>
    /// Gets or sets top tag frequency buckets.
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets preferred metadata language frequency buckets.
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> Languages { get; set; } = [];

    /// <summary>
    /// Gets or sets Jellyfin collection (BoxSet) size buckets.
    /// Count is Movie/Series members in that collection; Percent is share of all
    /// collection memberships (sum of those counts), consistent with role people charts.
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> Collections { get; set; } = [];

    /// <summary>
    /// Gets or sets how many Movie/Series titles contributed media-stream quality stats.
    /// Resolution / HDR / codec / audio percents use this as their denominator
    /// (titles with a resolvable primary video stream), not <see cref="TotalCount"/>.
    /// </summary>
    public int MediaInfoSampleCount { get; set; }

    /// <summary>
    /// Gets or sets video resolution buckets (480p, 720p, 1080p, …) from primary video Height.
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> Resolutions { get; set; } = [];

    /// <summary>
    /// Gets or sets video range / HDR buckets (SDR, HDR10, Dolby Vision, …) from VideoRangeType.
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> VideoRanges { get; set; } = [];

    /// <summary>
    /// Gets or sets primary video codec buckets (H.264, HEVC, AV1, …).
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> VideoCodecs { get; set; } = [];

    /// <summary>
    /// Gets or sets primary audio channel layout buckets (Stereo, 5.1, Atmos, …).
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> AudioChannels { get; set; } = [];

    /// <summary>
    /// Gets or sets primary audio codec buckets (when present on the sampled stream).
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> AudioCodecs { get; set; } = [];

    /// <summary>
    /// Gets or sets top-people lists grouped by PersonKind / role.
    /// </summary>
    public IReadOnlyList<LibraryStatsPeopleGroup> PeopleByRole { get; set; } = [];
}

/// <summary>
/// Top people for a single PersonKind / credit role.
/// </summary>
public class LibraryStatsPeopleGroup
{
    /// <summary>
    /// Gets or sets the role display name (e.g. Actor, Director, Guest Star).
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the underlying PersonKind name when known.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets top people for this role.
    /// </summary>
    public IReadOnlyList<LibraryStatsBucket> People { get; set; } = [];
}

/// <summary>
/// A named count/percent bucket within a library stats breakdown.
/// </summary>
public class LibraryStatsBucket
{
    /// <summary>
    /// Gets or sets the display name for this bucket.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how many items fall in this bucket.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the share of the library total (0–100).
    /// </summary>
    public double Percent { get; set; }
}
