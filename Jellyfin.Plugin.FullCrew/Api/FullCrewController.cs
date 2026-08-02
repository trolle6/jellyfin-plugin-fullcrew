using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.FullCrew.Models;
using Jellyfin.Plugin.FullCrew.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FullCrew.Api;

/// <summary>
/// API endpoints for full cast/crew data and client assets.
/// </summary>
[ApiController]
[Route("FullCrew")]
public class FullCrewController : ControllerBase
{
    private readonly CreditsService _creditsService;
    private readonly ILogger<FullCrewController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FullCrewController"/> class.
    /// </summary>
    public FullCrewController(CreditsService creditsService, ILogger<FullCrewController> logger)
    {
        _creditsService = creditsService;
        _logger = logger;
    }

    /// <summary>
    /// Gets categorized cast and crew for an item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Categorized credits.</returns>
    [HttpGet("{itemId}")]
    [Authorize]
    [ProducesResponseType(typeof(FullCrewResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FullCrewResponse>> GetCredits(
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken)
    {
        var result = await _creditsService.GetCreditsAsync(itemId, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Serves the client JavaScript.
    /// </summary>
    [HttpGet("fullcrew.js")]
    [HttpHead("fullcrew.js")]
    [AllowAnonymous]
    public ActionResult GetScript()
    {
        return GetClientAsset("fullcrew.js", "application/javascript");
    }

    /// <summary>
    /// Serves the client CSS.
    /// </summary>
    [HttpGet("fullcrew.css")]
    [HttpHead("fullcrew.css")]
    [AllowAnonymous]
    public ActionResult GetStyles()
    {
        return GetClientAsset("fullcrew.css", "text/css");
    }

    private ActionResult GetClientAsset(string fileName, string contentType)
    {
        // Prefer loose files next to the plugin DLL. Overwriting the DLL while Jellyfin is
        // running corrupts memory-mapped embedded resources; disk files stay readable.
        var diskPath = ResolvePluginWebPath(fileName);
        if (diskPath is not null && System.IO.File.Exists(diskPath))
        {
            return PhysicalFile(diskPath, contentType);
        }

        var assembly = typeof(Plugin).Assembly;
        var resourceName = $"{typeof(Plugin).Namespace}.Web.{fileName}";
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            _logger.LogWarning(
                "Full Crew client asset missing: {FileName} (disk and embedded). Restart Jellyfin after updating the plugin DLL.",
                fileName);
            return NotFound();
        }

        return new FileStreamResult(stream, contentType);
    }

    private static string? ResolvePluginWebPath(string fileName)
    {
        try
        {
            var assemblyPath = typeof(Plugin).Assembly.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                return null;
            }

            var pluginDir = Path.GetDirectoryName(assemblyPath);
            if (string.IsNullOrWhiteSpace(pluginDir))
            {
                return null;
            }

            return Path.Combine(pluginDir, "Web", fileName);
        }
        catch
        {
            return null;
        }
    }
}
