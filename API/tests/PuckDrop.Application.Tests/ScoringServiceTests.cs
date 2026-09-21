using NSubstitute;
using PuckDrop.Application.Models;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Application.Tests;

public class ScoringServiceTests
{
    private const string PollId = "poll-1";
    private const string SeasonId = "2025-26";
    private const string Question1Id = "question-1";
    private const string Question2Id = "question-2";
    private const string Q1CorrectOptionId = "q1-option-a";
    private const string Q1WrongOptionId = "q1-option-b";
    private const string Q2OptionAId = "q2-option-a";
    private const string Q2OptionBId = "q2-option-b";

    private static GameDayPoll BuildClosedPoll()
    {
        // Closed (not Open-with-passed-deadline) so MarkScored succeeds unconditionally,
        // independent of wall-clock time - keeps these tests deterministic.
        var poll = new GameDayPoll
        {
            PollId = PollId,
            SeasonId = SeasonId,
            GameDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Title = "Belfast Giants vs Sheffield Steelers",
            Deadline = DateTime.UtcNow.AddDays(-1),
            CreatedBy = "admin-user"
        };
        poll.Publish();
        poll.Close();
        return poll;
    }

    private static List<Question> BuildTwoQuestions() =>
    [
        new() { QuestionId = Question1Id, PollId = PollId, Text = "Who scores first?", SortOrder = 0 },
        new() { QuestionId = Question2Id, PollId = PollId, Text = "Final score margin?", SortOrder = 1 }
    ];

    private static List<UserAnswer> BuildAnswersForTwoUsers() =>
    [
        new() { UserId = "user-1", DisplayName = "Nathan", PollId = PollId, QuestionId = Question1Id, SelectedOptionId = Q1CorrectOptionId },
        new() { UserId = "user-1", DisplayName = "Nathan", PollId = PollId, QuestionId = Question2Id, SelectedOptionId = Q2OptionBId },
        new() { UserId = "user-2", DisplayName = "Friend", PollId = PollId, QuestionId = Question1Id, SelectedOptionId = Q1WrongOptionId },
        new() { UserId = "user-2", DisplayName = "Friend", PollId = PollId, QuestionId = Question2Id, SelectedOptionId = Q2OptionAId }
    ];

    private sealed class Fixture
    {
        public required ScoringService Service { get; init; }
        public required IPollRepository PollRepository { get; init; }
        public required IUserAnswerRepository AnswerRepository { get; init; }
        public required ILeaderboardRepository LeaderboardRepository { get; init; }
    }

    private static Fixture CreateFixture(
        PollWithQuestions? pollData,
        IReadOnlyList<UserAnswer>? allAnswers = null)
    {
        var pollRepository = Substitute.For<IPollRepository>();
        pollRepository.GetWithQuestionsAsync(PollId, Arg.Any<CancellationToken>()).Returns(pollData);

        var answerRepository = Substitute.For<IUserAnswerRepository>();
        answerRepository.GetAllAnswersForPollAsync(PollId, Arg.Any<CancellationToken>())
            .Returns(allAnswers ?? []);

        var leaderboardRepository = Substitute.For<ILeaderboardRepository>();

        return new Fixture
        {
            Service = new ScoringService(pollRepository, answerRepository, leaderboardRepository),
            PollRepository = pollRepository,
            AnswerRepository = answerRepository,
            LeaderboardRepository = leaderboardRepository
        };
    }

    /// <summary>
    /// The poll score facts the service recorded, keyed by user.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, PollScore>> RecordedScoresAsync(Fixture fixture)
    {
        var calls = await Task.FromResult(fixture.LeaderboardRepository.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILeaderboardRepository.SavePollScoresAsync))
            .ToList());

        var saved = Assert.Single(calls);
        var scores = (IReadOnlyList<PollScore>)saved.GetArguments()[0]!;
        return scores.ToDictionary(s => s.UserId);
    }

    [Fact]
    public async Task ScorePollAsync_PollNotFound_ThrowsKeyNotFoundException()
    {
        var fixture = CreateFixture(pollData: null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            fixture.Service.ScorePollAsync(PollId, [new QuestionScore(Question1Id, Q1CorrectOptionId)], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ScorePollAsync_PollNotInScorableState_PropagatesInvalidOperationException()
    {
        // Draft - GameDayPoll.MarkScored throws for this status; ScoringService doesn't catch it.
        var poll = new GameDayPoll
        {
            PollId = PollId,
            SeasonId = SeasonId,
            GameDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Title = "Belfast Giants vs Sheffield Steelers",
            Deadline = DateTime.UtcNow.AddDays(1),
            CreatedBy = "admin-user"
        };
        var fixture = CreateFixture(new PollWithQuestions(poll, [], []));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ScorePollAsync(PollId, [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ScorePollAsync_UnknownQuestionIdInCorrectAnswers_ThrowsArgumentException()
    {
        var pollData = new PollWithQuestions(BuildClosedPoll(), BuildTwoQuestions(), []);
        var fixture = CreateFixture(pollData);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.ScorePollAsync(PollId, [new QuestionScore("unknown-question", Q1CorrectOptionId)], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ScorePollAsync_QuestionOmittedFromCorrectAnswers_LeavesItsAnswersUngraded()
    {
        // Only Question1 is scored - Question2 is a real question on the poll but simply wasn't
        // included in correctAnswers (e.g. admin hasn't scored it yet). Its answers must stay
        // IsCorrect == null, not false - this is real, non-obvious ScoringService behavior.
        var questions = BuildTwoQuestions();
        var pollData = new PollWithQuestions(BuildClosedPoll(), questions, []);
        var answers = BuildAnswersForTwoUsers();
        var fixture = CreateFixture(pollData, answers);

        IReadOnlyList<UserAnswer>? scoredAnswers = null;
        _ = fixture.AnswerRepository
            .UpdateScoresAsync(Arg.Do<IReadOnlyList<UserAnswer>>(a => scoredAnswers = a), Arg.Any<CancellationToken>());

        await fixture.Service.ScorePollAsync(PollId, [new QuestionScore(Question1Id, Q1CorrectOptionId)], TestContext.Current.CancellationToken);

        Assert.NotNull(scoredAnswers);
        var question2Answers = scoredAnswers.Where(a => a.QuestionId == Question2Id);
        Assert.All(question2Answers, a => Assert.Null(a.IsCorrect));

        var question1Answers = scoredAnswers.Where(a => a.QuestionId == Question1Id).ToList();
        Assert.Contains(question1Answers, a => a.UserId == "user-1" && a.IsCorrect == true);
        Assert.Contains(question1Answers, a => a.UserId == "user-2" && a.IsCorrect == false);
    }

    [Fact]
    public async Task ScorePollAsync_RecordsWhatEachPlayerEarnedInThisPollAlone()
    {
        // Facts are per-poll, never a running total: the season is folded from them on read.
        var pollData = new PollWithQuestions(BuildClosedPoll(), BuildTwoQuestions(), []);
        var fixture = CreateFixture(pollData, BuildAnswersForTwoUsers());

        await fixture.Service.ScorePollAsync(PollId, [
            new QuestionScore(Question1Id, Q1CorrectOptionId),
            new QuestionScore(Question2Id, Q2OptionAId)
        ], TestContext.Current.CancellationToken);

        var scores = await RecordedScoresAsync(fixture);

        // user-1 got Question1 right and Question2 wrong; user-2 the other way round.
        Assert.Equal(1, scores["user-1"].Points);
        Assert.Equal(2, scores["user-1"].Answered);
        Assert.Equal("Nathan", scores["user-1"].DisplayName);
        Assert.Equal(1, scores["user-2"].Points);
        Assert.Equal(2, scores["user-2"].Answered);
        Assert.All(scores.Values, s => Assert.Equal(PollId, s.PollId));
        Assert.All(scores.Values, s => Assert.Equal(SeasonId, s.SeasonId));
    }

    [Fact]
    public async Task ScorePollAsync_RescoringAPoll_OverwritesItsFactsRatherThanDoubleCounting()
    {
        // The whole point of folding totals from facts: scoring the same poll twice - to fix a
        // wrong correct option - leaves one fact per player, carrying the corrected points.
        var poll = BuildClosedPoll();
        poll.MarkScored(DateTime.UtcNow);
        var pollData = new PollWithQuestions(poll, BuildTwoQuestions(), []);
        var fixture = CreateFixture(pollData, BuildAnswersForTwoUsers());

        // Question 2's correct option is now Q2OptionB, so user-1 gets both right.
        await fixture.Service.ScorePollAsync(PollId, [
            new QuestionScore(Question1Id, Q1CorrectOptionId),
            new QuestionScore(Question2Id, Q2OptionBId)
        ], TestContext.Current.CancellationToken);

        var scores = await RecordedScoresAsync(fixture);

        Assert.Equal(2, scores["user-1"].Points);
        Assert.Equal(0, scores["user-2"].Points);
    }

    [Fact]
    public async Task ScorePollAsync_UngradedAnswers_AreNotCountedAsAnswered()
    {
        // A question the admin never set a correct option for is not one the player got wrong.
        // Counting it would quietly tank everyone's accuracy on a partially scored poll.
        var pollData = new PollWithQuestions(BuildClosedPoll(), BuildTwoQuestions(), []);
        var fixture = CreateFixture(pollData, BuildAnswersForTwoUsers());

        await fixture.Service.ScorePollAsync(
            PollId, [new QuestionScore(Question1Id, Q1CorrectOptionId)], TestContext.Current.CancellationToken);

        var scores = await RecordedScoresAsync(fixture);

        Assert.Equal(1, scores["user-1"].Answered);
        Assert.Equal(1, scores["user-1"].Points);
        Assert.Equal(1, scores["user-2"].Answered);
        Assert.Equal(0, scores["user-2"].Points);
    }

    [Fact]
    public async Task ScorePollAsync_AnswerPersistenceFails_DoesNotSaveThePollAsScored()
    {
        // The status change is the last write precisely so a pass that dies partway leaves the poll
        // Closed and re-runnable, rather than Scored with only some of its answers graded.
        var pollData = new PollWithQuestions(BuildClosedPoll(), BuildTwoQuestions(), []);
        var fixture = CreateFixture(pollData, BuildAnswersForTwoUsers());
        fixture.AnswerRepository
            .UpdateScoresAsync(Arg.Any<IReadOnlyList<UserAnswer>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Unprocessed items remained.")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ScorePollAsync(PollId, [
                new QuestionScore(Question1Id, Q1CorrectOptionId),
                new QuestionScore(Question2Id, Q2OptionAId)
            ], TestContext.Current.CancellationToken));

        await fixture.PollRepository.DidNotReceive().SavePollAsync(Arg.Any<GameDayPoll>(), Arg.Any<CancellationToken>());
        Assert.Equal(Domain.Enums.PollStatus.Closed, pollData.Poll.Status);
    }

    [Fact]
    public async Task ScorePollAsync_HappyPath_SavesPollAsScored()
    {
        var pollData = new PollWithQuestions(BuildClosedPoll(), BuildTwoQuestions(), []);
        var fixture = CreateFixture(pollData, BuildAnswersForTwoUsers());

        await fixture.Service.ScorePollAsync(PollId, [
            new QuestionScore(Question1Id, Q1CorrectOptionId),
            new QuestionScore(Question2Id, Q2OptionAId)
        ], TestContext.Current.CancellationToken);

        await fixture.PollRepository.Received(1).SavePollAsync(
            Arg.Is<GameDayPoll>(p => p.Status == Domain.Enums.PollStatus.Scored), Arg.Any<CancellationToken>());
    }
}
