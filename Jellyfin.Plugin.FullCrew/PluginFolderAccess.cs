using System;
using System.IO;

namespace Jellyfin.Plugin.FullCrew;

/// <summary>
/// Startup helpers that keep Jellyfin 10.11 from taking the server down when it
/// cannot rewrite <c>meta.json</c>.
/// </summary>
/// <remarks>
/// Jellyfin 10.11 <c>PluginManager.CreatePluginInstance</c> treats a matching
/// assembly/manifest version string as "changed" and always calls
/// <c>SaveManifest</c>. That write only catches <see cref="ArgumentException"/>,
/// so an <see cref="UnauthorizedAccessException"/> is logged as
/// "Error creating Plugin" and the failure path writes <c>meta.json</c> again
/// without a catch — aborting boot. SMB-copied plugin folders are often not
/// writable by the container user, which is exactly this crash.
/// </remarks>
internal static class PluginFolderAccess
{
    /// <summary>
    /// Three-part version whose <see cref="Version.ToString()"/> is <c>x.y.z</c>,
    /// never the four-part <c>x.y.z.w</c> string shipped in <c>meta.json</c>
    /// and written by the catalog. That mismatch skips the fatal rewrite.
    /// </summary>
    internal static Version StartupSafeVersion(Version? assemblyVersion)
    {
        var version = assemblyVersion ?? new Version(1, 8, 8, 0);
        var build = version.Build < 0 ? 0 : version.Build;
        return new Version(version.Major, version.Minor, build);
    }

    /// <summary>
    /// Best-effort: clear the read-only bit and chmod the plugin folder plus
    /// <c>meta.json</c> so Jellyfin can persist state when it still writes.
    /// Never throws.
    /// </summary>
    internal static void TryEnsureWritable(string? pluginDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory))
            {
                return;
            }

            TryMakeWritable(pluginDirectory, isDirectory: true);
            var meta = Path.Combine(pluginDirectory, "meta.json");
            if (File.Exists(meta))
            {
                TryMakeWritable(meta, isDirectory: false);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private static void TryMakeWritable(string path, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                var info = new DirectoryInfo(path);
                if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    info.Attributes &= ~FileAttributes.ReadOnly;
                }
            }
            else
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    info.Attributes &= ~FileAttributes.ReadOnly;
                }
            }

            if (!OperatingSystem.IsWindows())
            {
                var mode = isDirectory
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                      | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                      | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite
                      | UnixFileMode.GroupRead | UnixFileMode.GroupWrite
                      | UnixFileMode.OtherRead | UnixFileMode.OtherWrite;
                File.SetUnixFileMode(path, mode);
            }
        }
        catch
        {
            // The version-string mismatch is the real guard.
        }
    }
}
