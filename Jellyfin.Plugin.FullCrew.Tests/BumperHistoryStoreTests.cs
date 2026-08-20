using System.Collections.Generic;
using Jellyfin.Plugin.FullCrew.Services;
using Xunit;

namespace Jellyfin.Plugin.FullCrew.Tests;

public class BumperHistoryStoreTests
{
    [Fact]
    public void PickUnseen_SkipsSeenAndReturnsNextBest()
    {
        var ranked = new[] { "a", "b", "c" };
        var seen = new HashSet<string> { "a" };

        var pick = BumperHistoryStore.PickUnseen(
            ranked,
            x => x,
            seen,
            skipKey: null,
            onPoolExhausted: null);

        Assert.Equal("b", pick);
    }

    [Fact]
    public void PickUnseen_SkipsCurrentClipWhenTryingAnother()
    {
        var ranked = new[] { "a", "b", "c" };
        var seen = new HashSet<string> { "a" };

        var pick = BumperHistoryStore.PickUnseen(
            ranked,
            x => x,
            seen,
            skipKey: "b",
            onPoolExhausted: null);

        Assert.Equal("c", pick);
    }

    [Fact]
    public void PickUnseen_ClearsPoolWhenEverythingWasSeen()
    {
        var ranked = new[] { "a", "b" };
        var seen = new HashSet<string> { "a", "b" };
        var cleared = false;

        var pick = BumperHistoryStore.PickUnseen(
            ranked,
            x => x,
            seen,
            skipKey: null,
            onPoolExhausted: () => cleared = true);

        Assert.True(cleared);
        Assert.Equal("a", pick);
    }

    [Fact]
    public void NormalizeShowKey_LowercasesAndTrims()
    {
        Assert.Equal("the-flintstones", BumperHistoryStore.NormalizeShowKey("The Flintstones"));
    }
}
