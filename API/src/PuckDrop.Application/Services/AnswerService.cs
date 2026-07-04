using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Models;
using PuckDrop.Application.Repositories;

namespace PuckDrop.Application.Services;

public class AnswerService(IUserAnswerRepository answerRepository, IPollRepository pollRepository)
{
    /// <summary>
    /// Gets a user's answers for a specific poll.
    /// </summary>
    public async Task<IReadOnlyList<UserAnswer>> GetUserAnswersAsync(
        string userId,
        string pollId,
        CancellationToken cancellationToken = default)
    {
        return await answerRepository.GetUserAnswersAsync(userId, pollId, cancellationToken);
    }

    /// <summary>
    /// Submits or updates answers for a user. Validates the poll is accepting answers
    /// and that all question/option references are valid.
    /// </summary>
    public async Task<IReadOnlyList<UserAnswer>> SubmitAnswersAsync(
        string userId,
        string pollId,
        IReadOnlyList<AnswerSubmission> answers,
        CancellationToken cancellationToken = default)
    {
        // Load poll with questions and options for validation
        var pollData = await pollRepository.GetWithQuestionsAsync(pollId, cancellationToken);
        if (pollData is null)
            throw new KeyNotFoundException($"Poll '{pollId}' not found.");

        var poll = pollData.Poll;
        var questions = pollData.Questions;
        var options = pollData.Options;

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

        foreach (var answer in answers)
        {
            if (!questionIds.Contains(answer.QuestionId))
                throw new ArgumentException($"Question '{answer.QuestionId}' does not belong to poll '{pollId}'.");

            if (!optionsByQuestion.TryGetValue(answer.QuestionId, out var validOptions) || !validOptions.Contains(answer.SelectedOptionId))
                throw new ArgumentException($"Option '{answer.SelectedOptionId}' does not belong to question '{answer.QuestionId}'.");

            userAnswers.Add(new UserAnswer
            {
                UserId = userId,
                PollId = pollId,
                QuestionId = answer.QuestionId,
                SelectedOptionId = answer.SelectedOptionId,
                SubmittedAt = utcNow
            });
        }

        await answerRepository.SaveAnswersAsync(userAnswers, cancellationToken);
        return userAnswers;
    }
}
