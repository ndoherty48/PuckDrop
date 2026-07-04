using Amazon.DynamoDBv2.DataModel;

namespace PuckDrop.Infrastructure.DynamoDb.Items;

/// <summary>
/// DynamoDB item for a GameDayPoll.
/// PK: POLL#{pollId}, SK: POLL
/// GSI1PK: POLL#{pollId}, GSI1SK: POLL
/// GSI2PK: SEASON#{seasonId}#STATUS#{status}, GSI2SK: DEADLINE#{deadline}
/// Also stored in season collection: PK: SEASON#{seasonId}, SK: POLL#{gameDate}#{pollId}
/// </summary>
public class PollItem : DynamoDbItem
{
    [DynamoDBProperty("pollId")]
    public string PollId { get; set; } = default!;

    [DynamoDBProperty("seasonId")]
    public string SeasonId { get; set; } = default!;

    [DynamoDBProperty("gameDate")]
    public string GameDate { get; set; } = default!;

    [DynamoDBProperty("title")]
    public string Title { get; set; } = default!;

    [DynamoDBProperty("deadline")]
    public string Deadline { get; set; } = default!;

    [DynamoDBProperty("status")]
    public string Status { get; set; } = default!;

    [DynamoDBProperty("createdBy")]
    public string CreatedBy { get; set; } = default!;

    [DynamoDBProperty("createdAt")]
    public string CreatedAt { get; set; } = default!;
}
