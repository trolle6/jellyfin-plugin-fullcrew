using System.Collections.Generic;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Normalized TMDB movie/series details consumed by <see cref="TitleFactsFormatter"/>.
/// Decoupled from the raw TMDB JSON so formatting can be unit-tested in isolation.
/// </summary>
public sealed record TitleFactsData
{
    /// <summary>Gets a value indicating whether this is a movie (true) or a series (false).</summary>
    public bool IsMovie { get; init; }

    /// <summary>Gets the release/production status (e.g. "Released", "Ended").</summary>
    public string? Status { get; init; }

    /// <summary>Gets the runtime in minutes (movie runtime or representative episode length).</summary>
    public int? Runtime { get; init; }

    /// <summary>Gets the production budget in whole USD (0/unknown is ignored).</summary>
    public long? Budget { get; init; }

    /// <summary>Gets the box-office revenue in whole USD (0/unknown is ignored).</summary>
    public long? Revenue { get; init; }

    /// <summary>Gets the movie release date (ISO yyyy-MM-dd).</summary>
    public string? ReleaseDate { get; init; }

    /// <summary>Gets the series first air date (ISO yyyy-MM-dd).</summary>
    public string? FirstAirDate { get; init; }

    /// <summary>Gets the series last air date (ISO yyyy-MM-dd).</summary>
    public string? LastAirDate { get; init; }

    /// <summary>Gets a value indicating whether the series is still in production.</summary>
    public bool InProduction { get; init; }

    /// <summary>Gets the number of seasons (series only).</summary>
    public int? NumberOfSeasons { get; init; }

    /// <summary>Gets the number of episodes (series only).</summary>
    public int? NumberOfEpisodes { get; init; }

    /// <summary>Gets the original-language ISO 639-1 code.</summary>
    public string? OriginalLanguage { get; init; }

    /// <summary>Gets the genre names.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Gets the production company names.</summary>
    public IReadOnlyList<string> ProductionCompanies { get; init; } = [];

    /// <summary>Gets the network names (series only).</summary>
    public IReadOnlyList<string> Networks { get; init; } = [];

    /// <summary>Gets the production country names.</summary>
    public IReadOnlyList<string> ProductionCountries { get; init; } = [];

    /// <summary>Gets the spoken languages (for original-language name resolution).</summary>
    public IReadOnlyList<SpokenLanguage> SpokenLanguages { get; init; } = [];

    /// <summary>Gets the TMDB user vote average (0–10).</summary>
    public double? VoteAverage { get; init; }

    /// <summary>Gets the TMDB user vote count.</summary>
    public int? VoteCount { get; init; }

    /// <summary>Gets the marketing tagline.</summary>
    public string? Tagline { get; init; }

    /// <summary>Gets the official homepage URL.</summary>
    public string? Homepage { get; init; }
}

/// <summary>
/// A TMDB spoken language entry.
/// </summary>
/// <param name="Iso6391">The ISO 639-1 code.</param>
/// <param name="EnglishName">The English display name.</param>
public sealed record SpokenLanguage(string? Iso6391, string? EnglishName);
