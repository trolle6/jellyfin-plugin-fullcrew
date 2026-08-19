using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.FullCrew.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Persists which bumpers were already shown per show title.
/// </summary>
public sealed class BumperHistoryStore
{
    private const int MaxEntriesPerShow = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ILogger<BumperHistoryStore> _logger;
    private readonly object _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="BumperHistoryStore"/> class.
    /// </summary>
    public BumperHistoryStore(ILogger<BumperHistoryStore> logger)
    {
        _logger = logger;
    }

    /// <summary>Builds a filesystem-safe key from a show title.</summary>
    public static string NormalizeShowKey(string showTitle)
    {
        if (string.IsNullOrWhiteSpace(showTitle))
        {
            return "unknown";
        }

        var trimmed = showTitle.Trim().ToLowerInvariant();
        trimmed = Regex.Replace(trimmed, @"[^\w\s-]", string.Empty);
        trimmed = Regex.Replace(trimmed, @"\s+", "-");
        return trimmed.Length > 120 ? trimmed[..120] : trimmed;
    }

    /// <summary>Stable key for a local library bumper.</summary>
    public static string LocalKey(Guid itemId) => "local:" + itemId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>Stable key for a YouTube bumper.</summary>
    public static string YouTubeKey(string videoId) => "yt:" + videoId.Trim();

    /// <summary>Returns seen bumper keys for a show (oldest first).</summary>
    public HashSet<string> GetSeenKeys(string showKey)
    {
        lock (_gate)
        {
            return LoadUnlocked(showKey).Entries
                .Select(e => e.Key)
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    /// <summary>Records that a bumper was served for this show.</summary>
    public void MarkSeen(string showKey, string bumperKey)
    {
        if (string.IsNullOrWhiteSpace(showKey) || string.IsNullOrWhiteSpace(bumperKey))
        {
            return;
        }

        lock (_gate)
        {
            var doc = LoadUnlocked(showKey);
            doc.ShowKey = showKey;
            doc.Entries.RemoveAll(e => string.Equals(e.Key, bumperKey, StringComparison.Ordinal));
            doc.Entries.Add(new BumperHistoryEntry
            {
                Key = bumperKey,
                LastSeenAt = DateTimeOffset.UtcNow
            });

            if (doc.Entries.Count > MaxEntriesPerShow)
            {
                doc.Entries = doc.Entries
                    .OrderByDescending(e => e.LastSeenAt)
                    .Take(MaxEntriesPerShow)
                    .OrderBy(e => e.LastSeenAt)
                    .ToList();
            }

            SaveUnlocked(showKey, doc);
        }
    }

    /// <summary>Clears history for a show so picks can start over.</summary>
    public void Clear(string showKey)
    {
        lock (_gate)
        {
            SaveUnlocked(showKey, new BumperHistoryDocument { ShowKey = showKey });
        }
    }

    /// <summary>
    /// Picks the first ranked candidate that is not skipped and not yet seen.
    /// When every candidate was seen, history is cleared and the best non-skipped pick is returned.
    /// </summary>
    public static T? PickUnseen<T>(
        IReadOnlyList<T> ranked,
        Func<T, string> keySelector,
        IReadOnlySet<string> seen,
        string? skipKey,
        Action? onPoolExhausted)
    {
        if (ranked.Count == 0)
        {
            return default;
        }

        var pick = TryPick(ranked, keySelector, seen, skipKey);
        if (pick is not null)
        {
            return pick;
        }

        onPoolExhausted?.Invoke();
        return TryPick(ranked, keySelector, seen: new HashSet<string>(StringComparer.Ordinal), skipKey);
    }

    private static T? TryPick<T>(
        IReadOnlyList<T> ranked,
        Func<T, string> keySelector,
        IReadOnlySet<string> seen,
        string? skipKey)
    {
        foreach (var item in ranked)
        {
            var key = keySelector(item);
            if (!string.IsNullOrEmpty(skipKey) && string.Equals(key, skipKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (seen.Contains(key))
            {
                continue;
            }

            return item;
        }

        return default;
    }

    private BumperHistoryDocument LoadUnlocked(string showKey)
    {
        var path = GetPath(showKey);
        if (path is null || !File.Exists(path))
        {
            return new BumperHistoryDocument { ShowKey = showKey };
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<BumperHistoryDocument>(json, JsonOptions);
            return doc ?? new BumperHistoryDocument { ShowKey = showKey };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed reading bumper history {Path}", path);
            return new BumperHistoryDocument { ShowKey = showKey };
        }
    }

    private void SaveUnlocked(string showKey, BumperHistoryDocument doc)
    {
        var path = GetPath(showKey);
        if (path is null)
        {
            _logger.LogWarning("No writable data folder for bumper history");
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed writing bumper history {Path}", path);
        }
    }

    private static string? GetPath(string showKey)
    {
        var root = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(showKey))
        {
            return null;
        }

        return Path.Combine(root, "bumper-history", showKey + ".json");
    }
}
