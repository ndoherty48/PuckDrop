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
        IReadOnlyList<UserAnswer>? allAnswers = null,
        Func<string, LeaderboardEntry?>? existingEntryFor = null)
    {
        var pollRepository = Substitute.For<IPollRepository>();
        pollRepository.GetWithQuestionsAsync(PollId, Arg.Any<CancellationToken>()).Returns(pollData);

        var answerRepository = Substitute.For<IUserAnswerRepository>();
        answerRepository.GetAllAnswersForPollAsync(PollId, Arg.Any<CancellationToken>())
            .Returns(allAnswers ?? []);

        var leaderboardRepository = Substitute.For<ILeaderboardRepository>();
        if (existingEntryFor is not null)
        {
            leaderboardRepository
                .GetEntryAsync(SeasonId, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => existingEntryFor(call.ArgAt<string>(1)));
        }

        return new Fixture
        {
            Service = new ScoringService(pollRepository, answerRepository, leaderboardRepository),
            PollRepository = pollRepository,
            AnswerRepository = answerRepository,
            LeaderboardRepository = leaderboardRepository
        };
    }

    [Fact]
    public async Task ScorePollAsync_PollNotFound_ThrowsKeyNotFoundException()
    {
        var fixture = CreateFixture(pollData: null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            fixture.Service.ScorePollAsync(PollId, [new QuestionScore(Question1Id, Q1CorrectOptionId)]));
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
            fixture.Service.ScorePollAsync(PollId, []));
    }

    [Fact]
    public async Task ScorePollAsync_UnknownQuestionIdInCorrectAnswers_ThrowsArgumentException()
    {
        var pollData = new PollWithQuestions(BuildClosedPoll(), BuildTwoQuestions(), []);
        var fixture = CreateFixture(pollData);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.ScorePollAsync(PollId, [new QuestionScore("unknown-question", Q1CorrectOptionId)]));
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

        await fixture.Service.ScorePollAsync(PollId, [new QuestionScore(Question1Id, Q1CorrectOptionId)]);

        Assert.NotNull(scoredAnswers);
        var question2Answers = scoredAnswers.Where(a => a.QuestionId == Question2Id);
        Assert.All(question2Answers, a => Assert.Null(a.IsCorrect));

        var question1Answers = scoredAnswers.Where(a => a.QuestionId == Question1Id).ToList();
        Assert.Contains(question1Answers, a => a.UserId == "user-1" && a.IsCorrect == true);
        Assert.Contains(question1Answers, a => a.UserId == "user-2" && a.IsCorrect == false);
    }

    [Fact]
    public async Task ScorePollAsync_ExistingLeaderboardEntry_SelfHealsDisplayNameAndAccumulatesPoints()
    {
        var questions = BuildTwoQuestions();
        var pollData = new PollWithQuestions(BuildClosedPoll(), questions, []);
        var answers = BuildAnswersForTwoUsers();
        var existing = new LeaderboardEntry
        {
            UserId = "user-1",
            SeasonId = SeasonId,
            DisplayName = "Stale Old Name",
            TotalPoints = 5,
            TotalAnswered = 10
        };
        var fixture = CreateFixture(pollData, answers, userId => userId == "user-1" ? existing : null);

        await fixture.Service.ScorePollAsync(PollId, [
            new QuestionScore(Question1Id, Q1CorrectOptionId),
            new QuestionScore(Question2Id, Q2OptionAId)
        ]);

        // user-1 got Question1 right (Q1CorrectOptionId) and Question2 wrong (picked Q2OptionB,
        // correct is Q2OptionA) -> 1 correct out of 2 answered this poll.
        await fixture.LeaderboardRepository.Received(1).SaveEntryAsync(
            Arg.Is<LeaderboardEntry>(e =>
                e.UserId == "user-1" &&
                e.DisplayName == "Nathan" && // self-healed from the current pass's UserAnswer.DisplayName
                e.TotalPoints == 6 &&        // 5 existing + 1 from this poll
                e.TotalAnswered == 12),      // 10 existing + 2 from this poll
            previousPoints: 5,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScorePollAsync_NoExistingLeaderboardEntry_CreatesNewOneWithNullPreviousPoints()
    {
        var questions = BuildTwoQuestions();
        var pollData = new PollWithQuestions(BuildClosedPoll(), questions, []);
        var answers = BuildAnswersForTwoUsers();
        var fixture = CreateFixture(pollData, answers, existingEntryFor: _ => null);

        await fixture.Service.ScorePollAsync(PollId, [
            new QuestionScore(Question1Id, Q1CorrectOptionId),
            new QuestionScore(Question2Id, Q2OptionAId)
        ]);

        await fixture.LeaderboardRepository.Received(1).SaveEntryAsync(
            Arg.Is<LeaderboardEntry>(e => e.UserId == "user-1" && e.TotalPoints == 1 && e.TotalAnswered == 2),
            previousPoints: null,
            Arg.Any<CancellationToken>());
        await fixture.LeaderboardRepository.Received(1).SaveEntryAsync(
            Arg.Is<LeaderboardEntry>(e => e.UserId == "user-2" && e.TotalPoints == 1 && e.TotalAnswered == 2),
            previousPoints: null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScorePollAsync_HappyPath_SavesPollAsScored()
    {
        var pollData = new PollWithQuestions(BuildClosedPoll(), BuildTwoQuestions(), []);
        var fixture = CreateFixture(pollData, BuildAnswersForTwoUsers(), existingEntryFor: _ => null);

        await fixture.Service.ScorePollAsync(PollId, [
            new QuestionScore(Question1Id, Q1CorrectOptionId),
            new QuestionScore(Question2Id, Q2OptionAId)
        ]);

        await fixture.PollRepository.Received(1).SavePollAsync(
            Arg.Is<GameDayPoll>(p => p.Status == Domain.Enums.PollStatus.Scored), Arg.Any<CancellationToken>());
    }
}
