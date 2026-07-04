using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("polls/{pollId}/questions")]
public class QuestionsController : ControllerBase
{
    private readonly PollService _pollService;

    public QuestionsController(PollService pollService)
    {
        _pollService = pollService;
    }

    [HttpPost]
    public async Task<ActionResult<QuestionResponse>> AddQuestion(
        string pollId, [FromBody] CreateQuestionRequest request, CancellationToken ct)
    {
        var options = request.Options.Select(o => (o.Text, o.SortOrder)).ToList();
        var question = await _pollService.AddQuestionAsync(pollId, request.Text, request.SortOrder, options, ct);

        // Read back to get generated option IDs
        var pollData = await _pollService.GetPollWithQuestionsAsync(pollId, ct);
        var (_, questions, allOptions) = pollData!.Value;

        var created = questions.First(q => q.QuestionId == question.QuestionId);
        var questionOptions = allOptions
            .Where(o => o.QuestionId == question.QuestionId)
            .Select(o => new OptionResponse(o.OptionId, o.Text, o.SortOrder))
            .ToList();

        return Created(
            $"polls/{pollId}/questions/{question.QuestionId}",
            new QuestionResponse(created.QuestionId, created.Text, created.SortOrder, created.CorrectOptionId, questionOptions));
    }

    [HttpPut("{questionId}")]
    public async Task<ActionResult<QuestionResponse>> UpdateQuestion(
        string pollId, string questionId, [FromBody] UpdateQuestionRequest request, CancellationToken ct)
    {
        await _pollService.DeleteQuestionAsync(pollId, questionId, ct);

        var options = request.Options.Select(o => (o.Text, o.SortOrder)).ToList();
        var question = await _pollService.AddQuestionAsync(pollId, request.Text, request.SortOrder, options, ct);

        var pollData = await _pollService.GetPollWithQuestionsAsync(pollId, ct);
        var (_, questions, allOptions) = pollData!.Value;

        var updated = questions.First(q => q.QuestionId == question.QuestionId);
        var questionOptions = allOptions
            .Where(o => o.QuestionId == question.QuestionId)
            .Select(o => new OptionResponse(o.OptionId, o.Text, o.SortOrder))
            .ToList();

        return Ok(new QuestionResponse(updated.QuestionId, updated.Text, updated.SortOrder, updated.CorrectOptionId, questionOptions));
    }

    [HttpDelete("{questionId}")]
    public async Task<IActionResult> DeleteQuestion(string pollId, string questionId, CancellationToken ct)
    {
        await _pollService.DeleteQuestionAsync(pollId, questionId, ct);
        return NoContent();
    }
}
