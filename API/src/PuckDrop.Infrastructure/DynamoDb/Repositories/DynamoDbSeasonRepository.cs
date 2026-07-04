using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Repositories;
using PuckDrop.Infrastructure.DynamoDb.Mappers;

namespace PuckDrop.Infrastructure.DynamoDb.Repositories;

public class DynamoDbSeasonRepository(IAmazonDynamoDB dynamoDb) : ISeasonRepository
{

    public async Task<Season?> GetByIdAsync(string seasonId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = DynamoDbKeys.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new(DynamoDbKeys.SeasonPK(seasonId)),
                ["SK"] = new(DynamoDbKeys.SeasonSK)
            }
        }, cancellationToken);

        if (!response.IsItemSet)
            return null;

        return MapFromAttributes(response.Item);
    }

    public async Task<IReadOnlyList<Season>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = DynamoDbKeys.TableName,
            KeyConditionExpression = "PK = :pk AND begins_with(SK, :skPrefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new(DynamoDbKeys.SeasonsCollectionPK),
                [":skPrefix"] = new("SEASON#")
            }
        }, cancellationToken);

        return response.Items.Select(MapFromAttributes).ToList();
    }

    public async Task SaveAsync(Season season, CancellationToken cancellationToken = default)
    {
        var mainItem = ToAttributes(DynamoDbMapper.ToItem(season));
        var collectionItem = ToAttributes(DynamoDbMapper.ToCollectionItem(season));

        await dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem { Put = new Put { TableName = DynamoDbKeys.TableName, Item = mainItem } },
                new TransactWriteItem { Put = new Put { TableName = DynamoDbKeys.TableName, Item = collectionItem } }
            ]
        }, cancellationToken);
    }

    private static Dictionary<string, AttributeValue> ToAttributes(Items.SeasonItem item) => new()
    {
        ["PK"] = new(item.PK),
        ["SK"] = new(item.SK),
        ["seasonId"] = new(item.SeasonId),
        ["name"] = new(item.Name),
        ["startDate"] = new(item.StartDate),
        ["endDate"] = new(item.EndDate)
    };

    private static Season MapFromAttributes(Dictionary<string, AttributeValue> attrs) =>
        DynamoDbMapper.ToDomain(new Items.SeasonItem
        {
            PK = attrs["PK"].S,
            SK = attrs["SK"].S,
            SeasonId = attrs["seasonId"].S,
            Name = attrs["name"].S,
            StartDate = attrs["startDate"].S,
            EndDate = attrs["endDate"].S
        });
}
