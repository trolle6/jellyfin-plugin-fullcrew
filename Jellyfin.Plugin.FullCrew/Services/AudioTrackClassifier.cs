using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// A kind of special audio track (commentary, description, …).
/// </summary>
/// <param name="Id">Stable slug used in APIs and URLs.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Short explanation shown in the browser.</param>
/// <param name="SortOrder">Display order, lowest first.</param>
public sealed record AudioTrackKind(string Id, string Name, string Description, int SortOrder)
{
    /// <summary>Director / cast / crew commentary.</summary>
    public static readonly AudioTrackKind Commentary = new(
        "commentary",
        "Commentary",
        "Director, cast, and crew commentary — across every show and movie.",
        0);

    /// <summary>Audio description / DVS tracks.</summary>
    public static readonly AudioTrackKind AudioDescription = new(
        "audiodescription",
        "Audio Description",
        "Descriptive narration and DVS tracks for visually impaired listeners.",
        1);

    /// <summary>Dubbed dialogue in another language.</summary>
    public static readonly AudioTrackKind Dub = new(
        "dub",
        "Dub",
        "Dubbed dialogue tracks (when the file labels them as a dub).",
        2);

    /// <summary>Isolated musical score.</summary>
    public static readonly AudioTrackKind IsolatedScore = new(
        "isolatedscore",
        "Isolated Score",
        "Music-only or isolated score tracks.",
        3);

    /// <summary>Isolated effects or music-and-effects.</summary>
    public static readonly AudioTrackKind IsolatedEffects = new(
        "isolatedeffects",
        "Isolated Effects",
        "Effects-only, isolated FX, and music-and-effects (M&E) tracks.",
        4);

    /// <summary>Karaoke.</summary>
    public static readonly AudioTrackKind Karaoke = new(
        "karaoke",
        "Karaoke",
        "Karaoke or instrumental sing-along tracks.",
        5);

    /// <summary>Any other non-generic titled track.</summary>
    public static readonly AudioTrackKind Labeled = new(
        "labeled",
        "Other labeled",
        "Tracks with a custom title that is not a standard dialogue/codec label.",
        6);

    /// <summary>
    /// Gets every known kind, in display order.
    /// </summary>
    public static IReadOnlyList<AudioTrackKind> All { get; } =
    [
        Commentary,
        AudioDescription,
        Dub,
        IsolatedScore,
        IsolatedEffects,
        Karaoke,
        Labeled
    ];

    /// <summary>
    /// Finds a kind by id, or <c>null</c> when unknown.
    /// </summary>
    /// <param name="id">Type id.</param>
    /// <returns>The matching kind, or <c>null</c>.</returns>
    public static AudioTrackKind? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || string.Equals(id, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return All.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Classifies audio streams from file titles/comments into special-purpose kinds.
/// Standard unlabeled dialogue tracks are ignored so the browser stays about
/// commentary, descriptions, dubs, isolated stems, and other labeled extras.
/// </summary>
public static class AudioTrackClassifier
{
    private static readonly Regex CommentaryPattern = new(
        @"\b(commentar(?:y|ies)|kommentar(?:e)?|commentaire(?:s)?|comentario(?:s)?|commento|commenti|trivia(?:\s+track)?|filmmaker(?:s)?'?\s+comments?)\b|コメント|コメンタリー?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AudioDescriptionPattern = new(
        @"\b(audio[\s-]?descriptions?|descriptive\s+(?:audio|narration|video)|visually?\s+impaired|vision[\s-]?impaired|dvs)\b|\((?:ad|vi)\)|\[(?:ad|vi)\]|(?:^|[\s/_-])ad(?:[\s/_-]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IsolatedScorePattern = new(
        @"\b(isolated\s+(?:score|music)|(?:score|music)[\s-]?only|music[\s-]?score)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IsolatedEffectsPattern = new(
        @"\b(isolated\s+(?:effects?|fx)|(?:effects?|fx)[\s-]?only|clean\s+effects?|music\s*(?:and|&)\s*effects?|m\s*(?:and|&)\s*e)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex KaraokePattern = new(
        @"\bkaraoke\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DubPattern = new(
        @"\b(dub(?:bed|bing)?|synchron(?:isation|isierung)|synchro)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex GenericTitlePattern = new(
        @"^(?:audio(?:\s*track)?|track(?:\s*\d+)?|default|original|main|primary|stereo|mono|surround|atmos|truehd|true-hd|dts(?:-?hd)?(?:\s*ma)?|e-?ac-?3|ac-?3|aac|flac|opus|pcm|lpcm|mp3|ogg|5\.1|7\.1|2\.0|atmos|commentary\s*off)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> LanguageTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "eng", "english", "en-us", "en-gb",
        "ja", "jpn", "jp", "japanese",
        "es", "spa", "spanish", "castilian",
        "fr", "fre", "fra", "french",
        "de", "ger", "deu", "german",
        "it", "ita", "italian",
        "pt", "por", "portuguese", "pt-br", "brazillian", "brazilian",
        "zh", "chi", "zho", "chinese", "cmn", "yue", "cantonese", "mandarin",
        "ko", "kor", "korean",
        "ru", "rus", "russian",
        "hi", "hin", "hindi",
        "ar", "ara", "arabic",
        "nl", "dut", "nld", "dutch",
        "sv", "swe", "swedish",
        "no", "nor", "nb", "nn", "norwegian",
        "da", "dan", "danish",
        "fi", "fin", "finnish",
        "pl", "pol", "polish",
        "tr", "tur", "turkish",
        "th", "tha", "thai",
        "vi", "vie", "vietnamese",
        "he", "heb", "hebrew",
        "cs", "cze", "ces", "czech",
        "hu", "hun", "hungarian",
        "ro", "rum", "ron", "romanian",
        "el", "gre", "ell", "greek",
        "uk", "ukr", "ukrainian",
        "id", "ind", "indonesian",
        "ms", "may", "msa", "malay",
        "und", "unk", "unknown", "undetermined", "mul", "multiple"
    };

    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "English",
        ["eng"] = "English",
        ["ja"] = "Japanese",
        ["jpn"] = "Japanese",
        ["jp"] = "Japanese",
        ["es"] = "Spanish",
        ["spa"] = "Spanish",
        ["fr"] = "French",
        ["fre"] = "French",
        ["fra"] = "French",
        ["de"] = "German",
        ["ger"] = "German",
        ["deu"] = "German",
        ["it"] = "Italian",
        ["ita"] = "Italian",
        ["pt"] = "Portuguese",
        ["por"] = "Portuguese",
        ["zh"] = "Chinese",
        ["chi"] = "Chinese",
        ["zho"] = "Chinese",
        ["ko"] = "Korean",
        ["kor"] = "Korean",
        ["ru"] = "Russian",
        ["rus"] = "Russian",
        ["hi"] = "Hindi",
        ["hin"] = "Hindi",
        ["ar"] = "Arabic",
        ["ara"] = "Arabic",
        ["nl"] = "Dutch",
        ["und"] = "Unknown"
    };

    /// <summary>
    /// Classifies a stream from its title and optional comment tag.
    /// </summary>
    /// <param name="title">Container title (MKV/MP4 track name).</param>
    /// <param name="comment">Optional comment tag.</param>
    /// <returns>The kind, or <c>null</c> for ordinary dialogue tracks.</returns>
    public static AudioTrackKind? Classify(string? title, string? comment = null)
    {
        var text = Join(title, comment);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // More specific kinds first so "Audio Description" is not "labeled"
        // and "Commentary (German Dub)" stays commentary.
        if (AudioDescriptionPattern.IsMatch(text))
        {
            return AudioTrackKind.AudioDescription;
        }

        if (CommentaryPattern.IsMatch(text))
        {
            return AudioTrackKind.Commentary;
        }

        if (KaraokePattern.IsMatch(text))
        {
            return AudioTrackKind.Karaoke;
        }

        if (IsolatedScorePattern.IsMatch(text))
        {
            return AudioTrackKind.IsolatedScore;
        }

        if (IsolatedEffectsPattern.IsMatch(text))
        {
            return AudioTrackKind.IsolatedEffects;
        }

        if (DubPattern.IsMatch(text))
        {
            return AudioTrackKind.Dub;
        }

        return IsGenericTitle(title) ? null : AudioTrackKind.Labeled;
    }

    /// <summary>
    /// Returns a friendly language name for an ISO code, or the code itself.
    /// </summary>
    /// <param name="code">Language code from the stream.</param>
    /// <returns>Display name, or <c>null</c> when empty.</returns>
    public static string? LanguageDisplayName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var trimmed = code.Trim();
        return LanguageNames.TryGetValue(trimmed, out var name) ? name : trimmed;
    }

    /// <summary>
    /// Builds a compact display string from stream fields.
    /// </summary>
    /// <param name="title">Track title.</param>
    /// <param name="language">Language code.</param>
    /// <param name="codec">Codec name.</param>
    /// <param name="channels">Channel count.</param>
    /// <returns>Human-readable summary.</returns>
    public static string FormatDisplayTitle(string? title, string? language, string? codec, int? channels)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(title))
        {
            parts.Add(title.Trim());
        }

        var languageName = LanguageDisplayName(language);
        if (!string.IsNullOrWhiteSpace(languageName)
            && (string.IsNullOrWhiteSpace(title) || title.IndexOf(languageName, StringComparison.OrdinalIgnoreCase) < 0))
        {
            parts.Add(languageName);
        }

        if (!string.IsNullOrWhiteSpace(codec))
        {
            parts.Add(codec.Trim().ToUpperInvariant());
        }

        if (channels is > 0)
        {
            parts.Add(channels.Value switch
            {
                1 => "Mono",
                2 => "Stereo",
                _ => channels.Value + "ch"
            });
        }

        return parts.Count == 0 ? "Audio" : string.Join(" · ", parts);
    }

    /// <summary>
    /// Returns whether a title is just a language, codec, or other generic label.
    /// </summary>
    /// <param name="title">Track title.</param>
    /// <returns><c>true</c> when the title is empty or uninteresting.</returns>
    public static bool IsGenericTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        var normalized = NormalizeTitle(title);
        if (normalized.Length == 0)
        {
            return true;
        }

        if (LanguageTokens.Contains(normalized))
        {
            return true;
        }

        if (GenericTitlePattern.IsMatch(normalized))
        {
            return true;
        }

        // "English Stereo", "eng 5.1", "Japanese AAC"
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens.All(IsGenericToken);
    }

    private static bool IsGenericToken(string token)
    {
        return LanguageTokens.Contains(token) || GenericTitlePattern.IsMatch(token);
    }

    private static string NormalizeTitle(string title)
    {
        var chars = title.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is '_' or '-' or '/' or '\\' or ',' or ':' or ';' or '(' or ')' or '[' or ']' or '{' or '}')
            {
                chars[i] = ' ';
            }
        }

        var collapsed = Regex.Replace(new string(chars), @"\s+", " ").Trim();
        return collapsed;
    }

    private static string Join(string? title, string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return title?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return comment.Trim();
        }

        return title.Trim() + " " + comment.Trim();
    }
}
