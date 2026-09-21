using PuckDrop.Application.Repositories;
using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Enums;

namespace PuckDrop.Application.Services;

/// <summary>
/// Voids and restores one player's picks for one game day.
/// </summary>
/// <remarks>
/// A void is recorded as its own fact, deliberately separate from the poll's scores: re-scoring
/// rewrites those, and must not be able to wipe out an admin's decision. Restoring is a single
/// delete, after which the player's total returns to exactly what it was.
/// </remarks>
public class PollVoidService(
    IPollRepository pollRepository,
    IUserAnswerRepository answerRepository,
    ILeaderboardRepository leaderboardRepository)
{
    /// <summary>
    /// Voids a player's picks for a poll, taking that game day's points and answered count off
    /// their season total.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown if the poll does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if voting is still open.</exception>
    /// <exception cref="ArgumentException">Thrown if the player made no picks, or the reason is invalid.</exception>
    public async Task VoidPicksAsync(
        string pollId, string userId, string reason, string voidedBy, CancellationToken cancellationToken = default)
    {
        var poll = await pollRepository.GetByIdAsync(pollId, cancellationToken)
            ?? throw new KeyNotFoundException($"Poll '{pollId}' not found.");

        // Voting still being open means there is nothing settled to void yet. Closed is allowed as
        // well as Scored, so an admin can act on game day without waiting for scoring.
        if (poll.Status is not (PollStatus.Closed or PollStatus.Scored))
            throw new InvalidOperationException(
                $"Cannot void picks on a poll with status '{poll.Status}'. Voting must be closed first.");

        var answers = await answerRepository.GetUserAnswersAsync(userId, pollId, cancellationToken);
        if (answers.Count == 0)
            throw new ArgumentException($"User '{userId}' made no picks for poll '{pollId}'.", nameof(userId));

        var pollVoid = PollVoid.Create(poll.SeasonId, pollId, userId, poll.Title, reason, voidedBy);
        await leaderboardRepository.SaveVoidAsync(pollVoid, cancellationToken);
    }

    /// <summary>
    /// Restores previously voided picks, returning the player's total to exactly what it was -
    /// or, if the poll was re-scored while voided, to whatever it now scores.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown if the poll does not exist, or was not voided for this player.</exception>
    public async Task RestorePicksAsync(string pollId, string userId, CancellationToken cancellationToken = default)
    {
        var poll = await pollRepository.GetByIdAsync(pollId, cancellationToken)
            ?? throw new KeyNotFoundException($"Poll '{pollId}' not found.");

        await leaderboardRepository.DeleteVoidAsync(poll.SeasonId, userId, pollId, cancellationToken);
    }
}
