using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Api.Mappings;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[Authorize(Policy = ApiServiceCollectionExtensions.AdminPolicy)]
[ApiController]
[Route("fixture-imports")]
public class FixtureImportController(FixtureImportService fixtureImportService) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<ActionResult<FixtureImportPreviewResponse>> Preview(
        [FromBody] FixtureImportPreviewRequest request, CancellationToken ct)
    {
        var fixtures = await fixtureImportService.PreviewAsync(
            request.IcsUrl,
            DateOnly.ParseExact(request.StartDate, "yyyy-MM-dd"),
            request.EndDate is not null ? DateOnly.ParseExact(request.EndDate, "yyyy-MM-dd") : null,
            ct);

        return Ok(new FixtureImportPreviewResponse(fixtures.Select(f => f.ToResponse()).ToList()));
    }
}
