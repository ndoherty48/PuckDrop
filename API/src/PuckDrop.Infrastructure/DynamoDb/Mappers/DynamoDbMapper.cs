using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Enums;
using PuckDrop.Infrastructure.DynamoDb.Items;

namespace PuckDrop.Infrastructure.DynamoDb.Mappers;

/// <summary>
/// Maps between domain entities and DynamoDB item models.
/// </summary>
public static class DynamoDbMapper
{
    // ─── Season ───────────────────────────────────────────────────────────────

    public static SeasonItem ToItem(Season season) => new()
    {
        PK = DynamoDbKeys.SeasonPK(season.SeasonId),
        SK = DynamoDbKeys.SeasonSK,
        SeasonId = season.SeasonId,
        Name = season.Name,
        StartDate = season.StartDate,
        EndDate = season.EndDate
    };

    public static SeasonItem ToCollectionItem(Season season) => new()
    {
        PK = DynamoDbKeys.SeasonsCollectionPK,
        SK = DynamoDbKeys.SeasonCollectionSK(season.SeasonId),
        SeasonId = season.SeasonId,
        Name = season.Name,
        StartDate = season.StartDate,
        EndDate = season.EndDate
    };

    public static Season ToDomain(SeasonItem item) => new()
    {
        SeasonId = item.SeasonId,
        Name = item.Name,
        StartDate = item.StartDate,
        EndDate = item.EndDate
    };

    // ─── Poll ─────────────────────────────────────────────────────────────────

    public static PollItem ToItem(GameDayPoll poll) => new()
    {
        PK = DynamoDbKeys.PollPK(poll.PollId),
        SK = DynamoDbKeys.PollSK,
        GSI1PK = DynamoDbKeys.PollPK(poll.PollId),
        GSI1SK = DynamoDbKeys.PollSK,
        GSI2PK = DynamoDbKeys.GSI2PK_PollStatus(poll.SeasonId, poll.Status.ToString()),
        GSI2SK = DynamoDbKeys.GSI2SK_Deadline(poll.Deadline),
        PollId = poll.PollId,
        SeasonId = poll.SeasonId,
        GameDate = poll.GameDate,
        Title = poll.Title,
        Deadline = poll.Deadline,
        Status = poll.Status.ToString(),
        CreatedBy = poll.CreatedBy,
        CreatedAt = poll.CreatedAt
    };

    public static PollItem ToSeasonCollectionItem(GameDayPoll poll) => new()
    {
        PK = DynamoDbKeys.SeasonPK(poll.SeasonId),
        SK = DynamoDbKeys.PollSeasonSK(poll.GameDate, poll.PollId),
        PollId = poll.PollId,
        SeasonId = poll.SeasonId,
        GameDate = poll.GameDate,
        Title = poll.Title,
        Deadline = poll.Deadline,
        Status = poll.Status.ToString(),
        CreatedBy = poll.CreatedBy,
        CreatedAt = poll.CreatedAt
    };

    public static GameDayPoll ToDomain(PollItem item) => new()
    {
        PollId = item.PollId,
        SeasonId = item.SeasonId,
        GameDate = item.GameDate,
        Title = item.Title,
        Deadline = item.Deadline,
        CreatedBy = item.CreatedBy,
        CreatedAt = item.CreatedAt
    };

    /// <summary>
    /// Sets the Status on a domain poll from a DynamoDB item.
    /// Uses a separate method because Status has a private setter.
    /// </summary>
    public static GameDayPoll ToDomainWithStatus(PollItem item)
    {
        var poll = ToDomain(item);
        // Use reflection or a dedicated internal method to hydrate status from persistence.
        // For now, we use an internal hydration approach.
        HydrateStatus(poll, item.Status);
        return poll;
    }

    // ─── Question ─────────────────────────────────────────────────────────────

    public static QuestionItem ToItem(Question question) => new()
    {
        PK = DynamoDbKeys.PollPK(question.PollId),
        SK = DynamoDbKeys.QuestionSK(question.SortOrder, question.QuestionId),
        GSI1PK = DynamoDbKeys.PollPK(question.PollId),
        GSI1SK = $"Q#{question.QuestionId}",
        QuestionId = question.QuestionId,
        PollId = question.PollId,
        Text = question.Text,
        SortOrder = question.SortOrder,
        CorrectOptionId = question.CorrectOptionId
    };

    public static Question ToDomain(QuestionItem item) => new()
    {
        QuestionId = item.QuestionId,
        PollId = item.PollId,
        Text = item.Text,
        SortOrder = item.SortOrder
    };

    public static Question ToDomainWithCorrectOption(QuestionItem item)
    {
        var question = ToDomain(item);
        if (item.CorrectOptionId is not null)
            question.SetCorrectOption(item.CorrectOptionId);
        return question;
    }

    // ─── Option ───────────────────────────────────────────────────────────────

    public static OptionItem ToItem(Option option, string pollId) => new()
    {
        PK = DynamoDbKeys.PollPK(pollId),
        SK = DynamoDbKeys.OptionSK(option.QuestionId, option.SortOrder),
        GSI1PK = DynamoDbKeys.PollPK(pollId),
        GSI1SK = $"OPT#{option.QuestionId}#{option.OptionId}",
        OptionId = option.OptionId,
        QuestionId = option.QuestionId,
        Text = option.Text,
        SortOrder = option.SortOrder
    };

    public static Option ToDomain(OptionItem item) => new()
    {
        OptionId = item.OptionId,
        QuestionId = item.QuestionId,
        Text = item.Text,
        SortOrder = item.SortOrder
    };

    // ─── UserAnswer ───────────────────────────────────────────────────────────

    public static UserAnswerItem ToItem(UserAnswer answer) => new()
    {
        PK = DynamoDbKeys.UserAnswerPK(answer.UserId, answer.PollId),
        SK = DynamoDbKeys.UserAnswerSK(answer.QuestionId),
        GSI1PK = DynamoDbKeys.PollPK(answer.PollId),
        GSI1SK = DynamoDbKeys.UserAnswerGSI1SK(answer.UserId, answer.QuestionId),
        UserId = answer.UserId,
        DisplayName = answer.DisplayName,
        PollId = answer.PollId,
        QuestionId = answer.QuestionId,
        SelectedOptionId = answer.SelectedOptionId,
        SubmittedAt = answer.SubmittedAt,
        IsCorrect = answer.IsCorrect
    };

    public static UserAnswer ToDomain(UserAnswerItem item) => new()
    {
        UserId = item.UserId,
        DisplayName = item.DisplayName,
        PollId = item.PollId,
        QuestionId = item.QuestionId,
        SelectedOptionId = item.SelectedOptionId,
        SubmittedAt = item.SubmittedAt
    };

    public static UserAnswer ToDomainWithScore(UserAnswerItem item)
    {
        var answer = ToDomain(item);
        if (item.IsCorrect.HasValue)
            answer.Evaluate(item.IsCorrect.Value ? item.SelectedOptionId : "___force_false___");
        return answer;
    }

    // ─── Leaderboard ──────────────────────────────────────────────────────────

    // ─── Scoring facts ────────────────────────────────────────────────────────

    public static PollScoreItem ToItem(PollScore score) => new()
    {
        PK = DynamoDbKeys.LeaderboardPK(score.SeasonId),
        SK = DynamoDbKeys.PollScoreSK(score.UserId, score.PollId),
        UserId = score.UserId,
        SeasonId = score.SeasonId,
        PollId = score.PollId,
        DisplayName = score.DisplayName,
        Points = score.Points,
        Answered = score.Answered,
        ScoredAt = score.ScoredAt
    };

    public static PollScore ToDomain(PollScoreItem item) => new()
    {
        SeasonId = item.SeasonId,
        PollId = item.PollId,
        UserId = item.UserId,
        DisplayName = item.DisplayName,
        Points = item.Points,
        Answered = item.Answered,
        ScoredAt = item.ScoredAt
    };

    public static PollVoidItem ToItem(PollVoid pollVoid) => new()
    {
        PK = DynamoDbKeys.LeaderboardPK(pollVoid.SeasonId),
        SK = DynamoDbKeys.PollVoidSK(pollVoid.UserId, pollVoid.PollId),
        UserId = pollVoid.UserId,
        SeasonId = pollVoid.SeasonId,
        PollId = pollVoid.PollId,
        PollTitle = pollVoid.PollTitle,
        Reason = pollVoid.Reason,
        VoidedBy = pollVoid.VoidedBy,
        VoidedAt = pollVoid.VoidedAt
    };

    public static PollVoid ToDomain(PollVoidItem item) => new()
    {
        SeasonId = item.SeasonId,
        PollId = item.PollId,
        UserId = item.UserId,
        PollTitle = item.PollTitle,
        Reason = item.Reason,
        VoidedBy = item.VoidedBy,
        VoidedAt = item.VoidedAt
    };

    public static PointAdjustmentItem ToItem(PointAdjustment adjustment) => new()
    {
        PK = DynamoDbKeys.LeaderboardPK(adjustment.SeasonId),
        SK = DynamoDbKeys.PointAdjustmentSK(adjustment.UserId, adjustment.AdjustmentId),
        UserId = adjustment.UserId,
        SeasonId = adjustment.SeasonId,
        AdjustmentId = adjustment.AdjustmentId,
        DisplayName = adjustment.DisplayName,
        Points = adjustment.Points,
        Reason = adjustment.Reason,
        CreatedBy = adjustment.CreatedBy,
        CreatedAt = adjustment.CreatedAt
    };

    public static PointAdjustment ToDomain(PointAdjustmentItem item) => new()
    {
        SeasonId = item.SeasonId,
        AdjustmentId = item.AdjustmentId,
        UserId = item.UserId,
        DisplayName = item.DisplayName,
        Points = item.Points,
        Reason = item.Reason,
        CreatedBy = item.CreatedBy,
        CreatedAt = item.CreatedAt
    };

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Hydrates the Status property on a GameDayPoll from a persisted string value.
    /// This works around the private setter by using the domain transition methods
    /// to reach the target state from the default (Draft).
    /// </summary>
    private static void HydrateStatus(GameDayPoll poll, string status)
    {
        var targetStatus = Enum.Parse<PollStatus>(status);

        switch (targetStatus)
        {
            case PollStatus.Draft:
                // Already default
                break;
            case PollStatus.Open:
                poll.Publish();
                break;
            case PollStatus.Closed:
                poll.Publish();
                poll.Close();
                break;
            case PollStatus.Scored:
                poll.Publish();
                poll.Close();
                poll.MarkScored(DateTime.UtcNow);
                break;
        }
    }
}
