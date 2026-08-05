using System;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.FullCrew.Models;
using Jellyfin.Plugin.FullCrew.Services;
using MediaBrowser.Controller.Library;
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
    private const string JellyfinUserIdClaim = "Jellyfin-UserId";

    private readonly CreditsService _creditsService;
    private readonly BumperService _bumperService;
    private readonly LibraryStatsService _libraryStatsService;
    private readonly StudioPageService _studioPageService;
    private readonly IUserManager _userManager;
    private readonly ILogger<FullCrewController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FullCrewController"/> class.
    /// </summary>
    public FullCrewController(
        CreditsService creditsService,
        BumperService bumperService,
        LibraryStatsService libraryStatsService,
        StudioPageService studioPageService,
        IUserManager userManager,
        ILogger<FullCrewController> logger)
    {
        _creditsService = creditsService;
        _bumperService = bumperService;
        _libraryStatsService = libraryStatsService;
        _studioPageService = studioPageService;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets categorized cast and crew for an item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Categorized credits.</returns>
    [HttpGet("{itemId:guid}")]
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
    /// Resolves a nostalgia break-bumper for an item (local library, curated YouTube, or search).
    /// </summary>
    [HttpGet("{itemId:guid}/bumper")]
    [Authorize]
    [ProducesResponseType(typeof(BumperResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BumperResponse>> GetBumper(
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken)
    {
        var result = await _bumperService.GetBumperAsync(itemId, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Resolves an official trailer when Jellyfin's native trailer button is unavailable.
    /// </summary>
    [HttpGet("{itemId:guid}/trailer")]
    [Authorize]
    [ProducesResponseType(typeof(BumperResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BumperResponse>> GetTrailer(
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken)
    {
        var result = await _bumperService.GetTrailerAsync(itemId, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Gets aggregated Movie + Series library statistics for charts and insights.
    /// </summary>
    [HttpGet("stats")]
    [Authorize]
    [ProducesResponseType(typeof(LibraryStatsResponse), StatusCodes.Status200OK)]
    public ActionResult<LibraryStatsResponse> GetLibraryStats()
    {
        var user = TryGetCurrentUser();
        var result = _libraryStatsService.GetStats(user);
        return Ok(result);
    }

    /// <summary>
    /// Gets the full ranked bucket list for a single stats category (detail page).
    /// </summary>
    /// <param name="category">Category key such as actors, genres, hdr, videoCodecs.</param>
    [HttpGet("stats/{category}")]
    [Authorize]
    [ProducesResponseType(typeof(LibraryStatsCategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<LibraryStatsCategoryResponse> GetLibraryStatsCategory([FromRoute] string category)
    {
        var user = TryGetCurrentUser();
        var result = _libraryStatsService.GetCategoryStats(user, category);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    /// <summary>
    /// Studio detail page: TMDB company metadata + library titles for a studio / name-root cluster.
    /// </summary>
    /// <param name="name">Studio or cluster display name (route key).</param>
    /// <param name="id">Optional Jellyfin Studio item id.</param>
    /// <param name="branches">Optional comma/pipe-separated exact studio credit names for clusters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("studio/{name}")]
    [Authorize]
    [ProducesResponseType(typeof(StudioPageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StudioPageResponse>> GetStudioPage(
        [FromRoute] string name,
        [FromQuery] string? id,
        [FromQuery] string? branches,
        CancellationToken cancellationToken)
    {
        Guid? itemId = null;
        if (!string.IsNullOrWhiteSpace(id) && Guid.TryParse(id, out var parsed) && parsed != Guid.Empty)
        {
            itemId = parsed;
        }

        var branchList = ParseBranchList(branches);
        var decodedName = Uri.UnescapeDataString(name ?? string.Empty);
        var user = TryGetCurrentUser();
        var result = await _studioPageService
            .GetStudioPageAsync(user, itemId, decodedName, branchList, cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Studio detail by Jellyfin item id only.
    /// </summary>
    [HttpGet("studio/item/{itemId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(StudioPageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StudioPageResponse>> GetStudioPageByItem(
        [FromRoute] Guid itemId,
        [FromQuery] string? branches,
        CancellationToken cancellationToken)
    {
        var user = TryGetCurrentUser();
        var result = await _studioPageService
            .GetStudioPageAsync(user, itemId, null, ParseBranchList(branches), cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }

    private static IReadOnlyList<string>? ParseBranchList(string? branches)
    {
        if (string.IsNullOrWhiteSpace(branches))
        {
            return null;
        }

        var parts = branches.Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : parts;
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

    private User? TryGetCurrentUser()
    {
        var claim = User.FindFirstValue(JellyfinUserIdClaim)
                    ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(claim, out var userId) || userId == Guid.Empty)
        {
            return null;
        }

        return _userManager.GetUserById(userId);
    }
}
