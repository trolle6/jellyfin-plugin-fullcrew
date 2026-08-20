using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Registers <see cref="IndexHtmlInjectionMiddleware"/> early in the ASP.NET pipeline.
/// </summary>
public sealed class ScriptInjectionStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<IndexHtmlInjectionMiddleware>();
            next(app);
        };
    }
}
