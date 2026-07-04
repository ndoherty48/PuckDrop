using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("polls/{pollId}")]
public class ScoringController(ScoringService scoringService) : ControllerBase
{
    [HttpPost("score")]
    public async Task<IActionResult> ScorePoll(
        string pollId, [FromBody] ScorePollRequest request, CancellationToken ct)
    {
        var correctAnswers = request.Answers.Select(a => (a.QuestionId, a.CorrectOptionId)).ToList();

        // TODO: Resolve display names from Cognito in Phase 5
        string DisplayNameResolver(string userId) => userId;

        await scoringService.ScorePollAsync(pollId, correctAnswers, DisplayNameResolver, ct);

        return Ok(new { message = "Poll scored successfully." });
    }
}
