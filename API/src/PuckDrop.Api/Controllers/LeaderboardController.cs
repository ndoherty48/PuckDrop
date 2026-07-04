using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Api.Mappings;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[Authorize]
[ApiController]
[Route("leaderboard")]
public class LeaderboardController(LeaderboardService leaderboardService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LeaderboardResponse>> GetLeaderboard(
        [FromQuery] string? seasonId, CancellationToken ct)
    {
        var result = await leaderboardService.GetLeaderboardAsync(seasonId, ct);
        return Ok(result.ToResponse());
    }
}
