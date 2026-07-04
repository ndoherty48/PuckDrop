using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Repositories;

namespace PuckDrop.Application.Services;

public class AnswerService
{
    private readonly IUserAnswerRepository _answerRepository;
    private readonly IPollRepository _pollRepository;

    public AnswerService(IUserAnswerRepository answerRepository, IPollRepository pollRepository)
    {
        _answerRepository = answerRepository;
        _pollRepository = pollRepository;
    }

    /// <summary>
    /// Gets a user's answers for a specific poll.
    /// </summary>
    public async Task<IReadOnlyList<UserAnswer>> GetUserAnswersAsync(
        string userId,
        string pollId,
        CancellationToken cancellationToken = default)
    {
        return await _answerRepository.GetUserAnswersAsync(userId, pollId, cancellationToken);
    }

    /// <summary>
    /// Submits or updates answers for a user. Validates the poll is accepting answers
    /// and that all question/option references are valid.
    /// </summary>
    public async Task<IReadOnlyList<UserAnswer>> SubmitAnswersAsync(
        string userId,
        string pollId,
        IReadOnlyList<(string QuestionId, string SelectedOptionId)> answers,
        CancellationToken cancellationToken = default)
    {
        // Load poll with questions and options for validation
        var pollData = await _pollRepository.GetWithQuestionsAsync(pollId, cancellationToken);
        if (pollData is null)
            throw new KeyNotFoundException($"Poll '{pollId}' not found.");

        var (poll, questions, options) = pollData.Value;

        // Check poll is accepting answers
        var utcNow = DateTime.UtcNow;
        if (!poll.IsAcceptingAnswers(utcNow))
        {
            if (utcNow >= poll.Deadline)
                throw new InvalidOperationException("Voting has closed for this poll.");
            else
                throw new InvalidOperationException($"Poll is not open for voting (status: {poll.Status}).");
        }

        // Validate all question IDs belong to this poll
        var questionIds = questions.Select(q => q.QuestionId).ToHashSet();
        var optionsByQuestion = options.GroupBy(o => o.QuestionId)
            .ToDictionary(g => g.Key, g => g.Select(o => o.OptionId).ToHashSet());

        var userAnswers = new List<UserAnswer>();

        foreach (var (questionId, selectedOptionId) in answers)
        {
            if (!questionIds.Contains(questionId))
                throw new ArgumentException($"Question '{questionId}' does not belong to poll '{pollId}'.");

            if (!optionsByQuestion.TryGetValue(questionId, out var validOptions) || !validOptions.Contains(selectedOptionId))
                throw new ArgumentException($"Option '{selectedOptionId}' does not belong to question '{questionId}'.");

            userAnswers.Add(new UserAnswer
            {
                UserId = userId,
                PollId = pollId,
                QuestionId = questionId,
                SelectedOptionId = selectedOptionId,
                SubmittedAt = utcNow
            });
        }

        await _answerRepository.SaveAnswersAsync(userAnswers, cancellationToken);
        return userAnswers;
    }
}
