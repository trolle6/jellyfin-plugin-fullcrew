using System;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Route-key → stored bucket map + display title. String-only so tests do not
/// need to load Jellyfin.Data just to resolve "actors" / "videoCodecs".
/// </summary>
internal static class LibraryStatsCategories
{
    /// <summary>
    /// Maps a category route key to the stored bucket-item map key and display title.
    /// </summary>
    internal static bool TryResolve(string? category, out string canonical, out string title)
    {
        canonical = string.Empty;
        title = string.Empty;
        if (string.IsNullOrWhiteSpace(category))
        {
            return false;
        }

        var key = Normalize(category);
        if (key.Length == 0)
        {
            return false;
        }

        if (key.StartsWith("people", StringComparison.Ordinal) && key.Length > "people".Length)
        {
            key = key["people".Length..];
        }

        (string Canonical, string Title)? match = key switch
        {
            "actors" or "actor" => ("actors", "Top Actor"),
            "directors" or "director" => ("directors", "Top Director"),
            "writers" or "writer" => ("writers", "Top Writer"),
            "creators" or "creator" => ("creators", "Top Creator"),
            "producers" or "producer" => ("producers", "Top Producer"),
            "gueststars" or "gueststar" => ("guestStars", "Top Guest Star"),
            "composers" or "composer" => ("composers", "Top Composer"),
            "editors" or "editor" => ("editors", "Top Editor"),
            "artists" or "artist" => ("artists", "Top Artist"),
            "authors" or "author" => ("authors", "Top Author"),
            "albumartists" or "albumartist" => ("albumArtists", "Top Album Artist"),
            "coverartists" or "coverartist" => ("coverArtists", "Top Cover Artist"),
            "unknown" => ("unknown", "Top Unknown"),
            "types" => ("types", "Types"),
            "genres" => ("genres", "Genres"),
            "studios" => ("studios", "Studios"),
            "tags" => ("tags", "Tags"),
            "collections" => ("collections", "Collections"),
            "decades" => ("decades", "Decades"),
            "years" => ("years", "Release years"),
            "ratings" or "officialratings" => ("ratings", "Official ratings"),
            "community" or "communityratings" => ("community", "Community scores"),
            "languages" => ("languages", "Languages"),
            "resolutions" => ("resolutions", "Resolutions"),
            "hdr" or "videoranges" => ("hdr", "HDR / range"),
            "videocodecs" => ("videoCodecs", "Video codecs"),
            "audio" or "audiochannels" => ("audioChannels", "Audio channels"),
            "audiocodecs" => ("audioCodecs", "Audio codecs"),
            "audiotracks" or "specialaudio" or "specialaudiotracks" => ("audioTracks", "Special audio"),
            _ => null
        };

        if (match is null)
        {
            return false;
        }

        canonical = match.Value.Canonical;
        title = match.Value.Title;
        return true;
    }

    internal static string Normalize(string category)
    {
        var trimmed = category.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var buffer = new char[trimmed.Length];
        var n = 0;
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[n++] = char.ToLowerInvariant(ch);
            }
        }

        return n == 0 ? string.Empty : new string(buffer, 0, n);
    }
}
