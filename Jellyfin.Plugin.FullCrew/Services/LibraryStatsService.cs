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
    private const int CacheMinutes = 30;
    private const int TopBucketLimit = 25;
    private const int TopPeopleLimit = 25;
    /// <summary>Per-title people cap for movies (keeps guest lists from exploding).</summary>
    private const int MaxPeoplePerMovie = 60;
    /// <summary>Per-series people cap after series+episode merge (once per person/kind).</summary>
    private const int MaxPeoplePerSeries = 200;
    private const int MaxCategoryBuckets = 2000;
    /// <summary>Safety cap for bucket item lists.</summary>
    private const int MaxBucketItems = 20000;
    private const int SeriesEpisodeSampleLimit = 12;
    /// <summary>Episodes sampled to fill series cast (always merged, still once per series).</summary>
    private const int SeriesPeopleEpisodeSampleLimit = 24;
    private const int MaxInsights = 14;
    /// <summary>When Other would exceed this share on overview charts, omit it (detail page still has full list).</summary>
    private const double OmitOtherPercentThreshold = 40.0;
    private const string CacheKeyPrefix = "fullcrew:library-stats:v10:";
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

        var aggregate = GetOrBuildAggregate(user);
        return ProjectOverview(aggregate, ResolveYearSort(config?.LibraryStatsYearSort));
    }

    /// <summary>
    /// Gets the full ranked list for one stats category (detail page).
    /// </summary>
    /// <param name="user">The requesting user when available; otherwise library-wide.</param>
    /// <param name="category">Category key such as actors, genres, hdr.</param>
    /// <returns>Category detail DTO, or null when the key is unknown.</returns>
    public LibraryStatsCategoryResponse? GetCategoryStats(User? user, string category)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is { EnableLibraryStats: false })
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        var aggregate = GetOrBuildAggregate(user);
        return ProjectCategory(aggregate, category);
    }

    /// <summary>
    /// Gets every Movie/Series title that contributes to one stats bucket.
    /// </summary>
    public LibraryStatsBucketItemsResponse? GetBucketItems(User? user, string category, string? bucket)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is { EnableLibraryStats: false })
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(bucket))
        {
            return null;
        }

        var aggregate = GetOrBuildAggregate(user);
        var catInfo = ProjectCategory(aggregate, category);
        if (catInfo is null)
        {
            return null;
        }

        var catKey = NormalizeCategoryKey(catInfo.Category);
        if (string.IsNullOrEmpty(catKey))
        {
            catKey = NormalizeCategoryKey(category);
        }

        IReadOnlyList<BucketItemRef> matches = [];
        if (aggregate.BucketItems.TryGetValue(catKey, out var byBucket))
        {
            List<BucketItemRef>? list = null;
            if (!byBucket.TryGetValue(bucket.Trim(), out list))
            {
                foreach (var kv in byBucket)
                {
                    if (kv.Key.Equals(bucket.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        list = kv.Value;
                        break;
                    }
                }
            }

            if (list is { Count: > 0 })
            {
                matches = list;
            }
        }

        var ordered = matches
            .OrderByDescending(i => i.Year ?? 0)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalCount = ordered.Count;
        var truncated = totalCount > MaxBucketItems;
        if (truncated)
        {
            ordered = ordered.Take(MaxBucketItems).ToList();
        }

        return new LibraryStatsBucketItemsResponse
        {
            Category = catInfo.Category,
            CategoryTitle = catInfo.Title,
            Bucket = bucket.Trim(),
            GeneratedAt = aggregate.GeneratedAt,
            TotalCount = totalCount,
            Truncated = truncated,
            Items = ordered.Select(ToBucketItemDto).ToList()
        };
    }

    private LibraryStatsAggregate GetOrBuildAggregate(User? user)
    {
        var cacheKey = CacheKeyPrefix + (user?.Id.ToString("N", CultureInfo.InvariantCulture) ?? "all");
        if (_memoryCache.TryGetValue(cacheKey, out LibraryStatsAggregate? cached) && cached is not null)
        {
            return cached;
        }

        var aggregate = BuildAggregate(user);
        _memoryCache.Set(cacheKey, aggregate, TimeSpan.FromMinutes(CacheMinutes));
        return aggregate;
    }

    private LibraryStatsAggregate BuildAggregate(User? user)
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
            var yearCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var languageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var locationCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var communityCounts = CommunityRatingBucketOrder.ToDictionary(
                name => name,
                _ => 0,
                StringComparer.Ordinal);
            var peopleByKind = new Dictionary<PersonKind, Dictionary<string, int>>();
            var personIdsFromCredits = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var resolutionCounts = ResolutionBucketOrder.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
            var videoRangeCounts = VideoRangeBucketOrder.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
            var videoCodecCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var audioChannelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var audioCodecCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var productionYears = new List<int>(items.Count);

            var animationCount = 0;
            long movieRuntimeTotal = 0;
            var movieRuntimeSamples = 0;
            long seriesRuntimeTotal = 0;
            var seriesRuntimeSamples = 0;
            var mediaInfoSampleCount = 0;
            TitleYearFact? oldestMovie = null;
            TitleYearFact? newestMovie = null;
            SeriesSpanFact? longestSeries = null;

            var libraryItemIds = items.Select(i => i.Id).ToHashSet();
            var (collectionCounts, collectionItemIds, collectionMemberships) = BuildCollectionData(user, libraryItemIds);
            var actorCollectionSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var bucketItems = new Dictionary<string, Dictionary<string, List<BucketItemRef>>>(StringComparer.Ordinal);

            foreach (var item in items)
            {
                var kind = item.GetBaseItemKind();
                var itemRef = ToBucketItemRef(item);
                RecordBucketItem(bucketItems, "types", kind == BaseItemKind.Series ? "Series" : "Movie", itemRef);

                var itemGenres = item.Genres ?? [];
                if (itemGenres.Any(g => g.Equals(AnimationGenre, StringComparison.OrdinalIgnoreCase)))
                {
                    animationCount++;
                }

                foreach (var genre in itemGenres.Where(g => !string.IsNullOrWhiteSpace(g)))
                {
                    var g = genre.Trim();
                    Increment(genreCounts, g);
                    RecordBucketItem(bucketItems, "genres", g, itemRef);
                }

                foreach (var studio in (item.Studios ?? []).Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    var s = studio.Trim();
                    Increment(studioCounts, s);
                    RecordBucketItem(bucketItems, "studios", s, itemRef);
                }

                foreach (var tag in (item.Tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    var t = tag.Trim();
                    Increment(tagCounts, t);
                    RecordBucketItem(bucketItems, "tags", t, itemRef);
                }

                foreach (var location in (item.ProductionLocations ?? []).Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    Increment(locationCounts, location.Trim());
                }

                var rating = string.IsNullOrWhiteSpace(item.OfficialRating)
                    ? UnknownBucketName
                    : item.OfficialRating.Trim();
                Increment(ratingCounts, rating);
                RecordBucketItem(bucketItems, "ratings", rating, itemRef);

                if (item.ProductionYear is int year && year >= 1000)
                {
                    productionYears.Add(year);
                    var decadeLabel = ((year / 10) * 10).ToString(CultureInfo.InvariantCulture) + "s";
                    Increment(decadeCounts, decadeLabel);
                    RecordBucketItem(bucketItems, "decades", decadeLabel, itemRef);

                    var yearLabel = year.ToString(CultureInfo.InvariantCulture);
                    Increment(yearCounts, yearLabel);
                    RecordBucketItem(bucketItems, "years", yearLabel, itemRef);
                }
                else
                {
                    Increment(decadeCounts, UnknownBucketName);
                    RecordBucketItem(bucketItems, "decades", UnknownBucketName, itemRef);
                    Increment(yearCounts, UnknownBucketName);
                    RecordBucketItem(bucketItems, "years", UnknownBucketName, itemRef);
                }

                var communityBucket = CommunityRatingBucket(item.CommunityRating);
                Increment(communityCounts, communityBucket);
                RecordBucketItem(bucketItems, "community", communityBucket, itemRef);

                var language = item.PreferredMetadataLanguage;
                if (!string.IsNullOrWhiteSpace(language))
                {
                    var lang = language.Trim();
                    Increment(languageCounts, lang);
                    RecordBucketItem(bucketItems, "languages", lang, itemRef);
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

                if (kind == BaseItemKind.Movie)
                {
                    TrackMovieYearFacts(item, ref oldestMovie, ref newestMovie);
                }
                else if (kind == BaseItemKind.Series)
                {
                    TrackLongestSeries(item, ref longestSeries);
                }

                if (item.SupportsPeople || kind is BaseItemKind.Movie or BaseItemKind.Series)
                {
                    AggregatePeople(item, user, peopleByKind, personIdsFromCredits, bucketItems, itemRef);

                    if (collectionMemberships.TryGetValue(item.Id, out var memberOfForActors)
                        && memberOfForActors.Count > 0)
                    {
                        AggregateActorCollections(item, user, memberOfForActors, actorCollectionSets);
                    }
                }

                if (collectionMemberships.TryGetValue(item.Id, out var memberOf)
                    && memberOf.Count > 0)
                {
                    foreach (var collectionName in memberOf)
                    {
                        RecordBucketItem(bucketItems, "collections", collectionName, itemRef);
                    }
                }

                if (TryAggregateMediaQuality(
                        item,
                        user,
                        resolutionCounts,
                        videoRangeCounts,
                        videoCodecCounts,
                        audioChannelCounts,
                        audioCodecCounts,
                        bucketItems,
                        itemRef))
                {
                    mediaInfoSampleCount++;
                }
            }

            var topActorByCollections = actorCollectionSets
                .Select(kv => (Name: kv.Key, Count: kv.Value.Count))
                .Where(x => x.Count >= 2)
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            var personNames = peopleByKind.Values
                .SelectMany(m => m.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // PersonInfo.Id from credits is a strong default; library Person items overwrite
            // when present so navigation targets the real BaseItem id Jellyfin Web expects.
            var personItemIds = new Dictionary<string, Guid>(personIdsFromCredits, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in BuildPersonItemIdMap(personNames))
            {
                if (kv.Value != Guid.Empty)
                {
                    personItemIds[kv.Key] = kv.Value;
                }
            }

            var aggregate = new LibraryStatsAggregate
            {
                GeneratedAt = DateTime.UtcNow,
                MovieCount = movieCount,
                SeriesCount = seriesCount,
                TotalCount = total,
                AnimationPercent = Percent(animationCount, total),
                MovieRuntimeTicksTotal = movieRuntimeTotal,
                MovieRuntimeTicksAverage = movieRuntimeSamples > 0 ? movieRuntimeTotal / movieRuntimeSamples : 0,
                MovieRuntimeSampleCount = movieRuntimeSamples,
                SeriesRuntimeTicksTotal = seriesRuntimeTotal,
                SeriesRuntimeTicksAverage = seriesRuntimeSamples > 0 ? seriesRuntimeTotal / seriesRuntimeSamples : 0,
                SeriesRuntimeSampleCount = seriesRuntimeSamples,
                MediaInfoSampleCount = mediaInfoSampleCount,
                GenreCounts = genreCounts,
                StudioCounts = studioCounts,
                RatingCounts = ratingCounts,
                DecadeCounts = decadeCounts,
                YearCounts = yearCounts,
                TagCounts = tagCounts,
                LanguageCounts = languageCounts,
                LocationCounts = locationCounts,
                CommunityCounts = communityCounts,
                ResolutionCounts = resolutionCounts,
                VideoRangeCounts = videoRangeCounts,
                VideoCodecCounts = videoCodecCounts,
                AudioChannelCounts = audioChannelCounts,
                AudioCodecCounts = audioCodecCounts,
                CollectionCounts = collectionCounts,
                PeopleByKind = peopleByKind,
                PersonItemIds = personItemIds,
                GenreItemIds = BuildNamedItemIdMap(genreCounts.Keys, BaseItemKind.Genre),
                StudioItemIds = BuildNamedItemIdMap(studioCounts.Keys, BaseItemKind.Studio),
                // Jellyfin has no Tag BaseItemKind / detail page — leave unlinked.
                TagItemIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase),
                CollectionItemIds = collectionItemIds,
                BucketItems = bucketItems,
                MedianProductionYear = MedianYear(productionYears),
                OldestMovie = oldestMovie,
                NewestMovie = newestMovie,
                LongestSeries = longestSeries,
                TopActorByCollectionCount = topActorByCollections.Name is null
                    ? null
                    : new NamedCountFact { Name = topActorByCollections.Name, Count = topActorByCollections.Count }
            };

            _logger.LogDebug(
                "Library stats aggregate built for user {UserId}: {Total} items ({Movies} movies, {Series} series, {Roles} people roles, {MediaSamples} media samples)",
                user?.Id,
                total,
                movieCount,
                seriesCount,
                peopleByKind.Count,
                mediaInfoSampleCount);

            return aggregate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build library stats");
            return new LibraryStatsAggregate { GeneratedAt = DateTime.UtcNow };
        }
    }

    private static LibraryStatsResponse ProjectOverview(LibraryStatsAggregate agg, string yearSort)
    {
        var total = agg.TotalCount;
        var types = BuildTypeBuckets(agg.MovieCount, agg.SeriesCount, total);
        // Rule B — multi-label: percent of total assignments (not title count).
        // Overview: top 25 named; omit Other when it would dominate (>40%).
        var genres = ToTopBuckets(agg.GenreCounts, SumCounts(agg.GenreCounts), TopBucketLimit, omitDominantOther: true, itemIds: agg.GenreItemIds, itemType: "Genre");
        var studios = ToTopStudioBuckets(agg.StudioCounts, SumCounts(agg.StudioCounts), TopBucketLimit, omitDominantOther: true, itemIds: agg.StudioItemIds);
        var tags = ToTopBuckets(agg.TagCounts, SumCounts(agg.TagCounts), TopBucketLimit, omitDominantOther: true, itemIds: agg.TagItemIds, itemType: "Tag");
        // Rule A — exclusive single-value: percent of titles (or media samples).
        var ratings = ToTopBuckets(agg.RatingCounts, total, TopBucketLimit, foldUnknown: false, omitDominantOther: true);
        var decades = ToDecadeBuckets(agg.DecadeCounts, total);
        // Years: every year with titles (no Top-N / Other), chronological by default.
        var years = ToYearBuckets(agg.YearCounts, total, yearSort);
        var communityRatings = ToOrderedBuckets(agg.CommunityCounts, total, CommunityRatingBucketOrder);
        var languages = ToTopBuckets(agg.LanguageCounts, total, TopBucketLimit, foldUnknown: false, omitDominantOther: true);
        var collections = ToTopBuckets(agg.CollectionCounts, SumCounts(agg.CollectionCounts), TopBucketLimit, foldUnknown: false, omitDominantOther: true, itemIds: agg.CollectionItemIds, itemType: "BoxSet");
        // Rule C — people: percent of credits within that role (see BuildPeopleGroups).
        var peopleByRole = BuildPeopleGroups(agg.PeopleByKind, agg.PersonItemIds);
        var mediaDenom = agg.MediaInfoSampleCount;
        var resolutions = ToOrderedBuckets(agg.ResolutionCounts, mediaDenom, ResolutionBucketOrder);
        var videoRanges = ToOrderedBuckets(agg.VideoRangeCounts, mediaDenom, VideoRangeBucketOrder);
        var videoCodecs = ToTopBuckets(agg.VideoCodecCounts, mediaDenom, TopBucketLimit, foldUnknown: false, omitDominantOther: true);
        var audioChannels = ToTopBuckets(agg.AudioChannelCounts, mediaDenom, TopBucketLimit, foldUnknown: false, omitDominantOther: true);
        var audioCodecs = ToTopBuckets(agg.AudioCodecCounts, mediaDenom, TopBucketLimit, foldUnknown: false, omitDominantOther: true);

        return new LibraryStatsResponse
        {
            GeneratedAt = agg.GeneratedAt,
            MovieCount = agg.MovieCount,
            SeriesCount = agg.SeriesCount,
            TotalCount = total,
            AnimationPercent = agg.AnimationPercent,
            MovieRuntimeTicksTotal = agg.MovieRuntimeTicksTotal,
            MovieRuntimeTicksAverage = agg.MovieRuntimeTicksAverage,
            MovieRuntimeSampleCount = agg.MovieRuntimeSampleCount,
            SeriesRuntimeTicksTotal = agg.SeriesRuntimeTicksTotal,
            SeriesRuntimeTicksAverage = agg.SeriesRuntimeTicksAverage,
            SeriesRuntimeSampleCount = agg.SeriesRuntimeSampleCount,
            Types = types,
            Genres = genres,
            Studios = studios,
            OfficialRatings = ratings,
            Decades = decades,
            Years = years,
            CommunityRatings = communityRatings,
            Tags = tags,
            Languages = languages,
            Collections = collections,
            MediaInfoSampleCount = mediaDenom,
            Resolutions = resolutions,
            VideoRanges = videoRanges,
            VideoCodecs = videoCodecs,
            AudioChannels = audioChannels,
            AudioCodecs = audioCodecs,
            PeopleByRole = peopleByRole,
            Insights = BuildInsights(agg, genres, studios, ratings, decades, years, communityRatings, peopleByRole, tags, languages, collections, resolutions, videoRanges, videoCodecs)
        };
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
        IDictionary<string, int> audioCodecCounts,
        Dictionary<string, Dictionary<string, List<BucketItemRef>>>? bucketItems = null,
        BucketItemRef? itemRef = null)
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

            var resolution = ResolutionBucket(video.Width, video.Height);
            var range = VideoRangeBucket(video.VideoRangeType);
            var videoCodec = NormalizeVideoCodec(video.Codec);
            Increment(resolutionCounts, resolution);
            Increment(videoRangeCounts, range);
            Increment(videoCodecCounts, videoCodec);
            if (itemRef is not null && bucketItems is not null)
            {
                RecordBucketItem(bucketItems, "resolutions", resolution, itemRef);
                RecordBucketItem(bucketItems, "hdr", range, itemRef);
                RecordBucketItem(bucketItems, "videocodecs", videoCodec, itemRef);
            }

            var audio = PickPrimaryAudioStream(streams);
            if (audio is not null)
            {
                var channels = AudioChannelBucket(audio);
                Increment(audioChannelCounts, channels);
                if (itemRef is not null && bucketItems is not null)
                {
                    RecordBucketItem(bucketItems, "audiochannels", channels, itemRef);
                }

                var audioCodec = NormalizeAudioCodec(audio.Codec);
                if (!string.IsNullOrWhiteSpace(audioCodec))
                {
                    Increment(audioCodecCounts, audioCodec);
                    if (itemRef is not null && bucketItems is not null)
                    {
                        RecordBucketItem(bucketItems, "audiocodecs", audioCodec, itemRef);
                    }
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
    /// Builds collection size counts, BoxSet item ids, and per-item membership from Jellyfin BoxSets.
    /// Count = Movie/Series members visible in the scoped library.
    /// </summary>
    private (Dictionary<string, int> Counts, Dictionary<string, Guid> ItemIds, Dictionary<Guid, HashSet<string>> Memberships) BuildCollectionData(
        User? user,
        HashSet<Guid> libraryItemIds)
    {
        var emptyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var emptyIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var emptyMemberships = new Dictionary<Guid, HashSet<string>>();
        if (libraryItemIds.Count == 0)
        {
            return (emptyCounts, emptyIds, emptyMemberships);
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
                return (emptyCounts, emptyIds, emptyMemberships);
            }

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var itemIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var memberships = new Dictionary<Guid, HashSet<string>>();
            foreach (var boxSet in boxSets)
            {
                var name = boxSet.Name.Trim();
                var memberIds = GetCollectionMemberIds(boxSet, libraryItemIds);
                if (memberIds.Count == 0)
                {
                    continue;
                }

                if (counts.TryGetValue(name, out var existing))
                {
                    counts[name] = existing + memberIds.Count;
                }
                else
                {
                    counts[name] = memberIds.Count;
                }

                // Prefer first BoxSet id when duplicate names collide.
                if (!itemIds.ContainsKey(name) && boxSet.Id != Guid.Empty)
                {
                    itemIds[name] = boxSet.Id;
                }

                foreach (var id in memberIds)
                {
                    if (!memberships.TryGetValue(id, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        memberships[id] = set;
                    }

                    set.Add(name);
                }
            }

            return (counts, itemIds, memberships);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping collection stats");
            return (emptyCounts, emptyIds, emptyMemberships);
        }
    }

    private HashSet<Guid> GetCollectionMemberIds(BaseItem boxSet, HashSet<Guid> libraryItemIds)
    {
        var members = new HashSet<Guid>();

        // Prefer LinkedChildren ItemIds — already on the BoxSet, no per-member lookup.
        if (boxSet is Folder folder && folder.LinkedChildren is { Length: > 0 } linked)
        {
            foreach (var child in linked)
            {
                if (child.ItemId is not Guid id || id == Guid.Empty)
                {
                    continue;
                }

                if (libraryItemIds.Contains(id))
                {
                    members.Add(id);
                }
            }

            return members;
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

            foreach (var child in children)
            {
                if (child is not null && libraryItemIds.Contains(child.Id))
                {
                    members.Add(child.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping members for collection {CollectionId}", boxSet.Id);
        }

        return members;
    }

    private void AggregateActorCollections(
        BaseItem item,
        User? user,
        IReadOnlyCollection<string> collectionNames,
        IDictionary<string, HashSet<string>> actorCollectionSets)
    {
        IReadOnlyList<PersonInfo> people;
        try
        {
            people = GetPeopleForStatsItem(item, user);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping actor-collection stats for item {ItemId}", item.Id);
            return;
        }

        if (people.Count == 0)
        {
            return;
        }

        foreach (var person in PrioritizePeople(people, MaxPeoplePerSeries))
        {
            if (person.Type != PersonKind.Actor || string.IsNullOrWhiteSpace(person.Name))
            {
                continue;
            }

            var name = person.Name.Trim();
            if (!actorCollectionSets.TryGetValue(name, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                actorCollectionSets[name] = set;
            }

            foreach (var collection in collectionNames)
            {
                set.Add(collection);
            }
        }
    }

    private void AggregatePeople(
        BaseItem item,
        User? user,
        IDictionary<PersonKind, Dictionary<string, int>> peopleByKind,
        IDictionary<string, Guid> personItemIds,
        Dictionary<string, Dictionary<string, List<BucketItemRef>>>? bucketItems = null,
        BucketItemRef? itemRef = null)
    {
        IReadOnlyList<PersonInfo> people;
        try
        {
            people = GetPeopleForStatsItem(item, user);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping people for item {ItemId}", item.Id);
            return;
        }

        if (people.Count == 0)
        {
            return;
        }

        var cap = item.GetBaseItemKind() == BaseItemKind.Series ? MaxPeoplePerSeries : MaxPeoplePerMovie;
        foreach (var person in PrioritizePeople(people, cap))
        {
            if (string.IsNullOrWhiteSpace(person.Name))
            {
                continue;
            }

            var name = person.Name.Trim();
            var type = person.Type;
            if (!peopleByKind.TryGetValue(type, out var map))
            {
                map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                peopleByKind[type] = map;
            }

            Increment(map, name);

            if (person.Id != Guid.Empty && !personItemIds.ContainsKey(name))
            {
                personItemIds[name] = person.Id;
            }

            if (itemRef is not null
                && bucketItems is not null
                && TryPersonKindCategoryKey(type, out var peopleCat))
            {
                RecordBucketItem(bucketItems, peopleCat, name, itemRef);
            }
        }
    }

    /// <summary>
    /// Prefer billed cast/crew when applying a per-title cap (Actors before deep GuestStar lists).
    /// </summary>
    private static IEnumerable<PersonInfo> PrioritizePeople(IReadOnlyList<PersonInfo> people, int cap)
    {
        return people
            .OrderBy(p => PersonKindPriority(p.Type))
            .ThenBy(p => p.SortOrder ?? int.MaxValue)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Take(cap);
    }

    private static int PersonKindPriority(PersonKind kind)
        => kind switch
        {
            PersonKind.Actor => 0,
            PersonKind.GuestStar => 1,
            PersonKind.Director => 2,
            PersonKind.Creator => 3,
            PersonKind.Writer => 4,
            PersonKind.Producer => 5,
            PersonKind.Composer => 6,
            PersonKind.Editor => 7,
            _ => 20
        };

    /// <summary>
    /// People for one Movie/Series title. Movies use attached people.
    /// Series: series-level people merged with a bounded episode sample — each person
    /// counted at most once for that series (not per episode). Episode merge is always on
    /// so partial series cast cannot hide episode-only leads (e.g. Steve Carell on The Office).
    /// </summary>
    private IReadOnlyList<PersonInfo> GetPeopleForStatsItem(BaseItem item, User? user)
    {
        var kind = item.GetBaseItemKind();
        if (kind == BaseItemKind.Series)
        {
            return GetSeriesPeopleDistinct(item, user);
        }

        return SafeGetPeople(item);
    }

    private IReadOnlyList<PersonInfo> GetSeriesPeopleDistinct(BaseItem series, User? user)
    {
        var byKey = new Dictionary<(string Name, PersonKind Kind), PersonInfo>();

        void AddPeople(IEnumerable<PersonInfo> list)
        {
            foreach (var person in list)
            {
                if (string.IsNullOrWhiteSpace(person.Name))
                {
                    continue;
                }

                var name = person.Name.Trim();
                var key = (name.ToLowerInvariant(), person.Type);
                if (!byKey.ContainsKey(key))
                {
                    byKey[key] = person;
                }
            }
        }

        AddPeople(SafeGetPeople(series));

        // Always merge episode people (distinct). Series-level lists are often incomplete —
        // Rainn Wilson may be present while Steve Carell only appears on episode credits.
        foreach (var episode in SampleSeriesEpisodesForPeople(series, user, SeriesPeopleEpisodeSampleLimit))
        {
            AddPeople(SafeGetPeople(episode));
            // Stop once we have a healthy distinct cast — avoid scanning every sampled episode.
            if (byKey.Count >= MaxPeoplePerSeries)
            {
                break;
            }
        }

        return byKey.Values.ToList();
    }

    /// <summary>
    /// Sample early episodes plus the first episode of later seasons for better cast coverage.
    /// </summary>
    private List<BaseItem> SampleSeriesEpisodesForPeople(BaseItem series, User? user, int limit)
    {
        var query = user is null
            ? new InternalItemsQuery()
            : new InternalItemsQuery(user);

        query.ParentId = series.Id;
        query.Recursive = true;
        query.IncludeItemTypes = [BaseItemKind.Episode];
        query.IsVirtualItem = false;
        query.Limit = Math.Max(limit * 2, 40);
        query.OrderBy =
        [
            (ItemSortBy.ParentIndexNumber, SortOrder.Ascending),
            (ItemSortBy.IndexNumber, SortOrder.Ascending)
        ];

        try
        {
            var all = _libraryManager.GetItemList(query)
                .Where(e => e is not null && !e.IsVirtualItem)
                .ToList();

            if (all.Count <= limit)
            {
                return all;
            }

            // First ~half from the start (S01 early eps), then first episode of each later season.
            var picked = new List<BaseItem>();
            var seen = new HashSet<Guid>();
            var early = Math.Max(limit / 2, 12);

            foreach (var ep in all.Take(early))
            {
                if (seen.Add(ep.Id))
                {
                    picked.Add(ep);
                }
            }

            foreach (var seasonGroup in all.GroupBy(e => e.ParentIndexNumber ?? 0).OrderBy(g => g.Key))
            {
                if (picked.Count >= limit)
                {
                    break;
                }

                var first = seasonGroup.OrderBy(e => e.IndexNumber ?? 0).FirstOrDefault();
                if (first is not null && seen.Add(first.Id))
                {
                    picked.Add(first);
                }
            }

            // Fill remaining from chronological order.
            foreach (var ep in all)
            {
                if (picked.Count >= limit)
                {
                    break;
                }

                if (seen.Add(ep.Id))
                {
                    picked.Add(ep);
                }
            }

            return picked;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping episode people sample for series {SeriesId}", series.Id);
            return [];
        }
    }

    private IReadOnlyList<PersonInfo> SafeGetPeople(BaseItem item)
    {
        try
        {
            return _libraryManager.GetPeople(item) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetPeople failed for item {ItemId}", item.Id);
            return [];
        }
    }

    private Dictionary<string, Guid> BuildPersonItemIdMap(IReadOnlyCollection<string> names)
    {
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return map;
        }

        // Prefer a single Person library query over per-name GetPerson lookups.
        try
        {
            var query = new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = [BaseItemKind.Person],
                IsVirtualItem = false
            };

            var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            foreach (var person in _libraryManager.GetItemList(query))
            {
                if (person is null || string.IsNullOrWhiteSpace(person.Name) || person.Id == Guid.Empty)
                {
                    continue;
                }

                var name = person.Name.Trim();
                if (wanted.Contains(name) && !map.ContainsKey(name))
                {
                    map[name] = person.Id;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to build person item id map");
        }

        // Fill gaps via GetPerson for names still missing (path-based Person items).
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || map.ContainsKey(name))
            {
                continue;
            }

            try
            {
                var person = _libraryManager.GetPerson(name.Trim());
                if (person is not null && person.Id != Guid.Empty)
                {
                    map[name.Trim()] = person.Id;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetPerson failed for {Name}", name);
            }
        }

        return map;
    }

    private Dictionary<string, Guid> BuildNamedItemIdMap(IEnumerable<string> names, BaseItemKind itemKind)
    {
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var wanted = new HashSet<string>(
            names.Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            return map;
        }

        try
        {
            if (itemKind == BaseItemKind.Genre)
            {
                foreach (var name in wanted)
                {
                    try
                    {
                        var genre = _libraryManager.GetGenre(name);
                        if (genre is not null && genre.Id != Guid.Empty)
                        {
                            map[name] = genre.Id;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "GetGenre failed for {Name}", name);
                    }
                }

                return map;
            }

            if (itemKind == BaseItemKind.Studio)
            {
                foreach (var name in wanted)
                {
                    try
                    {
                        var studio = _libraryManager.GetStudio(name);
                        if (studio is not null && studio.Id != Guid.Empty)
                        {
                            map[name] = studio.Id;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "GetStudio failed for {Name}", name);
                    }
                }

                return map;
            }

            var query = new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = [itemKind],
                IsVirtualItem = false
            };

            foreach (var item in _libraryManager.GetItemList(query))
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Name) || item.Id == Guid.Empty)
                {
                    continue;
                }

                var name = item.Name.Trim();
                if (wanted.Contains(name) && !map.ContainsKey(name))
                {
                    map[name] = item.Id;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to build item id map for {ItemKind}", itemKind);
        }

        return map;
    }

    private static IReadOnlyList<LibraryStatsPeopleGroup> BuildPeopleGroups(
        IReadOnlyDictionary<PersonKind, Dictionary<string, int>> peopleByKind,
        IReadOnlyDictionary<string, Guid>? personItemIds)
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

            // Top N named; omit Other when it would dominate overview (>40%).
            var buckets = ToTopBuckets(
                map,
                roleTotal,
                TopPeopleLimit,
                foldUnknown: false,
                omitDominantOther: true,
                itemIds: personItemIds,
                itemType: "Person");
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
        bool foldUnknown = true,
        bool omitDominantOther = false,
        IReadOnlyDictionary<string, Guid>? itemIds = null,
        string? itemType = null)
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
            .Select(kv => MakeBucket(kv.Key, kv.Value, total, itemIds, itemType))
            .ToList();

        if (otherCount > 0)
        {
            var otherPercent = Percent(otherCount, total);
            // Never emit a broken Other (>100%). On overview, also omit when Other would dominate.
            if (otherPercent is > 0 and <= 100
                && !(omitDominantOther && otherPercent > OmitOtherPercentThreshold))
            {
                buckets.Add(new LibraryStatsBucket
                {
                    Name = OtherBucketName,
                    Count = otherCount,
                    Percent = otherPercent
                });
            }
        }

        return buckets;
    }

    /// <summary>
    /// Overview studios: cluster by shared name root, then take Top N (+ Other).
    /// </summary>
    private static IReadOnlyList<LibraryStatsBucket> ToTopStudioBuckets(
        IReadOnlyDictionary<string, int> counts,
        int total,
        int limit,
        bool omitDominantOther,
        IReadOnlyDictionary<string, Guid>? itemIds)
    {
        var clustered = BuildClusteredStudioBuckets(counts, total, itemIds);
        if (clustered.Count == 0 || total <= 0)
        {
            return [];
        }

        var top = clustered.Take(limit).ToList();
        var otherCount = clustered.Skip(limit).Sum(b => b.Count);
        if (otherCount > 0)
        {
            var otherPercent = Percent(otherCount, total);
            if (otherPercent is > 0 and <= 100
                && !(omitDominantOther && otherPercent > OmitOtherPercentThreshold))
            {
                top.Add(new LibraryStatsBucket
                {
                    Name = OtherBucketName,
                    Count = otherCount,
                    Percent = otherPercent
                });
            }
        }

        return top;
    }

    private static IReadOnlyList<LibraryStatsBucket> BuildAllStudioBuckets(
        IReadOnlyDictionary<string, int> counts,
        int total,
        IReadOnlyDictionary<string, Guid>? itemIds)
    {
        var clustered = BuildClusteredStudioBuckets(counts, total, itemIds);
        if (clustered.Count <= MaxCategoryBuckets)
        {
            return clustered;
        }

        // Safety cap: keep top clusters, fold the rest into Other (no children).
        var kept = clustered.Take(MaxCategoryBuckets).ToList();
        var otherCount = clustered.Skip(MaxCategoryBuckets).Sum(b => b.Count);
        if (otherCount > 0)
        {
            kept.Add(new LibraryStatsBucket
            {
                Name = OtherBucketName,
                Count = otherCount,
                Percent = Percent(otherCount, total)
            });
        }

        return kept;
    }

    /// <summary>
    /// Groups studio credit strings by a shared brand/name root (prefix/token clustering).
    /// Parents sum counts; Children hold each exact studio string.
    /// </summary>
    private static IReadOnlyList<LibraryStatsBucket> BuildClusteredStudioBuckets(
        IReadOnlyDictionary<string, int> counts,
        int total,
        IReadOnlyDictionary<string, Guid>? itemIds)
    {
        if (counts.Count == 0 || total <= 0)
        {
            return [];
        }

        var groups = new Dictionary<string, List<(string Name, int Count)>>(StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in counts.Where(kv => kv.Value > 0))
        {
            var (rootKey, label) = StudioNameClustering.ResolveRoot(kv.Key);
            if (!groups.TryGetValue(rootKey, out var list))
            {
                list = [];
                groups[rootKey] = list;
                labels[rootKey] = label;
            }

            list.Add((kv.Key, kv.Value));
        }

        var buckets = new List<LibraryStatsBucket>(groups.Count);
        foreach (var kv in groups)
        {
            var members = kv.Value
                .OrderByDescending(m => m.Count)
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var sum = members.Sum(m => m.Count);
            if (sum <= 0)
            {
                continue;
            }

            // Singleton clusters stay as the exact studio name (linkable leaf).
            if (members.Count == 1)
            {
                buckets.Add(MakeBucket(members[0].Name, members[0].Count, total, itemIds, "Studio"));
                continue;
            }

            var children = members
                .Select(m => MakeBucket(m.Name, m.Count, total, itemIds, "Studio"))
                .ToList();

            // Parent: display root label; link best-effort to the largest child's ItemId.
            string? parentId = null;
            foreach (var child in children)
            {
                if (!string.IsNullOrEmpty(child.ItemId))
                {
                    parentId = child.ItemId;
                    break;
                }
            }

            buckets.Add(new LibraryStatsBucket
            {
                Name = labels[kv.Key],
                Count = sum,
                Percent = Percent(sum, total),
                ItemId = parentId,
                ItemType = "Studio",
                Children = children
            });
        }

        return buckets
            .OrderByDescending(b => b.Count)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static LibraryStatsBucket MakeBucket(
        string name,
        int count,
        int total,
        IReadOnlyDictionary<string, Guid>? itemIds,
        string? itemType)
    {
        string? id = null;
        if (itemIds is not null
            && itemIds.TryGetValue(name, out var guid)
            && guid != Guid.Empty)
        {
            id = guid.ToString("N", CultureInfo.InvariantCulture);
        }

        return new LibraryStatsBucket
        {
            Name = name,
            Count = count,
            Percent = Percent(count, total),
            ItemId = id,
            ItemType = itemType
        };
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

    internal static IReadOnlyList<LibraryStatsBucket> ToYearBuckets(
        IReadOnlyDictionary<string, int> counts,
        int total,
        string sortMode = "OldestFirst")
    {
        if (counts.Count == 0 || total <= 0)
        {
            return [];
        }

        var items = counts.Where(kv => kv.Value > 0).ToList();
        IEnumerable<KeyValuePair<string, int>> ordered = NormalizeYearSort(sortMode) switch
        {
            "MostTitles" => items
                .OrderByDescending(kv => kv.Value)
                .ThenByDescending(kv => YearSortKey(kv.Key, unknownLast: true)),
            "NewestFirst" => items
                .OrderByDescending(kv => YearSortKey(kv.Key, unknownLast: false)),
            _ => items
                .OrderBy(kv => YearSortKey(kv.Key, unknownLast: true))
        };

        return ordered
            .Select(kv => new LibraryStatsBucket
            {
                Name = kv.Key,
                Count = kv.Value,
                Percent = Percent(kv.Value, total)
            })
            .ToList();
    }

    /// <summary>
    /// Normalizes year-sort config to OldestFirst, NewestFirst, or MostTitles.
    /// </summary>
    internal static string ResolveYearSort(string? sortMode)
    {
        return NormalizeYearSort(sortMode);
    }

    private static string NormalizeYearSort(string? sortMode)
    {
        if (string.Equals(sortMode, "NewestFirst", StringComparison.OrdinalIgnoreCase))
        {
            return "NewestFirst";
        }

        if (string.Equals(sortMode, "MostTitles", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sortMode, "ByCount", StringComparison.OrdinalIgnoreCase))
        {
            return "MostTitles";
        }

        return "OldestFirst";
    }

    private static int YearSortKey(string name, bool unknownLast)
    {
        if (name.Equals(UnknownBucketName, StringComparison.OrdinalIgnoreCase))
        {
            // OldestFirst: unknown last (MaxValue). NewestFirst uses unknownLast=false → MinValue so descending puts it last.
            return unknownLast ? int.MaxValue : int.MinValue;
        }

        return int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : (unknownLast ? int.MaxValue - 1 : int.MinValue + 1);
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
        LibraryStatsAggregate agg,
        IReadOnlyList<LibraryStatsBucket> genres,
        IReadOnlyList<LibraryStatsBucket> studios,
        IReadOnlyList<LibraryStatsBucket> ratings,
        IReadOnlyList<LibraryStatsBucket> decades,
        IReadOnlyList<LibraryStatsBucket> years,
        IReadOnlyList<LibraryStatsBucket> communityRatings,
        IReadOnlyList<LibraryStatsPeopleGroup> peopleByRole,
        IReadOnlyList<LibraryStatsBucket> tags,
        IReadOnlyList<LibraryStatsBucket> languages,
        IReadOnlyList<LibraryStatsBucket> collections,
        IReadOnlyList<LibraryStatsBucket> resolutions,
        IReadOnlyList<LibraryStatsBucket> videoRanges,
        IReadOnlyList<LibraryStatsBucket> videoCodecs)
    {
        var movieCount = agg.MovieCount;
        var seriesCount = agg.SeriesCount;
        var total = agg.TotalCount;
        var animationPercent = agg.AnimationPercent;
        var movieRuntimeSamples = agg.MovieRuntimeSampleCount;
        var movieRuntimeTotal = agg.MovieRuntimeTicksTotal;
        var mediaInfoSampleCount = agg.MediaInfoSampleCount;

        if (total <= 0)
        {
            return ["Your library has no movies or series to summarize yet."];
        }

        var insights = new List<string>(MaxInsights)
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
                AddInsight(insights, string.Format(
                    CultureInfo.InvariantCulture,
                    "Movies make up {0}% of the library; series are {1}%.",
                    moviePct,
                    seriesPct));
            }
            else
            {
                AddInsight(insights, string.Format(
                    CultureInfo.InvariantCulture,
                    "Series make up {0}% of the library; movies are {1}%.",
                    seriesPct,
                    moviePct));
            }
        }

        if (agg.OldestMovie is { } oldest)
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "Oldest movie: {0} ({1}).",
                oldest.Name,
                FormatYearLabel(oldest)));
        }

        if (agg.NewestMovie is { } newest
            && (agg.OldestMovie is null
                || !string.Equals(newest.Name, agg.OldestMovie.Name, StringComparison.OrdinalIgnoreCase)
                || newest.Year != agg.OldestMovie.Year))
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "Newest movie: {0} ({1}).",
                newest.Name,
                FormatYearLabel(newest)));
        }

        if (agg.MedianProductionYear is int medianYear)
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "Median release year is {0}.",
                medianYear));
        }

        if (agg.LongestSeries is { } longest && longest.SpanYears >= 1)
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "Longest-running series: {0} ({1}–{2}, {3} years).",
                longest.Name,
                longest.StartYear,
                longest.EndYear,
                longest.SpanYears));
        }

        var topCollection = FirstNamed(collections);
        if (topCollection is not null && topCollection.Count >= 2)
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "{0} is your largest collection with {1} titles.",
                topCollection.Name,
                topCollection.Count));
        }

        if (agg.TopActorByCollectionCount is { } franchiseActor && franchiseActor.Count >= 2)
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "{0} appears in the most collections/franchises ({1}).",
                franchiseActor.Name,
                franchiseActor.Count));
        }

        var topLocation = agg.LocationCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (!string.IsNullOrEmpty(topLocation.Key) && topLocation.Value > 0)
        {
            var locTotal = SumCounts(agg.LocationCounts);
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "Most common production location is {0} ({1}% of location tags).",
                topLocation.Key,
                Percent(topLocation.Value, locTotal)));
        }

        var topResolution = FirstNamed(resolutions, allowUnknown: false);
        if (topResolution is not null && mediaInfoSampleCount > 0)
        {
            AddInsight(insights, string.Format(
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
                AddInsight(insights, string.Format(
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
                    AddInsight(insights, string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}% of titles with media info are SDR.",
                        sdr.Percent));
                }
            }
        }

        var topCodec = FirstNamed(videoCodecs, allowUnknown: false);
        if (topCodec is not null && mediaInfoSampleCount > 0)
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "Most common video codec is {0} ({1}%).",
                topCodec.Name,
                topCodec.Percent));
        }

        var topGenre = FirstNamed(genres);
        if (topGenre is not null)
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "Most common genre tag is {0} ({1}% of genre tags).",
                topGenre.Name,
                topGenre.Percent));
        }

        var topStudio = FirstNamed(studios);
        if (topStudio is not null)
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "{0}% of studio credits are {1}.",
                topStudio.Percent,
                topStudio.Name));
        }

        AddTopPersonInsight(insights, peopleByRole, "Actor");
        AddTopPersonInsight(insights, peopleByRole, "Director");
        AddTopPersonInsight(insights, peopleByRole, "Writer");
        AddTopPersonInsight(insights, peopleByRole, "Creator");
        AddTopPersonInsight(insights, peopleByRole, "Producer");

        if (animationPercent > 0)
        {
            AddInsight(insights, string.Format(
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
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "Most titles with a community score fall in {0} ({1}%).",
                topCommunity.Name,
                topCommunity.Percent));
        }

        if (movieRuntimeSamples > 0 && movieRuntimeTotal > 0)
        {
            var avgMinutes = (movieRuntimeTotal / movieRuntimeSamples) / TimeSpan.TicksPerMinute;
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "Average movie runtime is about {0} minutes ({1} movies with runtime data).",
                avgMinutes,
                movieRuntimeSamples));
        }

        var topTag = FirstNamed(tags);
        if (topTag is not null)
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "Most used tag is {0} ({1}% of tag assignments).",
                topTag.Name,
                topTag.Percent));
        }

        var topLanguage = FirstNamed(languages);
        if (topLanguage is not null)
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "Preferred metadata language is most often {0} ({1}%).",
                topLanguage.Name,
                topLanguage.Percent));
        }

        var topRating = FirstNamed(ratings, allowUnknown: false);
        if (topRating is not null)
        {
            AddInsight(insights, string.Format(
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
        if (topDecade is not null)
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "Most titles are from the {0} ({1}%).",
                topDecade.Name,
                topDecade.Percent));
        }

        var topYear = years
            .Where(y => !y.Name.Equals(UnknownBucketName, StringComparison.OrdinalIgnoreCase)
                        && !y.Name.Equals(OtherBucketName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(y => y.Count)
            .ThenByDescending(y => YearSortKey(y.Name, unknownLast: true))
            .FirstOrDefault();
        if (topYear is not null && topYear.Count >= 2)
        {
            AddInsight(insights, string.Format(
                CultureInfo.InvariantCulture,
                "{0} titles share release year {1} ({2}%).",
                topYear.Count,
                topYear.Name,
                topYear.Percent));
        }

        return insights.Take(MaxInsights).ToList();
    }

    private static void AddInsight(ICollection<string> insights, string line)
    {
        if (insights.Count >= MaxInsights)
        {
            return;
        }

        insights.Add(line);
    }

    private static void AddTopPersonInsight(
        ICollection<string> insights,
        IReadOnlyList<LibraryStatsPeopleGroup> peopleByRole,
        string role)
    {
        if (insights.Count >= MaxInsights)
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
            "{0} is {1}% of {2} credits ({3} titles).",
            top.Name,
            top.Percent,
            roleLabel,
            top.Count));
    }

    private static void TrackMovieYearFacts(
        BaseItem item,
        ref TitleYearFact? oldest,
        ref TitleYearFact? newest)
    {
        if (string.IsNullOrWhiteSpace(item.Name))
        {
            return;
        }

        var sortKey = ResolveTitleSortDate(item);
        if (sortKey is null)
        {
            return;
        }

        var fact = new TitleYearFact
        {
            Name = item.Name.Trim(),
            Year = sortKey.Value.Year,
            SortKey = sortKey.Value
        };

        if (oldest is null || fact.SortKey < oldest.SortKey
            || (fact.SortKey == oldest.SortKey
                && string.Compare(fact.Name, oldest.Name, StringComparison.OrdinalIgnoreCase) < 0))
        {
            oldest = fact;
        }

        if (newest is null || fact.SortKey > newest.SortKey
            || (fact.SortKey == newest.SortKey
                && string.Compare(fact.Name, newest.Name, StringComparison.OrdinalIgnoreCase) < 0))
        {
            newest = fact;
        }
    }

    private static void TrackLongestSeries(BaseItem item, ref SeriesSpanFact? longest)
    {
        if (string.IsNullOrWhiteSpace(item.Name))
        {
            return;
        }

        var startYear = item.PremiereDate?.Year
            ?? (item.ProductionYear is int py && py >= 1000 ? py : null);
        if (startYear is null)
        {
            return;
        }

        var endYear = item.EndDate?.Year;
        if (endYear is null || endYear < startYear)
        {
            // Ongoing / unknown end: treat as running through the start year only (no span insight).
            return;
        }

        var span = endYear.Value - startYear.Value;
        if (span < 1)
        {
            return;
        }

        var candidate = new SeriesSpanFact
        {
            Name = item.Name.Trim(),
            StartYear = startYear.Value,
            EndYear = endYear.Value,
            SpanYears = span
        };

        if (longest is null
            || candidate.SpanYears > longest.SpanYears
            || (candidate.SpanYears == longest.SpanYears
                && string.Compare(candidate.Name, longest.Name, StringComparison.OrdinalIgnoreCase) < 0))
        {
            longest = candidate;
        }
    }

    private static DateTime? ResolveTitleSortDate(BaseItem item)
    {
        if (item.PremiereDate is DateTime premiere && premiere.Year >= 1000)
        {
            return premiere.Date;
        }

        if (item.ProductionYear is int year && year >= 1000)
        {
            return new DateTime(year, 1, 1);
        }

        return null;
    }

    private static int? MedianYear(List<int> years)
    {
        if (years.Count == 0)
        {
            return null;
        }

        years.Sort();
        var mid = years.Count / 2;
        if (years.Count % 2 == 1)
        {
            return years[mid];
        }

        // Even count: average of the two middle years, rounded away from zero.
        return (int)Math.Round((years[mid - 1] + years[mid]) / 2.0, MidpointRounding.AwayFromZero);
    }

    private static string FormatYearLabel(TitleYearFact fact)
        => fact.Year.ToString(CultureInfo.InvariantCulture);

    private static LibraryStatsBucket? FirstNamed(
        IReadOnlyList<LibraryStatsBucket> buckets,
        bool allowUnknown = true)
    {
        return buckets.FirstOrDefault(b =>
            !b.Name.Equals(OtherBucketName, StringComparison.OrdinalIgnoreCase)
            && (allowUnknown || !b.Name.Equals(UnknownBucketName, StringComparison.OrdinalIgnoreCase)));
    }

    private static LibraryStatsCategoryResponse? ProjectCategory(LibraryStatsAggregate agg, string category)
    {
        var key = NormalizeCategoryKey(category);
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        // Accept People:Actor / peopleActor after alphanumeric normalize → peopleactor.
        if (key.StartsWith("people", StringComparison.Ordinal) && key.Length > "people".Length)
        {
            var kindKey = key["people".Length..];
            if (TryProjectPeopleCategory(agg, kindKey, out var prefixedPeople))
            {
                return prefixedPeople;
            }
        }

        if (TryProjectPeopleCategory(agg, key, out var peopleResult))
        {
            return peopleResult;
        }

        return key switch
        {
            "types" => CategoryFromBuckets(
                "types",
                "Types",
                "Share of titles",
                BuildTypeBuckets(agg.MovieCount, agg.SeriesCount, agg.TotalCount),
                agg.TotalCount,
                agg.GeneratedAt),
            "genres" => CategoryFromCounts(
                "genres",
                "Genres",
                "Share of genre tags",
                agg.GenreCounts,
                SumCounts(agg.GenreCounts),
                agg.GeneratedAt,
                agg.GenreItemIds,
                "Genre"),
            "studios" => CategoryFromBuckets(
                "studios",
                "Studios",
                "Share of studio credits (name-root clusters expand to exact studios)",
                BuildAllStudioBuckets(agg.StudioCounts, SumCounts(agg.StudioCounts), agg.StudioItemIds),
                SumCounts(agg.StudioCounts),
                agg.GeneratedAt),
            "tags" => CategoryFromCounts(
                "tags",
                "Tags",
                "Share of tag assignments",
                agg.TagCounts,
                SumCounts(agg.TagCounts),
                agg.GeneratedAt,
                agg.TagItemIds,
                "Tag"),
            "collections" => CategoryFromCounts(
                "collections",
                "Collections",
                "Share of collection memberships",
                agg.CollectionCounts,
                SumCounts(agg.CollectionCounts),
                agg.GeneratedAt,
                agg.CollectionItemIds,
                "BoxSet"),
            "decades" => CategoryFromBuckets(
                "decades",
                "Decades",
                "Share of titles",
                ToDecadeBuckets(agg.DecadeCounts, agg.TotalCount),
                agg.TotalCount,
                agg.GeneratedAt),
            "years" => CategoryFromBuckets(
                "years",
                "Release years",
                "Share of titles · every year with library titles",
                ToYearBuckets(
                    agg.YearCounts,
                    agg.TotalCount,
                    ResolveYearSort(Plugin.Instance?.Configuration?.LibraryStatsYearSort)),
                agg.TotalCount,
                agg.GeneratedAt),
            "ratings" or "officialratings" => CategoryFromCounts(
                "ratings",
                "Official ratings",
                "Share of titles",
                agg.RatingCounts,
                agg.TotalCount,
                agg.GeneratedAt),
            "community" or "communityratings" => CategoryFromBuckets(
                "community",
                "Community scores",
                "Share of titles",
                ToOrderedBuckets(agg.CommunityCounts, agg.TotalCount, CommunityRatingBucketOrder),
                agg.TotalCount,
                agg.GeneratedAt),
            "languages" => CategoryFromCounts(
                "languages",
                "Languages",
                "Share of titles",
                agg.LanguageCounts,
                agg.TotalCount,
                agg.GeneratedAt),
            "resolutions" => CategoryFromBuckets(
                "resolutions",
                "Resolutions",
                "Share of titles with media info",
                ToOrderedBuckets(agg.ResolutionCounts, agg.MediaInfoSampleCount, ResolutionBucketOrder),
                agg.MediaInfoSampleCount,
                agg.GeneratedAt),
            "hdr" or "videoranges" => CategoryFromBuckets(
                "hdr",
                "HDR / range",
                "Share of titles with media info",
                ToOrderedBuckets(agg.VideoRangeCounts, agg.MediaInfoSampleCount, VideoRangeBucketOrder),
                agg.MediaInfoSampleCount,
                agg.GeneratedAt),
            "videocodecs" => CategoryFromCounts(
                "videoCodecs",
                "Video codecs",
                "Share of titles with media info",
                agg.VideoCodecCounts,
                agg.MediaInfoSampleCount,
                agg.GeneratedAt),
            "audio" or "audiochannels" => CategoryFromCounts(
                "audioChannels",
                "Audio channels",
                "Share of titles with media info",
                agg.AudioChannelCounts,
                agg.MediaInfoSampleCount,
                agg.GeneratedAt),
            "audiocodecs" => CategoryFromCounts(
                "audioCodecs",
                "Audio codecs",
                "Share of titles with media info",
                agg.AudioCodecCounts,
                agg.MediaInfoSampleCount,
                agg.GeneratedAt),
            _ => null
        };
    }

    private static bool TryProjectPeopleCategory(
        LibraryStatsAggregate agg,
        string key,
        out LibraryStatsCategoryResponse? result)
    {
        result = null;
        if (!TryResolvePersonKind(key, out var kind, out var canonical, out var title))
        {
            return false;
        }

        if (!agg.PeopleByKind.TryGetValue(kind, out var map) || map.Count == 0)
        {
            result = new LibraryStatsCategoryResponse
            {
                Category = canonical,
                Title = title,
                DenominatorHint = "Share of " + FormatPersonKind(kind).ToLowerInvariant()
                    + " credits (each movie/series once)",
                Denominator = 0,
                GeneratedAt = agg.GeneratedAt,
                Buckets = []
            };
            return true;
        }

        var roleTotal = map.Values.Sum();
        var (buckets, truncated, totalBuckets) = ToAllBuckets(map, roleTotal, agg.PersonItemIds, "Person");
        result = new LibraryStatsCategoryResponse
        {
            Category = canonical,
            Title = title,
            DenominatorHint = "Share of " + FormatPersonKind(kind).ToLowerInvariant()
                + " credits (each movie/series once)",
            Denominator = roleTotal,
            GeneratedAt = agg.GeneratedAt,
            Truncated = truncated,
            TotalBuckets = totalBuckets,
            Buckets = buckets
        };
        return true;
    }

    private static bool TryResolvePersonKind(
        string key,
        out PersonKind kind,
        out string canonical,
        out string title)
    {
        kind = PersonKind.Unknown;
        canonical = string.Empty;
        title = string.Empty;

        (PersonKind Kind, string Canonical, string Title)? match = key switch
        {
            "actors" or "actor" => (PersonKind.Actor, "actors", "Top Actor"),
            "directors" or "director" => (PersonKind.Director, "directors", "Top Director"),
            "writers" or "writer" => (PersonKind.Writer, "writers", "Top Writer"),
            "creators" or "creator" => (PersonKind.Creator, "creators", "Top Creator"),
            "producers" or "producer" => (PersonKind.Producer, "producers", "Top Producer"),
            "gueststars" or "gueststar" => (PersonKind.GuestStar, "guestStars", "Top Guest Star"),
            "composers" or "composer" => (PersonKind.Composer, "composers", "Top Composer"),
            "editors" or "editor" => (PersonKind.Editor, "editors", "Top Editor"),
            "artists" or "artist" => (PersonKind.Artist, "artists", "Top Artist"),
            "authors" or "author" => (PersonKind.Author, "authors", "Top Author"),
            "albumartists" or "albumartist" => (PersonKind.AlbumArtist, "albumArtists", "Top Album Artist"),
            "coverartists" or "coverartist" => (PersonKind.CoverArtist, "coverArtists", "Top Cover Artist"),
            "unknown" => (PersonKind.Unknown, "unknown", "Top Unknown"),
            _ => null
        };

        if (match is null)
        {
            return false;
        }

        kind = match.Value.Kind;
        canonical = match.Value.Canonical;
        title = match.Value.Title;
        return true;
    }

    private static string NormalizeCategoryKey(string category)
    {
        var trimmed = category.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        // Keep a compact alphanumeric key: actors, videoCodecs, guestStars, people:Actor → peopleactor
        var buffer = new char[trimmed.Length];
        var n = 0;
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[n++] = char.ToLowerInvariant(ch);
            }
        }

        return n == 0 ? string.Empty : new string(buffer, 0, n);
    }

    private static LibraryStatsCategoryResponse CategoryFromCounts(
        string category,
        string title,
        string hint,
        IReadOnlyDictionary<string, int> counts,
        int denominator,
        DateTime generatedAt,
        IReadOnlyDictionary<string, Guid>? itemIds = null,
        string? itemType = null)
    {
        var (buckets, truncated, totalBuckets) = ToAllBuckets(counts, denominator, itemIds, itemType);
        return new LibraryStatsCategoryResponse
        {
            Category = category,
            Title = title,
            DenominatorHint = hint,
            Denominator = denominator,
            GeneratedAt = generatedAt,
            Truncated = truncated,
            TotalBuckets = totalBuckets,
            Buckets = buckets
        };
    }

    private static LibraryStatsCategoryResponse CategoryFromBuckets(
        string category,
        string title,
        string hint,
        IReadOnlyList<LibraryStatsBucket> buckets,
        int denominator,
        DateTime generatedAt)
    {
        return new LibraryStatsCategoryResponse
        {
            Category = category,
            Title = title,
            DenominatorHint = hint,
            Denominator = denominator,
            GeneratedAt = generatedAt,
            Truncated = false,
            TotalBuckets = buckets.Count,
            Buckets = buckets
        };
    }

    private static (IReadOnlyList<LibraryStatsBucket> Buckets, bool Truncated, int TotalBuckets) ToAllBuckets(
        IReadOnlyDictionary<string, int> counts,
        int total,
        IReadOnlyDictionary<string, Guid>? itemIds = null,
        string? itemType = null)
    {
        if (counts.Count == 0 || total <= 0)
        {
            return ([], false, 0);
        }

        var ordered = counts
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalBuckets = ordered.Count;
        var truncated = totalBuckets > MaxCategoryBuckets;
        if (truncated)
        {
            ordered = ordered.Take(MaxCategoryBuckets).ToList();
        }

        var buckets = ordered
            .Select(kv => MakeBucket(kv.Key, kv.Value, total, itemIds, itemType))
            .ToList();

        return (buckets, truncated, totalBuckets);
    }

    /// <summary>
    /// Raw count maps shared by overview (Top N + Other) and category detail (full list).
    /// </summary>
    private sealed class LibraryStatsAggregate
    {
        public DateTime GeneratedAt { get; set; }

        public int MovieCount { get; set; }

        public int SeriesCount { get; set; }

        public int TotalCount { get; set; }

        public double AnimationPercent { get; set; }

        public long MovieRuntimeTicksTotal { get; set; }

        public long MovieRuntimeTicksAverage { get; set; }

        public int MovieRuntimeSampleCount { get; set; }

        public long SeriesRuntimeTicksTotal { get; set; }

        public long SeriesRuntimeTicksAverage { get; set; }

        public int SeriesRuntimeSampleCount { get; set; }

        public int MediaInfoSampleCount { get; set; }

        public Dictionary<string, int> GenreCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> StudioCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> RatingCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> DecadeCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> YearCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> TagCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> LanguageCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> LocationCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> CommunityCounts { get; set; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> ResolutionCounts { get; set; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> VideoRangeCounts { get; set; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> VideoCodecCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> AudioChannelCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> AudioCodecCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> CollectionCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<PersonKind, Dictionary<string, int>> PeopleByKind { get; set; } = new();

        public Dictionary<string, Guid> PersonItemIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Guid> GenreItemIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Guid> StudioItemIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Guid> TagItemIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Guid> CollectionItemIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Normalized category key → bucket name → contributing titles.
        /// </summary>
        public Dictionary<string, Dictionary<string, List<BucketItemRef>>> BucketItems { get; set; } = new(StringComparer.Ordinal);

        public int? MedianProductionYear { get; set; }

        public TitleYearFact? OldestMovie { get; set; }

        public TitleYearFact? NewestMovie { get; set; }

        public SeriesSpanFact? LongestSeries { get; set; }

        public NamedCountFact? TopActorByCollectionCount { get; set; }
    }

    private sealed class TitleYearFact
    {
        public string Name { get; set; } = string.Empty;

        public int Year { get; set; }

        public DateTime SortKey { get; set; }
    }

    private sealed class SeriesSpanFact
    {
        public string Name { get; set; } = string.Empty;

        public int StartYear { get; set; }

        public int EndYear { get; set; }

        public int SpanYears { get; set; }
    }

    private sealed class NamedCountFact
    {
        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    /// <summary>
    /// Compact title reference stored per stats bucket for drill-down lists.
    /// </summary>
    private sealed class BucketItemRef
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Type { get; init; } = string.Empty;

        public int? Year { get; init; }

        public bool HasPrimaryImage { get; init; }
    }

    private static BucketItemRef ToBucketItemRef(BaseItem item)
    {
        var kind = item.GetBaseItemKind();
        return new BucketItemRef
        {
            Id = item.Id,
            Name = item.Name ?? string.Empty,
            Type = kind == BaseItemKind.Series ? "Series" : "Movie",
            Year = item.ProductionYear,
            HasPrimaryImage = item.HasImage(ImageType.Primary)
        };
    }

    private static LibraryStatsBucketItem ToBucketItemDto(BucketItemRef item)
        => new()
        {
            Id = item.Id.ToString("N", CultureInfo.InvariantCulture),
            Name = item.Name,
            Type = item.Type,
            ProductionYear = item.Year,
            ImageTag = item.HasPrimaryImage ? "primary" : null
        };

    private static void RecordBucketItem(
        Dictionary<string, Dictionary<string, List<BucketItemRef>>> map,
        string categoryKey,
        string bucket,
        BucketItemRef item)
    {
        if (string.IsNullOrWhiteSpace(categoryKey) || string.IsNullOrWhiteSpace(bucket))
        {
            return;
        }

        var cat = NormalizeCategoryKey(categoryKey);
        if (string.IsNullOrEmpty(cat))
        {
            return;
        }

        if (!map.TryGetValue(cat, out var byBucket))
        {
            byBucket = new Dictionary<string, List<BucketItemRef>>(StringComparer.OrdinalIgnoreCase);
            map[cat] = byBucket;
        }

        if (!byBucket.TryGetValue(bucket, out var list))
        {
            list = [];
            byBucket[bucket] = list;
        }

        list.Add(item);
    }

    private static bool TryPersonKindCategoryKey(PersonKind kind, out string categoryKey)
    {
        categoryKey = kind switch
        {
            PersonKind.Actor => "actors",
            PersonKind.Director => "directors",
            PersonKind.Writer => "writers",
            PersonKind.Creator => "creators",
            PersonKind.Producer => "producers",
            PersonKind.GuestStar => "gueststars",
            PersonKind.Composer => "composers",
            PersonKind.Editor => "editors",
            PersonKind.Artist => "artists",
            PersonKind.Author => "authors",
            PersonKind.AlbumArtist => "albumartists",
            PersonKind.CoverArtist => "coverartists",
            PersonKind.Unknown => "unknown",
            _ => string.Empty
        };
        return categoryKey.Length > 0;
    }
}
