using PuckDrop.Application.Models;

namespace PuckDrop.Application.Services.Abstractions;

/// <summary>
/// Fetches and parses a public ICS calendar feed into fixture candidates - implemented in
/// PuckDrop.Infrastructure, which is the only layer that knows about HTTP-fetching a third-party
/// calendar and the Ical.Net library, the same way only it knows about DynamoDB.
/// </summary>
public interface IIcsFixtureFeedFetcher
{
    /// <summary>
    /// Returns every fixture in the feed, unfiltered - date-range filtering is a business concern
    /// that belongs in FixtureImportService, not here.
    /// </summary>
    Task<IReadOnlyList<FixtureCandidate>> FetchAsync(string icsUrl, CancellationToken cancellationToken = default);
}
