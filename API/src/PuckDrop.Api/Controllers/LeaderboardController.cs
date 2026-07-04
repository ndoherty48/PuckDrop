using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("leaderboard")]
public class LeaderboardController(LeaderboardService leaderboardService) : ControllerBase
{
    /// <summary>
    /// Get the season leaderboard. Defaults to current season if seasonId not provided.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<LeaderboardResponse>> GetLeaderboard(
        [FromQuery] string? seasonId,
        CancellationToken cancellationToken)
    {
        var result = await leaderboardService.GetLeaderboardAsync(seasonId, cancellationToken);

        var entries = result.Entries.Select(e => new LeaderboardEntryResponse(
            e.Entry.UserId,
            e.Entry.DisplayName,
            e.Entry.TotalPoints,
            e.Entry.TotalAnswered,
            e.Rank
        )).ToList();

        return Ok(new LeaderboardResponse(result.SeasonId, entries));
    }
}
