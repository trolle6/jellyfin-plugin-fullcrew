using System;
using System.Collections.Generic;
using Jellyfin.Plugin.FullCrew.Services;
using Xunit;

namespace Jellyfin.Plugin.FullCrew.Tests;

public class AudioVisibilityTests
{
    [Fact]
    public void FilterByVisibleItemIds_NullSet_KeepsEverything()
    {
        var tracks = new[]
        {
            (Id: Guid.NewGuid(), Name: "a"),
            (Id: Guid.NewGuid(), Name: "b")
        };

        var kept = AudioTrackIndexService.FilterByVisibleItemIds(tracks, t => t.Id, null);

        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void FilterByVisibleItemIds_DropsHiddenItems()
    {
        var visible = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        var tracks = new[]
        {
            (Id: visible, Name: "keep"),
            (Id: hidden, Name: "drop"),
            (Id: visible, Name: "keep-too")
        };

        var kept = AudioTrackIndexService.FilterByVisibleItemIds(
            tracks,
            t => t.Id,
            new HashSet<Guid> { visible });

        Assert.Equal(2, kept.Count);
        Assert.All(kept, t => Assert.Equal(visible, t.Id));
    }
}
