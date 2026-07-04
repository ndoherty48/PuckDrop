using Amazon.DynamoDBv2.DataModel;

namespace PuckDrop.Infrastructure.DynamoDb.Items;

/// <summary>
/// Base class for all DynamoDB items in the PuckDrop single-table design.
/// </summary>
[DynamoDBTable("PuckDrop")]
public abstract class DynamoDbItem
{
    [DynamoDBHashKey("PK")]
    public string PK { get; set; } = default!;

    [DynamoDBRangeKey("SK")]
    public string SK { get; set; } = default!;

    [DynamoDBProperty("GSI1PK")]
    public string? GSI1PK { get; set; }

    [DynamoDBProperty("GSI1SK")]
    public string? GSI1SK { get; set; }

    [DynamoDBProperty("GSI2PK")]
    public string? GSI2PK { get; set; }

    [DynamoDBProperty("GSI2SK")]
    public string? GSI2SK { get; set; }
}
