using PuckDrop.Domain.Entities;
using PuckDrop.Application.Models;
using PuckDrop.Application.Repositories;

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
    public async Task ScorePollAsync(
        string pollId,
        IReadOnlyList<QuestionScore> correctAnswers,
        CancellationToken cancellationToken = default)
    {
        // 1. Load the poll with questions
        var pollData = await pollRepository.GetWithQuestionsAsync(pollId, cancellationToken);
        if (pollData is null)
            throw new KeyNotFoundException($"Poll '{pollId}' not found.");

        var poll = pollData.Poll;
        var questions = pollData.Questions;

        // Validate poll can be scored
        var utcNow = DateTime.UtcNow;
        poll.MarkScored(utcNow);

        // 2. Set correct options on questions
        var questionMap = questions.ToDictionary(q => q.QuestionId);
        foreach (var score in correctAnswers)
        {
            if (!questionMap.TryGetValue(score.QuestionId, out var question))
                throw new ArgumentException($"Question '{score.QuestionId}' not found in poll '{pollId}'.");

            question.SetCorrectOption(score.CorrectOptionId);
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
                DisplayName = g.First().DisplayName,
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
                // Refresh the denormalised name on every scoring pass so a name captured
                // incorrectly (or since changed) self-heals instead of staying stale forever.
                existingEntry.DisplayName = userScore.DisplayName;
                existingEntry.AddPollResults(userScore.CorrectCount, userScore.TotalAnswered);
                await leaderboardRepository.SaveEntryAsync(existingEntry, previousPoints, cancellationToken);
            }
            else
            {
                var entry = new LeaderboardEntry
                {
                    UserId = userScore.UserId,
                    SeasonId = poll.SeasonId,
                    DisplayName = userScore.DisplayName
                };
                entry.AddPollResults(userScore.CorrectCount, userScore.TotalAnswered);
                await leaderboardRepository.SaveEntryAsync(entry, cancellationToken: cancellationToken);
            }
        }

        // 5. Save poll with Scored status
        await pollRepository.SavePollAsync(poll, cancellationToken);
    }
}
