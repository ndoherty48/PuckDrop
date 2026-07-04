using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("polls")]
public class PollsController(PollService pollService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PollResponse>>> ListPolls(
        [FromQuery] string seasonId, CancellationToken ct)
    {
        var polls = await pollService.ListPollsAsync(seasonId, ct);
        return Ok(polls.Select(ToPollResponse).ToList());
    }

    [HttpGet("active")]
    public async Task<ActionResult<IReadOnlyList<PollResponse>>> GetActivePolls(
        [FromQuery] string seasonId, CancellationToken ct)
    {
        var polls = await pollService.GetActivePollsAsync(seasonId, ct);
        return Ok(polls.Select(ToPollResponse).ToList());
    }

    [HttpGet("{pollId}")]
    public async Task<ActionResult<PollDetailResponse>> GetPoll(string pollId, CancellationToken ct)
    {
        var result = await pollService.GetPollWithQuestionsAsync(pollId, ct);
        if (result is null)
            return NotFound(new ErrorResponse("POLL_NOT_FOUND", $"Poll '{pollId}' not found."));

        var (poll, questions, options) = result.Value;
        return Ok(ToPollDetailResponse(poll, questions, options));
    }

    [HttpPost]
    public async Task<ActionResult<PollResponse>> CreatePoll(
        [FromBody] CreatePollRequest request, CancellationToken ct)
    {
        var poll = await pollService.CreatePollAsync(
            request.Title,
            DateOnly.ParseExact(request.GameDate, "yyyy-MM-dd"),
            DateTime.Parse(request.Deadline).ToUniversalTime(),
            GetUserId(),
            ct);

        return CreatedAtAction(nameof(GetPoll), new { pollId = poll.PollId }, ToPollResponse(poll));
    }

    [HttpPut("{pollId}")]
    public async Task<ActionResult<PollResponse>> UpdatePoll(
        string pollId, [FromBody] UpdatePollRequest request, CancellationToken ct)
    {
        var deadline = request.Deadline is not null
            ? DateTime.Parse(request.Deadline).ToUniversalTime()
            : (DateTime?)null;

        var poll = await pollService.UpdatePollAsync(pollId, request.Title, deadline, ct);
        return Ok(ToPollResponse(poll));
    }

    [HttpPost("{pollId}/publish")]
    public async Task<ActionResult<PollResponse>> PublishPoll(string pollId, CancellationToken ct)
    {
        var poll = await pollService.PublishPollAsync(pollId, ct);
        return Ok(ToPollResponse(poll));
    }

    [HttpPost("{pollId}/close")]
    public async Task<ActionResult<PollResponse>> ClosePoll(string pollId, CancellationToken ct)
    {
        var poll = await pollService.ClosePollAsync(pollId, ct);
        return Ok(ToPollResponse(poll));
    }

    // ─── Mapping ──────────────────────────────────────────────────────────────

    private static PollResponse ToPollResponse(GameDayPoll poll) => new(
        poll.PollId, poll.SeasonId, poll.GameDate.ToString("yyyy-MM-dd"),
        poll.Title, poll.Deadline.ToString("O"), poll.Status.ToString(),
        poll.CreatedBy, poll.CreatedAt.ToString("O"));

    private static PollDetailResponse ToPollDetailResponse(
        GameDayPoll poll, IReadOnlyList<Question> questions, IReadOnlyList<Option> options)
    {
        var optionsByQuestion = options.GroupBy(o => o.QuestionId).ToDictionary(g => g.Key, g => g.ToList());

        var questionResponses = questions.Select(q => new QuestionResponse(
            q.QuestionId, q.Text, q.SortOrder, q.CorrectOptionId,
            optionsByQuestion.GetValueOrDefault(q.QuestionId, [])
                .Select(o => new OptionResponse(o.OptionId, o.Text, o.SortOrder)).ToList()
        )).ToList();

        return new PollDetailResponse(
            poll.PollId, poll.SeasonId, poll.GameDate.ToString("yyyy-MM-dd"),
            poll.Title, poll.Deadline.ToString("O"), poll.Status.ToString(),
            poll.CreatedBy, poll.CreatedAt.ToString("O"), questionResponses);
    }

    private string GetUserId() => User.FindFirst("sub")?.Value ?? "anonymous";
}
