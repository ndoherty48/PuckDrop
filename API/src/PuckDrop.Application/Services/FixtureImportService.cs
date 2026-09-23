using PuckDrop.Application.Models;
using PuckDrop.Application.Services.Abstractions;

namespace PuckDrop.Application.Services;

public class FixtureImportService(IIcsFixtureFeedFetcher fixtureFeedFetcher)
{
    /// <summary>
    /// Fetches the feed and returns fixtures within [startDate, endDate], ordered by game date.
    /// Doesn't create anything - the admin picks which ones to import from this preview.
    /// </summary>
    public async Task<IReadOnlyList<FixtureCandidate>> PreviewAsync(
        string icsUrl,
        DateOnly startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken = default)
    {
        var fixtures = await fixtureFeedFetcher.FetchAsync(icsUrl, cancellationToken);

        return fixtures
            .Where(f => f.GameDate >= startDate && (endDate is null || f.GameDate <= endDate))
            .OrderBy(f => f.Deadline)
            .ToList();
    }
}
