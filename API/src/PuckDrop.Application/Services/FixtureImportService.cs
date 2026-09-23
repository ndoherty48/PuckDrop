using PuckDrop.Application.Models;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services.Abstractions;
using PuckDrop.Domain.Entities;

namespace PuckDrop.Application.Services;

public class FixtureImportService(
    IIcsFixtureFeedFetcher fixtureFeedFetcher,
    IPollRepository pollRepository,
    ISeasonRepository seasonRepository)
{
    /// <summary>
    /// Fetches the feed and returns fixtures within [startDate, endDate], ordered by game date,
    /// each flagged with whether a poll for it already exists. Doesn't create anything - the admin
    /// picks which ones to import from this preview.
    /// </summary>
    public async Task<IReadOnlyList<FixtureCandidate>> PreviewAsync(
        string icsUrl,
        DateOnly startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken = default)
    {
        var fixtures = (await fixtureFeedFetcher.FetchAsync(icsUrl, cancellationToken))
            .Where(f => f.GameDate >= startDate && (endDate is null || f.GameDate <= endDate))
            .OrderBy(f => f.Deadline)
            .ToList();

        var existing = await LoadExistingFixtureKeysAsync(fixtures, cancellationToken);

        return fixtures
            .Select(f => f with { AlreadyImported = existing.Contains((f.GameDate, f.Title)) })
            .ToList();
    }

    /// <summary>
    /// A fixture is "already imported" when a poll with the same title lands on the same game date
    /// in that date's season. Only seasons the candidate fixtures actually touch are checked, and
    /// only ones that already exist - a season that's never had a poll created in it can't have a
    /// duplicate, so this never triggers the season auto-creation that CreatePollAsync does.
    /// </summary>
    private async Task<HashSet<(DateOnly GameDate, string Title)>> LoadExistingFixtureKeysAsync(
        IReadOnlyList<FixtureCandidate> fixtures, CancellationToken cancellationToken)
    {
        var seasonIds = fixtures.Select(f => Season.DeriveSeasonId(f.GameDate)).Distinct();
        var keys = new HashSet<(DateOnly, string)>();

        foreach (var seasonId in seasonIds)
        {
            if (await seasonRepository.GetByIdAsync(seasonId, cancellationToken) is null)
                continue;

            var polls = await pollRepository.ListBySeasonAsync(seasonId, cancellationToken);
            foreach (var poll in polls)
                keys.Add((poll.GameDate, poll.Title));
        }

        return keys;
    }
}
