using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Enums;
using PuckDrop.Domain.Repositories;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("polls/{pollId}")]
public class ResultsController : ControllerBase
{
    private readonly PollService _pollService;
    private readonly IUserAnswerRepository _answerRepository;

    public ResultsController(PollService pollService, IUserAnswerRepository answerRepository)
    {
        _pollService = pollService;
        _answerRepository = answerRepository;
    }

    /// <summary>
    /// Get all users' answers and scores for a scored poll.
    /// Only accessible when poll status is Scored.
    /// </summary>
    [HttpGet("results")]
    public async Task<ActionResult<PollResultsResponse>> GetResults(string pollId, CancellationToken cancellationToken)
    {
        var pollData = await _pollService.GetPollWithQuestionsAsync(pollId, cancellationToken);
        if (pollData is null)
            return NotFound(new ErrorResponse("POLL_NOT_FOUND", $"Poll '{pollId}' not found."));

        var (poll, questions, options) = pollData.Value;

        if (poll.Status != PollStatus.Scored)
            return BadRequest(new ErrorResponse("VALIDATION_ERROR", "Results are only available for scored polls."));

        // Build question responses
        var optionsByQuestion = options.GroupBy(o => o.QuestionId).ToDictionary(g => g.Key, g => g.ToList());
        var questionResponses = questions.Select(q => new QuestionResponse(
            q.QuestionId,
            q.Text,
            q.SortOrder,
            q.CorrectOptionId,
            optionsByQuestion.GetValueOrDefault(q.QuestionId, [])
                .Select(o => new OptionResponse(o.OptionId, o.Text, o.SortOrder)).ToList()
        )).ToList();

        // Get all user answers
        var allAnswers = await _answerRepository.GetAllAnswersForPollAsync(pollId, cancellationToken);

        // Group by user
        var userResults = allAnswers
            .GroupBy(a => a.UserId)
            .Select(g => new UserResultResponse(
                g.Key,
                g.Key, // TODO: Resolve display name from Cognito in Phase 5
                g.Select(a => new UserAnswerResponse(
                    a.QuestionId, a.SelectedOptionId, a.SubmittedAt.ToString("O"), a.IsCorrect
                )).ToList(),
                g.Count(a => a.IsCorrect == true)
            ))
            .OrderByDescending(u => u.Points)
            .ToList();

        return Ok(new PollResultsResponse(poll.PollId, poll.Status.ToString(), questionResponses, userResults));
    }
}
