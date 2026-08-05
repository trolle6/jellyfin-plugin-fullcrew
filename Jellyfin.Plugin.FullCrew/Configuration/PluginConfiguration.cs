using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.FullCrew.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        TmdbApiKey = string.Empty;
        CacheHours = 12;
        MaxPeoplePerDepartment = 100;
        EnableBumpers = true;
        EnableYouTubeBumpers = true;
        EnableLibraryStats = true;
        BumpersCollectionName = "Bumpers";
        EnabledDepartments =
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

    /// <summary>
    /// Gets or sets an optional TMDB API key.
    /// When empty, the plugin uses the same shared key as Jellyfin's built-in TheMovieDb provider.
    /// </summary>
    public string TmdbApiKey { get; set; }

    /// <summary>
    /// Gets or sets how long credits are cached, in hours.
    /// </summary>
    public int CacheHours { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of people shown per department.
    /// </summary>
    public int MaxPeoplePerDepartment { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Break Bumper button is shown.
    /// </summary>
    public bool EnableBumpers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether YouTube curated/search bumpers are allowed.
    /// </summary>
    public bool EnableYouTubeBumpers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Library Stats page and API are enabled.
    /// </summary>
    public bool EnableLibraryStats { get; set; }

    /// <summary>
    /// Gets or sets the local collection/folder name to prefer for bumpers.
    /// </summary>
    public string BumpersCollectionName { get; set; }

    /// <summary>
    /// Gets or sets the department names to display.
    /// </summary>
    public string[] EnabledDepartments { get; set; }
}
