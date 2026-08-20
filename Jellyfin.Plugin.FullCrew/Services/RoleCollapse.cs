using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Collapses redundant cast/crew role strings into unique character/job names
/// (e.g. dozens of “Mr. Slate / …” variants → distinct names, ranked by frequency).
/// </summary>
public static class RoleCollapse
{
    /// <summary>Default number of unique roles shown before “+N more”.</summary>
    public const int DefaultMaxVisible = 2;

    private static readonly HashSet<string> KnownCrewJobs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Creator",
        "Executive Producer",
        "Co-Executive Producer",
        "Producer",
        "Co-Producer",
        "Associate Producer",
        "Line Producer",
        "Director",
        "Co-Director",
        "Writer",
        "Screenplay",
        "Story",
        "Storyboard Artist",
        "Characters",
        "Editor",
        "Supervising Editor",
        "Director of Photography",
        "Cinematography",
        "Original Music Composer",
        "Music",
        "Actor",
        "Self",
        "Voice Director",
        "Additional Writing",
        "Consulting Producer",
        "Supervising Producer",
        "Co-Writer",
        "Head Writer",
        "Staff Writer"
    };

    private static readonly Regex TrailingParenNotes = new(
        @"\s*\([^)]*\)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> NameSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "jr", "jr.", "sr", "sr.", "ii", "iii", "iv", "v", "phd", "md", "esq", "esq."
    };

    /// <summary>
    /// Collapse raw credit strings into unique display names, ranked by how often
    /// each name appears as a primary (first segment) then secondary occurrence.
    /// </summary>
    /// <param name="credits">Raw character/job strings (may contain / or ,).</param>
    /// <returns>Unique nicest display forms, most frequent first.</returns>
    public static IReadOnlyList<string> Collapse(IEnumerable<string?>? credits)
    {
        if (credits is null)
        {
            return [];
        }

        var stats = new Dictionary<string, RoleStat>(StringComparer.OrdinalIgnoreCase);

        foreach (var credit in credits)
        {
            if (string.IsNullOrWhiteSpace(credit))
            {
                continue;
            }

            var segments = SplitSegments(credit);
            for (var i = 0; i < segments.Count; i++)
            {
                var display = CleanDisplay(segments[i]);
                if (string.IsNullOrWhiteSpace(display))
                {
                    continue;
                }

                var key = NormalizeKey(display);
                if (key.Length == 0)
                {
                    continue;
                }

                if (!stats.TryGetValue(key, out var stat))
                {
                    stat = new RoleStat(display);
                    stats[key] = stat;
                }
                else
                {
                    PreferNicestDisplay(stat, display);
                }

                if (i == 0)
                {
                    stat.PrimaryCount++;
                }
                else
                {
                    stat.SecondaryCount++;
                }
            }
        }

        return stats.Values
            .OrderByDescending(s => s.PrimaryCount)
            .ThenByDescending(s => s.SecondaryCount)
            .ThenByDescending(s => s.Display.Length)
            .ThenBy(s => s.Display, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Display)
            .ToList();
    }

    /// <summary>
    /// For Cast cards, show billed characters before generic crew job titles
    /// when one person was merged across cast + production credits.
    /// </summary>
    public static IReadOnlyList<string> PrioritizeCastCharacters(IReadOnlyList<string> uniqueRoles)
    {
        if (uniqueRoles.Count <= 1)
        {
            return uniqueRoles;
        }

        var characters = new List<string>();
        var jobs = new List<string>();

        foreach (var role in uniqueRoles)
        {
            if (IsKnownCrewJob(role))
            {
                jobs.Add(role);
            }
            else
            {
                characters.Add(role);
            }
        }

        if (characters.Count == 0 || jobs.Count == 0)
        {
            return uniqueRoles;
        }

        characters.AddRange(jobs);
        return characters;
    }

    /// <summary>
    /// Build a short label and full tooltip from already-collapsed unique names.
    /// </summary>
    /// <param name="uniqueRoles">Unique role display names.</param>
    /// <param name="maxVisible">How many names to show before “+N more”.</param>
    /// <returns>Visible label, full tooltip, and hidden count.</returns>
    public static RolePreview FormatPreview(IReadOnlyList<string>? uniqueRoles, int maxVisible = DefaultMaxVisible)
    {
        if (uniqueRoles is null || uniqueRoles.Count == 0)
        {
            return new RolePreview([], string.Empty, 0);
        }

        var visibleCount = Math.Clamp(maxVisible, 1, 10);
        var tooltip = string.Join(" · ", uniqueRoles);
        if (uniqueRoles.Count <= visibleCount)
        {
            return new RolePreview(uniqueRoles.ToList(), tooltip, 0);
        }

        var hidden = uniqueRoles.Count - visibleCount;
        return new RolePreview(uniqueRoles.Take(visibleCount).ToList(), tooltip, hidden);
    }

    private static bool IsKnownCrewJob(string role)
    {
        var trimmed = role.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (KnownCrewJobs.Contains(trimmed))
        {
            return true;
        }

        foreach (var job in KnownCrewJobs)
        {
            if (trimmed.StartsWith(job + " ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Split a credit on / and sensible commas into character/job segments.</summary>
    public static IReadOnlyList<string> SplitSegments(string credit)
    {
        if (string.IsNullOrWhiteSpace(credit))
        {
            return [];
        }

        var parts = new List<string>();
        foreach (var slashPart in credit.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            parts.AddRange(SplitCommaSensible(slashPart));
        }

        return parts;
    }

    private static IEnumerable<string> SplitCommaSensible(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains(',', StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return text.Trim();
            }

            yield break;
        }

        var pieces = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length == 0)
        {
            yield break;
        }

        var buffer = pieces[0];
        for (var i = 1; i < pieces.Length; i++)
        {
            var next = pieces[i];
            // "Jr. (uncredited)" must still count as a name suffix, not a second role.
            var nextSuffix = CleanDisplay(next);
            if (NameSuffixes.Contains(next) || NameSuffixes.Contains(nextSuffix))
            {
                buffer = buffer + ", " + (string.IsNullOrWhiteSpace(nextSuffix) ? next : nextSuffix);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(buffer))
            {
                yield return buffer;
            }

            buffer = next;
        }

        if (!string.IsNullOrWhiteSpace(buffer))
        {
            yield return buffer;
        }
    }

    private static string CleanDisplay(string segment)
    {
        var s = segment.Trim();
        // Drop trailing credit notes for a cleaner card line; tooltip still lists uniques.
        while (TrailingParenNotes.IsMatch(s))
        {
            s = TrailingParenNotes.Replace(s, string.Empty).Trim();
        }

        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string NormalizeKey(string display)
    {
        return Regex.Replace(display.Trim().ToLowerInvariant(), @"\s+", " ");
    }

    private static void PreferNicestDisplay(RoleStat stat, string candidate)
    {
        // Prefer longer cleaned forms (more complete character name).
        if (candidate.Length > stat.Display.Length)
        {
            stat.Display = candidate;
        }
    }

    private sealed class RoleStat(string display)
    {
        public string Display { get; set; } = display;

        public int PrimaryCount { get; set; }

        public int SecondaryCount { get; set; }
    }
}

/// <summary>Collapsed role preview for UI cards.</summary>
/// <param name="VisibleRoles">Role lines shown on the card (one per row).</param>
/// <param name="Tooltip">Full unique list for hover.</param>
/// <param name="HiddenCount">How many unique names are omitted from <paramref name="VisibleRoles"/>.</param>
public readonly record struct RolePreview(IReadOnlyList<string> VisibleRoles, string Tooltip, int HiddenCount)
{
    /// <summary>Legacy single-line label (middot-joined visible roles).</summary>
    public string Label => string.Join(" · ", VisibleRoles);
}
