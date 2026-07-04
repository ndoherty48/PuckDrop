using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Api.Mappings;
using PuckDrop.Application.Services;
using PuckDrop.Application.Models;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("polls/{pollId}/answers")]
public class AnswersController(AnswerService answerService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserAnswerResponse>>> GetAnswers(
        string pollId, CancellationToken ct)
    {
        var userId = GetUserId();
        var answers = await answerService.GetUserAnswersAsync(userId, pollId, ct);
        return Ok(answers.Select(a => a.ToResponse()).ToList());
    }

    [HttpPut]
    public async Task<ActionResult<IReadOnlyList<UserAnswerResponse>>> SubmitAnswers(
        string pollId, [FromBody] SubmitAnswersRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var submissions = request.Answers
            .Select(a => new AnswerSubmission(a.QuestionId, a.SelectedOptionId))
            .ToList();

        var answers = await answerService.SubmitAnswersAsync(userId, pollId, submissions, ct);
        return Ok(answers.Select(a => a.ToResponse()).ToList());
    }

    private string GetUserId() => User.FindFirst("sub")?.Value ?? "anonymous";
}
