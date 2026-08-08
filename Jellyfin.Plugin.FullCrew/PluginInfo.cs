using System.Reflection;

namespace Jellyfin.Plugin.FullCrew;

/// <summary>
/// Assembly-aligned version and outbound HTTP identity for Full Crew services.
/// </summary>
internal static class PluginInfo
{
    /// <summary>Four-part plugin version from the assembly (falls back to 1.5.1.0).</summary>
    public static string Version { get; } =
        typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0]
        ?? typeof(Plugin).Assembly.GetName().Version?.ToString()
        ?? "1.5.1.0";

    /// <summary>User-Agent sent on TMDB / YouTube requests.</summary>
    public static string UserAgent { get; } = "Jellyfin-Plugin-FullCrew/" + Version;
}
