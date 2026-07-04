using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Enums;
using PuckDrop.Domain.Models;
using PuckDrop.Application.Repositories;
using PuckDrop.Infrastructure.DynamoDb.Items;
using PuckDrop.Infrastructure.DynamoDb.Mappers;

namespace PuckDrop.Infrastructure.DynamoDb.Repositories;

public class DynamoDbPollRepository(IAmazonDynamoDB dynamoDb) : IPollRepository
{

    public async Task<GameDayPoll?> GetByIdAsync(string pollId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = DynamoDbKeys.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new(DynamoDbKeys.PollPK(pollId)),
                ["SK"] = new(DynamoDbKeys.PollSK)
            }
        }, cancellationToken);

        if (!response.IsItemSet)
            return null;

        return MapPollFromAttributes(response.Item);
    }

    public async Task<PollWithQuestions?> GetWithQuestionsAsync(
        string pollId, CancellationToken cancellationToken = default)
    {
        // Query GSI1 to get poll + questions + options in a single query
        var response = await dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = DynamoDbKeys.TableName,
            IndexName = DynamoDbKeys.GSI1IndexName,
            KeyConditionExpression = "GSI1PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new(DynamoDbKeys.PollPK(pollId))
            }
        }, cancellationToken);

        if (response.Items.Count == 0)
            return null;

        GameDayPoll? poll = null;
        var questions = new List<Question>();
        var options = new List<Option>();

        foreach (var item in response.Items)
        {
            var sk = item["GSI1SK"].S;

            if (sk == DynamoDbKeys.PollSK)
            {
                poll = MapPollFromAttributes(item);
            }
            else if (sk.StartsWith("Q#"))
            {
                questions.Add(MapQuestionFromAttributes(item));
            }
            else if (sk.StartsWith("OPT#"))
            {
                options.Add(MapOptionFromAttributes(item));
            }
        }

        if (poll is null)
            return null;

        return new PollWithQuestions(poll, questions, options);
    }

    public async Task<IReadOnlyList<GameDayPoll>> ListBySeasonAsync(string seasonId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = DynamoDbKeys.TableName,
            KeyConditionExpression = "PK = :pk AND begins_with(SK, :skPrefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new(DynamoDbKeys.SeasonPK(seasonId)),
                [":skPrefix"] = new("POLL#")
            }
        }, cancellationToken);

        return response.Items.Select(MapPollFromAttributes).ToList();
    }

    public async Task<IReadOnlyList<GameDayPoll>> GetActiveAsync(string seasonId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = DynamoDbKeys.TableName,
            IndexName = DynamoDbKeys.GSI2IndexName,
            KeyConditionExpression = "GSI2PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new(DynamoDbKeys.GSI2PK_PollStatus(seasonId, PollStatus.Open.ToString()))
            }
        }, cancellationToken);

        return response.Items.Select(MapPollFromAttributes).ToList();
    }

    public async Task SavePollAsync(GameDayPoll poll, CancellationToken cancellationToken = default)
    {
        var mainItem = ToPollAttributes(DynamoDbMapper.ToItem(poll));
        var collectionItem = ToPollAttributes(DynamoDbMapper.ToSeasonCollectionItem(poll));

        await dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem { Put = new Put { TableName = DynamoDbKeys.TableName, Item = mainItem } },
                new TransactWriteItem { Put = new Put { TableName = DynamoDbKeys.TableName, Item = collectionItem } }
            ]
        }, cancellationToken);
    }

    public async Task SaveQuestionAsync(Question question, IReadOnlyList<Option> options, CancellationToken cancellationToken = default)
    {
        var transactItems = new List<TransactWriteItem>
        {
            new() { Put = new Put { TableName = DynamoDbKeys.TableName, Item = ToQuestionAttributes(DynamoDbMapper.ToItem(question)) } }
        };

        foreach (var option in options)
        {
            transactItems.Add(new TransactWriteItem
            {
                Put = new Put { TableName = DynamoDbKeys.TableName, Item = ToOptionAttributes(DynamoDbMapper.ToItem(option, question.PollId)) }
            });
        }

        await dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = transactItems
        }, cancellationToken);
    }

    public async Task DeleteQuestionAsync(string pollId, string questionId, CancellationToken cancellationToken = default)
    {
        // First, find the question and its options to delete them
        var response = await dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = DynamoDbKeys.TableName,
            KeyConditionExpression = "PK = :pk",
            FilterExpression = "begins_with(SK, :qPrefix) OR begins_with(SK, :optPrefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new(DynamoDbKeys.PollPK(pollId)),
                [":qPrefix"] = new($"Q#"),
                [":optPrefix"] = new($"OPT#{questionId}#")
            }
        }, cancellationToken);

        var transactItems = new List<TransactWriteItem>();

        foreach (var item in response.Items)
        {
            var sk = item["SK"].S;

            // Match the specific question or its options
            bool isTargetQuestion = sk.StartsWith("Q#") && item.ContainsKey("questionId") && item["questionId"].S == questionId;
            bool isTargetOption = sk.StartsWith($"OPT#{questionId}#");

            if (isTargetQuestion || isTargetOption)
            {
                transactItems.Add(new TransactWriteItem
                {
                    Delete = new Delete
                    {
                        TableName = DynamoDbKeys.TableName,
                        Key = new Dictionary<string, AttributeValue>
                        {
                            ["PK"] = item["PK"],
                            ["SK"] = item["SK"]
                        }
                    }
                });
            }
        }

        if (transactItems.Count > 0)
        {
            await dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems = transactItems
            }, cancellationToken);
        }
    }

    public async Task UpdateQuestionsAsync(IReadOnlyList<Question> questions, CancellationToken cancellationToken = default)
    {
        var transactItems = questions.Select(q => new TransactWriteItem
        {
            Put = new Put
            {
                TableName = DynamoDbKeys.TableName,
                Item = ToQuestionAttributes(DynamoDbMapper.ToItem(q))
            }
        }).ToList();

        await dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = transactItems
        }, cancellationToken);
    }

    // ─── Attribute Helpers ────────────────────────────────────────────────────

    private static Dictionary<string, AttributeValue> ToPollAttributes(PollItem item)
    {
        var attrs = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new(item.PK),
            ["SK"] = new(item.SK),
            ["pollId"] = new(item.PollId),
            ["seasonId"] = new(item.SeasonId),
            ["gameDate"] = new(item.GameDate),
            ["title"] = new(item.Title),
            ["deadline"] = new(item.Deadline),
            ["status"] = new(item.Status),
            ["createdBy"] = new(item.CreatedBy),
            ["createdAt"] = new(item.CreatedAt)
        };

        if (item.GSI1PK is not null) attrs["GSI1PK"] = new(item.GSI1PK);
        if (item.GSI1SK is not null) attrs["GSI1SK"] = new(item.GSI1SK);
        if (item.GSI2PK is not null) attrs["GSI2PK"] = new(item.GSI2PK);
        if (item.GSI2SK is not null) attrs["GSI2SK"] = new(item.GSI2SK);

        return attrs;
    }

    private static Dictionary<string, AttributeValue> ToQuestionAttributes(QuestionItem item)
    {
        var attrs = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new(item.PK),
            ["SK"] = new(item.SK),
            ["questionId"] = new(item.QuestionId),
            ["pollId"] = new(item.PollId),
            ["text"] = new(item.Text),
            ["sortOrder"] = new() { N = item.SortOrder.ToString() }
        };

        if (item.GSI1PK is not null) attrs["GSI1PK"] = new(item.GSI1PK);
        if (item.GSI1SK is not null) attrs["GSI1SK"] = new(item.GSI1SK);
        if (item.CorrectOptionId is not null) attrs["correctOptionId"] = new(item.CorrectOptionId);

        return attrs;
    }

    private static Dictionary<string, AttributeValue> ToOptionAttributes(OptionItem item)
    {
        var attrs = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new(item.PK),
            ["SK"] = new(item.SK),
            ["optionId"] = new(item.OptionId),
            ["questionId"] = new(item.QuestionId),
            ["text"] = new(item.Text),
            ["sortOrder"] = new() { N = item.SortOrder.ToString() }
        };

        if (item.GSI1PK is not null) attrs["GSI1PK"] = new(item.GSI1PK);
        if (item.GSI1SK is not null) attrs["GSI1SK"] = new(item.GSI1SK);

        return attrs;
    }

    private static GameDayPoll MapPollFromAttributes(Dictionary<string, AttributeValue> attrs)
    {
        var item = new PollItem
        {
            PK = attrs["PK"].S,
            SK = attrs["SK"].S,
            PollId = attrs["pollId"].S,
            SeasonId = attrs["seasonId"].S,
            GameDate = attrs["gameDate"].S,
            Title = attrs["title"].S,
            Deadline = attrs["deadline"].S,
            Status = attrs["status"].S,
            CreatedBy = attrs["createdBy"].S,
            CreatedAt = attrs["createdAt"].S
        };
        return DynamoDbMapper.ToDomainWithStatus(item);
    }

    private static Question MapQuestionFromAttributes(Dictionary<string, AttributeValue> attrs)
    {
        var item = new QuestionItem
        {
            PK = attrs["PK"].S,
            SK = attrs["SK"].S,
            QuestionId = attrs["questionId"].S,
            PollId = attrs["pollId"].S,
            Text = attrs["text"].S,
            SortOrder = int.Parse(attrs["sortOrder"].N),
            CorrectOptionId = attrs.TryGetValue("correctOptionId", out var val) ? val.S : null
        };
        return DynamoDbMapper.ToDomainWithCorrectOption(item);
    }

    private static Option MapOptionFromAttributes(Dictionary<string, AttributeValue> attrs)
    {
        var item = new OptionItem
        {
            PK = attrs["PK"].S,
            SK = attrs["SK"].S,
            OptionId = attrs["optionId"].S,
            QuestionId = attrs["questionId"].S,
            Text = attrs["text"].S,
            SortOrder = int.Parse(attrs["sortOrder"].N)
        };
        return DynamoDbMapper.ToDomain(item);
    }
}
