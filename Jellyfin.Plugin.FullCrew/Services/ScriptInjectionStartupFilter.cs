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
            try
            {
                app.UseMiddleware<IndexHtmlInjectionMiddleware>();
            }
            catch
            {
                // Never block Jellyfin from starting if request-time injection cannot be wired.
            }

            next(app);
        };
    }
}
