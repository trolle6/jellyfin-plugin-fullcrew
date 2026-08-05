using Jellyfin.Plugin.FullCrew.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.FullCrew;

/// <summary>
/// Registers plugin services with the DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<CreditsService>();
        serviceCollection.AddSingleton<BumperService>();
        serviceCollection.AddSingleton<LibraryStatsService>();
        serviceCollection.AddSingleton<StudioPageService>();
        serviceCollection.AddSingleton<IStartupFilter, ScriptInjectionStartupFilter>();
        serviceCollection.AddHostedService<ScriptInjectionService>();
    }
}
