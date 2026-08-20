using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.FullCrew.Services;
using Xunit;

namespace Jellyfin.Plugin.FullCrew.Tests;

public class YearBucketsTests
{
    [Theory]
    [InlineData(null, "OldestFirst")]
    [InlineData("", "OldestFirst")]
    [InlineData("OldestFirst", "OldestFirst")]
    [InlineData("NewestFirst", "NewestFirst")]
    [InlineData("MostTitles", "MostTitles")]
    [InlineData("ByCount", "MostTitles")]
    public void ResolveYearSort_NormalizesAliases(string? input, string expected)
    {
        Assert.Equal(expected, LibraryStatsService.ResolveYearSort(input));
    }

    [Fact]
    public void ToYearBuckets_OldestFirst_ListsEveryYearChronologically()
    {
        var counts = new Dictionary<string, int>
        {
            ["2015"] = 3,
            ["2001"] = 1,
            ["2020"] = 5,
            ["Unknown"] = 2
        };

        var buckets = LibraryStatsService.ToYearBuckets(counts, total: 11, "OldestFirst");

        Assert.Equal(4, buckets.Count);
        Assert.Equal(["2001", "2015", "2020", "Unknown"], buckets.Select(b => b.Name).ToList());
        Assert.DoesNotContain(buckets, b => b.Name == "Other");
    }

    [Fact]
    public void ToYearBuckets_NewestFirst_PutsRecentYearsFirst()
    {
        var counts = new Dictionary<string, int>
        {
            ["2015"] = 3,
            ["2001"] = 1,
            ["2020"] = 5,
            ["Unknown"] = 2
        };

        var buckets = LibraryStatsService.ToYearBuckets(counts, total: 11, "NewestFirst");

        Assert.Equal(["2020", "2015", "2001", "Unknown"], buckets.Select(b => b.Name).ToList());
    }

    [Fact]
    public void ToYearBuckets_MostTitles_OrdersByCount()
    {
        var counts = new Dictionary<string, int>
        {
            ["2015"] = 3,
            ["2001"] = 1,
            ["2020"] = 5
        };

        var buckets = LibraryStatsService.ToYearBuckets(counts, total: 9, "MostTitles");

        Assert.Equal(["2020", "2015", "2001"], buckets.Select(b => b.Name).ToList());
    }
}
