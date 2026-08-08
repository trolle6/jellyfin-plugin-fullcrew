using Jellyfin.Plugin.FullCrew.Services;
using Xunit;

namespace Jellyfin.Plugin.FullCrew.Tests;

public class SceneIndexStoreTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(9_000_0000L, 0)] // 9 seconds
    [InlineData(10_000_0000L, 10)] // 10 seconds
    [InlineData(25_000_0000L, 20)]
    public void ToBucketSeconds_FloorsToTenSecondBuckets(long ticks, long expectedBucket)
    {
        Assert.Equal(expectedBucket, SceneIndexStore.ToBucketSeconds(ticks));
    }
}
