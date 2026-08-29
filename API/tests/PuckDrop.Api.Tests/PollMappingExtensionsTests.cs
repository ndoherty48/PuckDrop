using PuckDrop.Api.Mappings;
using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Api.Tests;

public class PollMappingExtensionsTests
{
    private static GameDayPoll BuildPoll() => new()
    {
        PollId = "poll-1",
        SeasonId = "2025-26",
        GameDate = new DateOnly(2026, 1, 15),
        Title = "Belfast Giants vs Sheffield Steelers",
        Deadline = new DateTime(2026, 1, 15, 19, 0, 0, DateTimeKind.Utc),
        CreatedBy = "admin-user",
        CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void ToResponse_FormatsGameDateAsYyyyMmDd()
    {
        var response = BuildPoll().ToResponse();

        Assert.Equal("2026-01-15", response.GameDate);
    }

    [Fact]
    public void ToResponse_FormatsDeadlineAndCreatedAtAsRoundTripO()
    {
        var poll = BuildPoll();

        var response = poll.ToResponse();

        // "O" round-trips exactly back to the same instant - the UI's model binding relies on
        // this exact format (see PuckDropApiClient), not just "a" date/time string.
        Assert.Equal(poll.Deadline, DateTime.Parse(response.Deadline).ToUniversalTime());
        Assert.Equal(poll.CreatedAt, DateTime.Parse(response.CreatedAt).ToUniversalTime());
    }

    [Fact]
    public void ToDetailResponse_GroupsOptionsUnderTheirOwnQuestion_NotAllQuestions()
    {
        var poll = BuildPoll();
        var questions = new List<Question>
        {
            new() { QuestionId = "q1", PollId = poll.PollId, Text = "Q1", SortOrder = 0 },
            new() { QuestionId = "q2", PollId = poll.PollId, Text = "Q2", SortOrder = 1 }
        };
        var options = new List<Option>
        {
            new() { OptionId = "q1-a", QuestionId = "q1", Text = "A", SortOrder = 0 },
            new() { OptionId = "q1-b", QuestionId = "q1", Text = "B", SortOrder = 1 },
            new() { OptionId = "q2-a", QuestionId = "q2", Text = "A", SortOrder = 0 }
        };

        var response = poll.ToDetailResponse(questions, options);

        var q1 = response.Questions.Single(q => q.QuestionId == "q1");
        var q2 = response.Questions.Single(q => q.QuestionId == "q2");
        Assert.Equal(2, q1.Options.Count);
        Assert.Single(q2.Options);
    }

    [Fact]
    public void ToDetailResponse_QuestionWithNoOptions_GetsEmptyOptionsListNotNull()
    {
        var poll = BuildPoll();
        var questions = new List<Question> { new() { QuestionId = "q1", PollId = poll.PollId, Text = "Q1", SortOrder = 0 } };

        var response = poll.ToDetailResponse(questions, options: []);

        Assert.Empty(response.Questions.Single().Options);
    }
}
