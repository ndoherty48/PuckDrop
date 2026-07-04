using Amazon.DynamoDBv2.DataModel;

namespace PuckDrop.Infrastructure.DynamoDb.Items;

/// <summary>
/// DynamoDB item for a LeaderboardEntry.
/// PK: LEADERBOARD#{seasonId}, SK: SCORE#{invertedPoints}#{userId}
/// invertedPoints = zero-padded (999999 - totalPoints) for descending sort.
/// </summary>
public class LeaderboardItem : DynamoDbItem
{
    [DynamoDBProperty("userId")]
    public string UserId { get; set; } = default!;

    [DynamoDBProperty("seasonId")]
    public string SeasonId { get; set; } = default!;

    [DynamoDBProperty("displayName")]
    public string DisplayName { get; set; } = default!;

    [DynamoDBProperty("totalPoints")]
    public int TotalPoints { get; set; }

    [DynamoDBProperty("totalAnswered")]
    public int TotalAnswered { get; set; }

    [DynamoDBProperty("lastUpdated")]
    public string LastUpdated { get; set; } = default!;
}
