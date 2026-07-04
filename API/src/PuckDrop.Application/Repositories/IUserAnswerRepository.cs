using PuckDrop.Domain.Entities;

namespace PuckDrop.Application.Repositories;

public interface IUserAnswerRepository
{
    /// <summary>
    /// Gets a user's answers for a specific poll.
    /// </summary>
    Task<IReadOnlyList<UserAnswer>> GetUserAnswersAsync(string userId, string pollId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all users' answers for a poll (used for scoring and results).
    /// </summary>
    Task<IReadOnlyList<UserAnswer>> GetAllAnswersForPollAsync(string pollId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a batch of answers for a user (creates or overwrites).
    /// </summary>
    Task SaveAnswersAsync(IReadOnlyList<UserAnswer> answers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the IsCorrect field on a batch of answers after scoring.
    /// </summary>
    Task UpdateScoresAsync(IReadOnlyList<UserAnswer> answers, CancellationToken cancellationToken = default);
}
