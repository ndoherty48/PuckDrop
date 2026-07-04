using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Api.Mappings;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[Authorize]
[ApiController]
[Route("polls/{pollId}")]
public class ResultsController(ResultsService resultsService) : ControllerBase
{
    [HttpGet("results")]
    public async Task<ActionResult<PollResultsResponse>> GetResults(string pollId, CancellationToken ct)
    {
        var results = await resultsService.GetPollResultsAsync(pollId, ct);
        return Ok(results.ToResponse());
    }
}
