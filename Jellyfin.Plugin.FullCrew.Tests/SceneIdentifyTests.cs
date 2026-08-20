using System.Collections.Generic;
using Jellyfin.Plugin.FullCrew.Models;
using Jellyfin.Plugin.FullCrew.Services;
using Xunit;

namespace Jellyfin.Plugin.FullCrew.Tests;

public class SceneIdentifyTests
{
    [Fact]
    public void ParseVisionContent_ReadsMatches()
    {
        var raw = """{"matches":[{"name":"Tom Hanks","tmdbPersonId":31,"confidence":0.91}]}""";
        var parsed = SceneIdentifyService.ParseVisionContent(raw);
        Assert.Single(parsed);
        Assert.Equal("Tom Hanks", parsed[0].Name);
        Assert.Equal(31, parsed[0].TmdbPersonId);
        Assert.Equal(0.91, parsed[0].Confidence);
    }

    [Fact]
    public void FilterToCast_DropsUnknownPeople()
    {
        var candidates = new List<SceneIdentifyService.CastCandidate>
        {
            new("Tom Hanks", "Woody", 31, null),
            new("Tim Allen", "Buzz", 48, null)
        };
        var raw = new List<SceneIdentifyService.RawVisionMatch>
        {
            new("Tom Hanks", 31, 0.9),
            new("Someone Else", 999, 0.99)
        };

        var matches = SceneIdentifyService.FilterToCast(raw, candidates);
        Assert.Single(matches);
        Assert.Equal("Tom Hanks", matches[0].Name);
        Assert.Equal("Woody", matches[0].Role);
    }

    [Fact]
    public void FilterToCast_DropsLowConfidence()
    {
        var candidates = new List<SceneIdentifyService.CastCandidate>
        {
            new("Tom Hanks", "Woody", 31, null)
        };
        var raw = new List<SceneIdentifyService.RawVisionMatch>
        {
            new("Tom Hanks", 31, 0.1)
        };

        Assert.Empty(SceneIdentifyService.FilterToCast(raw, candidates));
    }

    [Fact]
    public void ExtractCastCandidates_TakesCastDepartmentOnly()
    {
        var credits = new FullCrewResponse
        {
            Departments =
            [
                new CrewDepartment
                {
                    Name = "Directing",
                    People = [new CrewPerson { Name = "Director Person", TmdbPersonId = 1 }]
                },
                new CrewDepartment
                {
                    Name = "Cast",
                    People =
                    [
                        new CrewPerson { Name = "A", Role = "Lead", TmdbPersonId = 2, Order = 0 },
                        new CrewPerson { Name = "B", Role = "Friend", TmdbPersonId = 3, Order = 1 }
                    ]
                }
            ]
        };

        var cast = SceneIdentifyService.ExtractCastCandidates(credits);
        Assert.Equal(2, cast.Count);
        Assert.Equal("A", cast[0].Name);
    }

    [Fact]
    public void ExtractCastCandidates_FallsBackWhenCastDepartmentMissing()
    {
        var credits = new FullCrewResponse
        {
            Departments =
            [
                new CrewDepartment
                {
                    Name = "Directing",
                    People = [new CrewPerson { Name = "Director Person", TmdbPersonId = 1, Order = 0 }]
                },
                new CrewDepartment
                {
                    Name = "Writing",
                    People = [new CrewPerson { Name = "Writer Person", TmdbPersonId = 2, Order = 1 }]
                }
            ]
        };

        var cast = SceneIdentifyService.ExtractCastCandidates(credits);
        Assert.Equal(2, cast.Count);
        Assert.Contains(cast, c => c.Name == "Director Person");
        Assert.Contains(cast, c => c.Name == "Writer Person");
    }
}
