using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.FullCrew.Models;

/// <summary>
/// Tracks which bumpers were already served for a show so picks rotate.
/// </summary>
public class BumperHistoryDocument
{
    /// <summary>Gets or sets a stable key for the show (normalized title).</summary>
    public string ShowKey { get; set; } = string.Empty;

    /// <summary>Gets or sets seen bumper keys in oldest-first order.</summary>
    public List<BumperHistoryEntry> Entries { get; set; } = [];
}

/// <summary>
/// One bumper that was already picked for a show.
/// </summary>
public class BumperHistoryEntry
{
    /// <summary>Gets or sets the bumper key (local:… or yt:…).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets when this bumper was last served (UTC).</summary>
    public DateTimeOffset LastSeenAt { get; set; }
}
