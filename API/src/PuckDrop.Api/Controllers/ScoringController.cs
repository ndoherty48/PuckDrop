using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("polls/{pollId}")]
public class ScoringController : ControllerBase
{
    private readonly ScoringService _scoringService;

    public ScoringController(ScoringService scoringService)
    {
        _scoringService = scoringService;
    }

    /// <summary>
    /// Score a poll — mark correct answers for all questions (admin only).
    /// Triggers scoring evaluation, leaderboard updates, and status transition to Scored.
    /// </summary>
    [HttpPost("score")]
    public async Task<IActionResult> ScorePoll(
        string pollId,
        [FromBody] ScorePollRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var correctAnswers = request.Answers
                .Select(a => (a.QuestionId, a.CorrectOptionId))
                .ToList();

            // TODO: In Phase 5, resolve display names from Cognito user pool
            // For now, use a placeholder resolver
            string DisplayNameResolver(string userId) => userId;

            await _scoringService.ScorePollAsync(pollId, correctAnswers, DisplayNameResolver, cancellationToken);

            return Ok(new { message = "Poll scored successfully." });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ErrorResponse("POLL_NOT_FOUND", $"Poll '{pollId}' not found."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse("VALIDATION_ERROR", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse("VALIDATION_ERROR", ex.Message));
        }
    }
}
