using Amazon.DynamoDBv2.DataModel;

namespace PuckDrop.Infrastructure.DynamoDb.Items;

/// <summary>
/// DynamoDB item for a Question.
/// PK: POLL#{pollId}, SK: Q#{sortOrder}#{questionId}
/// GSI1PK: POLL#{pollId}, GSI1SK: Q#{questionId}
/// </summary>
public class QuestionItem : DynamoDbItem
{
    [DynamoDBProperty("questionId")]
    public string QuestionId { get; set; } = default!;

    [DynamoDBProperty("pollId")]
    public string PollId { get; set; } = default!;

    [DynamoDBProperty("text")]
    public string Text { get; set; } = default!;

    [DynamoDBProperty("sortOrder")]
    public int SortOrder { get; set; }

    [DynamoDBProperty("correctOptionId")]
    public string? CorrectOptionId { get; set; }
}
