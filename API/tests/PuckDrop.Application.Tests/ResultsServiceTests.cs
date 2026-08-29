using NSubstitute;
using PuckDrop.Application.Models;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Application.Tests;

public class ResultsServiceTests
{
    private const string PollId = "poll-1";

    private static GameDayPoll BuildPoll(bool scored)
    {
        var poll = new GameDayPoll
        {
            PollId = PollId,
            SeasonId = "2025-26",
            GameDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Title = "Belfast Giants vs Sheffield Steelers",
            Deadline = DateTime.UtcNow.AddDays(-1),
            CreatedBy = "admin-user"
        };
        if (scored)
        {
            poll.Publish();
            poll.MarkScored(DateTime.UtcNow);
        }
        return poll;
    }

    private static (ResultsService Service, IPollRepository PollRepository, IUserAnswerRepository AnswerRepository) CreateService(
        GameDayPoll poll, IReadOnlyList<UserAnswer> answers)
    {
        var pollRepository = Substitute.For<IPollRepository>();
        pollRepository.GetWithQuestionsAsync(PollId, Arg.Any<CancellationToken>())
            .Returns(new PollWithQuestions(poll, [], []));

        var answerRepository = Substitute.For<IUserAnswerRepository>();
        answerRepository.GetAllAnswersForPollAsync(PollId, Arg.Any<CancellationToken>()).Returns(answers);

        return (new ResultsService(pollRepository, answerRepository), pollRepository, answerRepository);
    }

    [Fact]
    public async Task GetPollResultsAsync_PollNotFound_ThrowsKeyNotFoundException()
    {
        var pollRepository = Substitute.For<IPollRepository>();
        pollRepository.GetWithQuestionsAsync(PollId, Arg.Any<CancellationToken>()).Returns((PollWithQuestions?)null);
        var service = new ResultsService(pollRepository, Substitute.For<IUserAnswerRepository>());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetPollResultsAsync(PollId));
    }

    [Fact]
    public async Task GetPollResultsAsync_PollNotScored_ThrowsInvalidOperationException()
    {
        var (service, _, _) = CreateService(BuildPoll(scored: false), []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPollResultsAsync(PollId));
    }

    [Fact]
    public async Task GetPollResultsAsync_GroupsAnswersByUser_AndOrdersByPointsDescending()
    {
        var answers = new List<UserAnswer>
        {
            Answer("user-1", "Nathan", "q1", isCorrect: true),
            Answer("user-1", "Nathan", "q2", isCorrect: false),
            Answer("user-2", "Friend", "q1", isCorrect: true),
            Answer("user-2", "Friend", "q2", isCorrect: true)
        };
        var (service, _, _) = CreateService(BuildPoll(scored: true), answers);

        var results = await service.GetPollResultsAsync(PollId);

        Assert.Equal(2, results.UserResults.Count);
        Assert.Equal("user-2", results.UserResults[0].UserId); // 2 points, ranks above user-1's 1
        Assert.Equal(2, results.UserResults[0].Points);
        Assert.Equal("user-1", results.UserResults[1].UserId);
        Assert.Equal(1, results.UserResults[1].Points);
    }

    [Fact]
    public async Task GetPollResultsAsync_UserWithZeroCorrectAnswers_StillAppearsInResults()
    {
        var answers = new List<UserAnswer> { Answer("user-1", "Nathan", "q1", isCorrect: false) };
        var (service, _, _) = CreateService(BuildPoll(scored: true), answers);

        var results = await service.GetPollResultsAsync(PollId);

        Assert.Single(results.UserResults);
        Assert.Equal(0, results.UserResults[0].Points);
    }

    private static UserAnswer Answer(string userId, string displayName, string questionId, bool isCorrect)
    {
        var answer = new UserAnswer
        {
            UserId = userId,
            DisplayName = displayName,
            PollId = PollId,
            QuestionId = questionId,
            SelectedOptionId = "some-option"
        };
        answer.Evaluate(isCorrect ? "some-option" : "different-option");
        return answer;
    }
}
