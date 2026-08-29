using NSubstitute;
using PuckDrop.Application.Models;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Application.Tests;

public class AnswerServiceTests
{
    private const string PollId = "poll-1";
    private const string QuestionId = "question-1";
    private const string OptionId = "option-1";
    private const string OtherOptionId = "option-2";

    private static PollWithQuestions BuildOpenPoll(DateTime deadline)
    {
        var poll = new GameDayPoll
        {
            PollId = PollId,
            SeasonId = "2025-26",
            GameDate = DateOnly.FromDateTime(deadline),
            Title = "Belfast Giants vs Sheffield Steelers",
            Deadline = deadline,
            CreatedBy = "admin-user"
        };
        poll.Publish();

        var question = new Question { QuestionId = QuestionId, PollId = PollId, Text = "Who scores first?", SortOrder = 0 };
        var options = new List<Option>
        {
            new() { OptionId = OptionId, QuestionId = QuestionId, Text = "Home", SortOrder = 0 },
            new() { OptionId = OtherOptionId, QuestionId = QuestionId, Text = "Away", SortOrder = 1 }
        };

        return new PollWithQuestions(poll, [question], options);
    }

    private static (AnswerService Service, IUserAnswerRepository AnswerRepository, IPollRepository PollRepository) CreateService(
        PollWithQuestions? pollData)
    {
        var answerRepository = Substitute.For<IUserAnswerRepository>();
        var pollRepository = Substitute.For<IPollRepository>();
        pollRepository.GetWithQuestionsAsync(PollId, Arg.Any<CancellationToken>()).Returns(pollData);

        return (new AnswerService(answerRepository, pollRepository), answerRepository, pollRepository);
    }

    [Fact]
    public async Task SubmitAnswersAsync_PollNotFound_ThrowsKeyNotFoundException()
    {
        var (service, _, _) = CreateService(pollData: null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.SubmitAnswersAsync("user-1", "Nathan", PollId, [new AnswerSubmission(QuestionId, OptionId)]));
    }

    [Fact]
    public async Task SubmitAnswersAsync_DeadlinePassed_ThrowsWithVotingClosedMessage()
    {
        var pollData = BuildOpenPoll(DateTime.UtcNow.AddMinutes(-1));
        var (service, _, _) = CreateService(pollData);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAnswersAsync("user-1", "Nathan", PollId, [new AnswerSubmission(QuestionId, OptionId)]));
        Assert.Contains("closed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitAnswersAsync_PollNotOpen_ThrowsWithStatusMessage()
    {
        // Draft, never published - distinct message/code path from the deadline-passed case above,
        // even though both produce InvalidOperationException from the same guard clause.
        var poll = new GameDayPoll
        {
            PollId = PollId,
            SeasonId = "2025-26",
            GameDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Title = "Belfast Giants vs Sheffield Steelers",
            Deadline = DateTime.UtcNow.AddDays(1),
            CreatedBy = "admin-user"
        };
        var pollData = new PollWithQuestions(poll, [], []);
        var (service, _, _) = CreateService(pollData);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAnswersAsync("user-1", "Nathan", PollId, [new AnswerSubmission(QuestionId, OptionId)]));
        Assert.Contains("not open", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitAnswersAsync_UnknownQuestionId_ThrowsArgumentException()
    {
        var pollData = BuildOpenPoll(DateTime.UtcNow.AddDays(1));
        var (service, _, _) = CreateService(pollData);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAnswersAsync("user-1", "Nathan", PollId, [new AnswerSubmission("unknown-question", OptionId)]));
    }

    [Fact]
    public async Task SubmitAnswersAsync_OptionDoesNotBelongToQuestion_ThrowsArgumentException()
    {
        var pollData = BuildOpenPoll(DateTime.UtcNow.AddDays(1));
        var (service, _, _) = CreateService(pollData);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAnswersAsync("user-1", "Nathan", PollId, [new AnswerSubmission(QuestionId, "option-from-a-different-question")]));
    }

    [Fact]
    public async Task SubmitAnswersAsync_ValidAnswers_SavesAndReturnsThem()
    {
        var pollData = BuildOpenPoll(DateTime.UtcNow.AddDays(1));
        var (service, answerRepository, _) = CreateService(pollData);

        var result = await service.SubmitAnswersAsync(
            "user-1", "Nathan", PollId, [new AnswerSubmission(QuestionId, OptionId)]);

        Assert.Single(result);
        Assert.Equal("user-1", result[0].UserId);
        Assert.Equal("Nathan", result[0].DisplayName);
        Assert.Equal(OptionId, result[0].SelectedOptionId);
        await answerRepository.Received(1).SaveAnswersAsync(
            Arg.Is<IReadOnlyList<UserAnswer>>(a => a.Count == 1), Arg.Any<CancellationToken>());
    }
}
