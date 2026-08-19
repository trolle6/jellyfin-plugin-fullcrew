using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.FullCrew.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Resolves nostalgia break-bumpers from the local library or YouTube,
/// keyed to the specific show/movie the user is viewing.
/// </summary>
public partial class BumperService
{
    private const int MaxBumperSeconds = 5 * 60;
    private const int MaxTrailerSeconds = 6 * 60;

    private readonly ILibraryManager _libraryManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BumperHistoryStore _bumperHistory;
    private readonly ILogger<BumperService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BumperService"/> class.
    /// </summary>
    public BumperService(
        ILibraryManager libraryManager,
        IHttpClientFactory httpClientFactory,
        BumperHistoryStore bumperHistory,
        ILogger<BumperService> logger)
    {
        _libraryManager = libraryManager;
        _httpClientFactory = httpClientFactory;
        _bumperHistory = bumperHistory;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a bumper for the given Jellyfin item (local → show-specific YouTube).
    /// </summary>
    /// <param name="itemId">Library item id.</param>
    /// <param name="tryAnother">When true, skip <paramref name="skipKey"/> and pick the next unseen bumper.</param>
    /// <param name="skipKey">Bumper key to skip (current clip when trying another).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<BumperResponse> GetBumperAsync(
        Guid itemId,
        bool tryAnother,
        string? skipKey,
        CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return new BumperResponse
            {
                ItemId = itemId.ToString("N", CultureInfo.InvariantCulture),
                Error = "Item not found."
            };
        }

        var config = Plugin.Instance?.Configuration;
        if (config is { EnableBumpers: false })
        {
            return new BumperResponse
            {
                ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                Error = "Bumpers are disabled in Full Crew settings."
            };
        }

        var showTitle = ResolveShowTitle(item);
        var hints = CollectNetworkHints(item).ToList();
        var showKey = BumperHistoryStore.NormalizeShowKey(showTitle);
        var seen = _bumperHistory.GetSeenKeys(showKey);
        var activeSkip = tryAnother ? skipKey : null;

        var collectionName = string.IsNullOrWhiteSpace(config?.BumpersCollectionName)
            ? "Bumpers"
            : config!.BumpersCollectionName.Trim();

        var localRanked = GetLocalBumperCandidates(showTitle, hints, collectionName);
        var local = BumperHistoryStore.PickUnseen(
            localRanked,
            item => BumperHistoryStore.LocalKey(item.Id),
            seen,
            activeSkip,
            () => _bumperHistory.Clear(showKey));

        if (local is not null)
        {
            var localKey = BumperHistoryStore.LocalKey(local.Id);
            _bumperHistory.MarkSeen(showKey, localKey);
            return new BumperResponse
            {
                ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                Source = "Local",
                Title = local.Name,
                Network = hints.FirstOrDefault(),
                LocalItemId = local.Id.ToString("N", CultureInfo.InvariantCulture),
                NetworkHints = hints,
                BumperKey = localKey,
                AlternatesAvailable = CountAlternates(localRanked, item => BumperHistoryStore.LocalKey(item.Id), seen, localKey)
            };
        }

        if (config is null or { EnableYouTubeBumpers: true })
        {
            var youtubeRanked = await CollectYouTubeBumperCandidatesAsync(showTitle, hints, cancellationToken)
                .ConfigureAwait(false);
            var youtubePick = BumperHistoryStore.PickUnseen(
                youtubeRanked,
                c => BumperHistoryStore.YouTubeKey(c.Id),
                seen,
                activeSkip,
                () => _bumperHistory.Clear(showKey));

            if (!string.IsNullOrWhiteSpace(youtubePick.Id))
            {
                var ytKey = BumperHistoryStore.YouTubeKey(youtubePick.Id);
                _bumperHistory.MarkSeen(showKey, ytKey);
                return new BumperResponse
                {
                    ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                    Source = "YouTube",
                    Title = youtubePick.Title,
                    YouTubeVideoId = youtubePick.Id,
                    YouTubeUrl = "https://www.youtube.com/watch?v=" + youtubePick.Id,
                    SearchUrl = "https://www.youtube.com/results?search_query="
                        + Uri.EscapeDataString(BuildShowSearchQuery(showTitle)),
                    Network = hints.FirstOrDefault(),
                    NetworkHints = hints,
                    BumperKey = ytKey,
                    AlternatesAvailable = CountAlternates(
                        youtubeRanked,
                        c => BumperHistoryStore.YouTubeKey(c.Id),
                        seen,
                        ytKey)
                };
            }

            var query = BuildShowSearchQuery(showTitle);
            return new BumperResponse
            {
                ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                Source = "Search",
                Title = query,
                Network = hints.FirstOrDefault(),
                SearchUrl = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query),
                NetworkHints = hints,
                Error = "Couldn't find a short bumper clip automatically. Open search to pick one."
            };
        }

        return new BumperResponse
        {
            ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
            NetworkHints = hints,
            Error = "No local bumpers found. Enable YouTube bumpers or add a \"" + collectionName + "\" collection."
        };
    }

    /// <summary>
    /// Resolves an official trailer clip for the item when Jellyfin has no trailer metadata.
    /// </summary>
    public async Task<BumperResponse> GetTrailerAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return new BumperResponse
            {
                ItemId = itemId.ToString("N", CultureInfo.InvariantCulture),
                Error = "Item not found."
            };
        }

        var showTitle = ResolveShowTitle(item);
        if (string.IsNullOrWhiteSpace(showTitle))
        {
            return new BumperResponse
            {
                ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                Error = "Couldn't resolve a title for trailer search."
            };
        }

        var query = $"\"{showTitle}\" official trailer";
        var searchUrl = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query);
        var config = Plugin.Instance?.Configuration;

        // Honour the same YouTube gate as bumpers so disabling lookups stops outbound requests.
        if (config is null or { EnableYouTubeBumpers: true })
        {
            var youtube = await TryFindYouTubeTrailerAsync(showTitle, cancellationToken).ConfigureAwait(false);
            if (youtube is not null)
            {
                youtube.ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture);
                return youtube;
            }
        }

        return new BumperResponse
        {
            ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
            Source = "Search",
            Title = query,
            SearchUrl = searchUrl,
            Error = config is { EnableYouTubeBumpers: false }
                ? "YouTube lookups are disabled in Full Crew settings. Open search to pick a trailer."
                : "Couldn't find a trailer automatically. Open search to pick one."
        };
    }

    private async Task<IReadOnlyList<(string Id, string Title, int Seconds)>> CollectYouTubeBumperCandidatesAsync(
        string showTitle,
        IReadOnlyList<string> hints,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(showTitle))
        {
            return [];
        }

        var queries = BuildShowBumperQueries(showTitle, hints);

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetCts.CancelAfter(TimeSpan.FromSeconds(20));
        var budgetToken = budgetCts.Token;

        var candidates = new List<(string Id, string Title, int Seconds)>();
        foreach (var query in queries)
        {
            if (budgetToken.IsCancellationRequested)
            {
                break;
            }

            var found = await SearchYouTubeShortAsync(query, showTitle, MaxBumperSeconds, budgetToken).ConfigureAwait(false);
            candidates.AddRange(found);
            if (candidates.Count >= 16)
            {
                break;
            }
        }

        candidates = candidates
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .Where(c => c.Seconds > 0 && c.Seconds <= MaxBumperSeconds)
            .ToList();

        if (candidates.Count == 0)
        {
            return [];
        }

        return candidates
            .OrderByDescending(c => BumperTitleScore(c.Title, showTitle))
            .ThenBy(c => c.Seconds)
            .Take(20)
            .ToList();
    }

    private static int CountAlternates<T>(
        IReadOnlyList<T> ranked,
        Func<T, string> keySelector,
        IReadOnlySet<string> seen,
        string currentKey)
    {
        var count = 0;
        foreach (var item in ranked)
        {
            var key = keySelector(item);
            if (string.Equals(key, currentKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (!seen.Contains(key))
            {
                count++;
            }
        }

        return count;
    }

    private async Task<BumperResponse?> TryFindYouTubeTrailerAsync(string showTitle, CancellationToken cancellationToken)
    {
        var queries = new[]
        {
            $"\"{showTitle}\" official trailer",
            $"{showTitle} official trailer",
            $"\"{showTitle}\" trailer"
        };

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetCts.CancelAfter(TimeSpan.FromSeconds(15));
        var budgetToken = budgetCts.Token;

        var candidates = new List<(string Id, string Title, int Seconds)>();
        foreach (var query in queries)
        {
            if (budgetToken.IsCancellationRequested)
            {
                break;
            }

            var found = await SearchYouTubeShortAsync(query, showTitle, MaxTrailerSeconds, budgetToken).ConfigureAwait(false);
            candidates.AddRange(found);
            if (candidates.Count >= 8)
            {
                break;
            }
        }

        candidates = candidates
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .Where(c => c.Seconds > 0 && c.Seconds <= MaxTrailerSeconds)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var ranked = candidates
            .OrderByDescending(c => TrailerTitleScore(c.Title, showTitle))
            .ThenBy(c => Math.Abs(c.Seconds - 90)) // prefer ~90s trailers
            .Take(5)
            .ToList();

        var pick = ranked[0];
        return new BumperResponse
        {
            Source = "YouTube",
            Title = pick.Title,
            YouTubeVideoId = pick.Id,
            YouTubeUrl = "https://www.youtube.com/watch?v=" + pick.Id,
            SearchUrl = "https://www.youtube.com/results?search_query="
                + Uri.EscapeDataString($"\"{showTitle}\" official trailer")
        };
    }

    private async Task<IReadOnlyList<(string Id, string Title, int Seconds)>> SearchYouTubeShortAsync(
        string query,
        string showTitle,
        int maxSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var payload = new Dictionary<string, object>
            {
                ["context"] = new Dictionary<string, object>
                {
                    ["client"] = new Dictionary<string, object>
                    {
                        ["clientName"] = "WEB",
                        ["clientVersion"] = "2.20240101.00.00"
                    }
                },
                ["query"] = query
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://www.youtube.com/youtubei/v1/search?prettyPrint=false")
            {
                Content = content
            };
            request.Headers.TryAddWithoutValidation("User-Agent", PluginInfo.UserAgent);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await client
                .SendAsync(request, timeoutCts.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("YouTube search failed ({Status}) for {Query}", (int)response.StatusCode, query);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseYouTubeSearchResults(json, showTitle, maxSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "YouTube search error for {Query}", query);
            return [];
        }
    }

    private static IReadOnlyList<(string Id, string Title, int Seconds)> ParseYouTubeSearchResults(
        string json,
        string showTitle,
        int maxSeconds)
    {
        var results = new List<(string Id, string Title, int Seconds)>();

        var parts = json.Split("\"videoRenderer\":{", StringSplitOptions.None);
        for (var i = 1; i < parts.Length; i++)
        {
            var chunk = parts[i].Length > 5000 ? parts[i][..5000] : parts[i];

            var idMatch = Regex.Match(chunk, "^\\s*\"videoId\"\\s*:\\s*\"([A-Za-z0-9_-]{11})\"");
            if (!idMatch.Success)
            {
                idMatch = VideoIdRegex().Match(chunk);
            }

            var id = idMatch.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var length = LengthTextRegex().Match(chunk).Groups[1].Value;
            var seconds = ParseDurationSeconds(length);
            if (seconds is null)
            {
                var labelMatch = AccessibilitySecondsRegex().Match(chunk);
                if (labelMatch.Success
                    && int.TryParse(labelMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var labeledSeconds))
                {
                    seconds = labeledSeconds;
                }
            }

            if (seconds is null or <= 0 || seconds > maxSeconds)
            {
                continue;
            }

            var title = TitleRunRegex().Match(chunk).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = showTitle;
            }

            title = DecodeJsonString(title);
            results.Add((id, title, seconds.Value));
        }

        if (results.Count > 0)
        {
            return results;
        }

        foreach (Match m in PlaylistClipRegex().Matches(json))
        {
            var title = DecodeJsonString(m.Groups[1].Value);
            var seconds = ParseDurationSeconds(m.Groups[2].Value);
            var id = m.Groups[3].Value;
            if (seconds is null or <= 0 || seconds > maxSeconds || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            results.Add((id, title, seconds.Value));
        }

        return results;
    }

    private static int TrailerTitleScore(string title, string showTitle)
    {
        var score = 0;
        if (title.Contains(showTitle, StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (ContainsAny(title, "official trailer", "trailer"))
        {
            score += 4;
        }

        if (ContainsAny(title, "official"))
        {
            score += 2;
        }

        if (ContainsAny(title, "teaser", "trailer 2", "trailer #2", "season"))
        {
            score += 1;
        }

        if (ContainsAny(title, "reaction", "review", "explained", "breakdown", "fan made", "fan-made", "mashup"))
        {
            score -= 6;
        }

        return score;
    }

    private static IReadOnlyList<string> BuildShowBumperQueries(string showTitle, IReadOnlyList<string> hints)
    {
        var quoted = $"\"{showTitle.Trim()}\"";
        var queries = new List<string>
        {
            $"{quoted} bumper",
            $"{showTitle} bumper",
            $"{quoted} promo",
            $"{quoted} ident",
            $"{quoted} \"next up\"",
            $"{quoted} \"coming up next\"",
            $"{quoted} \"we'll be right back\"",
            $"{quoted} \"station id\"",
            $"{quoted} bump",
            $"{quoted} \"back to the show\"",
            $"{showTitle} \"and now back to\"",
        };

        var network = hints.FirstOrDefault(h => !string.Equals(h, "TV bumpers", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(network))
        {
            queries.Insert(1, $"{quoted} {network} bumper");
            queries.Insert(3, $"{quoted} {network} promo");
            queries.Insert(5, $"{quoted} {network} ident");
        }

        return queries;
    }

    private static int BumperTitleScore(string title, string showTitle)
    {
        var score = 0;
        if (title.Contains(showTitle, StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (ContainsAny(title,
                "bumper",
                "ident",
                "station id",
                "station identification",
                "coming up next",
                "next up",
                "we'll be right back",
                "we will be right back",
                "back to the show",
                "and now back to"))
        {
            score += 4;
        }
        else if (ContainsAny(title, "promo", "promotional"))
        {
            score += 3;
        }
        else if (BumpWordRegex().IsMatch(title))
        {
            // Standalone "bump" / "bumps" (not already counted via "bumper").
            score += 3;
        }

        if (ContainsAny(title,
                "compilation",
                "archive",
                "complete",
                "every bumper",
                "all bumpers",
                "hour",
                "full episode",
                "reaction",
                "explained"))
        {
            score -= 5;
        }

        return score;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
        => needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static int? ParseDurationSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Trim().Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        try
        {
            if (parts.Length == 2)
            {
                return (int.Parse(parts[0], CultureInfo.InvariantCulture) * 60)
                       + int.Parse(parts[1], CultureInfo.InvariantCulture);
            }

            return (int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600)
                   + (int.Parse(parts[1], CultureInfo.InvariantCulture) * 60)
                   + int.Parse(parts[2], CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static string DecodeJsonString(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>("\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"")
                   ?? value;
        }
        catch
        {
            return value
                .Replace("\\u0026", "&", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal);
        }
    }

    private static string ResolveShowTitle(BaseItem item)
    {
        if (item is Episode episode && !string.IsNullOrWhiteSpace(episode.Series?.Name))
        {
            return episode.Series.Name.Trim();
        }

        if (item is Season season && !string.IsNullOrWhiteSpace(season.Series?.Name))
        {
            return season.Series.Name.Trim();
        }

        return (item.Name ?? string.Empty).Trim();
    }

    private static string BuildShowSearchQuery(string showTitle)
        => string.IsNullOrWhiteSpace(showTitle) ? "TV bumper" : $"\"{showTitle.Trim()}\" bumper";

    private IReadOnlyList<BaseItem> GetLocalBumperCandidates(string showTitle, IReadOnlyList<string> hints, string collectionName)
    {
        try
        {
            var candidates = new List<BaseItem>();

            var roots = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = [BaseItemKind.BoxSet, BaseItemKind.Folder, BaseItemKind.CollectionFolder],
                Name = collectionName
            });

            foreach (var root in roots)
            {
                var children = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ParentId = root.Id,
                    Recursive = true,
                    IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Video, BaseItemKind.Episode, BaseItemKind.Trailer]
                });
                candidates.AddRange(children);
            }

            if (!string.IsNullOrWhiteSpace(showTitle))
            {
                var byShow = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Recursive = true,
                    IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Video, BaseItemKind.Episode, BaseItemKind.Trailer],
                    SearchTerm = showTitle + " bumper",
                    Limit = 40
                });
                candidates.AddRange(byShow);
            }

            var searched = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Video, BaseItemKind.Episode, BaseItemKind.Trailer],
                SearchTerm = "bumper",
                Limit = 80
            });
            candidates.AddRange(searched);

            candidates = candidates
                .Where(c => c is not null)
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .Where(LooksLikeBumper)
                .ToList();

            if (candidates.Count == 0)
            {
                return [];
            }

            var showMatched = candidates
                .Where(c => !string.IsNullOrWhiteSpace(showTitle)
                            && ((c.Name?.Contains(showTitle, StringComparison.OrdinalIgnoreCase) ?? false)
                                || (c.Path?.Contains(showTitle, StringComparison.OrdinalIgnoreCase) ?? false)))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var showIds = showMatched.Select(c => c.Id).ToHashSet();
            var networkMatched = candidates
                .Where(c => !showIds.Contains(c.Id))
                .Where(c => hints.Any(h =>
                    (c.Name?.Contains(h, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (c.Path?.Contains(h, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (c.Studios?.Any(s => s.Contains(h, StringComparison.OrdinalIgnoreCase)) ?? false)
                    || (c.Tags?.Any(t => t.Contains(h, StringComparison.OrdinalIgnoreCase)) ?? false)))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var tierIds = showMatched.Select(c => c.Id).Concat(networkMatched.Select(c => c.Id)).ToHashSet();
            var remainder = candidates
                .Where(c => !tierIds.Contains(c.Id))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ranked = new List<BaseItem>(candidates.Count);
            ranked.AddRange(showMatched);
            ranked.AddRange(networkMatched);
            ranked.AddRange(remainder);
            return ranked;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Local bumper search failed");
            return [];
        }
    }

    private static bool LooksLikeBumper(BaseItem item)
    {
        var name = item.Name ?? string.Empty;
        var path = item.Path ?? string.Empty;
        var tags = item.Tags ?? [];
        var blob = string.Join(' ', name, path, string.Join(' ', tags));

        if (ContainsAny(blob,
                "bumper",
                "ident",
                "station id",
                "next up",
                "coming up next",
                "we'll be right back",
                "promo",
                "/bumpers/",
                "\\bumpers\\"))
        {
            return true;
        }

        return BumpWordRegex().IsMatch(blob);
    }

    private static IEnumerable<string> CollectNetworkHints(BaseItem item)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmed = value.Trim();
            if (trimmed.Length < 2)
            {
                return;
            }

            seen.Add(trimmed);
        }

        void AddFrom(BaseItem? source)
        {
            if (source is null)
            {
                return;
            }

            if (source.Studios is { Length: > 0 })
            {
                foreach (var studio in source.Studios)
                {
                    Add(studio);
                }
            }

            if (source.Tags is { Length: > 0 })
            {
                foreach (var tag in source.Tags)
                {
                    if (tag.Contains("network", StringComparison.OrdinalIgnoreCase)
                        || tag.Contains("cartoon", StringComparison.OrdinalIgnoreCase)
                        || tag.Contains("nick", StringComparison.OrdinalIgnoreCase))
                    {
                        Add(tag);
                    }
                }
            }
        }

        AddFrom(item);

        if (item is Episode episode)
        {
            AddFrom(episode.Series);
            AddFrom(episode.Season);
        }
        else if (item is Season season)
        {
            AddFrom(season.Series);
        }

        if (seen.Count == 0)
        {
            Add("TV bumpers");
        }

        return seen;
    }

    [GeneratedRegex("\"videoId\"\\s*:\\s*\"([A-Za-z0-9_-]{11})\"")]
    private static partial Regex VideoIdRegex();

    [GeneratedRegex("\"lengthText\"\\s*:\\s*\\{[\\s\\S]*?\"simpleText\"\\s*:\\s*\"([0-9:]+)\"")]
    private static partial Regex LengthTextRegex();

    [GeneratedRegex("\"label\"\\s*:\\s*\"(\\d+)\\s+seconds?\"", RegexOptions.IgnoreCase)]
    private static partial Regex AccessibilitySecondsRegex();

    [GeneratedRegex("\"title\"\\s*:\\s*\\{\\s*\"runs\"\\s*:\\s*\\[\\s*\\{\\s*\"text\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"")]
    private static partial Regex TitleRunRegex();

    [GeneratedRegex("\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\]){3,120}?)\\s*·\\s*([0-9:]{3,8})\"[\\s\\S]{0,400}?\"videoId\"\\s*:\\s*\"([A-Za-z0-9_-]{11})\"")]
    private static partial Regex PlaylistClipRegex();

    [GeneratedRegex(@"\bbumps?\b", RegexOptions.IgnoreCase)]
    private static partial Regex BumpWordRegex();
}
