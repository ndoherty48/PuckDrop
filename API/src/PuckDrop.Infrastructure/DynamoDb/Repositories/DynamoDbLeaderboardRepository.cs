using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Repositories;
using PuckDrop.Infrastructure.DynamoDb.Items;
using PuckDrop.Infrastructure.DynamoDb.Mappers;

namespace PuckDrop.Infrastructure.DynamoDb.Repositories;

public class DynamoDbLeaderboardRepository : ILeaderboardRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;

    public DynamoDbLeaderboardRepository(IAmazonDynamoDB dynamoDb)
    {
        _dynamoDb = dynamoDb;
    }

    public async Task<IReadOnlyList<LeaderboardEntry>> GetLeaderboardAsync(string seasonId, CancellationToken cancellationToken = default)
    {
        var response = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = DynamoDbKeys.TableName,
            KeyConditionExpression = "PK = :pk AND begins_with(SK, :skPrefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new(DynamoDbKeys.LeaderboardPK(seasonId)),
                [":skPrefix"] = new("SCORE#")
            }
        }, cancellationToken);

        // Items come back sorted by SK ascending, which means highest points first
        // (because we use inverted scores: 999999 - points)
        return response.Items.Select(MapFromAttributes).ToList();
    }

    public async Task<LeaderboardEntry?> GetEntryAsync(string seasonId, string userId, CancellationToken cancellationToken = default)
    {
        // We need to scan the leaderboard partition for this user since the SK includes inverted points
        var response = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = DynamoDbKeys.TableName,
            KeyConditionExpression = "PK = :pk AND begins_with(SK, :skPrefix)",
            FilterExpression = "userId = :userId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new(DynamoDbKeys.LeaderboardPK(seasonId)),
                [":skPrefix"] = new("SCORE#"),
                [":userId"] = new(userId)
            }
        }, cancellationToken);

        if (response.Items.Count == 0)
            return null;

        return MapFromAttributes(response.Items[0]);
    }

    public async Task SaveEntryAsync(LeaderboardEntry entry, int? previousPoints = null, CancellationToken cancellationToken = default)
    {
        var transactItems = new List<TransactWriteItem>();

        // If this is an update (points changed), delete the old sort key item first
        if (previousPoints.HasValue)
        {
            var oldSK = DynamoDbKeys.LeaderboardSK(previousPoints.Value, entry.UserId);
            transactItems.Add(new TransactWriteItem
            {
                Delete = new Delete
                {
                    TableName = DynamoDbKeys.TableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["PK"] = new(DynamoDbKeys.LeaderboardPK(entry.SeasonId)),
                        ["SK"] = new(oldSK)
                    }
                }
            });
        }

        // Write the new entry with updated sort key
        transactItems.Add(new TransactWriteItem
        {
            Put = new Put
            {
                TableName = DynamoDbKeys.TableName,
                Item = ToAttributes(entry)
            }
        });

        await _dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = transactItems
        }, cancellationToken);
    }

    // ─── Attribute Helpers ────────────────────────────────────────────────────

    private static Dictionary<string, AttributeValue> ToAttributes(LeaderboardEntry entry)
    {
        var item = DynamoDbMapper.ToItem(entry);
        return new Dictionary<string, AttributeValue>
        {
            ["PK"] = new(item.PK),
            ["SK"] = new(item.SK),
            ["userId"] = new(item.UserId),
            ["seasonId"] = new(item.SeasonId),
            ["displayName"] = new(item.DisplayName),
            ["totalPoints"] = new() { N = item.TotalPoints.ToString() },
            ["totalAnswered"] = new() { N = item.TotalAnswered.ToString() },
            ["lastUpdated"] = new(item.LastUpdated)
        };
    }

    private static LeaderboardEntry MapFromAttributes(Dictionary<string, AttributeValue> attrs)
    {
        var item = new LeaderboardItem
        {
            PK = attrs["PK"].S,
            SK = attrs["SK"].S,
            UserId = attrs["userId"].S,
            SeasonId = attrs["seasonId"].S,
            DisplayName = attrs["displayName"].S,
            TotalPoints = int.Parse(attrs["totalPoints"].N),
            TotalAnswered = int.Parse(attrs["totalAnswered"].N),
            LastUpdated = attrs["lastUpdated"].S
        };
        return DynamoDbMapper.ToDomain(item);
    }
}
