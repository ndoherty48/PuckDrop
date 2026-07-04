using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Api.Mappings;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Models;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("polls/{pollId}/questions")]
public class QuestionsController(PollService pollService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<QuestionResponse>> AddQuestion(
        string pollId, [FromBody] CreateQuestionRequest request, CancellationToken ct)
    {
        var options = request.Options.Select(o => new OptionDefinition(o.Text, o.SortOrder)).ToList();
        var question = await pollService.AddQuestionAsync(pollId, request.Text, request.SortOrder, options, ct);

        var response = await GetQuestionResponse(pollId, question.QuestionId, ct);
        return Created($"polls/{pollId}/questions/{question.QuestionId}", response);
    }

    [HttpPut("{questionId}")]
    public async Task<ActionResult<QuestionResponse>> UpdateQuestion(
        string pollId, string questionId, [FromBody] UpdateQuestionRequest request, CancellationToken ct)
    {
        await pollService.DeleteQuestionAsync(pollId, questionId, ct);

        var options = request.Options.Select(o => new OptionDefinition(o.Text, o.SortOrder)).ToList();
        var question = await pollService.AddQuestionAsync(pollId, request.Text, request.SortOrder, options, ct);

        var response = await GetQuestionResponse(pollId, question.QuestionId, ct);
        return Ok(response);
    }

    [HttpDelete("{questionId}")]
    public async Task<IActionResult> DeleteQuestion(string pollId, string questionId, CancellationToken ct)
    {
        await pollService.DeleteQuestionAsync(pollId, questionId, ct);
        return NoContent();
    }

    private async Task<QuestionResponse> GetQuestionResponse(string pollId, string questionId, CancellationToken ct)
    {
        var pollData = await pollService.GetPollWithQuestionsAsync(pollId, ct)
            ?? throw new KeyNotFoundException($"Poll '{pollId}' not found.");

        var question = pollData.Questions.First(q => q.QuestionId == questionId);
        var questionOptions = pollData.Options.Where(o => o.QuestionId == questionId).ToList();

        return question.ToResponse(questionOptions);
    }
}
