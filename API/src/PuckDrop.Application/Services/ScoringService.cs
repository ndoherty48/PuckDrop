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
    /// Scores a poll: marks correct answers on questions, evaluates all user answers, records
    /// what each player earned, and transitions the poll to Scored.
    /// </summary>
    /// <remarks>
    /// Safe to re-run. Every write uses a deterministic key, so re-scoring a poll - to fix a wrong
    /// correct option, to finish a partially scored one, or simply to retry a pass that failed
    /// halfway - overwrites rather than double-counting.
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

        // Validate up front, but don't transition yet - the status change is the last write, so a
        // pass that fails partway leaves the poll Closed and can simply be run again.
        var utcNow = DateTime.UtcNow;
        poll.EnsureCanBeScored(utcNow);

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

        // 4. Record what each player earned. These facts are the leaderboard's only source -
        // season totals are folded from them on read, never accumulated - so re-scoring just
        // overwrites this poll's facts and every total follows automatically.
        var pollScores = allAnswers
            .GroupBy(a => a.UserId)
            .Select(g => new PollScore
            {
                SeasonId = poll.SeasonId,
                PollId = pollId,
                UserId = g.Key,
                DisplayName = g.First().DisplayName,
                Points = g.Count(a => a.IsCorrect == true),
                // Graded answers only. A question the admin never set a correct option for is not
                // one the player got wrong, and counting it would quietly tank their accuracy.
                Answered = g.Count(a => a.IsCorrect is not null),
                ScoredAt = utcNow
            })
            .ToList();

        await leaderboardRepository.SavePollScoresAsync(pollScores, cancellationToken);

        // 5. Everything has landed - now transition the poll to Scored
        poll.MarkScored(utcNow);
        await pollRepository.SavePollAsync(poll, cancellationToken: cancellationToken);
    }
}
