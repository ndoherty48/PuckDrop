using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Repositories;
using PuckDrop.Infrastructure.DynamoDb.Items;
using PuckDrop.Infrastructure.DynamoDb.Mappers;

namespace PuckDrop.Infrastructure.DynamoDb.Repositories;

public class DynamoDbUserAnswerRepository(IAmazonDynamoDB dynamoDb) : IUserAnswerRepository
{

    public async Task<IReadOnlyList<UserAnswer>> GetUserAnswersAsync(string userId, string pollId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = DynamoDbKeys.TableName,
            KeyConditionExpression = "PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new(DynamoDbKeys.UserAnswerPK(userId, pollId))
            }
        }, cancellationToken);

        return response.Items.Select(MapFromAttributes).ToList();
    }

    public async Task<IReadOnlyList<UserAnswer>> GetAllAnswersForPollAsync(string pollId, CancellationToken cancellationToken = default)
    {
        // Use GSI1 to get all answers for a poll
        var response = await dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = DynamoDbKeys.TableName,
            IndexName = DynamoDbKeys.GSI1IndexName,
            KeyConditionExpression = "GSI1PK = :pk AND begins_with(GSI1SK, :skPrefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new(DynamoDbKeys.PollPK(pollId)),
                [":skPrefix"] = new(DynamoDbKeys.AnswerGSI1SKPrefix)
            }
        }, cancellationToken);

        return response.Items.Select(MapFromAttributes).ToList();
    }

    public async Task SaveAnswersAsync(IReadOnlyList<UserAnswer> answers, CancellationToken cancellationToken = default)
    {
        if (answers.Count == 0) return;

        // DynamoDB TransactWriteItems supports max 100 items
        // For a friend-group app this should never be hit (max ~10 questions per poll)
        var transactItems = answers.Select(answer => new TransactWriteItem
        {
            Put = new Put
            {
                TableName = DynamoDbKeys.TableName,
                Item = ToAttributes(answer)
            }
        }).ToList();

        await dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = transactItems
        }, cancellationToken);
    }

    public async Task UpdateScoresAsync(IReadOnlyList<UserAnswer> answers, CancellationToken cancellationToken = default)
    {
        if (answers.Count == 0) return;

        // Batch update IsCorrect on each answer
        // Use BatchWriteItem for efficiency (scoring can touch many items)
        var writeRequests = answers.Select(answer => new WriteRequest
        {
            PutRequest = new PutRequest { Item = ToAttributes(answer) }
        }).ToList();

        // BatchWriteItem supports max 25 items per request
        foreach (var batch in writeRequests.Chunk(25))
        {
            await dynamoDb.BatchWriteItemAsync(new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, List<WriteRequest>>
                {
                    [DynamoDbKeys.TableName] = batch.ToList()
                }
            }, cancellationToken);
        }
    }

    // ─── Attribute Helpers ────────────────────────────────────────────────────

    private static Dictionary<string, AttributeValue> ToAttributes(UserAnswer answer)
    {
        var item = DynamoDbMapper.ToItem(answer);
        var attrs = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new(item.PK),
            ["SK"] = new(item.SK),
            ["GSI1PK"] = new(DynamoDbKeys.PollPK(answer.PollId)),
            ["GSI1SK"] = new(DynamoDbKeys.UserAnswerGSI1SK(answer.UserId, answer.QuestionId)),
            ["userId"] = new(item.UserId),
            ["pollId"] = new(item.PollId),
            ["questionId"] = new(item.QuestionId),
            ["selectedOptionId"] = new(item.SelectedOptionId),
            ["submittedAt"] = new(item.SubmittedAt)
        };

        if (item.IsCorrect.HasValue)
            attrs["isCorrect"] = new() { BOOL = item.IsCorrect.Value };

        return attrs;
    }

    private static UserAnswer MapFromAttributes(Dictionary<string, AttributeValue> attrs)
    {
        var item = new UserAnswerItem
        {
            PK = attrs["PK"].S,
            SK = attrs["SK"].S,
            UserId = attrs["userId"].S,
            PollId = attrs["pollId"].S,
            QuestionId = attrs["questionId"].S,
            SelectedOptionId = attrs["selectedOptionId"].S,
            SubmittedAt = attrs["submittedAt"].S,
            IsCorrect = attrs.TryGetValue("isCorrect", out var val) ? val.BOOL : null
        };

        return DynamoDbMapper.ToDomainWithScore(item);
    }
}
