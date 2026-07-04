using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("polls/{pollId}/answers")]
public class AnswersController : ControllerBase
{
    private readonly AnswerService _answerService;

    public AnswersController(AnswerService answerService)
    {
        _answerService = answerService;
    }

    /// <summary>
    /// Get the current user's answers for a poll.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserAnswerResponse>>> GetAnswers(
        string pollId,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var answers = await _answerService.GetUserAnswersAsync(userId, pollId, cancellationToken);

        var response = answers.Select(a => new UserAnswerResponse(
            a.QuestionId, a.SelectedOptionId, a.SubmittedAt.ToString("O"), a.IsCorrect
        )).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Submit or update answers for a poll.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<IReadOnlyList<UserAnswerResponse>>> SubmitAnswers(
        string pollId,
        [FromBody] SubmitAnswersRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        try
        {
            var answerTuples = request.Answers
                .Select(a => (a.QuestionId, a.SelectedOptionId))
                .ToList();

            var answers = await _answerService.SubmitAnswersAsync(userId, pollId, answerTuples, cancellationToken);

            var response = answers.Select(a => new UserAnswerResponse(
                a.QuestionId, a.SelectedOptionId, a.SubmittedAt.ToString("O"), a.IsCorrect
            )).ToList();

            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ErrorResponse("POLL_NOT_FOUND", $"Poll '{pollId}' not found."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse("POLL_CLOSED", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse("VALIDATION_ERROR", ex.Message));
        }
    }

    private string GetUserId()
    {
        // TODO: Extract from Cognito JWT claims (sub) in Phase 5
        return User.FindFirst("sub")?.Value ?? "anonymous";
    }
}
