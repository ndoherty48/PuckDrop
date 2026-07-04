using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Api.Mappings;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[Authorize]
[ApiController]
[Route("seasons")]
public class SeasonsController(SeasonService seasonService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SeasonResponse>>> ListSeasons(CancellationToken ct)
    {
        var seasons = await seasonService.ListSeasonsAsync(ct);
        return Ok(seasons.Select(s => s.ToResponse()).ToList());
    }

    [HttpGet("current")]
    public async Task<ActionResult<SeasonResponse>> GetCurrentSeason(CancellationToken ct)
    {
        var season = await seasonService.GetCurrentSeasonAsync(ct);
        if (season is null)
            return NotFound(new ErrorResponse("SEASON_NOT_FOUND", "No active season found."));

        return Ok(season.ToResponse());
    }
}
