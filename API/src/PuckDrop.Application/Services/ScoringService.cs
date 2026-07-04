using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Repositories;

namespace PuckDrop.Application.Services;

public class ScoringService(
    IPollRepository pollRepository,
    IUserAnswerRepository answerRepository,
    ILeaderboardRepository leaderboardRepository)
{
    /// <summary>
    /// Scores a poll: marks correct answers on questions, evaluates all user answers,
    /// updates the leaderboard, and transitions the poll to Scored.
    /// </summary>
    /// <param name="pollId">The poll to score.</param>
    /// <param name="correctAnswers">Map of questionId → correctOptionId.</param>
    /// <param name="displayNameResolver">Function to resolve userId → displayName for new leaderboard entries.</param>
    public async Task ScorePollAsync(
        string pollId,
        IReadOnlyList<(string QuestionId, string CorrectOptionId)> correctAnswers,
        Func<string, string> displayNameResolver,
        CancellationToken cancellationToken = default)
    {
        // 1. Load the poll with questions
        var pollData = await pollRepository.GetWithQuestionsAsync(pollId, cancellationToken);
        if (pollData is null)
            throw new KeyNotFoundException($"Poll '{pollId}' not found.");

        var (poll, questions, options) = pollData.Value;

        // Validate poll can be scored
        var utcNow = DateTime.UtcNow;
        poll.MarkScored(utcNow);

        // 2. Set correct options on questions
        var questionMap = questions.ToDictionary(q => q.QuestionId);
        foreach (var (questionId, correctOptionId) in correctAnswers)
        {
            if (!questionMap.TryGetValue(questionId, out var question))
                throw new ArgumentException($"Question '{questionId}' not found in poll '{pollId}'.");

            question.SetCorrectOption(correctOptionId);
        }

        // Persist updated questions (with correctOptionId set)
        await pollRepository.UpdateQuestionsAsync(questions.ToList(), cancellationToken);

        // 3. Evaluate all user answers
        var allAnswers = await answerRepository.GetAllAnswersForPollAsync(pollId, cancellationToken);

        foreach (var answer in allAnswers)
        {
            if (questionMap.TryGetValue(answer.QuestionId, out var question) && question.CorrectOptionId is not null)
            {
                answer.Evaluate(question.CorrectOptionId);
            }
        }

        // Persist scored answers
        await answerRepository.UpdateScoresAsync(allAnswers.ToList(), cancellationToken);

        // 4. Aggregate points per user and update leaderboard
        var pointsByUser = allAnswers
            .GroupBy(a => a.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                CorrectCount = g.Count(a => a.IsCorrect == true),
                TotalAnswered = g.Count()
            })
            .ToList();

        foreach (var userScore in pointsByUser)
        {
            var existingEntry = await leaderboardRepository.GetEntryAsync(poll.SeasonId, userScore.UserId, cancellationToken);

            if (existingEntry is not null)
            {
                var previousPoints = existingEntry.TotalPoints;
                existingEntry.AddPollResults(userScore.CorrectCount, userScore.TotalAnswered);
                await leaderboardRepository.SaveEntryAsync(existingEntry, previousPoints, cancellationToken);
            }
            else
            {
                var entry = new LeaderboardEntry
                {
                    UserId = userScore.UserId,
                    SeasonId = poll.SeasonId,
                    DisplayName = displayNameResolver(userScore.UserId)
                };
                entry.AddPollResults(userScore.CorrectCount, userScore.TotalAnswered);
                await leaderboardRepository.SaveEntryAsync(entry, cancellationToken: cancellationToken);
            }
        }

        // 5. Save poll with Scored status
        await pollRepository.SavePollAsync(poll, cancellationToken);
    }
}
