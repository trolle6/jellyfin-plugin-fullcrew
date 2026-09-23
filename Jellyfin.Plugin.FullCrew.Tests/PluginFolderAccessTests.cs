using System;
using System.IO;
using Xunit;

namespace Jellyfin.Plugin.FullCrew.Tests;

public class PluginFolderAccessTests
{
    [Fact]
    public void StartupSafeVersion_IsThreePart_AndDoesNotEqualFourPartAssemblyString()
    {
        var assembly = new Version(1, 8, 7, 0);
        var safe = PluginFolderAccess.StartupSafeVersion(assembly);

        Assert.Equal("1.8.7", safe.ToString());
        Assert.NotEqual(assembly.ToString(), safe.ToString());
        Assert.NotEqual("1.8.7.0", safe.ToString());
    }

    [Theory]
    [InlineData(1, 8, 1, 0, "1.8.1")]
    [InlineData(1, 8, 3, 0, "1.8.3")]
    [InlineData(1, 7, 6, 2, "1.7.6")]
    public void StartupSafeVersion_DropsRevision(int major, int minor, int build, int revision, string expected)
    {
        var assembly = new Version(major, minor, build, revision);
        Assert.Equal(expected, PluginFolderAccess.StartupSafeVersion(assembly).ToString());
        Assert.NotEqual(assembly.ToString(), PluginFolderAccess.StartupSafeVersion(assembly).ToString());
    }

    [Fact]
    public void StartupSafeVersion_NullFallsBackToThreePart()
    {
        Assert.Equal("1.8.7", PluginFolderAccess.StartupSafeVersion(null).ToString());
    }

    [Fact]
    public void TryEnsureWritable_ClearsReadOnlyMetaJson()
    {
        var dir = Directory.CreateTempSubdirectory("fullcrew-meta-");
        try
        {
            var meta = Path.Combine(dir.FullName, "meta.json");
            File.WriteAllText(meta, "{}");
            File.SetAttributes(meta, File.GetAttributes(meta) | FileAttributes.ReadOnly);

            PluginFolderAccess.TryEnsureWritable(dir.FullName);

            Assert.False(File.GetAttributes(meta).HasFlag(FileAttributes.ReadOnly));
            File.WriteAllText(meta, "{\"ok\":true}");
        }
        finally
        {
            foreach (var file in dir.GetFiles())
            {
                file.Attributes = FileAttributes.Normal;
            }

            dir.Delete(true);
        }
    }

    [Fact]
    public void TryEnsureWritable_IgnoresMissingDirectory()
    {
        PluginFolderAccess.TryEnsureWritable(null);
        PluginFolderAccess.TryEnsureWritable(string.Empty);
        PluginFolderAccess.TryEnsureWritable(Path.Combine(Path.GetTempPath(), "fullcrew-missing-" + Guid.NewGuid()));
    }
}
