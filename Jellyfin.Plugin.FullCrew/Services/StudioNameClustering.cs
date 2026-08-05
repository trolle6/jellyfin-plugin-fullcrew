using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Clusters studio credit strings by shared name root (prefix/token), not corporate families.
/// </summary>
internal static class StudioNameClustering
{
    private static readonly string[] BrandRoots =
    [
        "cartoon network",
        "adult swim",
        "warner brothers",
        "warner bros",
        "walt disney",
        "nickelodeon",
        "nicktoons",
        "nick jr",
        "dreamworks",
        "dream works",
        "sony pictures",
        "20th century",
        "twentieth century",
        "metro goldwyn",
        "disney",
        "pixar",
        "marvel",
        "lucasfilm",
        "columbia",
        "tristar",
        "universal",
        "illumination",
        "paramount",
        "lionsgate",
        "miramax",
        "netflix",
        "amazon",
        "hbo",
        "bbc",
        "toei",
        "sunrise",
        "bones",
        "madhouse",
        "kyoto"
    ];

    private static readonly HashSet<string> RootFillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "of", "for",
        "walt", "pictures", "picture", "studios", "studio",
        "animation", "animations", "television", "tv",
        "productions", "production", "entertainment", "media",
        "films", "film", "company", "group", "channel",
        "network", "video", "home", "interactive", "toon", "toons",
        "bros", "brothers", "inc", "llc", "ltd", "co", "corp",
        "corporation", "limited", "feature", "features", "shorts",
        "short", "original", "originals", "plus", "xd"
    };

    /// <summary>
    /// Resolves the stable cluster key and display label for a studio credit string.
    /// </summary>
    public static (string Key, string Label) ResolveRoot(string rawName)
    {
        var normalized = Normalize(rawName);
        if (string.IsNullOrEmpty(normalized))
        {
            var fallback = (rawName ?? string.Empty).Trim();
            return (fallback.ToLowerInvariant(), fallback);
        }

        foreach (var root in BrandRoots)
        {
            if (MatchesRoot(normalized, root))
            {
                return CanonicalRoot(root);
            }
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (token.Length < 5 || RootFillers.Contains(token))
            {
                continue;
            }

            return (token, ToTitleCase(token));
        }

        var self = tokens.Length > 0 ? string.Join(' ', tokens) : normalized;
        return (self, ToTitleCase(self));
    }

    /// <summary>
    /// Returns every name in <paramref name="candidates"/> that shares the same cluster root as
    /// <paramref name="seedName"/>, always including the seed itself when non-empty.
    /// </summary>
    public static IReadOnlyList<string> ExpandBranches(string seedName, IEnumerable<string> candidates)
    {
        var seed = (seedName ?? string.Empty).Trim();
        if (seed.Length == 0)
        {
            return [];
        }

        var (key, _) = ResolveRoot(seed);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { seed };
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var name = candidate.Trim();
            if (ResolveRoot(name).Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                set.Add(name);
            }
        }

        return set
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (string Key, string Label) CanonicalRoot(string matchedRoot)
    {
        return matchedRoot switch
        {
            "walt disney" => ("disney", "Disney"),
            "warner brothers" => ("warner bros", "Warner Bros"),
            "dream works" => ("dreamworks", "Dreamworks"),
            "twentieth century" => ("20th century", "20th Century"),
            "nick jr" => ("nickelodeon", "Nickelodeon"),
            "nicktoons" => ("nickelodeon", "Nickelodeon"),
            _ => (matchedRoot, ToTitleCase(matchedRoot))
        };
    }

    private static bool MatchesRoot(string normalized, string root)
    {
        if (normalized == root || normalized.StartsWith(root + " ", StringComparison.Ordinal))
        {
            return true;
        }

        if (normalized.Contains(" " + root + " ", StringComparison.Ordinal)
            || normalized.EndsWith(" " + root, StringComparison.Ordinal))
        {
            return true;
        }

        var rootCompact = root.Replace(" ", string.Empty, StringComparison.Ordinal);
        var compact = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (compact.StartsWith(rootCompact, StringComparison.Ordinal)
            && compact.Length > rootCompact.Length)
        {
            return true;
        }

        if (!root.Contains(' ', StringComparison.Ordinal) && root.Length >= 5)
        {
            foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith(root, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length * 2);
        char prev = '\0';
        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (sb.Length > 0
                    && char.IsUpper(ch)
                    && char.IsLetter(prev)
                    && !char.IsUpper(prev))
                {
                    sb.Append(' ');
                }
                else if (sb.Length > 0
                         && char.IsLetter(ch)
                         && char.IsDigit(prev))
                {
                    sb.Append(' ');
                }
                else if (sb.Length > 0
                         && char.IsDigit(ch)
                         && char.IsLetter(prev))
                {
                    sb.Append(' ');
                }

                sb.Append(char.ToLowerInvariant(ch));
                prev = ch;
            }
            else if (sb.Length > 0 && sb[^1] != ' ')
            {
                sb.Append(' ');
                prev = ' ';
            }
        }

        return string.Join(
            ' ',
            sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ToTitleCase(string root)
    {
        var parts = root.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Equals("bros", StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = "Bros";
            }
            else if (p.Equals("hbo", StringComparison.OrdinalIgnoreCase)
                     || p.Equals("bbc", StringComparison.OrdinalIgnoreCase)
                     || p.Equals("xd", StringComparison.OrdinalIgnoreCase)
                     || p.Equals("tv", StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = p.ToUpperInvariant();
            }
            else if (p.Length > 0)
            {
                parts[i] = char.ToUpperInvariant(p[0]) + p[1..];
            }
        }

        return string.Join(' ', parts);
    }
}
