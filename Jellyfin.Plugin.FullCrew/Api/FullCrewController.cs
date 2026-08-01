using System;
using System.Reflection;
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
    [AllowAnonymous]
    public ActionResult GetScript()
    {
        return GetEmbeddedResource("Web.fullcrew.js", "application/javascript");
    }

    /// <summary>
    /// Serves the client CSS.
    /// </summary>
    [HttpGet("fullcrew.css")]
    [AllowAnonymous]
    public ActionResult GetStyles()
    {
        return GetEmbeddedResource("Web.fullcrew.css", "text/css");
    }

    private ActionResult GetEmbeddedResource(string relativeName, string contentType)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{typeof(Plugin).Namespace}.{relativeName}";
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            _logger.LogWarning("Embedded resource not found: {ResourceName}", resourceName);
            return NotFound();
        }

        return new FileStreamResult(stream, contentType);
    }
}
