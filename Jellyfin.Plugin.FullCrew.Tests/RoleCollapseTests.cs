using Jellyfin.Plugin.FullCrew.Services;
using Xunit;

namespace Jellyfin.Plugin.FullCrew.Tests;

public class RoleCollapseTests
{
    [Fact]
    public void Collapse_FlattensNearDuplicateSlateVariants()
    {
        var credits = new[]
        {
            "Mr. Slate (voice)",
            "Mr. Slate / Announcer (voice)",
            "Mr. Slate / TV Announcer (voice)",
            "Mr. Slate / Cop (voice)",
            "Grand Poobah (voice)",
            "mr. slate / Judge (voice)"
        };

        var unique = RoleCollapse.Collapse(credits);

        Assert.Contains("Mr. Slate", unique);
        Assert.Contains("Announcer", unique);
        Assert.Contains("TV Announcer", unique);
        Assert.Contains("Cop", unique);
        Assert.Contains("Grand Poobah", unique);
        Assert.Contains("Judge", unique);
        Assert.Equal("Mr. Slate", unique[0]);
        Assert.DoesNotContain(unique, r => r.Contains("(voice)", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Collapse_PreservesDistinctCrewJobs()
    {
        var unique = RoleCollapse.Collapse(["Director", "Writer", "Executive Producer"]);

        Assert.Equal(3, unique.Count);
        Assert.Contains("Director", unique);
        Assert.Contains("Writer", unique);
        Assert.Contains("Executive Producer", unique);
    }

    [Fact]
    public void Collapse_KeepsNameSuffixesWithComma()
    {
        var unique = RoleCollapse.Collapse(["John Smith, Jr. (uncredited)"]);

        Assert.Single(unique);
        Assert.Equal("John Smith, Jr.", unique[0]);
    }

    [Fact]
    public void FormatPreview_ShowsPlusNWhenOverflow()
    {
        var unique = RoleCollapse.Collapse([
            "Mr. Slate / Announcer (voice)",
            "Mr. Slate / Cop (voice)",
            "Grand Poobah (voice)",
            "Judge (voice)"
        ]);

        var preview = RoleCollapse.FormatPreview(unique, maxVisible: 3);

        Assert.True(preview.HiddenCount >= 1);
        Assert.Contains(" · ", preview.Label);
        Assert.DoesNotContain("+", preview.Label, StringComparison.Ordinal);
        Assert.Equal(string.Join(" · ", unique), preview.Tooltip);
    }

    [Fact]
    public void FormatPreview_NoHiddenWhenShort()
    {
        var preview = RoleCollapse.FormatPreview(["Fred", "Barney"], maxVisible: 3);

        Assert.Equal(0, preview.HiddenCount);
        Assert.Equal("Fred · Barney", preview.Label);
        Assert.Equal(preview.Label, preview.Tooltip);
    }

    [Fact]
    public void SplitSegments_SplitsSlashAndComma()
    {
        var parts = RoleCollapse.SplitSegments("Fred / Wilma, Barney");

        Assert.Equal(["Fred", "Wilma", "Barney"], parts);
    }
}
