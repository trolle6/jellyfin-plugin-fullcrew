using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Jellyfin.Plugin.FullCrew.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

[assembly: Guid("a8f3c2e1-9b4d-4f6a-8e2c-1d5b7a9c0e3f")]

namespace Jellyfin.Plugin.FullCrew;

/// <summary>
/// The main Full Crew plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        // Jellyfin 10.11 rewrites meta.json whenever Version.ToString() equals
        // the on-disk manifest version. A permission failure on that write is
        // fatal. Never throw here — and keep the reported version string from
        // matching the four-part meta.json / catalog value.
        try
        {
            SetAttributes(
                AssemblyFilePath,
                DataFolderPath,
                PluginFolderAccess.StartupSafeVersion(typeof(Plugin).Assembly.GetName().Version));
            PluginFolderAccess.TryEnsureWritable(Path.GetDirectoryName(AssemblyFilePath));
        }
        catch
        {
            // Constructor must stay non-throwing.
        }
    }

    /// <inheritdoc />
    public override string Name => "Full Crew";

    /// <inheritdoc />
    public override string Description =>
        "Full cast and crew by department, audio-type browsing, break bumpers, library stats, and studio pages.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("a8f3c2e1-9b4d-4f6a-8e2c-1d5b7a9c0e3f");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        ];
    }
}
