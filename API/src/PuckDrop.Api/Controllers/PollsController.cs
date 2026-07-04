using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("polls")]
public class PollsController : ControllerBase
{
    private readonly PollService _pollService;

    public PollsController(PollService pollService)
    {
        _pollService = pollService;
    }

    /// <summary>
    /// List polls for a season.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PollResponse>>> ListPolls(
        [FromQuery] string seasonId,
        CancellationToken cancellationToken)
    {
        var polls = await _pollService.ListPollsAsync(seasonId, cancellationToken);
        return Ok(polls.Select(ToPollResponse).ToList());
    }

    /// <summary>
    /// Get currently open polls for the current season.
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<IReadOnlyList<PollResponse>>> GetActivePolls(
        [FromQuery] string seasonId,
        CancellationToken cancellationToken)
    {
        var polls = await _pollService.GetActivePollsAsync(seasonId, cancellationToken);
        return Ok(polls.Select(ToPollResponse).ToList());
    }

    /// <summary>
    /// Get a poll with its questions and options.
    /// </summary>
    [HttpGet("{pollId}")]
    public async Task<ActionResult<PollDetailResponse>> GetPoll(string pollId, CancellationToken cancellationToken)
    {
        var result = await _pollService.GetPollWithQuestionsAsync(pollId, cancellationToken);
        if (result is null)
            return NotFound(new ErrorResponse("POLL_NOT_FOUND", $"Poll '{pollId}' not found."));

        var (poll, questions, options) = result.Value;
        var optionsByQuestion = options.GroupBy(o => o.QuestionId).ToDictionary(g => g.Key, g => g.ToList());

        var questionResponses = questions.Select(q => new QuestionResponse(
            q.QuestionId,
            q.Text,
            q.SortOrder,
            q.CorrectOptionId,
            optionsByQuestion.GetValueOrDefault(q.QuestionId, [])
                .Select(o => new OptionResponse(o.OptionId, o.Text, o.SortOrder)).ToList()
        )).ToList();

        return Ok(new PollDetailResponse(
            poll.PollId, poll.SeasonId, poll.GameDate.ToString("yyyy-MM-dd"),
            poll.Title, poll.Deadline.ToString("O"), poll.Status.ToString(),
            poll.CreatedBy, poll.CreatedAt.ToString("O"), questionResponses));
    }

    /// <summary>
    /// Create a new poll (admin only).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PollResponse>> CreatePoll(
        [FromBody] CreatePollRequest request,
        CancellationToken cancellationToken)
    {
        // TODO: Extract userId from JWT claims once auth is wired up
        var userId = GetUserId();

        var poll = await _pollService.CreatePollAsync(
            request.Title,
            DateOnly.ParseExact(request.GameDate, "yyyy-MM-dd"),
            DateTime.Parse(request.Deadline).ToUniversalTime(),
            userId,
            cancellationToken);

        return CreatedAtAction(nameof(GetPoll), new { pollId = poll.PollId }, ToPollResponse(poll));
    }

    /// <summary>
    /// Update a poll's title or deadline (admin only).
    /// </summary>
    [HttpPut("{pollId}")]
    public async Task<ActionResult<PollResponse>> UpdatePoll(
        string pollId,
        [FromBody] UpdatePollRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var deadline = request.Deadline is not null
                ? DateTime.Parse(request.Deadline).ToUniversalTime()
                : (DateTime?)null;

            var poll = await _pollService.UpdatePollAsync(pollId, request.Title, deadline, cancellationToken);
            return Ok(ToPollResponse(poll));
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
    /// Publish a poll (Draft → Open, admin only).
    /// </summary>
    [HttpPost("{pollId}/publish")]
    public async Task<ActionResult<PollResponse>> PublishPoll(string pollId, CancellationToken cancellationToken)
    {
        try
        {
            var poll = await _pollService.PublishPollAsync(pollId, cancellationToken);
            return Ok(ToPollResponse(poll));
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
    /// Manually close a poll (Open → Closed, admin only).
    /// </summary>
    [HttpPost("{pollId}/close")]
    public async Task<ActionResult<PollResponse>> ClosePoll(string pollId, CancellationToken cancellationToken)
    {
        try
        {
            var poll = await _pollService.ClosePollAsync(pollId, cancellationToken);
            return Ok(ToPollResponse(poll));
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

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static PollResponse ToPollResponse(GameDayPoll poll) => new(
        poll.PollId, poll.SeasonId, poll.GameDate.ToString("yyyy-MM-dd"),
        poll.Title, poll.Deadline.ToString("O"), poll.Status.ToString(),
        poll.CreatedBy, poll.CreatedAt.ToString("O"));

    private string GetUserId()
    {
        // TODO: Extract from Cognito JWT claims (sub) in Phase 5
        return User.FindFirst("sub")?.Value ?? "anonymous";
    }
}
