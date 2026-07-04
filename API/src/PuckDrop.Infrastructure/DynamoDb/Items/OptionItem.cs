using Amazon.DynamoDBv2.DataModel;

namespace PuckDrop.Infrastructure.DynamoDb.Items;

/// <summary>
/// DynamoDB item for an Option.
/// PK: POLL#{pollId}, SK: OPT#{questionId}#{sortOrder}
/// GSI1PK: POLL#{pollId}, GSI1SK: OPT#{questionId}#{optionId}
/// </summary>
public class OptionItem : DynamoDbItem
{
    [DynamoDBProperty("optionId")]
    public string OptionId { get; set; } = default!;

    [DynamoDBProperty("questionId")]
    public string QuestionId { get; set; } = default!;

    [DynamoDBProperty("text")]
    public string Text { get; set; } = default!;

    [DynamoDBProperty("sortOrder")]
    public int SortOrder { get; set; }
}
