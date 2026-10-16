using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.FullCrew.Services;
using Xunit;

namespace Jellyfin.Plugin.FullCrew.Tests;

public class TitleFactsFormatterTests
{
    private static string? Value(IReadOnlyList<Jellyfin.Plugin.FullCrew.Models.TitleFact> facts, string label)
        => facts.FirstOrDefault(f => f.Label == label)?.Value;

    [Theory]
    [InlineData(63000000, "$63M")]
    [InlineData(100853753, "$100.85M")]
    [InlineData(1020000000, "$1.02B")]
    [InlineData(45000, "$45K")]
    [InlineData(500, "$500")]
    public void FormatMoney_AbbreviatesWithDollarSign(long amount, string expected)
    {
        Assert.Equal(expected, TitleFactsFormatter.FormatMoney(amount));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData(null)]
    public void FormatMoney_ReturnsNullForUnknownOrNonPositive(long? amount)
    {
        Assert.Null(TitleFactsFormatter.FormatMoney(amount));
    }

    [Fact]
    public void FormatNet_ProfitHasPlusSign()
    {
        Assert.Equal("+$37.85M", TitleFactsFormatter.FormatNet(63000000, 100853753));
    }

    [Fact]
    public void FormatNet_LossHasMinusSign()
    {
        Assert.Equal("-$50M", TitleFactsFormatter.FormatNet(200000000, 150000000));
    }

    [Fact]
    public void FormatNet_RequiresBothBudgetAndRevenue()
    {
        Assert.Null(TitleFactsFormatter.FormatNet(0, 100000000));
        Assert.Null(TitleFactsFormatter.FormatNet(100000000, 0));
        Assert.Null(TitleFactsFormatter.FormatNet(null, null));
    }

    [Theory]
    [InlineData(139, "2h 19m")]
    [InlineData(60, "1h")]
    [InlineData(45, "45m")]
    [InlineData(0, null)]
    [InlineData(null, null)]
    public void FormatRuntime_FormatsHoursAndMinutes(int? minutes, string? expected)
    {
        Assert.Equal(expected, TitleFactsFormatter.FormatRuntime(minutes));
    }

    [Fact]
    public void FormatDate_ReadsIsoDate()
    {
        Assert.Equal("Oct 15, 1999", TitleFactsFormatter.FormatDate("1999-10-15"));
        Assert.Null(TitleFactsFormatter.FormatDate("not-a-date"));
        Assert.Null(TitleFactsFormatter.FormatDate(""));
    }

    [Fact]
    public void FormatAirSpan_HandlesRangePresentAndSingleYear()
    {
        Assert.Equal("2008 – 2013", TitleFactsFormatter.FormatAirSpan("2008-01-20", "2013-09-29", false));
        Assert.Equal("2008 – present", TitleFactsFormatter.FormatAirSpan("2008-01-20", "2010-01-01", true));
        Assert.Equal("2008", TitleFactsFormatter.FormatAirSpan("2008-01-20", "2008-12-01", false));
        Assert.Equal("2008", TitleFactsFormatter.FormatAirSpan("2008-01-20", null, false));
        Assert.Null(TitleFactsFormatter.FormatAirSpan(null, null, false));
    }

    [Fact]
    public void Build_MovieProducesOrderedFactsWithNetAndTagline()
    {
        var data = new TitleFactsData
        {
            IsMovie = true,
            Status = "Released",
            Runtime = 139,
            Budget = 63000000,
            Revenue = 100853753,
            ReleaseDate = "1999-10-15",
            OriginalLanguage = "en",
            Genres = ["Drama", "Thriller"],
            ProductionCompanies = ["Fox 2000 Pictures", "Regency Enterprises", "Linson", "20th Century Fox"],
            ProductionCountries = ["Germany", "United States of America"],
            SpokenLanguages = [new SpokenLanguage("en", "English")],
            VoteAverage = 8.4,
            VoteCount = 33032,
            Tagline = "Mischief. Mayhem. Soap.",
            Homepage = "https://example.com"
        };

        var (tagline, homepage, facts) = TitleFactsFormatter.Build(data);

        Assert.Equal("Mischief. Mayhem. Soap.", tagline);
        Assert.Equal("https://example.com", homepage);
        Assert.Equal("Released", Value(facts, "Status"));
        Assert.Equal("Oct 15, 1999", Value(facts, "Released"));
        Assert.Equal("2h 19m", Value(facts, "Runtime"));
        Assert.Equal("$63M", Value(facts, "Budget"));
        Assert.Equal("$100.85M", Value(facts, "Box office"));
        Assert.Equal("+$37.85M", Value(facts, "Net"));
        Assert.Equal("8.4/10 (33,032 votes)", Value(facts, "Rating"));
        Assert.Equal("Drama, Thriller", Value(facts, "Genres"));
        Assert.Equal("English", Value(facts, "Language"));
        Assert.Equal("Fox 2000 Pictures, Regency Enterprises, Linson +1", Value(facts, "Studio"));
        Assert.Equal("Germany, United States of America", Value(facts, "Country"));

        // Status comes first; Net sits right after Box office.
        Assert.Equal("Status", facts[0].Label);
        var labels = facts.Select(f => f.Label).ToList();
        Assert.Equal(labels.IndexOf("Box office") + 1, labels.IndexOf("Net"));
    }

    [Fact]
    public void Build_SeriesUsesAirSpanSeasonsEpisodesAndNetworks()
    {
        var data = new TitleFactsData
        {
            IsMovie = false,
            Status = "Ended",
            FirstAirDate = "2008-01-20",
            LastAirDate = "2013-09-29",
            InProduction = false,
            NumberOfSeasons = 5,
            NumberOfEpisodes = 62,
            Networks = ["AMC"],
            OriginalLanguage = "en",
            Genres = ["Drama", "Crime"],
            SpokenLanguages = [new SpokenLanguage("en", "English")],
            VoteAverage = 8.9
        };

        var (_, _, facts) = TitleFactsFormatter.Build(data);

        Assert.Equal("Ended", Value(facts, "Status"));
        Assert.Equal("2008 – 2013", Value(facts, "Aired"));
        Assert.Equal("5 seasons", Value(facts, "Seasons"));
        Assert.Equal("62 episodes", Value(facts, "Episodes"));
        Assert.Equal("AMC", Value(facts, "Network"));
        Assert.Equal("8.9/10", Value(facts, "Rating"));
        Assert.Null(Value(facts, "Budget"));
        Assert.Null(Value(facts, "Box office"));
    }

    [Fact]
    public void Build_SingleSeasonIsNotPluralized()
    {
        var data = new TitleFactsData
        {
            IsMovie = false,
            NumberOfSeasons = 1,
            NumberOfEpisodes = 1
        };

        var (_, _, facts) = TitleFactsFormatter.Build(data);

        Assert.Equal("1 season", Value(facts, "Seasons"));
        Assert.Equal("1 episode", Value(facts, "Episodes"));
    }

    [Fact]
    public void Build_FallsBackToUppercaseLanguageCodeWhenUnknown()
    {
        var data = new TitleFactsData
        {
            IsMovie = true,
            OriginalLanguage = "xx"
        };

        var (_, _, facts) = TitleFactsFormatter.Build(data);

        Assert.Equal("XX", Value(facts, "Language"));
    }

    [Fact]
    public void Build_OmitsFactsWithNoData()
    {
        var data = new TitleFactsData { IsMovie = true };

        var (tagline, homepage, facts) = TitleFactsFormatter.Build(data);

        Assert.Null(tagline);
        Assert.Null(homepage);
        Assert.Empty(facts);
    }
}
