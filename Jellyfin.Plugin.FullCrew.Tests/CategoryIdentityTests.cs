using Jellyfin.Plugin.FullCrew.Services;
using Xunit;

namespace Jellyfin.Plugin.FullCrew.Tests;

public class CategoryIdentityTests
{
    [Theory]
    [InlineData("actors", "actors", "Top Actor")]
    [InlineData("People:Actor", "actors", "Top Actor")]
    [InlineData("videoCodecs", "videoCodecs", "Video codecs")]
    [InlineData("hdr", "hdr", "HDR / range")]
    [InlineData("years", "years", "Release years")]
    [InlineData("guestStars", "guestStars", "Top Guest Star")]
    public void TryResolveCategoryIdentity_MapsKnownKeys(string input, string canonical, string title)
    {
        Assert.True(LibraryStatsCategories.TryResolve(input, out var resolved, out var resolvedTitle));
        Assert.Equal(canonical, resolved);
        Assert.Equal(title, resolvedTitle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-category")]
    [InlineData("   ")]
    public void TryResolveCategoryIdentity_RejectsUnknown(string input)
    {
        Assert.False(LibraryStatsCategories.TryResolve(input, out _, out _));
    }
}
