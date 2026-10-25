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

    // ─── Cancellation ───────────────────────────────────────────────────────

    private static readonly DateTime CancelledAt = new(2026, 1, 14, 9, 30, 0, DateTimeKind.Utc);

    private static GameDayPoll BuildCancelledPoll()
    {
        var poll = BuildPoll();
        poll.Publish();
        poll.Cancel("Postponed: storm.", "admin-2", "Ciara D.", CancelledAt);
        return poll;
    }

    [Fact]
    public void ToResponse_PollNotCancelled_HasNoCancellation()
    {
        Assert.Null(BuildPoll().ToResponse().Cancellation);
    }

    [Fact]
    public void ToResponse_Cancelled_ForAPlayer_GivesTheReasonAndWhatItWasCancelledFrom_ButNotWhoOrWhen()
    {
        var response = BuildCancelledPoll().ToResponse();

        Assert.Equal("Cancelled", response.Status);
        var cancellation = Assert.IsType<Contracts.PollCancellationResponse>(response.Cancellation);
        Assert.Equal("Postponed: storm.", cancellation.Reason);
        Assert.Equal("Open", cancellation.CancelledFromStatus);
        Assert.Null(cancellation.CancelledByName);
        Assert.Null(cancellation.CancelledAt);
    }

    [Fact]
    public void ToResponse_Cancelled_ForAnAdmin_AlsoGivesWhoCancelledItAndWhen()
    {
        var response = BuildCancelledPoll().ToResponse(includeAdminDetail: true);

        var cancellation = Assert.IsType<Contracts.PollCancellationResponse>(response.Cancellation);
        Assert.Equal("Ciara D.", cancellation.CancelledByName);
        Assert.Equal(CancelledAt, DateTime.Parse(cancellation.CancelledAt!).ToUniversalTime());
    }

    [Fact]
    public void ToDetailResponse_Cancelled_CarriesTheSameCancellation()
    {
        var response = BuildCancelledPoll().ToDetailResponse([], [], includeAdminDetail: true);

        Assert.Equal("Postponed: storm.", response.Cancellation?.Reason);
        Assert.Equal("Ciara D.", response.Cancellation?.CancelledByName);
    }
}
