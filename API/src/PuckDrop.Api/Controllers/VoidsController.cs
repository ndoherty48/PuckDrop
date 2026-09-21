using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Auth;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[Authorize(Policy = ApiServiceCollectionExtensions.AdminPolicy)]
[ApiController]
[Route("polls/{pollId}")]
public class VoidsController(PollVoidService pollVoidService) : ControllerBase
{
    [HttpPost("voids")]
    public async Task<IActionResult> VoidPicks(
        string pollId, [FromBody] VoidPicksRequest request, CancellationToken ct)
    {
        await pollVoidService.VoidPicksAsync(pollId, request.UserId, request.Reason, User.GetUserId(), ct);
        return Ok(new { message = "Picks voided." });
    }

    [HttpDelete("voids/{userId}")]
    public async Task<IActionResult> RestorePicks(string pollId, string userId, CancellationToken ct)
    {
        await pollVoidService.RestorePicksAsync(pollId, userId, ct);
        return Ok(new { message = "Picks restored." });
    }
}
