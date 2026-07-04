using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("leaderboard")]
public class LeaderboardController : ControllerBase
{
    private readonly LeaderboardService _leaderboardService;

    public LeaderboardController(LeaderboardService leaderboardService)
    {
        _leaderboardService = leaderboardService;
    }

    /// <summary>
    /// Get the season leaderboard. Defaults to current season if seasonId not provided.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<LeaderboardResponse>> GetLeaderboard(
        [FromQuery] string? seasonId,
        CancellationToken cancellationToken)
    {
        var result = await _leaderboardService.GetLeaderboardAsync(seasonId, cancellationToken);

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
