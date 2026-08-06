namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Canonical TMDB department display order shared by config defaults and credits grouping.
/// </summary>
internal static class CrewDepartments
{
    /// <summary>Default enabled departments and accordion sort order.</summary>
    public static readonly string[] DefaultOrder =
    [
        "Cast",
        "Directing",
        "Writing",
        "Production",
        "Camera",
        "Editing",
        "Sound",
        "Art",
        "Costume & Make-Up",
        "Visual Effects",
        "Lighting",
        "Crew",
        "Other"
    ];
}
