using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.FullCrew.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using User = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Builds and serves an in-memory index of special audio tracks across the library.
/// </summary>
public sealed class AudioTrackIndexService : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ILogger<AudioTrackIndexService> _logger;
    private readonly object _sync = new();
    private readonly List<IndexedTrack> _tracks = [];

    private DateTime? _indexedAt;
    private bool _isIndexing;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioTrackIndexService"/> class.
    /// </summary>
    public AudioTrackIndexService(
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        ILogger<AudioTrackIndexService> logger)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
        _libraryManager.ItemRemoved += OnItemRemoved;
        _ = Task.Run(() => RebuildSafe(), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
        _libraryManager.ItemRemoved -= OnItemRemoved;
    }

    /// <summary>
    /// Rebuilds the index on a background thread.
    /// </summary>
    public void RequestRebuild()
    {
        _ = Task.Run(() => RebuildSafe(), CancellationToken.None);
    }

    /// <summary>
    /// Gets library-wide type counts visible to the user.
    /// </summary>
    /// <param name="user">The current user, or <c>null</c> to skip visibility checks.</param>
    /// <returns>Type overview.</returns>
    public AudioTypesResponse GetTypes(User? user)
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.EnableAudioBrowser == false)
        {
            return new AudioTypesResponse { Enabled = false };
        }

        IReadOnlyList<IndexedTrack> snapshot;
        bool isIndexing;
        DateTime? indexedAt;
        lock (_sync)
        {
            snapshot = _tracks.ToList();
            isIndexing = _isIndexing;
            indexedAt = _indexedAt;
        }

        var visible = FilterVisible(snapshot, user);
        var byType = visible
            .GroupBy(t => t.TypeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var types = new List<AudioTypeSummary>();
        foreach (var kind in AudioTrackKind.All)
        {
            if (!byType.TryGetValue(kind.Id, out var list) || list.Count == 0)
            {
                continue;
            }

            types.Add(new AudioTypeSummary
            {
                Id = kind.Id,
                Name = kind.Name,
                Description = kind.Description,
                TrackCount = list.Count,
                ItemCount = list.Select(t => t.ItemId).Distinct().Count()
            });
        }

        return new AudioTypesResponse
        {
            Enabled = true,
            IsIndexing = isIndexing,
            IndexedAt = indexedAt,
            ItemCount = visible.Select(t => t.ItemId).Distinct().Count(),
            TrackCount = visible.Count,
            Types = types
        };
    }

    /// <summary>
    /// Gets a paged list of items that have a given audio type.
    /// </summary>
    /// <param name="typeId">Type id, or <c>all</c>.</param>
    /// <param name="search">Optional free-text search.</param>
    /// <param name="language">Optional language code or display name.</param>
    /// <param name="sort">Sort key: name, series, year, date, added.</param>
    /// <param name="startIndex">Page offset.</param>
    /// <param name="limit">Page size.</param>
    /// <param name="user">The current user, or <c>null</c> to skip visibility checks.</param>
    /// <returns>Paged items.</returns>
    public AudioTypeItemsResponse GetItems(
        string? typeId,
        string? search,
        string? language,
        string? sort,
        int startIndex,
        int limit,
        User? user)
    {
        var kind = AudioTrackKind.Find(typeId);
        var typeName = kind?.Name ?? "All special audio";
        var resolvedTypeId = kind?.Id ?? "all";

        IReadOnlyList<IndexedTrack> snapshot;
        bool isIndexing;
        lock (_sync)
        {
            snapshot = _tracks.ToList();
            isIndexing = _isIndexing;
        }

        var matches = FilterVisible(snapshot, user).AsEnumerable();
        if (kind is not null)
        {
            matches = matches.Where(t => string.Equals(t.TypeId, kind.Id, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            var lang = language.Trim();
            matches = matches.Where(t =>
                string.Equals(t.Language, lang, StringComparison.OrdinalIgnoreCase)
                || string.Equals(t.LanguageName, lang, StringComparison.OrdinalIgnoreCase)
                || (string.Equals(lang, "unknown", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(t.Language)));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            matches = matches.Where(t => t.SearchText.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var trackList = matches.ToList();
        var languages = trackList
            .Select(t => t.LanguageName ?? t.Language)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var grouped = trackList
            .GroupBy(t => t.ItemId)
            .Select(g => (Item: ToItem(g), First: g.First()))
            .ToList();

        var sorted = SortGroups(grouped, sort);

        startIndex = Math.Max(0, startIndex);
        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);

        return new AudioTypeItemsResponse
        {
            TypeId = resolvedTypeId,
            TypeName = typeName,
            IsIndexing = isIndexing,
            TotalCount = sorted.Count,
            StartIndex = startIndex,
            Languages = languages,
            Items = sorted.Skip(startIndex).Take(limit).Select(g => g.Item).ToList()
        };
    }

    /// <summary>
    /// Classifies special audio tracks on a single item (live, not from the index).
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>Classified tracks.</returns>
    public IReadOnlyList<AudioTrackInfo> GetTracksForItem(BaseItem item)
    {
        return Extract(item).Select(ToTrackInfo).ToList();
    }

    /// <summary>
    /// Classifies special audio tracks on one library item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <returns>Classified tracks, or empty when the item is missing.</returns>
    public ItemAudioTracksResponse GetItemTracks(Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return new ItemAudioTracksResponse { ItemId = itemId.ToString("D") };
        }

        return new ItemAudioTracksResponse
        {
            ItemId = item.Id.ToString("D"),
            Tracks = GetTracksForItem(item)
        };
    }

    private void RebuildSafe()
    {
        try
        {
            Rebuild();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Full Crew: audio track index rebuild failed.");
            lock (_sync)
            {
                _isIndexing = false;
            }
        }
    }

    private void Rebuild()
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.EnableAudioBrowser == false)
        {
            lock (_sync)
            {
                _tracks.Clear();
                _indexedAt = DateTime.UtcNow;
                _isIndexing = false;
            }

            return;
        }

        lock (_sync)
        {
            _isIndexing = true;
        }

        var kinds = ResolveItemKinds(config);
        var query = new InternalItemsQuery
        {
            Recursive = true,
            IsVirtualItem = false,
            IncludeItemTypes = kinds
        };

        var items = _libraryManager.GetItemList(query);
        var next = new List<IndexedTrack>();
        var itemIds = new HashSet<Guid>();

        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            var extracted = Extract(item);
            if (extracted.Count == 0)
            {
                continue;
            }

            itemIds.Add(item.Id);
            next.AddRange(extracted);
        }

        lock (_sync)
        {
            _tracks.Clear();
            _tracks.AddRange(next);
            _indexedAt = DateTime.UtcNow;
            _isIndexing = false;
        }

        _logger.LogInformation(
            "Full Crew: indexed {TrackCount} special audio tracks on {ItemCount} items.",
            next.Count,
            itemIds.Count);
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        var item = e.Item;
        if (item is null)
        {
            return;
        }

        if (!ShouldIndex(item))
        {
            RemoveItem(item.Id);
            return;
        }

        var extracted = Extract(item);
        lock (_sync)
        {
            _tracks.RemoveAll(t => t.ItemId == item.Id);
            _tracks.AddRange(extracted);
        }
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is not null)
        {
            RemoveItem(e.Item.Id);
        }
    }

    private void RemoveItem(Guid itemId)
    {
        lock (_sync)
        {
            _tracks.RemoveAll(t => t.ItemId == itemId);
        }
    }

    private List<IndexedTrack> FilterVisible(IReadOnlyList<IndexedTrack> snapshot, User? user)
    {
        if (user is null)
        {
            return snapshot.ToList();
        }

        var visibility = new Dictionary<Guid, bool>();
        var result = new List<IndexedTrack>(snapshot.Count);
        foreach (var track in snapshot)
        {
            if (!visibility.TryGetValue(track.ItemId, out var visible))
            {
                var item = _libraryManager.GetItemById(track.ItemId);
                visible = item is not null && item.IsVisibleStandalone(user);
                visibility[track.ItemId] = visible;
            }

            if (visible)
            {
                result.Add(track);
            }
        }

        return result;
    }

    private List<IndexedTrack> Extract(BaseItem item)
    {
        IReadOnlyList<MediaStream> streams;
        try
        {
            streams = _mediaSourceManager.GetMediaStreams(item.Id);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Full Crew: failed to read audio streams for {ItemId}", item.Id);
            return [];
        }

        if (streams is null || streams.Count == 0)
        {
            return [];
        }

        var list = new List<IndexedTrack>();
        foreach (var stream in streams)
        {
            if (stream.Type != MediaStreamType.Audio)
            {
                continue;
            }

            var kind = AudioTrackClassifier.Classify(stream.Title, stream.Comment);
            if (kind is null)
            {
                continue;
            }

            var seriesName = item is Episode episode ? episode.SeriesName : null;
            var seriesId = item is Episode ep && ep.SeriesId != Guid.Empty ? ep.SeriesId : (Guid?)null;
            var languageName = AudioTrackClassifier.LanguageDisplayName(stream.Language);
            var display = AudioTrackClassifier.FormatDisplayTitle(
                stream.Title,
                stream.Language,
                stream.Codec,
                stream.Channels);

            var search = string.Join(
                ' ',
                new[]
                {
                    item.Name,
                    seriesName,
                    stream.Title,
                    stream.Comment,
                    stream.Language,
                    languageName,
                    kind.Name,
                    item.ProductionYear?.ToString(CultureInfo.InvariantCulture)
                }.Where(s => !string.IsNullOrWhiteSpace(s)));

            list.Add(new IndexedTrack
            {
                ItemId = item.Id,
                Name = item.Name ?? string.Empty,
                MediaType = item.GetBaseItemKind().ToString(),
                SeriesName = seriesName,
                SeriesId = seriesId,
                SeasonNumber = item.ParentIndexNumber,
                EpisodeNumber = item.IndexNumber,
                ProductionYear = item.ProductionYear,
                PremiereDate = item.PremiereDate,
                DateCreated = item.DateCreated,
                StreamIndex = stream.Index,
                TypeId = kind.Id,
                TypeName = kind.Name,
                Title = stream.Title,
                Language = stream.Language,
                LanguageName = languageName,
                Codec = stream.Codec,
                Channels = stream.Channels,
                DisplayTitle = display,
                SearchText = search
            });
        }

        return list;
    }

    private static AudioTrackItem ToItem(IGrouping<Guid, IndexedTrack> group)
    {
        var first = group.First();
        var imageId = first.ItemId;
        if (first.SeriesId is Guid seriesId)
        {
            imageId = seriesId;
        }

        return new AudioTrackItem
        {
            ItemId = first.ItemId.ToString("D", CultureInfo.InvariantCulture),
            Name = first.Name,
            MediaType = first.MediaType,
            SeriesName = first.SeriesName,
            SeriesId = first.SeriesId?.ToString("D", CultureInfo.InvariantCulture),
            SeasonNumber = first.SeasonNumber,
            EpisodeNumber = first.EpisodeNumber,
            ProductionYear = first.ProductionYear,
            ImageItemId = imageId.ToString("D", CultureInfo.InvariantCulture),
            Tracks = group
                .OrderBy(t => t.StreamIndex)
                .Select(ToTrackInfo)
                .ToList()
        };
    }

    private static AudioTrackInfo ToTrackInfo(IndexedTrack track)
    {
        return new AudioTrackInfo
        {
            Index = track.StreamIndex,
            TypeId = track.TypeId,
            TypeName = track.TypeName,
            Title = track.Title,
            Language = track.Language,
            LanguageName = track.LanguageName,
            Codec = track.Codec,
            Channels = track.Channels,
            DisplayTitle = track.DisplayTitle
        };
    }

    private static List<(AudioTrackItem Item, IndexedTrack First)> SortGroups(
        List<(AudioTrackItem Item, IndexedTrack First)> groups,
        string? sort)
    {
        IOrderedEnumerable<(AudioTrackItem Item, IndexedTrack First)> ordered = (sort ?? "name").Trim().ToLowerInvariant() switch
        {
            "year" => groups
                .OrderByDescending(g => g.First.ProductionYear ?? 0)
                .ThenBy(g => g.First.SeriesName ?? g.First.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.First.SeasonNumber ?? 0)
                .ThenBy(g => g.First.EpisodeNumber ?? 0),
            "series" => groups
                .OrderBy(g => g.First.SeriesName ?? g.First.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.First.SeasonNumber ?? 0)
                .ThenBy(g => g.First.EpisodeNumber ?? 0)
                .ThenBy(g => g.First.Name, StringComparer.OrdinalIgnoreCase),
            "date" => groups
                .OrderByDescending(g => g.First.PremiereDate ?? DateTime.MinValue)
                .ThenBy(g => g.First.SeriesName ?? g.First.Name, StringComparer.OrdinalIgnoreCase),
            "added" => groups
                .OrderByDescending(g => g.First.DateCreated)
                .ThenBy(g => g.First.SeriesName ?? g.First.Name, StringComparer.OrdinalIgnoreCase),
            _ => groups
                .OrderBy(g => g.First.SeriesName ?? g.First.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.First.SeasonNumber ?? 0)
                .ThenBy(g => g.First.EpisodeNumber ?? 0)
                .ThenBy(g => g.First.Name, StringComparer.OrdinalIgnoreCase)
        };

        return ordered.ToList();
    }

    private static bool ShouldIndex(BaseItem item)
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.EnableAudioBrowser == false)
        {
            return false;
        }

        var kind = item.GetBaseItemKind();
        return kind switch
        {
            BaseItemKind.Movie => config?.IndexMovies ?? true,
            BaseItemKind.Episode => config?.IndexEpisodes ?? true,
            BaseItemKind.Video => config?.IndexVideos ?? false,
            _ => false
        };
    }

    private static BaseItemKind[] ResolveItemKinds(Configuration.PluginConfiguration? config)
    {
        var kinds = new List<BaseItemKind>();
        if (config?.IndexMovies ?? true)
        {
            kinds.Add(BaseItemKind.Movie);
        }

        if (config?.IndexEpisodes ?? true)
        {
            kinds.Add(BaseItemKind.Episode);
        }

        if (config?.IndexVideos ?? false)
        {
            kinds.Add(BaseItemKind.Video);
        }

        if (kinds.Count == 0)
        {
            kinds.Add(BaseItemKind.Movie);
            kinds.Add(BaseItemKind.Episode);
        }

        return kinds.ToArray();
    }

    private sealed class IndexedTrack
    {
        public Guid ItemId { get; init; }

        public string Name { get; init; } = string.Empty;

        public string MediaType { get; init; } = string.Empty;

        public string? SeriesName { get; init; }

        public Guid? SeriesId { get; init; }

        public int? SeasonNumber { get; init; }

        public int? EpisodeNumber { get; init; }

        public int? ProductionYear { get; init; }

        public DateTime? PremiereDate { get; init; }

        public DateTime DateCreated { get; init; }

        public int StreamIndex { get; init; }

        public string TypeId { get; init; } = string.Empty;

        public string TypeName { get; init; } = string.Empty;

        public string? Title { get; init; }

        public string? Language { get; init; }

        public string? LanguageName { get; init; }

        public string? Codec { get; init; }

        public int? Channels { get; init; }

        public string DisplayTitle { get; init; } = string.Empty;

        public string SearchText { get; init; } = string.Empty;
    }
}
