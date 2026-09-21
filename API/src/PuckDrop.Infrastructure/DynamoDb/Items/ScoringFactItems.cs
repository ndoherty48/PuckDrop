using Amazon.DynamoDBv2.DataModel;

namespace PuckDrop.Infrastructure.DynamoDb.Items;

/// <summary>
/// DynamoDB item for a PollScore - what one player earned in one scored poll.
/// PK: LEADERBOARD#{seasonId}, SK: U#{userId}#POLL#{pollId}
/// </summary>
public class PollScoreItem : DynamoDbItem
{
    [DynamoDBProperty("userId")]
    public string UserId { get; set; } = default!;

    [DynamoDBProperty("seasonId")]
    public string SeasonId { get; set; } = default!;

    [DynamoDBProperty("pollId")]
    public string PollId { get; set; } = default!;

    [DynamoDBProperty("displayName")]
    public string DisplayName { get; set; } = default!;

    [DynamoDBProperty("points")]
    public int Points { get; set; }

    [DynamoDBProperty("answered")]
    public int Answered { get; set; }

    [DynamoDBProperty("scoredAt")]
    public DateTime ScoredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// DynamoDB item for a PollVoid - an admin voiding one player's picks for one game day.
/// PK: LEADERBOARD#{seasonId}, SK: U#{userId}#VOID#{pollId}
/// </summary>
public class PollVoidItem : DynamoDbItem
{
    [DynamoDBProperty("userId")]
    public string UserId { get; set; } = default!;

    [DynamoDBProperty("seasonId")]
    public string SeasonId { get; set; } = default!;

    [DynamoDBProperty("pollId")]
    public string PollId { get; set; } = default!;

    [DynamoDBProperty("pollTitle")]
    public string PollTitle { get; set; } = default!;

    [DynamoDBProperty("reason")]
    public string Reason { get; set; } = default!;

    [DynamoDBProperty("voidedBy")]
    public string VoidedBy { get; set; } = default!;

    [DynamoDBProperty("voidedAt")]
    public DateTime VoidedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// DynamoDB item for a PointAdjustment - a manual points change applied by an admin.
/// PK: LEADERBOARD#{seasonId}, SK: U#{userId}#ADJ#{adjustmentId}
/// </summary>
public class PointAdjustmentItem : DynamoDbItem
{
    [DynamoDBProperty("userId")]
    public string UserId { get; set; } = default!;

    [DynamoDBProperty("seasonId")]
    public string SeasonId { get; set; } = default!;

    [DynamoDBProperty("adjustmentId")]
    public string AdjustmentId { get; set; } = default!;

    [DynamoDBProperty("displayName")]
    public string DisplayName { get; set; } = default!;

    [DynamoDBProperty("points")]
    public int Points { get; set; }

    [DynamoDBProperty("reason")]
    public string Reason { get; set; } = default!;

    [DynamoDBProperty("createdBy")]
    public string CreatedBy { get; set; } = default!;

    [DynamoDBProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
