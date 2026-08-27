using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Enums;
using PuckDrop.Application.Models;
using PuckDrop.Application.Repositories;

namespace PuckDrop.Application.Services;

public class ResultsService(IPollRepository pollRepository, IUserAnswerRepository answerRepository)
{
    /// <summary>
    /// Gets the full results for a scored poll: questions, options, and all users' answers with scores.
    /// </summary>
    public async Task<PollResults> GetPollResultsAsync(string pollId, CancellationToken cancellationToken = default)
    {
        var pollData = await pollRepository.GetWithQuestionsAsync(pollId, cancellationToken);
        if (pollData is null)
            throw new KeyNotFoundException($"Poll '{pollId}' not found.");

        if (pollData.Poll.Status != PollStatus.Scored)
            throw new InvalidOperationException("Results are only available for scored polls.");

        var allAnswers = await answerRepository.GetAllAnswersForPollAsync(pollId, cancellationToken);

        var userResults = allAnswers
            .GroupBy(a => a.UserId)
            .Select(g => new UserPollResult(
                UserId: g.Key,
                DisplayName: g.First().DisplayName,
                Answers: g.ToList(),
                Points: g.Count(a => a.IsCorrect == true)))
            .OrderByDescending(u => u.Points)
            .ToList();

        return new PollResults(pollData.Poll, pollData.Questions, pollData.Options, userResults);
    }
}

public record PollResults(
    GameDayPoll Poll,
    IReadOnlyList<Question> Questions,
    IReadOnlyList<Option> Options,
    IReadOnlyList<UserPollResult> UserResults);

public record UserPollResult(string UserId, string DisplayName, IReadOnlyList<UserAnswer> Answers, int Points);
