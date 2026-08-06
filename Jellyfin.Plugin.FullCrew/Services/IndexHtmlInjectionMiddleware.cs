using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Injects the Full Crew script into jellyfin-web HTML responses at request time
/// (works when index.html is read-only, same approach as Jellyfin Enhanced).
/// </summary>
public sealed class IndexHtmlInjectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IndexHtmlInjectionMiddleware> _logger;
    private static bool _loggedOnce;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexHtmlInjectionMiddleware"/> class.
    /// </summary>
    public IndexHtmlInjectionMiddleware(RequestDelegate next, ILogger<IndexHtmlInjectionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldIntercept(context.Request))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await _next(context).ConfigureAwait(false);

        if (context.Response.StatusCode is < 200 or >= 300)
        {
            buffer.Seek(0, SeekOrigin.Begin);
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            return;
        }

        buffer.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync().ConfigureAwait(false);

        if (!string.IsNullOrEmpty(body)
            && body.Contains("</body>", StringComparison.OrdinalIgnoreCase)
            && !body.Contains("/FullCrew/fullcrew.js", StringComparison.OrdinalIgnoreCase)
            && !body.Contains("FullCrew-early", StringComparison.OrdinalIgnoreCase))
        {
            body = InsertScript(body);

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                _logger.LogInformation("Full Crew: injected client script via request-time middleware.");
            }
        }

        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentLength = bytes.Length;
        context.Response.Body = originalBody;
        await context.Response.Body.WriteAsync(bytes).ConfigureAwait(false);
    }

    private static bool ShouldIntercept(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        var path = request.Path.Value ?? string.Empty;
        // Only jellyfin-web entry points — not every path that happens to end in index.html.
        return path.Equals("/web/", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/web", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/web/index.html", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase);
    }

    private static string InsertScript(string html)
    {
        const string tag = ScriptInjectionService.ScriptTag;

        // Prefer sitting next to Jellyfin Enhanced's injected tag when present.
        const string enhancedMarker = "JellyfinEnhanced/script";
        var enhancedIdx = html.IndexOf(enhancedMarker, StringComparison.OrdinalIgnoreCase);
        if (enhancedIdx >= 0)
        {
            var scriptEnd = html.IndexOf("</script>", enhancedIdx, StringComparison.OrdinalIgnoreCase);
            if (scriptEnd >= 0)
            {
                return html.Insert(scriptEnd + "</script>".Length, tag);
            }
        }

        var bodyIdx = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyIdx < 0 ? html : html.Insert(bodyIdx, tag);
    }
}
