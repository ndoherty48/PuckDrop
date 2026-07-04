using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

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

        return Ok(answers.Select(a => new UserAnswerResponse(
            a.QuestionId, a.SelectedOptionId, a.SubmittedAt.ToString("O"), a.IsCorrect
        )).ToList());
    }

    [HttpPut]
    public async Task<ActionResult<IReadOnlyList<UserAnswerResponse>>> SubmitAnswers(
        string pollId, [FromBody] SubmitAnswersRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var answerTuples = request.Answers.Select(a => (a.QuestionId, a.SelectedOptionId)).ToList();

        var answers = await answerService.SubmitAnswersAsync(userId, pollId, answerTuples, ct);

        return Ok(answers.Select(a => new UserAnswerResponse(
            a.QuestionId, a.SelectedOptionId, a.SubmittedAt.ToString("O"), a.IsCorrect
        )).ToList());
    }

    private string GetUserId() => User.FindFirst("sub")?.Value ?? "anonymous";
}
