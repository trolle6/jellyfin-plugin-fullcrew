using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.FullCrew.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Persistent per-item scene identify index (builds X-Ray metadata over time).
/// </summary>
public sealed class SceneIndexStore
{
    /// <summary>Bucket width for grouping nearby timestamps.</summary>
    public const int BucketSeconds = 10;

    /// <summary>How many adjacent buckets to search when looking up a position.</summary>
    public const int LookupRadiusBuckets = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ILogger<SceneIndexStore> _logger;
    private readonly object _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneIndexStore"/> class.
    /// </summary>
    public SceneIndexStore(ILogger<SceneIndexStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Converts ticks to a bucket index in seconds.
    /// </summary>
    public static long ToBucketSeconds(long positionTicks)
    {
        if (positionTicks < 0)
        {
            positionTicks = 0;
        }

        var seconds = positionTicks / TimeSpan.TicksPerSecond;
        return seconds - (seconds % BucketSeconds);
    }

    /// <summary>
    /// Finds a nearby indexed scene for an item position.
    /// </summary>
    public SceneIdentifyResponse? FindNearby(Guid itemId, long positionTicks)
    {
        var doc = Load(itemId);
        if (doc.Entries.Count == 0)
        {
            return null;
        }

        var center = ToBucketSeconds(positionTicks);
        SceneIndexEntry? best = null;
        var bestDelta = long.MaxValue;

        foreach (var entry in doc.Entries)
        {
            var delta = Math.Abs(entry.BucketSeconds - center);
            if (delta > LookupRadiusBuckets * BucketSeconds)
            {
                continue;
            }

            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = entry;
            }
        }

        if (best is null)
        {
            return null;
        }

        return ToResponse(itemId, best, fromIndex: true);
    }

    /// <summary>
    /// Returns how many indexed moments exist for an item.
    /// </summary>
    public int Count(Guid itemId) => Load(itemId).Entries.Count;

    /// <summary>
    /// Upserts an identify result into the index for this position.
    /// </summary>
    public void Upsert(Guid itemId, long positionTicks, IReadOnlyList<SceneIdentifyMatch> matches, string? note)
    {
        var bucket = ToBucketSeconds(positionTicks);
        lock (_gate)
        {
            var doc = LoadUnlocked(itemId);
            doc.ItemId = itemId.ToString("N");
            var existing = doc.Entries.FirstOrDefault(e => e.BucketSeconds == bucket);
            if (existing is null)
            {
                existing = new SceneIndexEntry { BucketSeconds = bucket };
                doc.Entries.Add(existing);
            }

            existing.PositionTicks = positionTicks;
            existing.Matches = matches.Select(CloneMatch).ToList();
            existing.Note = note;
            existing.CreatedAt = DateTimeOffset.UtcNow;

            // Keep index bounded per title.
            if (doc.Entries.Count > 500)
            {
                doc.Entries = doc.Entries
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(500)
                    .OrderBy(e => e.BucketSeconds)
                    .ToList();
            }
            else
            {
                doc.Entries = doc.Entries.OrderBy(e => e.BucketSeconds).ToList();
            }

            SaveUnlocked(itemId, doc);
        }
    }

    private SceneIdentifyResponse ToResponse(Guid itemId, SceneIndexEntry entry, bool fromIndex)
    {
        return new SceneIdentifyResponse
        {
            ItemId = itemId.ToString("N"),
            Matches = entry.Matches,
            Note = entry.Note,
            FromCache = fromIndex,
            FromSceneIndex = fromIndex,
            PositionTicks = entry.PositionTicks,
            Source = fromIndex ? "index" : "vision"
        };
    }

    private SceneIndexDocument Load(Guid itemId)
    {
        lock (_gate)
        {
            return LoadUnlocked(itemId);
        }
    }

    private SceneIndexDocument LoadUnlocked(Guid itemId)
    {
        var path = GetPath(itemId);
        if (path is null || !File.Exists(path))
        {
            return new SceneIndexDocument { ItemId = itemId.ToString("N") };
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<SceneIndexDocument>(json, JsonOptions);
            return doc ?? new SceneIndexDocument { ItemId = itemId.ToString("N") };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed reading scene index {Path}", path);
            return new SceneIndexDocument { ItemId = itemId.ToString("N") };
        }
    }

    private void SaveUnlocked(Guid itemId, SceneIndexDocument doc)
    {
        var path = GetPath(itemId);
        if (path is null)
        {
            _logger.LogWarning("No writable data folder for scene index");
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
            _logger.LogWarning(ex, "Failed writing scene index {Path}", path);
        }
    }

    private static string? GetPath(Guid itemId)
    {
        var root = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        return Path.Combine(root, "scene-index", itemId.ToString("N") + ".json");
    }

    private static SceneIdentifyMatch CloneMatch(SceneIdentifyMatch m) => new()
    {
        Name = m.Name,
        Role = m.Role,
        TmdbPersonId = m.TmdbPersonId,
        ProfileUrl = m.ProfileUrl,
        Confidence = m.Confidence
    };
}
