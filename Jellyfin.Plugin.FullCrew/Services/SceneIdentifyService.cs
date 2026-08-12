using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.FullCrew.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// On-demand cast-grounded frame identification via OpenAI Vision.
/// </summary>
public sealed class SceneIdentifyService
{
    private const int MaxImageBytes = 450_000;
    private const int MaxCastCandidates = 40;
    private const int RateLimitPerMinute = 12;
    private static readonly TimeSpan FrameCacheTtl = TimeSpan.FromMinutes(10);

    private readonly CreditsService _creditsService;
    private readonly SceneIndexStore _sceneIndex;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<SceneIdentifyService> _logger;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<long>> _rateBuckets = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneIdentifyService"/> class.
    /// </summary>
    public SceneIdentifyService(
        CreditsService creditsService,
        SceneIndexStore sceneIndex,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        ILogger<SceneIdentifyService> logger)
    {
        _creditsService = creditsService;
        _sceneIndex = sceneIndex;
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    /// <summary>
    /// Returns whether scene identify is ready (opt-in + API key).
    /// </summary>
    public SceneIdentifyStatusResponse GetStatus()
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableSceneIdentify)
        {
            return new SceneIdentifyStatusResponse
            {
                Enabled = false,
                Reason = "Disabled in plugin settings (opt-in)."
            };
        }

        if (string.IsNullOrWhiteSpace(config.OpenAiApiKey))
        {
            return new SceneIdentifyStatusResponse
            {
                Enabled = false,
                Reason = "Add an OpenAI API key in Full Crew settings.",
                Model = string.IsNullOrWhiteSpace(config.OpenAiVisionModel) ? "gpt-4o-mini" : config.OpenAiVisionModel.Trim()
            };
        }

        return new SceneIdentifyStatusResponse
        {
            Enabled = true,
            Model = string.IsNullOrWhiteSpace(config.OpenAiVisionModel) ? "gpt-4o-mini" : config.OpenAiVisionModel.Trim()
        };
    }

    /// <summary>
    /// Title cast + nearby indexed scene for playback (no OpenAI call).
    /// </summary>
    public async Task<PlaybackSceneResponse> GetPlaybackSceneAsync(
        Guid itemId,
        long? positionTicks,
        CancellationToken cancellationToken)
    {
        var vision = GetStatus();
        var credits = await _creditsService
            .GetCreditsAsync(itemId, cancellationToken, forceIncludeCast: true, preferSeriesAggregate: true)
            .ConfigureAwait(false);
        var cast = ExtractCastCandidates(credits)
            .Select(c => new SceneIdentifyMatch
            {
                Name = c.Name,
                Role = c.Role,
                TmdbPersonId = c.TmdbPersonId,
                ProfileUrl = c.ProfileUrl
            })
            .ToList();

        SceneIdentifyResponse? scene = null;
        if (positionTicks is long ticks)
        {
            scene = _sceneIndex.FindNearby(itemId, ticks);
        }

        return new PlaybackSceneResponse
        {
            ItemId = itemId.ToString("N"),
            ItemName = string.IsNullOrWhiteSpace(credits.ItemName) ? null : credits.ItemName,
            Cast = cast,
            Scene = scene,
            IndexedSceneCount = _sceneIndex.Count(itemId),
            VisionEnabled = vision.Enabled,
            VisionReason = vision.Enabled ? null : vision.Reason,
            Error = credits.Error
        };
    }

    /// <summary>
    /// Identifies which billed cast members appear in a captured frame.
    /// </summary>
    public async Task<SceneIdentifyResponse> IdentifyAsync(
        Guid itemId,
        SceneIdentifyRequest request,
        string? rateLimitKey,
        CancellationToken cancellationToken)
    {
        // Prefer persistent scene index when we know the playback position (unless forced).
        if (!request.ForceRefresh && request.PositionTicks is long posTicks)
        {
            var indexed = _sceneIndex.FindNearby(itemId, posTicks);
            if (indexed is not null)
            {
                return indexed;
            }
        }

        var status = GetStatus();
        if (!status.Enabled)
        {
            return new SceneIdentifyResponse
            {
                ItemId = itemId.ToString("N"),
                Error = status.Reason ?? "Scene identify is not enabled.",
                PositionTicks = request.PositionTicks
            };
        }

        var config = Plugin.Instance!.Configuration;
        var apiKey = config.OpenAiApiKey.Trim();
        var model = string.IsNullOrWhiteSpace(config.OpenAiVisionModel)
            ? "gpt-4o-mini"
            : config.OpenAiVisionModel.Trim();

        if (!TryDecodeImage(request.ImageBase64, out var imageBytes, out var mime, out var decodeError))
        {
            return new SceneIdentifyResponse
            {
                ItemId = itemId.ToString("N"),
                Error = decodeError,
                PositionTicks = request.PositionTicks
            };
        }

        if (imageBytes.Length > MaxImageBytes)
        {
            return new SceneIdentifyResponse
            {
                ItemId = itemId.ToString("N"),
                Error = "Frame too large. Capture a smaller still (max ~450 KB).",
                PositionTicks = request.PositionTicks
            };
        }

        var bucketKey = string.IsNullOrWhiteSpace(rateLimitKey) ? "anon" : rateLimitKey.Trim();
        if (!TryConsumeRateLimit(bucketKey))
        {
            return new SceneIdentifyResponse
            {
                ItemId = itemId.ToString("N"),
                Error = "Rate limit: try again in a minute.",
                PositionTicks = request.PositionTicks
            };
        }

        var frameHash = Convert.ToHexString(SHA256.HashData(imageBytes));
        var cacheKey = $"fullcrew-scene-v1-{itemId:N}-{frameHash}";
        if (_memoryCache.TryGetValue(cacheKey, out SceneIdentifyResponse? cached) && cached is not null)
        {
            var memHit = new SceneIdentifyResponse
            {
                ItemId = cached.ItemId,
                Matches = cached.Matches,
                Note = cached.Note,
                Error = cached.Error,
                FromCache = true,
                Source = "memory",
                PositionTicks = request.PositionTicks
            };
            TryPersistIndex(itemId, request.PositionTicks, memHit);
            return memHit;
        }

        var credits = await _creditsService
            .GetCreditsAsync(itemId, cancellationToken, forceIncludeCast: true, preferSeriesAggregate: true)
            .ConfigureAwait(false);
        if (!string.IsNullOrEmpty(credits.Error))
        {
            return new SceneIdentifyResponse
            {
                ItemId = itemId.ToString("N"),
                Error = credits.Error,
                PositionTicks = request.PositionTicks
            };
        }

        var candidates = ExtractCastCandidates(credits);
        if (candidates.Count == 0)
        {
            return new SceneIdentifyResponse
            {
                ItemId = itemId.ToString("N"),
                Error = "No cast list available for this title.",
                Note = "Scene identify only matches people from this item's TMDB cast.",
                PositionTicks = request.PositionTicks
            };
        }

        try
        {
            var raw = await CallOpenAiAsync(
                    apiKey,
                    model,
                    imageBytes,
                    mime,
                    candidates,
                    cancellationToken)
                .ConfigureAwait(false);

            var matches = FilterToCast(raw, candidates);
            var response = new SceneIdentifyResponse
            {
                ItemId = itemId.ToString("N"),
                Matches = matches,
                Note = matches.Count == 0
                    ? "No confident cast matches in this frame."
                    : null,
                Source = "vision",
                PositionTicks = request.PositionTicks
            };

            _memoryCache.Set(cacheKey, response, FrameCacheTtl);
            TryPersistIndex(itemId, request.PositionTicks, response);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scene identify failed for {ItemId}", itemId);
            return new SceneIdentifyResponse
            {
                ItemId = itemId.ToString("N"),
                Error = "OpenAI identify request failed. Check the API key and server logs.",
                PositionTicks = request.PositionTicks
            };
        }
    }

    private void TryPersistIndex(Guid itemId, long? positionTicks, SceneIdentifyResponse response)
    {
        if (positionTicks is not long ticks || !string.IsNullOrEmpty(response.Error))
        {
            return;
        }

        try
        {
            _sceneIndex.Upsert(itemId, ticks, response.Matches, response.Note);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Scene index persist skipped for {ItemId}", itemId);
        }
    }

    /// <summary>Pull billed cast candidates from a credits response.</summary>
    public static IReadOnlyList<CastCandidate> ExtractCastCandidates(FullCrewResponse credits)
    {
        var castDept = credits.Departments?
            .FirstOrDefault(d => string.Equals(d.Name, "Cast", StringComparison.OrdinalIgnoreCase));

        IEnumerable<CrewPerson> people = castDept?.People ?? [];
        if (!people.Any(p => !string.IsNullOrWhiteSpace(p.Name))
            && credits.Departments is { Count: > 0 })
        {
            // Cast department missing/empty (e.g. disabled in accordion settings before forceInclude)
            // — fall back to any credited people so playback/identify still has candidates.
            people = credits.Departments
                .SelectMany(d => d.People ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p.Name));
        }

        return people
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.TmdbPersonId is int id and > 0
                ? "id:" + id.ToString(CultureInfo.InvariantCulture)
                : "name:" + p.Name.Trim().ToLowerInvariant())
            .Select(g => g
                .OrderBy(p => string.IsNullOrWhiteSpace(p.ProfileUrl) ? 1 : 0)
                .ThenBy(p => p.Order ?? int.MaxValue)
                .First())
            .OrderBy(p => p.Order ?? int.MaxValue)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxCastCandidates)
            .Select(p => new CastCandidate(
                p.Name.Trim(),
                string.IsNullOrWhiteSpace(p.Role) ? null : p.Role.Trim(),
                p.TmdbPersonId,
                p.ProfileUrl))
            .ToList();
    }

    /// <summary>Keep only Vision hits that map onto the provided cast list.</summary>
    public static IReadOnlyList<SceneIdentifyMatch> FilterToCast(
        IReadOnlyList<RawVisionMatch> raw,
        IReadOnlyList<CastCandidate> candidates)
    {
        if (raw.Count == 0 || candidates.Count == 0)
        {
            return [];
        }

        var byId = candidates
            .Where(c => c.TmdbPersonId.HasValue)
            .GroupBy(c => c.TmdbPersonId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var byName = new Dictionary<string, CastCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            var key = NormalizeName(c.Name);
            if (!byName.ContainsKey(key))
            {
                byName[key] = c;
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<SceneIdentifyMatch>();

        foreach (var hit in raw.OrderByDescending(r => r.Confidence ?? 0))
        {
            CastCandidate? candidate = null;
            if (hit.TmdbPersonId is int id && byId.TryGetValue(id, out var byIdHit))
            {
                candidate = byIdHit;
            }
            else if (!string.IsNullOrWhiteSpace(hit.Name))
            {
                var key = NormalizeName(hit.Name);
                byName.TryGetValue(key, out candidate);
                if (candidate is null)
                {
                    candidate = candidates.FirstOrDefault(c =>
                        NormalizeName(c.Name).Contains(key, StringComparison.Ordinal)
                        || key.Contains(NormalizeName(c.Name), StringComparison.Ordinal));
                }
            }

            if (candidate is null)
            {
                continue;
            }

            var dedupe = candidate.TmdbPersonId?.ToString(CultureInfo.InvariantCulture)
                         ?? NormalizeName(candidate.Name);
            if (!seen.Add(dedupe))
            {
                continue;
            }

            double? confidence = hit.Confidence;
            if (confidence is < 0 or > 1)
            {
                confidence = null;
            }

            // Drop very low-confidence guesses.
            if (confidence is < 0.35)
            {
                continue;
            }

            matches.Add(new SceneIdentifyMatch
            {
                Name = candidate.Name,
                Role = candidate.Role,
                TmdbPersonId = candidate.TmdbPersonId,
                ProfileUrl = candidate.ProfileUrl,
                Confidence = confidence
            });
        }

        return matches;
    }

    private async Task<IReadOnlyList<RawVisionMatch>> CallOpenAiAsync(
        string apiKey,
        string model,
        byte[] imageBytes,
        string mime,
        IReadOnlyList<CastCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var castJson = JsonSerializer.Serialize(
            candidates.Select(c => new
            {
                name = c.Name,
                tmdbPersonId = c.TmdbPersonId,
                role = c.Role
            }));

        var prompt =
            "You identify which people from a FIXED cast list appear in a single video still. "
            + "Only choose people from the provided cast JSON. Never invent names. "
            + "If nobody from the list is clearly visible, return an empty matches array. "
            + "Respond with JSON only: {\"matches\":[{\"name\":\"...\",\"tmdbPersonId\":123,\"confidence\":0.0}]}. "
            + "confidence is 0-1. Prefer fewer high-confidence matches over guessing.\n\n"
            + "Cast list:\n" + castJson;

        var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(imageBytes)}";
        var body = new
        {
            model,
            temperature = 0,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = prompt },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = dataUrl, detail = "low" }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", PluginInfo.UserAgent);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "OpenAI Vision HTTP {Status}: {Body}",
                (int)response.StatusCode,
                Truncate(responseText, 400));
            throw new InvalidOperationException($"OpenAI HTTP {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(responseText);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return ParseVisionContent(content);
    }

    /// <summary>Parse the model's JSON content into raw matches.</summary>
    public static IReadOnlyList<RawVisionMatch> ParseVisionContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var trimmed = content.Trim();
        // Models sometimes wrap JSON in fences.
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = Regex.Replace(trimmed, "^```(?:json)?\\s*|\\s*```$", string.Empty, RegexOptions.IgnoreCase).Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (!doc.RootElement.TryGetProperty("matches", out var matchesEl)
                || matchesEl.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<RawVisionMatch>();
            foreach (var el in matchesEl.EnumerateArray())
            {
                string? name = null;
                if (el.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                {
                    name = nameEl.GetString();
                }

                int? tmdbId = null;
                if (el.TryGetProperty("tmdbPersonId", out var idEl)
                    && idEl.ValueKind == JsonValueKind.Number
                    && idEl.TryGetInt32(out var idVal))
                {
                    tmdbId = idVal;
                }

                double? confidence = null;
                if (el.TryGetProperty("confidence", out var confEl)
                    && confEl.ValueKind == JsonValueKind.Number
                    && confEl.TryGetDouble(out var confVal))
                {
                    confidence = confVal;
                }

                if (string.IsNullOrWhiteSpace(name) && tmdbId is null)
                {
                    continue;
                }

                list.Add(new RawVisionMatch(name?.Trim(), tmdbId, confidence));
            }

            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool TryDecodeImage(
        string? input,
        out byte[] bytes,
        out string mime,
        out string? error)
    {
        bytes = [];
        mime = "image/jpeg";
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Missing image.";
            return false;
        }

        var raw = input.Trim();
        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = raw.IndexOf(',');
            if (comma < 0)
            {
                error = "Invalid data URL.";
                return false;
            }

            var header = raw[..comma];
            raw = raw[(comma + 1)..];
            var mimeMatch = Regex.Match(header, @"data:(image/(?:jpeg|jpg|png|webp));base64", RegexOptions.IgnoreCase);
            if (mimeMatch.Success)
            {
                mime = mimeMatch.Groups[1].Value.ToLowerInvariant();
                if (mime == "image/jpg")
                {
                    mime = "image/jpeg";
                }
            }
            else
            {
                error = "Only JPEG, PNG, or WebP frames are accepted.";
                return false;
            }
        }

        try
        {
            bytes = Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            error = "Invalid base64 image.";
            return false;
        }

        if (bytes.Length < 32)
        {
            error = "Image payload too small.";
            return false;
        }

        return true;
    }

    private bool TryConsumeRateLimit(string key)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - 60_000;
        var queue = _rateBuckets.GetOrAdd(key, _ => new ConcurrentQueue<long>());

        while (queue.TryPeek(out var oldest) && oldest < windowStart)
        {
            queue.TryDequeue(out _);
        }

        if (queue.Count >= RateLimitPerMinute)
        {
            return false;
        }

        queue.Enqueue(now);
        return true;
    }

    private static string NormalizeName(string name)
        => Regex.Replace(name.Trim().ToLowerInvariant(), @"\s+", " ");

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    /// <summary>One cast candidate offered to Vision.</summary>
    public sealed record CastCandidate(string Name, string? Role, int? TmdbPersonId, string? ProfileUrl);

    /// <summary>Raw model match before cast filtering.</summary>
    public sealed record RawVisionMatch(string? Name, int? TmdbPersonId, double? Confidence);
}
