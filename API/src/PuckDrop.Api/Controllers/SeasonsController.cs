using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("seasons")]
public class SeasonsController(SeasonService seasonService) : ControllerBase
{
    /// <summary>
    /// List all seasons.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SeasonResponse>>> ListSeasons(CancellationToken cancellationToken)
    {
        var seasons = await seasonService.ListSeasonsAsync(cancellationToken);
        var response = seasons.Select(s => new SeasonResponse(
            s.SeasonId, s.Name, s.StartDate.ToString("yyyy-MM-dd"), s.EndDate.ToString("yyyy-MM-dd")
        )).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Get the current active season.
    /// </summary>
    [HttpGet("current")]
    public async Task<ActionResult<SeasonResponse>> GetCurrentSeason(CancellationToken cancellationToken)
    {
        var season = await seasonService.GetCurrentSeasonAsync(cancellationToken);
        if (season is null)
            return NotFound(new ErrorResponse("SEASON_NOT_FOUND", "No active season found."));

        return Ok(new SeasonResponse(
            season.SeasonId, season.Name, season.StartDate.ToString("yyyy-MM-dd"), season.EndDate.ToString("yyyy-MM-dd")
        ));
    }
}
