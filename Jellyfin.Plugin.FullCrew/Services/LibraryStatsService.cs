using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.FullCrew.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Aggregates Movie + Series library statistics with a short in-memory cache.
/// </summary>
public class LibraryStatsService
{
    private const int CacheMinutes = 5;
    private const int TopBucketLimit = 20;
    private const int TopPeopleLimit = 20;
    private const int MaxPeoplePerItem = 40;
    private const int SeriesEpisodeSampleLimit = 12;
    private const string CacheKeyPrefix = "fullcrew:library-stats:v3:";
    private const string AnimationGenre = "Animation";
    private const string OtherBucketName = "Other";
    private const string UnknownBucketName = "Unknown";
    private const string UnratedBucketName = "Unrated";

    private static readonly PersonKind[] PreferredPeopleRoleOrder =
    [
        PersonKind.Actor,
        PersonKind.Director,
        PersonKind.Writer,
        PersonKind.Creator,
        PersonKind.Producer,
        PersonKind.GuestStar,
        PersonKind.Composer,
        PersonKind.Editor,
        PersonKind.Artist,
        PersonKind.Author
    ];

    private static readonly string[] CommunityRatingBucketOrder =
    [
        UnratedBucketName,
        "<5",
        "5–6",
        "6–7",
        "7–8",
        "8–9",
        "9–10"
    ];

    private static readonly string[] ResolutionBucketOrder =
    [
        "480p",
        "720p",
        "1080p",
        "1440p",
        "4K/2160p",
        "8K",
        OtherBucketName,
        UnknownBucketName
    ];

    private static readonly string[] VideoRangeBucketOrder =
    [
        "SDR",
        "HDR10",
        "HDR10+",
        "HLG",
        "Dolby Vision",
        OtherBucketName,
        UnknownBucketName
    ];

    private readonly ILibraryManager _libraryManager;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<LibraryStatsService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryStatsService"/> class.
    /// </summary>
    public LibraryStatsService(
        ILibraryManager libraryManager,
        IMemoryCache memoryCache,
        ILogger<LibraryStatsService> logger)
    {
        _libraryManager = libraryManager;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    /// <summary>
    /// Gets aggregated library stats, optionally scoped to a user's visible library.
    /// </summary>
    /// <param name="user">The requesting user when available; otherwise library-wide.</param>
    /// <returns>Aggregated stats DTO.</returns>
    public LibraryStatsResponse GetStats(User? user)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is { EnableLibraryStats: false })
        {
            return EmptyResponse();
        }

        var cacheKey = CacheKeyPrefix + (user?.Id.ToString("N", CultureInfo.InvariantCulture) ?? "all");
        if (_memoryCache.TryGetValue(cacheKey, out LibraryStatsResponse? cached) && cached is not null)
        {
            return cached;
        }

        var response = BuildStats(user);
        _memoryCache.Set(cacheKey, response, TimeSpan.FromMinutes(CacheMinutes));
        return response;
    }

    private LibraryStatsResponse BuildStats(User? user)
    {
        try
        {
            var query = user is null
                ? new InternalItemsQuery()
                : new InternalItemsQuery(user);

            query.Recursive = true;
            query.IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series];
            query.IsVirtualItem = false;

            var items = _libraryManager.GetItemList(query)
                .Where(i => i is not null && !i.IsVirtualItem)
                .ToList();

            var movieCount = items.Count(i => i.GetBaseItemKind() == BaseItemKind.Movie);
            var seriesCount = items.Count(i => i.GetBaseItemKind() == BaseItemKind.Series);
            var total = items.Count;

            var genreCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var studioCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var ratingCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var decadeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var languageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var communityCounts = CommunityRatingBucketOrder.ToDictionary(
                name => name,
                _ => 0,
                StringComparer.Ordinal);
            var peopleByKind = new Dictionary<PersonKind, Dictionary<string, int>>();
            var resolutionCounts = ResolutionBucketOrder.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
            var videoRangeCounts = VideoRangeBucketOrder.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
            var videoCodecCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var audioChannelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var audioCodecCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var animationCount = 0;
            long movieRuntimeTotal = 0;
            var movieRuntimeSamples = 0;
            long seriesRuntimeTotal = 0;
            var seriesRuntimeSamples = 0;
            var mediaInfoSampleCount = 0;

            foreach (var item in items)
            {
                var kind = item.GetBaseItemKind();
                var itemGenres = item.Genres ?? [];
                if (itemGenres.Any(g => g.Equals(AnimationGenre, StringComparison.OrdinalIgnoreCase)))
                {
                    animationCount++;
                }

                foreach (var genre in itemGenres.Where(g => !string.IsNullOrWhiteSpace(g)))
                {
                    Increment(genreCounts, genre.Trim());
                }

                foreach (var studio in (item.Studios ?? []).Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    Increment(studioCounts, studio.Trim());
                }

                foreach (var tag in (item.Tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    Increment(tagCounts, tag.Trim());
                }

                var rating = string.IsNullOrWhiteSpace(item.OfficialRating)
                    ? UnknownBucketName
                    : item.OfficialRating.Trim();
                Increment(ratingCounts, rating);

                if (item.ProductionYear is int year && year >= 1000)
                {
                    var decadeStart = (year / 10) * 10;
                    Increment(decadeCounts, decadeStart.ToString(CultureInfo.InvariantCulture) + "s");
                }
                else
                {
                    Increment(decadeCounts, UnknownBucketName);
                }

                Increment(communityCounts, CommunityRatingBucket(item.CommunityRating));

                var language = item.PreferredMetadataLanguage;
                if (!string.IsNullOrWhiteSpace(language))
                {
                    Increment(languageCounts, language.Trim());
                }

                if (item.RunTimeTicks is > 0 and long ticks)
                {
                    if (kind == BaseItemKind.Movie)
                    {
                        movieRuntimeTotal += ticks;
                        movieRuntimeSamples++;
                    }
                    else if (kind == BaseItemKind.Series)
                    {
                        seriesRuntimeTotal += ticks;
                        seriesRuntimeSamples++;
                    }
                }

                if (item.SupportsPeople)
                {
                    AggregatePeople(item, peopleByKind);
                }

                if (TryAggregateMediaQuality(
                        item,
                        user,
                        resolutionCounts,
                        videoRangeCounts,
                        videoCodecCounts,
                        audioChannelCounts,
                        audioCodecCounts))
                {
                    mediaInfoSampleCount++;
                }
            }

            var animationPercent = Percent(animationCount, total);
            var types = BuildTypeBuckets(movieCount, seriesCount, total);
            // Rule B — multi-label: percent of total assignments (not title count).
            var genres = ToTopBuckets(genreCounts, SumCounts(genreCounts), TopBucketLimit);
            var studios = ToTopBuckets(studioCounts, SumCounts(studioCounts), TopBucketLimit);
            var tags = ToTopBuckets(tagCounts, SumCounts(tagCounts), TopBucketLimit);
            // Rule A — exclusive single-value: percent of titles (or media samples).
            var ratings = ToTopBuckets(ratingCounts, total, TopBucketLimit, foldUnknown: false);
            var decades = ToDecadeBuckets(decadeCounts, total);
            var communityRatings = ToOrderedBuckets(communityCounts, total, CommunityRatingBucketOrder);
            var languages = ToTopBuckets(languageCounts, total, TopBucketLimit, foldUnknown: false);
            var libraryItemIds = items.Select(i => i.Id).ToHashSet();
            var collections = BuildCollectionBuckets(user, libraryItemIds);
            // Rule C — people: percent of credits within that role (see BuildPeopleGroups).
            var peopleByRole = BuildPeopleGroups(peopleByKind);
            var mediaDenom = mediaInfoSampleCount;
            var resolutions = ToOrderedBuckets(resolutionCounts, mediaDenom, ResolutionBucketOrder);
            var videoRanges = ToOrderedBuckets(videoRangeCounts, mediaDenom, VideoRangeBucketOrder);
            var videoCodecs = ToTopBuckets(videoCodecCounts, mediaDenom, TopBucketLimit, foldUnknown: false);
            var audioChannels = ToTopBuckets(audioChannelCounts, mediaDenom, TopBucketLimit, foldUnknown: false);
            var audioCodecs = ToTopBuckets(audioCodecCounts, mediaDenom, TopBucketLimit, foldUnknown: false);

            var response = new LibraryStatsResponse
            {
                GeneratedAt = DateTime.UtcNow,
                MovieCount = movieCount,
                SeriesCount = seriesCount,
                TotalCount = total,
                AnimationPercent = animationPercent,
                MovieRuntimeTicksTotal = movieRuntimeTotal,
                MovieRuntimeTicksAverage = movieRuntimeSamples > 0 ? movieRuntimeTotal / movieRuntimeSamples : 0,
                MovieRuntimeSampleCount = movieRuntimeSamples,
                SeriesRuntimeTicksTotal = seriesRuntimeTotal,
                SeriesRuntimeTicksAverage = seriesRuntimeSamples > 0 ? seriesRuntimeTotal / seriesRuntimeSamples : 0,
                SeriesRuntimeSampleCount = seriesRuntimeSamples,
                Types = types,
                Genres = genres,
                Studios = studios,
                OfficialRatings = ratings,
                Decades = decades,
                CommunityRatings = communityRatings,
                Tags = tags,
                Languages = languages,
                Collections = collections,
                MediaInfoSampleCount = mediaInfoSampleCount,
                Resolutions = resolutions,
                VideoRanges = videoRanges,
                VideoCodecs = videoCodecs,
                AudioChannels = audioChannels,
                AudioCodecs = audioCodecs,
                PeopleByRole = peopleByRole,
                Insights = BuildInsights(
                    movieCount,
                    seriesCount,
                    total,
                    animationPercent,
                    genres,
                    studios,
                    ratings,
                    decades,
                    communityRatings,
                    peopleByRole,
                    movieRuntimeSamples,
                    movieRuntimeTotal,
                    tags,
                    languages,
                    collections,
                    mediaInfoSampleCount,
                    resolutions,
                    videoRanges,
                    videoCodecs)
            };

            _logger.LogDebug(
                "Library stats built for user {UserId}: {Total} items ({Movies} movies, {Series} series, {Roles} people roles, {MediaSamples} media samples)",
                user?.Id,
                total,
                movieCount,
                seriesCount,
                peopleByRole.Count,
                mediaInfoSampleCount);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build library stats");
            return EmptyResponse();
        }
    }

    /// <summary>
    /// Samples primary video (+ default audio) for one Movie/Series title.
    /// Movies use the item's MediaStreams / GetDefaultVideoStream.
    /// Series sample up to <see cref="SeriesEpisodeSampleLimit"/> early episodes
    /// (season then episode index) and take the first with a video stream —
    /// never every episode.
    /// </summary>
    private bool TryAggregateMediaQuality(
        BaseItem item,
        User? user,
        IDictionary<string, int> resolutionCounts,
        IDictionary<string, int> videoRangeCounts,
        IDictionary<string, int> videoCodecCounts,
        IDictionary<string, int> audioChannelCounts,
        IDictionary<string, int> audioCodecCounts)
    {
        try
        {
            var kind = item.GetBaseItemKind();
            IReadOnlyList<MediaStream>? streams = null;
            MediaStream? video = null;

            if (kind == BaseItemKind.Movie)
            {
                streams = SafeGetMediaStreams(item);
                video = PickPrimaryVideoStream(item, streams);
            }
            else if (kind == BaseItemKind.Series)
            {
                (streams, video) = SampleSeriesMediaStreams(item, user);
            }

            if (video is null)
            {
                return false;
            }

            Increment(resolutionCounts, ResolutionBucket(video.Width, video.Height));
            Increment(videoRangeCounts, VideoRangeBucket(video.VideoRangeType));
            Increment(videoCodecCounts, NormalizeVideoCodec(video.Codec));

            var audio = PickPrimaryAudioStream(streams);
            if (audio is not null)
            {
                Increment(audioChannelCounts, AudioChannelBucket(audio));
                var audioCodec = NormalizeAudioCodec(audio.Codec);
                if (!string.IsNullOrWhiteSpace(audioCodec))
                {
                    Increment(audioCodecCounts, audioCodec);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping media quality for item {ItemId}", item.Id);
            return false;
        }
    }

    private (IReadOnlyList<MediaStream>? Streams, MediaStream? Video) SampleSeriesMediaStreams(
        BaseItem series,
        User? user)
    {
        var query = user is null
            ? new InternalItemsQuery()
            : new InternalItemsQuery(user);

        query.ParentId = series.Id;
        query.Recursive = true;
        query.IncludeItemTypes = [BaseItemKind.Episode];
        query.IsVirtualItem = false;
        query.Limit = SeriesEpisodeSampleLimit;
        query.OrderBy =
        [
            (ItemSortBy.ParentIndexNumber, SortOrder.Ascending),
            (ItemSortBy.IndexNumber, SortOrder.Ascending)
        ];

        List<BaseItem> episodes;
        try
        {
            episodes = _libraryManager.GetItemList(query)
                .Where(e => e is not null && !e.IsVirtualItem)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping episode sample for series {SeriesId}", series.Id);
            return (null, null);
        }

        foreach (var episode in episodes)
        {
            var streams = SafeGetMediaStreams(episode);
            var video = PickPrimaryVideoStream(episode, streams);
            if (video is not null)
            {
                return (streams, video);
            }
        }

        return (null, null);
    }

    private IReadOnlyList<MediaStream>? SafeGetMediaStreams(BaseItem item)
    {
        try
        {
            return item.GetMediaStreams();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetMediaStreams failed for item {ItemId}", item.Id);
            return null;
        }
    }

    private static MediaStream? PickPrimaryVideoStream(BaseItem item, IReadOnlyList<MediaStream>? streams)
    {
        if (streams is null || streams.Count == 0)
        {
            return null;
        }

        if (item is Video { DefaultVideoStreamIndex: int index })
        {
            var byIndex = streams.FirstOrDefault(s =>
                s.Type == MediaStreamType.Video && s.Index == index);
            if (byIndex is not null)
            {
                return byIndex;
            }
        }

        return streams
            .Where(s => s.Type == MediaStreamType.Video)
            .OrderByDescending(s => s.IsDefault)
            .ThenByDescending(s => s.Width ?? 0)
            .ThenByDescending(s => s.Height ?? 0)
            .FirstOrDefault();
    }

    private static MediaStream? PickPrimaryAudioStream(IReadOnlyList<MediaStream>? streams)
    {
        if (streams is null || streams.Count == 0)
        {
            return null;
        }

        return streams
            .Where(s => s.Type == MediaStreamType.Audio)
            .OrderByDescending(s => s.IsDefault)
            .ThenByDescending(s => s.Channels ?? 0)
            .FirstOrDefault();
    }

    private static string ResolutionBucket(int? width, int? height)
    {
        var h = height ?? 0;
        if (h <= 0 && (width is null or <= 0))
        {
            return UnknownBucketName;
        }

        if (h <= 0 && width is > 0)
        {
            // Portrait / missing height: approximate from width assuming 16:9.
            h = (int)Math.Round(width.Value * 9.0 / 16.0);
        }

        if (h >= 4200)
        {
            return "8K";
        }

        if (h >= 2000)
        {
            return "4K/2160p";
        }

        if (h >= 1300)
        {
            return "1440p";
        }

        if (h >= 900)
        {
            return "1080p";
        }

        if (h >= 600)
        {
            return "720p";
        }

        if (h >= 1)
        {
            return "480p";
        }

        return OtherBucketName;
    }

    private static string VideoRangeBucket(VideoRangeType rangeType)
        => rangeType switch
        {
            VideoRangeType.SDR => "SDR",
            VideoRangeType.HDR10 => "HDR10",
            VideoRangeType.HDR10Plus => "HDR10+",
            VideoRangeType.HLG => "HLG",
            VideoRangeType.DOVI
                or VideoRangeType.DOVIWithHDR10
                or VideoRangeType.DOVIWithHLG
                or VideoRangeType.DOVIWithSDR
                or VideoRangeType.DOVIWithEL
                or VideoRangeType.DOVIWithHDR10Plus
                or VideoRangeType.DOVIWithELHDR10Plus
                or VideoRangeType.DOVIInvalid => "Dolby Vision",
            VideoRangeType.Unknown => UnknownBucketName,
            _ => OtherBucketName
        };

    private static string NormalizeVideoCodec(string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec))
        {
            return UnknownBucketName;
        }

        var c = codec.Trim().ToLowerInvariant();
        return c switch
        {
            "h264" or "avc" or "avc1" or "x264" => "H.264",
            "hevc" or "h265" or "hvc1" or "hev1" or "x265" => "HEVC",
            "av1" or "av01" => "AV1",
            "vp9" or "vp09" => "VP9",
            "vp8" => "VP8",
            "mpeg2video" or "mpeg2" => "MPEG-2",
            "mpeg4" => "MPEG-4",
            "vc1" or "wmv3" => "VC-1",
            "mpeg1video" => "MPEG-1",
            "msmpeg4v3" or "msmpeg4" => "MS-MPEG4",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(c)
        };
    }

    private static string NormalizeAudioCodec(string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec))
        {
            return string.Empty;
        }

        var c = codec.Trim().ToLowerInvariant();
        return c switch
        {
            "aac" => "AAC",
            "ac3" => "AC3",
            "eac3" => "EAC3",
            "truehd" => "TrueHD",
            "dts" => "DTS",
            "dts-hd" or "dtshd" or "dca" => "DTS",
            "flac" => "FLAC",
            "pcm" or "pcm_s16le" or "pcm_s24le" or "pcm_bluray" => "PCM",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "mp3" => "MP3",
            "mp2" => "MP2",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(c)
        };
    }

    private static string AudioChannelBucket(MediaStream audio)
    {
        if (audio.AudioSpatialFormat == AudioSpatialFormat.DolbyAtmos
            || ContainsIgnoreCase(audio.ChannelLayout, "atmos")
            || ContainsIgnoreCase(audio.Title, "atmos")
            || ContainsIgnoreCase(audio.Profile, "atmos")
            || ContainsIgnoreCase(audio.DisplayTitle, "atmos"))
        {
            return "Atmos";
        }

        if (audio.AudioSpatialFormat == AudioSpatialFormat.DTSX
            || ContainsIgnoreCase(audio.ChannelLayout, "dts:x")
            || ContainsIgnoreCase(audio.Title, "dts:x")
            || ContainsIgnoreCase(audio.DisplayTitle, "dts:x"))
        {
            return "DTS:X";
        }

        if (audio.Channels is int channels and > 0)
        {
            return channels switch
            {
                1 => "Mono",
                2 => "Stereo",
                6 => "5.1",
                8 => "7.1",
                _ => channels.ToString(CultureInfo.InvariantCulture) + " ch"
            };
        }

        if (!string.IsNullOrWhiteSpace(audio.ChannelLayout))
        {
            return audio.ChannelLayout.Trim();
        }

        return UnknownBucketName;
    }

    private static bool ContainsIgnoreCase(string? haystack, string needle)
        => !string.IsNullOrWhiteSpace(haystack)
           && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds collection size buckets from Jellyfin BoxSets (native collections).
    /// Count = Movie/Series members visible in the scoped library; Percent = share of
    /// all such memberships across collections (not library TotalCount).
    /// </summary>
    private IReadOnlyList<LibraryStatsBucket> BuildCollectionBuckets(User? user, HashSet<Guid> libraryItemIds)
    {
        if (libraryItemIds.Count == 0)
        {
            return [];
        }

        try
        {
            var query = user is null
                ? new InternalItemsQuery()
                : new InternalItemsQuery(user);

            query.Recursive = true;
            query.IncludeItemTypes = [BaseItemKind.BoxSet];
            query.IsVirtualItem = false;

            var boxSets = _libraryManager.GetItemList(query)
                .Where(i => i is not null && !i.IsVirtualItem && !string.IsNullOrWhiteSpace(i.Name))
                .ToList();

            if (boxSets.Count == 0)
            {
                return [];
            }

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var boxSet in boxSets)
            {
                var memberCount = CountCollectionMembers(boxSet, libraryItemIds);
                if (memberCount <= 0)
                {
                    continue;
                }

                var name = boxSet.Name.Trim();
                if (counts.TryGetValue(name, out var existing))
                {
                    counts[name] = existing + memberCount;
                }
                else
                {
                    counts[name] = memberCount;
                }
            }

            var membershipTotal = counts.Values.Sum();
            return ToTopBuckets(counts, membershipTotal, TopBucketLimit, foldUnknown: false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping collection stats");
            return [];
        }
    }

    private int CountCollectionMembers(BaseItem boxSet, HashSet<Guid> libraryItemIds)
    {
        // Prefer LinkedChildren ItemIds — already on the BoxSet, no per-member lookup.
        if (boxSet is Folder folder && folder.LinkedChildren is { Length: > 0 } linked)
        {
            var seen = new HashSet<Guid>();
            var count = 0;
            foreach (var child in linked)
            {
                if (child.ItemId is not Guid id || id == Guid.Empty || !seen.Add(id))
                {
                    continue;
                }

                if (libraryItemIds.Contains(id))
                {
                    count++;
                }
            }

            return count;
        }

        // Legacy folder-style boxsets with no LinkedChildren: one ParentId query.
        try
        {
            var children = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = boxSet.Id,
                Recursive = true,
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
                IsVirtualItem = false
            });

            return children.Count(c => c is not null && libraryItemIds.Contains(c.Id));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping members for collection {CollectionId}", boxSet.Id);
            return 0;
        }
    }

    private void AggregatePeople(
        BaseItem item,
        IDictionary<PersonKind, Dictionary<string, int>> peopleByKind)
    {
        IReadOnlyList<PersonInfo> people;
        try
        {
            people = _libraryManager.GetPeople(item);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping people for item {ItemId}", item.Id);
            return;
        }

        if (people is null || people.Count == 0)
        {
            return;
        }

        // Cap per item so huge cast lists (and GuestStar spam) don't dominate runtime.
        foreach (var person in people.Take(MaxPeoplePerItem))
        {
            if (string.IsNullOrWhiteSpace(person.Name))
            {
                continue;
            }

            var type = person.Type;
            if (!peopleByKind.TryGetValue(type, out var map))
            {
                map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                peopleByKind[type] = map;
            }

            Increment(map, person.Name.Trim());
        }
    }

    private static IReadOnlyList<LibraryStatsPeopleGroup> BuildPeopleGroups(
        IReadOnlyDictionary<PersonKind, Dictionary<string, int>> peopleByKind)
    {
        if (peopleByKind.Count == 0)
        {
            return [];
        }

        var preferred = PreferredPeopleRoleOrder
            .Where(peopleByKind.ContainsKey)
            .ToList();
        var remaining = peopleByKind.Keys
            .Where(k => !preferred.Contains(k) && k != PersonKind.Unknown)
            .OrderBy(FormatPersonKind)
            .ToList();
        if (peopleByKind.ContainsKey(PersonKind.Unknown))
        {
            remaining.Add(PersonKind.Unknown);
        }

        var groups = new List<LibraryStatsPeopleGroup>(preferred.Count + remaining.Count);
        foreach (var kind in preferred.Concat(remaining))
        {
            var map = peopleByKind[kind];
            // Denominator = sum of appearances in this role only (never library TotalCount).
            var roleTotal = map.Values.Sum();
            if (roleTotal <= 0)
            {
                continue;
            }

            // Keep "Other" so top-N + Other percents sum to ~100% of this role's credits.
            var buckets = ToTopBuckets(map, roleTotal, TopPeopleLimit, foldUnknown: false);
            if (buckets.Count == 0)
            {
                continue;
            }

            groups.Add(new LibraryStatsPeopleGroup
            {
                Role = FormatPersonKind(kind),
                Kind = kind.ToString(),
                People = buckets
            });
        }

        return groups;
    }

    private static string FormatPersonKind(PersonKind kind)
        => kind switch
        {
            PersonKind.GuestStar => "Guest Star",
            PersonKind.AlbumArtist => "Album Artist",
            PersonKind.CoverArtist => "Cover Artist",
            _ => kind.ToString()
        };

    private static string CommunityRatingBucket(float? rating)
    {
        if (rating is null or <= 0)
        {
            return UnratedBucketName;
        }

        var value = rating.Value;
        if (value < 5f)
        {
            return "<5";
        }

        if (value < 6f)
        {
            return "5–6";
        }

        if (value < 7f)
        {
            return "6–7";
        }

        if (value < 8f)
        {
            return "7–8";
        }

        if (value < 9f)
        {
            return "8–9";
        }

        return "9–10";
    }

    private static LibraryStatsResponse EmptyResponse()
        => new()
        {
            GeneratedAt = DateTime.UtcNow,
            Insights = []
        };

    private static void Increment(IDictionary<string, int> map, string key)
    {
        if (map.TryGetValue(key, out var current))
        {
            map[key] = current + 1;
        }
        else
        {
            map[key] = 1;
        }
    }

    private static int SumCounts(IReadOnlyDictionary<string, int> counts)
        => counts.Count == 0 ? 0 : counts.Values.Sum();

    private static double Percent(int count, int total)
    {
        if (total <= 0 || count <= 0)
        {
            return 0;
        }

        return Math.Round(count * 100.0 / total, 1, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<LibraryStatsBucket> BuildTypeBuckets(int movieCount, int seriesCount, int total)
    {
        var buckets = new List<LibraryStatsBucket>(2);
        if (movieCount > 0)
        {
            buckets.Add(new LibraryStatsBucket
            {
                Name = "Movie",
                Count = movieCount,
                Percent = Percent(movieCount, total)
            });
        }

        if (seriesCount > 0)
        {
            buckets.Add(new LibraryStatsBucket
            {
                Name = "Series",
                Count = seriesCount,
                Percent = Percent(seriesCount, total)
            });
        }

        return buckets;
    }

    private static IReadOnlyList<LibraryStatsBucket> ToTopBuckets(
        IReadOnlyDictionary<string, int> counts,
        int total,
        int limit,
        bool foldUnknown = true)
    {
        if (counts.Count == 0 || total <= 0)
        {
            return [];
        }

        var ordered = counts
            .Where(kv => foldUnknown
                ? !kv.Key.Equals(UnknownBucketName, StringComparison.OrdinalIgnoreCase)
                : true)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var top = ordered.Take(limit).ToList();
        var otherCount = ordered.Skip(limit).Sum(kv => kv.Value);

        if (foldUnknown)
        {
            otherCount += counts
                .Where(kv => kv.Key.Equals(UnknownBucketName, StringComparison.OrdinalIgnoreCase))
                .Sum(kv => kv.Value);
        }

        var buckets = top
            .Select(kv => new LibraryStatsBucket
            {
                Name = kv.Key,
                Count = kv.Value,
                Percent = Percent(kv.Value, total)
            })
            .ToList();

        if (otherCount > 0)
        {
            buckets.Add(new LibraryStatsBucket
            {
                Name = OtherBucketName,
                Count = otherCount,
                Percent = Percent(otherCount, total)
            });
        }

        return buckets;
    }

    private static IReadOnlyList<LibraryStatsBucket> ToOrderedBuckets(
        IReadOnlyDictionary<string, int> counts,
        int total,
        IReadOnlyList<string> order)
    {
        if (total <= 0)
        {
            return [];
        }

        return order
            .Where(name => counts.TryGetValue(name, out var count) && count > 0)
            .Select(name => new LibraryStatsBucket
            {
                Name = name,
                Count = counts[name],
                Percent = Percent(counts[name], total)
            })
            .ToList();
    }

    private static IReadOnlyList<LibraryStatsBucket> ToDecadeBuckets(
        IReadOnlyDictionary<string, int> counts,
        int total)
    {
        if (counts.Count == 0 || total <= 0)
        {
            return [];
        }

        return counts
            .OrderBy(kv => DecadeSortKey(kv.Key))
            .Select(kv => new LibraryStatsBucket
            {
                Name = kv.Key,
                Count = kv.Value,
                Percent = Percent(kv.Value, total)
            })
            .ToList();
    }

    private static int DecadeSortKey(string name)
    {
        if (name.Equals(UnknownBucketName, StringComparison.OrdinalIgnoreCase))
        {
            return int.MaxValue;
        }

        if (name.Length >= 4
            && int.TryParse(name.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
        {
            return year;
        }

        return int.MaxValue - 1;
    }

    private static IReadOnlyList<string> BuildInsights(
        int movieCount,
        int seriesCount,
        int total,
        double animationPercent,
        IReadOnlyList<LibraryStatsBucket> genres,
        IReadOnlyList<LibraryStatsBucket> studios,
        IReadOnlyList<LibraryStatsBucket> ratings,
        IReadOnlyList<LibraryStatsBucket> decades,
        IReadOnlyList<LibraryStatsBucket> communityRatings,
        IReadOnlyList<LibraryStatsPeopleGroup> peopleByRole,
        int movieRuntimeSamples,
        long movieRuntimeTotal,
        IReadOnlyList<LibraryStatsBucket> tags,
        IReadOnlyList<LibraryStatsBucket> languages,
        IReadOnlyList<LibraryStatsBucket> collections,
        int mediaInfoSampleCount,
        IReadOnlyList<LibraryStatsBucket> resolutions,
        IReadOnlyList<LibraryStatsBucket> videoRanges,
        IReadOnlyList<LibraryStatsBucket> videoCodecs)
    {
        if (total <= 0)
        {
            return ["Your library has no movies or series to summarize yet."];
        }

        var insights = new List<string>(12)
        {
            string.Format(
                CultureInfo.InvariantCulture,
                "Your library has {0} titles ({1} movies, {2} series).",
                total,
                movieCount,
                seriesCount)
        };

        if (movieCount > 0 || seriesCount > 0)
        {
            var moviePct = Percent(movieCount, total);
            var seriesPct = Percent(seriesCount, total);
            if (moviePct >= seriesPct)
            {
                insights.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Movies make up {0}% of the library; series are {1}%.",
                    moviePct,
                    seriesPct));
            }
            else
            {
                insights.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Series make up {0}% of the library; movies are {1}%.",
                    seriesPct,
                    moviePct));
            }
        }

        var topResolution = FirstNamed(resolutions, allowUnknown: false);
        if (topResolution is not null && mediaInfoSampleCount > 0)
        {
            insights.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Most common resolution is {0} ({1}% of {2} titles with media info).",
                topResolution.Name,
                topResolution.Percent,
                mediaInfoSampleCount));
        }

        if (mediaInfoSampleCount > 0 && videoRanges.Count > 0)
        {
            var hdrShare = videoRanges
                .Where(b => !b.Name.Equals("SDR", StringComparison.OrdinalIgnoreCase)
                            && !b.Name.Equals(UnknownBucketName, StringComparison.OrdinalIgnoreCase)
                            && !b.Name.Equals(OtherBucketName, StringComparison.OrdinalIgnoreCase))
                .Sum(b => b.Count);
            var hdrPercent = Percent(hdrShare, mediaInfoSampleCount);
            if (hdrPercent > 0)
            {
                insights.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}% of titles with media info are HDR (HDR10 / HDR10+ / HLG / Dolby Vision).",
                    hdrPercent));
            }
            else
            {
                var sdr = videoRanges.FirstOrDefault(b =>
                    b.Name.Equals("SDR", StringComparison.OrdinalIgnoreCase));
                if (sdr is not null && sdr.Percent >= 50)
                {
                    insights.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}% of titles with media info are SDR.",
                        sdr.Percent));
                }
            }
        }

        var topCodec = FirstNamed(videoCodecs, allowUnknown: false);
        if (topCodec is not null && mediaInfoSampleCount > 0 && insights.Count < 12)
        {
            insights.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Most common video codec is {0} ({1}%).",
                topCodec.Name,
                topCodec.Percent));
        }

        var topGenre = FirstNamed(genres);
        if (topGenre is not null)
        {
            insights.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Most common genre tag is {0} ({1}% of genre tags).",
                topGenre.Name,
                topGenre.Percent));
        }

        var topStudio = FirstNamed(studios);
        if (topStudio is not null)
        {
            insights.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0}% of studio credits are {1}.",
                topStudio.Percent,
                topStudio.Name));
        }

        var topCollection = FirstNamed(collections);
        if (topCollection is not null && topCollection.Count >= 2)
        {
            insights.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0} is your largest collection with {1} titles.",
                topCollection.Name,
                topCollection.Count));
        }

        AddTopPersonInsight(insights, peopleByRole, "Actor");
        AddTopPersonInsight(insights, peopleByRole, "Director");
        AddTopPersonInsight(insights, peopleByRole, "Writer");
        AddTopPersonInsight(insights, peopleByRole, "Creator");
        AddTopPersonInsight(insights, peopleByRole, "Producer");

        if (animationPercent > 0)
        {
            insights.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0}% of titles are tagged Animation.",
                animationPercent));
        }

        var topCommunity = communityRatings
            .Where(b => !b.Name.Equals(UnratedBucketName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(b => b.Count)
            .FirstOrDefault();
        if (topCommunity is not null)
        {
            insights.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Most titles with a community score fall in {0} ({1}%).",
                topCommunity.Name,
                topCommunity.Percent));
        }

        if (movieRuntimeSamples > 0 && movieRuntimeTotal > 0)
        {
            var avgMinutes = (movieRuntimeTotal / movieRuntimeSamples) / TimeSpan.TicksPerMinute;
            insights.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Average movie runtime is about {0} minutes ({1} movies with runtime data).",
                avgMinutes,
                movieRuntimeSamples));
        }

        var topTag = FirstNamed(tags);
        if (topTag is not null && insights.Count < 12)
        {
            insights.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Most used tag is {0} ({1}% of tag assignments).",
                topTag.Name,
                topTag.Percent));
        }

        var topLanguage = FirstNamed(languages);
        if (topLanguage is not null && insights.Count < 12)
        {
            insights.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Preferred metadata language is most often {0} ({1}%).",
                topLanguage.Name,
                topLanguage.Percent));
        }

        var topRating = FirstNamed(ratings, allowUnknown: false);
        if (topRating is not null && insights.Count < 12)
        {
            insights.Add(string.Format(
                CultureInfo.InvariantCulture,
                "The most common official rating is {0} ({1}%).",
                topRating.Name,
                topRating.Percent));
        }

        var topDecade = decades
            .Where(d => !d.Name.Equals(UnknownBucketName, StringComparison.OrdinalIgnoreCase)
                        && !d.Name.Equals(OtherBucketName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.Count)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (topDecade is not null && insights.Count < 12)
        {
            insights.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Most titles are from the {0} ({1}%).",
                topDecade.Name,
                topDecade.Percent));
        }

        return insights.Take(12).ToList();
    }

    private static void AddTopPersonInsight(
        ICollection<string> insights,
        IReadOnlyList<LibraryStatsPeopleGroup> peopleByRole,
        string role)
    {
        if (insights.Count >= 12)
        {
            return;
        }

        var group = peopleByRole.FirstOrDefault(g =>
            g.Role.Equals(role, StringComparison.OrdinalIgnoreCase)
            || g.Kind.Equals(role, StringComparison.OrdinalIgnoreCase));
        var top = group?.People.FirstOrDefault(b =>
            !b.Name.Equals(OtherBucketName, StringComparison.OrdinalIgnoreCase));
        if (top is null || top.Count < 2)
        {
            return;
        }

        var roleLabel = string.IsNullOrWhiteSpace(group!.Role) ? role.ToLowerInvariant() : group.Role.ToLowerInvariant();
        insights.Add(string.Format(
            CultureInfo.InvariantCulture,
            "{0} is {1}% of {2} credits ({3}).",
            top.Name,
            top.Percent,
            roleLabel,
            top.Count));
    }

    private static LibraryStatsBucket? FirstNamed(
        IReadOnlyList<LibraryStatsBucket> buckets,
        bool allowUnknown = true)
    {
        return buckets.FirstOrDefault(b =>
            !b.Name.Equals(OtherBucketName, StringComparison.OrdinalIgnoreCase)
            && (allowUnknown || !b.Name.Equals(UnknownBucketName, StringComparison.OrdinalIgnoreCase)));
    }
}
