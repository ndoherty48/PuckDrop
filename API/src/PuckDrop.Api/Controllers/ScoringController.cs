using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;
using PuckDrop.Application.Models;

namespace PuckDrop.Api.Controllers;

[Authorize(Policy = ApiServiceCollectionExtensions.AdminPolicy)]
[ApiController]
[Route("polls/{pollId}")]
public class ScoringController(ScoringService scoringService) : ControllerBase
{
    [HttpPost("score")]
    public async Task<IActionResult> ScorePoll(
        string pollId, [FromBody] ScorePollRequest request, CancellationToken ct)
    {
        var correctAnswers = request.Answers
            .Select(a => new QuestionScore(a.QuestionId, a.CorrectOptionId))
            .ToList();

        await scoringService.ScorePollAsync(pollId, correctAnswers, ct);
        return Ok(new { message = "Poll scored successfully." });
    }
}
