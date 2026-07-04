using Amazon.DynamoDBv2.DataModel;

namespace PuckDrop.Infrastructure.DynamoDb.Items;

/// <summary>
/// DynamoDB item for a UserAnswer.
/// PK: USERANSWER#{userId}#{pollId}, SK: Q#{questionId}
/// GSI1PK: POLL#{pollId}, GSI1SK: ANSWER#{userId}#{questionId}
/// </summary>
public class UserAnswerItem : DynamoDbItem
{
    [DynamoDBProperty("userId")]
    public string UserId { get; set; } = default!;

    [DynamoDBProperty("pollId")]
    public string PollId { get; set; } = default!;

    [DynamoDBProperty("questionId")]
    public string QuestionId { get; set; } = default!;

    [DynamoDBProperty("selectedOptionId")]
    public string SelectedOptionId { get; set; } = default!;

    [DynamoDBProperty("submittedAt")]
    public string SubmittedAt { get; set; } = default!;

    [DynamoDBProperty("isCorrect")]
    public bool? IsCorrect { get; set; }
}
