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
        StartDate = season.StartDate.ToString("yyyy-MM-dd"),
        EndDate = season.EndDate.ToString("yyyy-MM-dd")
    };

    public static SeasonItem ToCollectionItem(Season season) => new()
    {
        PK = DynamoDbKeys.SeasonsCollectionPK,
        SK = DynamoDbKeys.SeasonCollectionSK(season.SeasonId),
        SeasonId = season.SeasonId,
        Name = season.Name,
        StartDate = season.StartDate.ToString("yyyy-MM-dd"),
        EndDate = season.EndDate.ToString("yyyy-MM-dd")
    };

    public static Season ToDomain(SeasonItem item) => new()
    {
        SeasonId = item.SeasonId,
        Name = item.Name,
        StartDate = DateOnly.ParseExact(item.StartDate, "yyyy-MM-dd"),
        EndDate = DateOnly.ParseExact(item.EndDate, "yyyy-MM-dd")
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
        GameDate = poll.GameDate.ToString("yyyy-MM-dd"),
        Title = poll.Title,
        Deadline = poll.Deadline.ToString("O"),
        Status = poll.Status.ToString(),
        CreatedBy = poll.CreatedBy,
        CreatedAt = poll.CreatedAt.ToString("O")
    };

    public static PollItem ToSeasonCollectionItem(GameDayPoll poll) => new()
    {
        PK = DynamoDbKeys.SeasonPK(poll.SeasonId),
        SK = DynamoDbKeys.PollSeasonSK(poll.GameDate, poll.PollId),
        PollId = poll.PollId,
        SeasonId = poll.SeasonId,
        GameDate = poll.GameDate.ToString("yyyy-MM-dd"),
        Title = poll.Title,
        Deadline = poll.Deadline.ToString("O"),
        Status = poll.Status.ToString(),
        CreatedBy = poll.CreatedBy,
        CreatedAt = poll.CreatedAt.ToString("O")
    };

    public static GameDayPoll ToDomain(PollItem item) => new()
    {
        PollId = item.PollId,
        SeasonId = item.SeasonId,
        GameDate = DateOnly.ParseExact(item.GameDate, "yyyy-MM-dd"),
        Title = item.Title,
        Deadline = DateTime.Parse(item.Deadline).ToUniversalTime(),
        CreatedBy = item.CreatedBy,
        CreatedAt = DateTime.Parse(item.CreatedAt).ToUniversalTime()
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
        PollId = answer.PollId,
        QuestionId = answer.QuestionId,
        SelectedOptionId = answer.SelectedOptionId,
        SubmittedAt = answer.SubmittedAt.ToString("O"),
        IsCorrect = answer.IsCorrect
    };

    public static UserAnswer ToDomain(UserAnswerItem item) => new()
    {
        UserId = item.UserId,
        PollId = item.PollId,
        QuestionId = item.QuestionId,
        SelectedOptionId = item.SelectedOptionId,
        SubmittedAt = DateTime.Parse(item.SubmittedAt).ToUniversalTime()
    };

    public static UserAnswer ToDomainWithScore(UserAnswerItem item)
    {
        var answer = ToDomain(item);
        if (item.IsCorrect.HasValue)
            answer.Evaluate(item.IsCorrect.Value ? item.SelectedOptionId : "___force_false___");
        return answer;
    }

    // ─── Leaderboard ──────────────────────────────────────────────────────────

    public static LeaderboardItem ToItem(LeaderboardEntry entry) => new()
    {
        PK = DynamoDbKeys.LeaderboardPK(entry.SeasonId),
        SK = DynamoDbKeys.LeaderboardSK(entry.TotalPoints, entry.UserId),
        UserId = entry.UserId,
        SeasonId = entry.SeasonId,
        DisplayName = entry.DisplayName,
        TotalPoints = entry.TotalPoints,
        TotalAnswered = entry.TotalAnswered,
        LastUpdated = entry.LastUpdated.ToString("O")
    };

    public static LeaderboardEntry ToDomain(LeaderboardItem item) => new()
    {
        UserId = item.UserId,
        SeasonId = item.SeasonId,
        DisplayName = item.DisplayName,
        TotalPoints = item.TotalPoints,
        TotalAnswered = item.TotalAnswered,
        LastUpdated = DateTime.Parse(item.LastUpdated).ToUniversalTime()
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
