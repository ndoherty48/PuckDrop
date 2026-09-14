using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Auth;
using PuckDrop.Api.Contracts;
using PuckDrop.Api.Mappings;
using PuckDrop.Application.Services;
using PuckDrop.Application.Models;
using PuckDrop.Application.Services.Abstractions;

namespace PuckDrop.Api.Controllers;

[Authorize]
[ApiController]
[Route("polls/{pollId}/answers")]
public class AnswersController(AnswerService answerService, IUserProfileService userProfileService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserAnswerResponse>>> GetAnswers(
        string pollId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var answers = await answerService.GetUserAnswersAsync(userId, pollId, ct);
        return Ok(answers.Select(a => a.ToResponse()).ToList());
    }

    [HttpPut]
    public async Task<ActionResult<IReadOnlyList<UserAnswerResponse>>> SubmitAnswers(
        string pollId, [FromBody] SubmitAnswersRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var displayName = await userProfileService.GetDisplayNameAsync(ct);
        var submissions = request.Answers
            .Select(a => new AnswerSubmission(a.QuestionId, a.SelectedOptionId))
            .ToList();

        var answers = await answerService.SubmitAnswersAsync(userId, displayName, pollId, submissions, ct);
        return Ok(answers.Select(a => a.ToResponse()).ToList());
    }

}
