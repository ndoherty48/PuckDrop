using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Auth;
using PuckDrop.Api.Contracts;
using PuckDrop.Api.Mappings;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[Authorize]
[ApiController]
[Route("polls")]
public class PollsController(PollService pollService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PollResponse>>> ListPolls(
        [FromQuery] string seasonId, CancellationToken ct)
    {
        var polls = await pollService.ListPollsAsync(seasonId, ct);
        return Ok(polls.Select(p => p.ToResponse()).ToList());
    }

    [HttpGet("active")]
    public async Task<ActionResult<IReadOnlyList<PollResponse>>> GetActivePolls(
        [FromQuery] string seasonId, CancellationToken ct)
    {
        var polls = await pollService.GetActivePollsAsync(seasonId, ct);
        return Ok(polls.Select(p => p.ToResponse()).ToList());
    }

    [HttpGet("{pollId}")]
    public async Task<ActionResult<PollDetailResponse>> GetPoll(string pollId, CancellationToken ct)
    {
        var result = await pollService.GetPollWithQuestionsAsync(pollId, ct);
        if (result is null)
            return NotFound(new ErrorResponse("POLL_NOT_FOUND", $"Poll '{pollId}' not found."));

        return Ok(result.Poll.ToDetailResponse(result.Questions, result.Options));
    }

    [Authorize(Policy = ApiServiceCollectionExtensions.AdminPolicy)]
    [HttpPost]
    public async Task<ActionResult<PollResponse>> CreatePoll(
        [FromBody] CreatePollRequest request, CancellationToken ct)
    {
        var poll = await pollService.CreatePollAsync(
            request.Title,
            DateOnly.ParseExact(request.GameDate, "yyyy-MM-dd"),
            DateTime.Parse(request.Deadline).ToUniversalTime(),
            User.GetUserId(),
            ct);

        return CreatedAtAction(nameof(GetPoll), new { pollId = poll.PollId }, poll.ToResponse());
    }

    [Authorize(Policy = ApiServiceCollectionExtensions.AdminPolicy)]
    [HttpPut("{pollId}")]
    public async Task<ActionResult<PollResponse>> UpdatePoll(
        string pollId, [FromBody] UpdatePollRequest request, CancellationToken ct)
    {
        var deadline = request.Deadline is not null
            ? DateTime.Parse(request.Deadline).ToUniversalTime()
            : (DateTime?)null;

        var poll = await pollService.UpdatePollAsync(pollId, request.Title, deadline, ct);
        return Ok(poll.ToResponse());
    }

    [Authorize(Policy = ApiServiceCollectionExtensions.AdminPolicy)]
    [HttpPost("{pollId}/publish")]
    public async Task<ActionResult<PollResponse>> PublishPoll(string pollId, CancellationToken ct)
    {
        var poll = await pollService.PublishPollAsync(pollId, ct);
        return Ok(poll.ToResponse());
    }

    [Authorize(Policy = ApiServiceCollectionExtensions.AdminPolicy)]
    [HttpPost("{pollId}/close")]
    public async Task<ActionResult<PollResponse>> ClosePoll(string pollId, CancellationToken ct)
    {
        var poll = await pollService.ClosePollAsync(pollId, ct);
        return Ok(poll.ToResponse());
    }

}
