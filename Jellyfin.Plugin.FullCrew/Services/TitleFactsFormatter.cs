using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.FullCrew.Models;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Pure, side-effect-free formatting of TMDB movie/series details into display-ready facts.
/// Kept separate from <see cref="TitleFactsService"/> so the logic is unit-testable without HTTP.
/// </summary>
public static class TitleFactsFormatter
{
    private const int MaxCompanies = 3;
    private const int MaxCountries = 3;

    /// <summary>
    /// Builds the display-ready tagline, homepage, and ordered facts for a title.
    /// </summary>
    /// <param name="data">Normalized TMDB details.</param>
    /// <returns>A tuple of tagline, homepage, and the ordered fact list.</returns>
    public static (string? Tagline, string? Homepage, IReadOnlyList<TitleFact> Facts) Build(TitleFactsData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var facts = new List<TitleFact>();

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                facts.Add(new TitleFact(label, value!));
            }
        }

        Add("Status", NormalizeStatus(data.Status));

        if (data.IsMovie)
        {
            Add("Released", FormatDate(data.ReleaseDate));
            Add("Runtime", FormatRuntime(data.Runtime));
            Add("Budget", FormatMoney(data.Budget));
            Add("Box office", FormatMoney(data.Revenue));
            Add("Net", FormatNet(data.Budget, data.Revenue));
        }
        else
        {
            Add("Aired", FormatAirSpan(data.FirstAirDate, data.LastAirDate, data.InProduction));
            Add("Seasons", FormatCount(data.NumberOfSeasons, "season"));
            Add("Episodes", FormatCount(data.NumberOfEpisodes, "episode"));
            Add("Episode length", FormatRuntime(data.Runtime));
            Add("Network", JoinNames(data.Networks, MaxCompanies));
        }

        Add("Rating", FormatRating(data.VoteAverage, data.VoteCount));
        Add("Genres", JoinNames(data.Genres, int.MaxValue));
        Add("Language", ResolveLanguage(data.OriginalLanguage, data.SpokenLanguages));
        Add(data.IsMovie ? "Studio" : "Studios", JoinNames(data.ProductionCompanies, MaxCompanies));
        Add("Country", JoinNames(data.ProductionCountries, MaxCountries));

        return (TmdbDefaults.NullIfEmpty(data.Tagline), TmdbDefaults.NullIfEmpty(data.Homepage), facts);
    }

    /// <summary>
    /// Formats a USD amount into a compact string such as <c>$63M</c> or <c>$100.85M</c>.
    /// Returns null for null or non-positive values (TMDB uses 0 for "unknown").
    /// </summary>
    /// <param name="amount">The amount in whole USD.</param>
    /// <returns>The formatted amount, or null.</returns>
    public static string? FormatMoney(long? amount)
    {
        if (amount is not long value || value <= 0)
        {
            return null;
        }

        return "$" + FormatMagnitude(value);
    }

    /// <summary>
    /// Formats box-office net (revenue minus budget) with an explicit sign, e.g. <c>+$37.85M</c>.
    /// Returns null unless both budget and revenue are known and positive.
    /// </summary>
    /// <param name="budget">The budget in whole USD.</param>
    /// <param name="revenue">The revenue in whole USD.</param>
    /// <returns>The signed net string, or null.</returns>
    public static string? FormatNet(long? budget, long? revenue)
    {
        if (budget is not long b || b <= 0 || revenue is not long r || r <= 0)
        {
            return null;
        }

        var net = r - b;
        if (net == 0)
        {
            return "$0";
        }

        var sign = net > 0 ? "+" : "-";
        return sign + "$" + FormatMagnitude(Math.Abs(net));
    }

    /// <summary>
    /// Formats a runtime in minutes into <c>2h 19m</c>, <c>1h</c>, or <c>45m</c>.
    /// </summary>
    /// <param name="minutes">The runtime in minutes.</param>
    /// <returns>The formatted runtime, or null when non-positive.</returns>
    public static string? FormatRuntime(int? minutes)
    {
        if (minutes is not int total || total <= 0)
        {
            return null;
        }

        var hours = total / 60;
        var mins = total % 60;
        if (hours <= 0)
        {
            return mins + "m";
        }

        return mins == 0
            ? hours + "h"
            : string.Create(CultureInfo.InvariantCulture, $"{hours}h {mins}m");
    }

    /// <summary>
    /// Formats an ISO date (<c>yyyy-MM-dd</c>) into <c>MMM d, yyyy</c>.
    /// </summary>
    /// <param name="iso">The ISO date string.</param>
    /// <returns>The formatted date, or null when unparseable.</returns>
    public static string? FormatDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            return null;
        }

        return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// Formats a TV air span from first/last air dates into <c>2008 – 2013</c> or <c>2008 – present</c>.
    /// </summary>
    /// <param name="firstIso">First air date (ISO).</param>
    /// <param name="lastIso">Last air date (ISO).</param>
    /// <param name="inProduction">Whether the show is still in production.</param>
    /// <returns>The formatted span, or null.</returns>
    public static string? FormatAirSpan(string? firstIso, string? lastIso, bool inProduction)
    {
        var first = Year(firstIso);
        if (first is null)
        {
            return null;
        }

        var last = Year(lastIso);
        if (inProduction)
        {
            return first + " – present";
        }

        if (last is null || last == first)
        {
            return first;
        }

        return first + " – " + last;
    }

    private static string? Year(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            return null;
        }

        return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.Year.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? FormatCount(int? count, string noun)
    {
        if (count is not int value || value <= 0)
        {
            return null;
        }

        var plural = value == 1 ? noun : noun + "s";
        return string.Create(CultureInfo.InvariantCulture, $"{value} {plural}");
    }

    private static string? FormatRating(double? average, int? votes)
    {
        if (average is not double score || score <= 0)
        {
            return null;
        }

        var rounded = Math.Round(score, 1).ToString("0.#", CultureInfo.InvariantCulture);
        if (votes is int count && count > 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{rounded}/10 ({count.ToString("N0", CultureInfo.InvariantCulture)} votes)");
        }

        return rounded + "/10";
    }

    private static string NormalizeStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim();
    }

    private static string? JoinNames(IReadOnlyList<string>? names, int max)
    {
        if (names is null || names.Count == 0)
        {
            return null;
        }

        var cleaned = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count == 0)
        {
            return null;
        }

        if (max != int.MaxValue && cleaned.Count > max)
        {
            var shown = cleaned.Take(max);
            var extra = cleaned.Count - max;
            return string.Join(", ", shown) + " +" + extra.ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(", ", cleaned);
    }

    private static string? ResolveLanguage(
        string? originalLanguage,
        IReadOnlyList<SpokenLanguage>? spoken)
    {
        var code = originalLanguage?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        if (spoken is not null)
        {
            var match = spoken.FirstOrDefault(
                s => string.Equals(s.Iso6391, code, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !string.IsNullOrWhiteSpace(match.EnglishName))
            {
                return match.EnglishName.Trim();
            }
        }

        return KnownLanguages.TryGetValue(code, out var name)
            ? name
            : code.ToUpperInvariant();
    }

    private static string FormatMagnitude(long value)
    {
        var abs = Math.Abs(value);
        if (abs >= 1_000_000_000)
        {
            return Trim(value / 1_000_000_000d) + "B";
        }

        if (abs >= 1_000_000)
        {
            return Trim(value / 1_000_000d) + "M";
        }

        if (abs >= 1_000)
        {
            return Trim(value / 1_000d) + "K";
        }

        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string Trim(double value)
    {
        // Up to two decimals, trailing zeros removed: 63.0 -> "63", 100.853 -> "100.85".
        return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static readonly IReadOnlyDictionary<string, string> KnownLanguages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "English",
            ["es"] = "Spanish",
            ["fr"] = "French",
            ["de"] = "German",
            ["it"] = "Italian",
            ["pt"] = "Portuguese",
            ["ru"] = "Russian",
            ["ja"] = "Japanese",
            ["ko"] = "Korean",
            ["zh"] = "Chinese",
            ["cn"] = "Chinese",
            ["hi"] = "Hindi",
            ["ar"] = "Arabic",
            ["nl"] = "Dutch",
            ["sv"] = "Swedish",
            ["no"] = "Norwegian",
            ["da"] = "Danish",
            ["fi"] = "Finnish",
            ["pl"] = "Polish",
            ["tr"] = "Turkish",
            ["th"] = "Thai",
        };
}
