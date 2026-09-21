using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Enums;
using PuckDrop.Application.Models;
using PuckDrop.Application.Repositories;

namespace PuckDrop.Application.Services;

public class ResultsService(
    IPollRepository pollRepository,
    IUserAnswerRepository answerRepository,
    ILeaderboardRepository leaderboardRepository)
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

        // Voided players are marked here too, so this page and the leaderboard tell the same story.
        var facts = await leaderboardRepository.GetSeasonFactsAsync(pollData.Poll.SeasonId, cancellationToken);
        var voidReasonByUser = facts.Voids
            .Where(v => v.PollId == pollId)
            .ToDictionary(v => v.UserId, v => v.Reason);

        var userResults = allAnswers
            .GroupBy(a => a.UserId)
            .Select(g => new UserPollResult(
                UserId: g.Key,
                DisplayName: g.First().DisplayName,
                Answers: g.ToList(),
                Points: g.Count(a => a.IsCorrect == true),
                VoidReason: voidReasonByUser.GetValueOrDefault(g.Key)))
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

public record UserPollResult(
    string UserId,
    string DisplayName,
    IReadOnlyList<UserAnswer> Answers,
    int Points,
    string? VoidReason = null)
{
    /// <summary>
    /// Whether an admin voided this player's picks for this game day. Their points still show on
    /// this page - they just don't count towards the season.
    /// </summary>
    public bool IsVoided => VoidReason is not null;
}
