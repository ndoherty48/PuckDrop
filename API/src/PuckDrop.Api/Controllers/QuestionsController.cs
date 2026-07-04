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

    /// <summary>
    /// Add a question with options to a poll (admin only).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<QuestionResponse>> AddQuestion(
        string pollId,
        [FromBody] CreateQuestionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = request.Options.Select(o => (o.Text, o.SortOrder)).ToList();
            var question = await _pollService.AddQuestionAsync(pollId, request.Text, request.SortOrder, options, cancellationToken);

            // Return the created question (options have their generated IDs)
            var pollData = await _pollService.GetPollWithQuestionsAsync(pollId, cancellationToken);
            if (pollData is null)
                return NotFound(new ErrorResponse("POLL_NOT_FOUND", $"Poll '{pollId}' not found."));

            var (_, questions, allOptions) = pollData.Value;
            var createdQuestion = questions.FirstOrDefault(q => q.QuestionId == question.QuestionId);
            if (createdQuestion is null)
                return StatusCode(500, new ErrorResponse("INTERNAL_ERROR", "Failed to retrieve created question."));

            var questionOptions = allOptions
                .Where(o => o.QuestionId == question.QuestionId)
                .Select(o => new OptionResponse(o.OptionId, o.Text, o.SortOrder))
                .ToList();

            return Created($"polls/{pollId}/questions/{question.QuestionId}",
                new QuestionResponse(createdQuestion.QuestionId, createdQuestion.Text, createdQuestion.SortOrder, createdQuestion.CorrectOptionId, questionOptions));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ErrorResponse("POLL_NOT_FOUND", $"Poll '{pollId}' not found."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse("VALIDATION_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Edit a question and its options (admin only).
    /// Replaces the question and options entirely.
    /// </summary>
    [HttpPut("{questionId}")]
    public async Task<ActionResult<QuestionResponse>> UpdateQuestion(
        string pollId,
        string questionId,
        [FromBody] UpdateQuestionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Delete old and re-create with same ID
            await _pollService.DeleteQuestionAsync(pollId, questionId, cancellationToken);

            var options = request.Options.Select(o => (o.Text, o.SortOrder)).ToList();
            // Re-use the existing questionId by calling the repo directly via a new service method
            // For now, delete + add (which generates a new ID) — this is a simplification
            var question = await _pollService.AddQuestionAsync(pollId, request.Text, request.SortOrder, options, cancellationToken);

            var pollData = await _pollService.GetPollWithQuestionsAsync(pollId, cancellationToken);
            var (_, questions, allOptions) = pollData!.Value;
            var updatedQuestion = questions.First(q => q.QuestionId == question.QuestionId);
            var questionOptions = allOptions
                .Where(o => o.QuestionId == question.QuestionId)
                .Select(o => new OptionResponse(o.OptionId, o.Text, o.SortOrder))
                .ToList();

            return Ok(new QuestionResponse(updatedQuestion.QuestionId, updatedQuestion.Text, updatedQuestion.SortOrder, updatedQuestion.CorrectOptionId, questionOptions));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ErrorResponse("POLL_NOT_FOUND", $"Poll '{pollId}' not found."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse("VALIDATION_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Delete a question and its options (admin only).
    /// </summary>
    [HttpDelete("{questionId}")]
    public async Task<IActionResult> DeleteQuestion(string pollId, string questionId, CancellationToken cancellationToken)
    {
        try
        {
            await _pollService.DeleteQuestionAsync(pollId, questionId, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ErrorResponse("POLL_NOT_FOUND", $"Poll '{pollId}' not found."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse("VALIDATION_ERROR", ex.Message));
        }
    }
}
