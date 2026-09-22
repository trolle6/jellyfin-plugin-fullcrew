using Jellyfin.Plugin.FullCrew.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.FullCrew;

/// <summary>
/// Registers plugin services with the DI container.
/// Do not register IHostedService here — a failing hosted service aborts Jellyfin startup.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<CreditsService>();
        serviceCollection.AddSingleton<BumperHistoryStore>();
        serviceCollection.AddSingleton<BumperService>();
        serviceCollection.AddSingleton<LibraryStatsService>();
        serviceCollection.AddSingleton<StudioPageService>();
        serviceCollection.AddSingleton<SceneIndexStore>();
        serviceCollection.AddSingleton<SceneIdentifyService>();
        serviceCollection.AddSingleton<ScriptInjectionService>();
        serviceCollection.AddSingleton<AudioTrackIndexService>();
        serviceCollection.AddSingleton<IStartupFilter, ScriptInjectionStartupFilter>();
    }
}
