using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Models;

namespace PuckDrop.Application.Repositories;

public interface IPollRepository
{
    Task<GameDayPoll?> GetByIdAsync(string pollId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a poll with all its questions and options loaded.
    /// </summary>
    Task<PollWithQuestions?> GetWithQuestionsAsync(string pollId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameDayPoll>> ListBySeasonAsync(string seasonId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameDayPoll>> GetActiveAsync(string seasonId, CancellationToken cancellationToken = default);
    Task SavePollAsync(GameDayPoll poll, CancellationToken cancellationToken = default);
    Task SaveQuestionAsync(Question question, IReadOnlyList<Option> options, CancellationToken cancellationToken = default);
    Task DeleteQuestionAsync(string pollId, string questionId, CancellationToken cancellationToken = default);
    Task UpdateQuestionsAsync(IReadOnlyList<Question> questions, CancellationToken cancellationToken = default);
}
