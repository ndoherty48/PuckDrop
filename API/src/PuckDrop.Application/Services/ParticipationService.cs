using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Enums;
using PuckDrop.Application.Repositories;

namespace PuckDrop.Application.Services;

public class ParticipationService(IPollRepository pollRepository, IUserAnswerRepository answerRepository)
{
    /// <summary>
    /// How many players have picked in each published poll this season, out of everyone who has
    /// picked in any of them. There's no member list, so the season's pickers are the roster: a
    /// brand-new player counts from their first pick. Drafts can't have picks, so they're skipped.
    /// </summary>
    public async Task<SeasonParticipation> GetSeasonParticipationAsync(
        string seasonId, CancellationToken cancellationToken = default)
    {
        var answersByPoll = await GetSeasonAnswersAsync(seasonId, cancellationToken);

        // A player who answered only some questions has still picked
        var polls = answersByPoll
            .Select(p => new PollParticipation(p.PollId, p.Answers.Select(a => a.UserId).Distinct().Count()))
            .ToList();
        var playerCount = answersByPoll.SelectMany(p => p.Answers).Select(a => a.UserId).Distinct().Count();

        return new SeasonParticipation(playerCount, polls);
    }

    /// <summary>
    /// One poll's pick count against the same season roster, plus who on it hasn't picked yet -
    /// named by the display name from their latest pick anywhere this season.
    /// </summary>
    public async Task<PollParticipationDetail> GetPollParticipationAsync(
        string pollId, CancellationToken cancellationToken = default)
    {
        var poll = await pollRepository.GetByIdAsync(pollId, cancellationToken)
            ?? throw new KeyNotFoundException($"Poll '{pollId}' not found.");

        var answersByPoll = await GetSeasonAnswersAsync(poll.SeasonId, cancellationToken);

        var players = answersByPoll
            .SelectMany(p => p.Answers)
            .GroupBy(a => a.UserId)
            .Select(g => new Player(g.Key, g.MaxBy(a => a.SubmittedAt)!.DisplayName))
            .ToList();
        var pickers = answersByPoll
            .Where(p => p.PollId == pollId)
            .SelectMany(p => p.Answers)
            .Select(a => a.UserId)
            .ToHashSet();

        var stillToPick = players
            .Where(p => !pickers.Contains(p.UserId))
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new PollParticipationDetail(pollId, pickers.Count, players.Count, stillToPick);
    }

    private async Task<List<(string PollId, IReadOnlyList<UserAnswer> Answers)>> GetSeasonAnswersAsync(
        string seasonId, CancellationToken cancellationToken)
    {
        var polls = await pollRepository.ListBySeasonAsync(seasonId, cancellationToken);
        var published = polls.Where(p => p.Status != PollStatus.Draft).ToList();

        var answers = await Task.WhenAll(published.Select(p =>
            answerRepository.GetAllAnswersForPollAsync(p.PollId, cancellationToken)));

        return published.Zip(answers, (poll, a) => (poll.PollId, a)).ToList();
    }
}

public record SeasonParticipation(int PlayerCount, IReadOnlyList<PollParticipation> Polls);

public record PollParticipation(string PollId, int PickedCount);

public record PollParticipationDetail(
    string PollId, int PickedCount, int PlayerCount, IReadOnlyList<Player> StillToPick);

public record Player(string UserId, string DisplayName);
