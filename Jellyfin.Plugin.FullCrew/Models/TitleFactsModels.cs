using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.FullCrew.Models;

/// <summary>
/// Production facts (budget, box office, runtime, status, …) for a movie or series detail page.
/// </summary>
public class TitleFactsResponse
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
    /// Gets or sets the media kind (Movie or Series).
    /// </summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an error message when facts could not be loaded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the marketing tagline, when TMDB has one.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tagline { get; set; }

    /// <summary>
    /// Gets or sets the official homepage URL, when TMDB has one.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Homepage { get; set; }

    /// <summary>
    /// Gets or sets the display-ready label/value facts, already formatted server-side.
    /// </summary>
    public IReadOnlyList<TitleFact> Facts { get; set; } = [];
}

/// <summary>
/// A single display-ready fact (label + formatted value).
/// </summary>
public class TitleFact
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TitleFact"/> class.
    /// </summary>
    public TitleFact()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleFact"/> class.
    /// </summary>
    /// <param name="label">The fact label.</param>
    /// <param name="value">The formatted fact value.</param>
    public TitleFact(string label, string value)
    {
        Label = label;
        Value = value;
    }

    /// <summary>
    /// Gets or sets the fact label (e.g. "Budget").
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the formatted fact value (e.g. "$63M").
    /// </summary>
    public string Value { get; set; } = string.Empty;
}
