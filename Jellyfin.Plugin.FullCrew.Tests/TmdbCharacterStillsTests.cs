using System.Collections.Generic;
using Jellyfin.Plugin.FullCrew.Services;
using Xunit;

namespace Jellyfin.Plugin.FullCrew.Tests;

public class TmdbCharacterStillsTests
{
    [Fact]
    public void PickTaggedStillPath_PrefersLandscapeFromMatchingTitle()
    {
        var images = new List<TmdbTaggedImage>
        {
            new()
            {
                FilePath = "/portrait.jpg",
                AspectRatio = 0.67,
                MediaType = "tv",
                Media = new TmdbTaggedMedia { Id = 1396 },
                VoteAverage = 9
            },
            new()
            {
                FilePath = "/other-show.jpg",
                AspectRatio = 1.78,
                MediaType = "tv",
                Media = new TmdbTaggedMedia { Id = 999 },
                VoteAverage = 9
            },
            new()
            {
                FilePath = "/in-show.jpg",
                AspectRatio = 1.78,
                ImageType = "backdrop",
                MediaType = "tv",
                Media = new TmdbTaggedMedia { Id = 1396 },
                VoteAverage = 4
            }
        };

        var path = TmdbCharacterStills.PickTaggedStillPath(images, 1396, "tv");
        Assert.Equal("/in-show.jpg", path);
    }

    [Fact]
    public void PickTaggedStillPath_IgnoresOtherMediaType()
    {
        var images = new List<TmdbTaggedImage>
        {
            new()
            {
                FilePath = "/movie-still.jpg",
                AspectRatio = 1.78,
                MediaType = "movie",
                Media = new TmdbTaggedMedia { Id = 1396 }
            }
        };

        Assert.Null(TmdbCharacterStills.PickTaggedStillPath(images, 1396, "tv"));
    }

    [Fact]
    public void ToStillUrl_PrefixesCdn()
    {
        Assert.Equal("https://image.tmdb.org/t/p/w500/abc.jpg", TmdbCharacterStills.ToStillUrl("abc.jpg"));
        Assert.Null(TmdbCharacterStills.ToStillUrl(" "));
    }

    [Fact]
    public void ExpectedMediaType_MapsMovieVsTv()
    {
        Assert.Equal("movie", TmdbCharacterStills.ExpectedMediaType("Movie"));
        Assert.Equal("tv", TmdbCharacterStills.ExpectedMediaType("Episode"));
        Assert.Equal("tv", TmdbCharacterStills.ExpectedMediaType("Series"));
    }
}
