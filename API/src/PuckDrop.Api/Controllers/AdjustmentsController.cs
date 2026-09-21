using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Auth;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[Authorize(Policy = ApiServiceCollectionExtensions.AdminPolicy)]
[ApiController]
[Route("leaderboard/adjustments")]
public class AdjustmentsController(PointAdjustmentService adjustmentService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> AddAdjustment([FromBody] CreateAdjustmentRequest request, CancellationToken ct)
    {
        var adjustment = await adjustmentService.AddAsync(
            request.SeasonId, request.UserId, request.Points, request.Reason, User.GetUserId(), ct);

        return Ok(new LeaderboardAdjustmentResponse(
            adjustment.AdjustmentId, adjustment.Points, adjustment.Reason));
    }

    /// <summary>
    /// Removes an adjustment. Season and user are required as well as the id: the adjustment is
    /// stored under both, and there is no index on the id alone.
    /// </summary>
    [HttpDelete("{adjustmentId}")]
    public async Task<IActionResult> RemoveAdjustment(
        string adjustmentId,
        [FromQuery] string userId,
        [FromQuery] string? seasonId,
        CancellationToken ct)
    {
        await adjustmentService.RemoveAsync(seasonId, userId, adjustmentId, ct);
        return Ok(new { message = "Adjustment removed." });
    }
}
