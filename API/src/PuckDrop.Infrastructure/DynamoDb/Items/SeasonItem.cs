using Amazon.DynamoDBv2.DataModel;

namespace PuckDrop.Infrastructure.DynamoDb.Items;

/// <summary>
/// DynamoDB item for a Season.
/// PK: SEASON#{seasonId}, SK: SEASON
/// Also stored in collection: PK: SEASONS, SK: SEASON#{seasonId}
/// </summary>
public class SeasonItem : DynamoDbItem
{
    [DynamoDBProperty("seasonId")]
    public string SeasonId { get; set; } = default!;

    [DynamoDBProperty("name")]
    public string Name { get; set; } = default!;

    [DynamoDBProperty("startDate")]
    public string StartDate { get; set; } = default!;

    [DynamoDBProperty("endDate")]
    public string EndDate { get; set; } = default!;
}
