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
        var polls = await pollRepository.ListBySeasonAsync(seasonId, cancellationToken);
        var published = polls.Where(p => p.Status != PollStatus.Draft).ToList();

        var answersByPoll = await Task.WhenAll(published.Select(p =>
            answerRepository.GetAllAnswersForPollAsync(p.PollId, cancellationToken)));

        // A player who answered only some questions has still picked
        var pickersByPoll = published
            .Zip(answersByPoll, (poll, answers) => (poll.PollId, Pickers: answers.Select(a => a.UserId).ToHashSet()))
            .ToList();

        var players = pickersByPoll.SelectMany(p => p.Pickers).ToHashSet();

        return new SeasonParticipation(
            players.Count,
            pickersByPoll.Select(p => new PollParticipation(p.PollId, p.Pickers.Count)).ToList());
    }
}

public record SeasonParticipation(int PlayerCount, IReadOnlyList<PollParticipation> Polls);

public record PollParticipation(string PollId, int PickedCount);
