using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.FullCrew.Models;

/// <summary>
/// Full cast and crew response for an item.
/// </summary>
public class FullCrewResponse
{
    /// <summary>
    /// Gets or sets the Jellyfin item id.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TMDB id used for the lookup.
    /// </summary>
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the media kind (Movie, Series, Episode).
    /// </summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an error message when credits could not be loaded.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets credit departments.
    /// </summary>
    public IReadOnlyList<CrewDepartment> Departments { get; set; } = [];
}

/// <summary>
/// A department grouping of people.
/// </summary>
public class CrewDepartment
{
    /// <summary>
    /// Gets or sets the department display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets people in this department.
    /// </summary>
    public IReadOnlyList<CrewPerson> People { get; set; } = [];
}

/// <summary>
/// A cast or crew member.
/// </summary>
public class CrewPerson
{
    /// <summary>
    /// Gets or sets the person name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the character name (cast) or job title (crew).
    /// Joined with “ · ” when multiple unique roles remain after collapse.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets unique collapsed role names (preferred for UI).
    /// Empty when only <see cref="Role"/> is available.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Roles { get; set; }

    /// <summary>
    /// Gets or sets the TMDB person id.
    /// </summary>
    public int? TmdbPersonId { get; set; }

    /// <summary>
    /// Gets or sets the profile image URL.
    /// </summary>
    public string? ProfileUrl { get; set; }

    /// <summary>
    /// Gets or sets cast billing order when available.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Order { get; set; }
}
