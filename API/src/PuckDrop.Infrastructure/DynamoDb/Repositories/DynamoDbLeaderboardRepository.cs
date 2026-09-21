using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using PuckDrop.Domain.Entities;
using PuckDrop.Application.Models;
using PuckDrop.Application.Repositories;
using PuckDrop.Infrastructure.DynamoDb.Items;
using PuckDrop.Infrastructure.DynamoDb.Mappers;

namespace PuckDrop.Infrastructure.DynamoDb.Repositories;

public class DynamoDbLeaderboardRepository(IAmazonDynamoDB dynamoDb) : ILeaderboardRepository
{

    // ─── Scoring facts ────────────────────────────────────────────────────────

    public async Task<SeasonFacts> GetSeasonFactsAsync(string seasonId, CancellationToken cancellationToken = default)
    {
        var scores = new List<PollScore>();
        var voids = new List<PollVoid>();
        var adjustments = new List<PointAdjustment>();

        Dictionary<string, AttributeValue>? startKey = null;

        do
        {
            var response = await dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = DynamoDbKeys.TableName,
                KeyConditionExpression = "PK = :pk AND begins_with(SK, :skPrefix)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new(DynamoDbKeys.LeaderboardPK(seasonId)),
                    [":skPrefix"] = new(DynamoDbKeys.UserFactSKPrefix)
                },
                // Scoring writes facts and folds them moments later, so an eventually consistent
                // read here could miss the poll being scored and publish a total without it.
                ConsistentRead = true,
                ExclusiveStartKey = startKey
            }, cancellationToken);

            foreach (var attrs in response.Items)
            {
                switch (FactMarker(attrs))
                {
                    case var m when m.StartsWith("POLL#"): scores.Add(MapPollScore(attrs)); break;
                    case var m when m.StartsWith("VOID#"): voids.Add(MapPollVoid(attrs)); break;
                    case var m when m.StartsWith("ADJ#"): adjustments.Add(MapAdjustment(attrs)); break;
                }
            }

            startKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        }
        while (startKey is not null);

        return new SeasonFacts(scores, voids, adjustments);
    }

    public async Task SavePollScoresAsync(IReadOnlyList<PollScore> scores, CancellationToken cancellationToken = default)
    {
        if (scores.Count == 0) return;

        // Deterministic keys plus Put means re-scoring overwrites rather than accumulating, so the
        // whole operation is idempotent and safe to re-run after a failure.
        var writeRequests = scores
            .Select(score => new WriteRequest { PutRequest = new PutRequest { Item = ToAttributes(score) } })
            .ToList();

        await DynamoDbBatchWriter.WriteAllAsync(dynamoDb, writeRequests, cancellationToken);
    }

    public async Task SaveVoidAsync(PollVoid pollVoid, CancellationToken cancellationToken = default)
    {
        await dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = DynamoDbKeys.TableName,
            Item = ToAttributes(pollVoid)
        }, cancellationToken);
    }

    public Task DeleteVoidAsync(string seasonId, string userId, string pollId, CancellationToken cancellationToken = default) =>
        DeleteFactAsync(
            seasonId,
            DynamoDbKeys.PollVoidSK(userId, pollId),
            $"No voided picks found for user '{userId}' on poll '{pollId}'.",
            cancellationToken);

    public async Task SaveAdjustmentAsync(PointAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        await dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = DynamoDbKeys.TableName,
            Item = ToAttributes(adjustment)
        }, cancellationToken);
    }

    public Task DeleteAdjustmentAsync(string seasonId, string userId, string adjustmentId, CancellationToken cancellationToken = default) =>
        DeleteFactAsync(
            seasonId,
            DynamoDbKeys.PointAdjustmentSK(userId, adjustmentId),
            $"Adjustment '{adjustmentId}' not found for user '{userId}'.",
            cancellationToken);

    /// <summary>
    /// Deletes one fact, insisting it was actually there.
    /// </summary>
    /// <remarks>
    /// An unconditional Delete on a missing key succeeds, so undoing a sanction that does not exist
    /// would report success having changed nothing. The condition turns that into a 404.
    /// </remarks>
    private async Task DeleteFactAsync(
        string seasonId, string sortKey, string notFoundMessage, CancellationToken cancellationToken)
    {
        try
        {
            await dynamoDb.DeleteItemAsync(new DeleteItemRequest
            {
                TableName = DynamoDbKeys.TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new(DynamoDbKeys.LeaderboardPK(seasonId)),
                    ["SK"] = new(sortKey)
                },
                ConditionExpression = "attribute_exists(PK)"
            }, cancellationToken);
        }
        catch (ConditionalCheckFailedException)
        {
            throw new KeyNotFoundException(notFoundMessage);
        }
    }

    /// <summary>
    /// Identifies which kind of fact an item is, from the segment of its sort key that follows
    /// U#{userId}#.
    /// </summary>
    private static string FactMarker(Dictionary<string, AttributeValue> attrs) =>
        attrs["SK"].S[DynamoDbKeys.UserFactSKPrefixFor(attrs["userId"].S).Length..];

    // ─── Attribute Helpers ────────────────────────────────────────────────────

    private static Dictionary<string, AttributeValue> ToAttributes(PollScore score)
    {
        var item = DynamoDbMapper.ToItem(score);
        return new Dictionary<string, AttributeValue>
        {
            ["PK"] = new(item.PK),
            ["SK"] = new(item.SK),
            ["userId"] = new(item.UserId),
            ["seasonId"] = new(item.SeasonId),
            ["pollId"] = new(item.PollId),
            ["displayName"] = new(item.DisplayName),
            ["points"] = new() { N = item.Points.ToString() },
            ["answered"] = new() { N = item.Answered.ToString() },
            ["scoredAt"] = new(Timestamp(item.ScoredAt))
        };
    }

    private static PollScore MapPollScore(Dictionary<string, AttributeValue> attrs) =>
        DynamoDbMapper.ToDomain(new PollScoreItem
        {
            PK = attrs["PK"].S,
            SK = attrs["SK"].S,
            UserId = attrs["userId"].S,
            SeasonId = attrs["seasonId"].S,
            PollId = attrs["pollId"].S,
            DisplayName = attrs["displayName"].S,
            Points = int.Parse(attrs["points"].N),
            Answered = int.Parse(attrs["answered"].N),
            ScoredAt = ParseTimestamp(attrs["scoredAt"].S)
        });

    private static Dictionary<string, AttributeValue> ToAttributes(PollVoid pollVoid)
    {
        var item = DynamoDbMapper.ToItem(pollVoid);
        return new Dictionary<string, AttributeValue>
        {
            ["PK"] = new(item.PK),
            ["SK"] = new(item.SK),
            ["userId"] = new(item.UserId),
            ["seasonId"] = new(item.SeasonId),
            ["pollId"] = new(item.PollId),
            ["pollTitle"] = new(item.PollTitle),
            ["reason"] = new(item.Reason),
            ["voidedBy"] = new(item.VoidedBy),
            ["voidedAt"] = new(Timestamp(item.VoidedAt))
        };
    }

    private static PollVoid MapPollVoid(Dictionary<string, AttributeValue> attrs) =>
        DynamoDbMapper.ToDomain(new PollVoidItem
        {
            PK = attrs["PK"].S,
            SK = attrs["SK"].S,
            UserId = attrs["userId"].S,
            SeasonId = attrs["seasonId"].S,
            PollId = attrs["pollId"].S,
            PollTitle = attrs["pollTitle"].S,
            Reason = attrs["reason"].S,
            VoidedBy = attrs["voidedBy"].S,
            VoidedAt = ParseTimestamp(attrs["voidedAt"].S)
        });

    private static Dictionary<string, AttributeValue> ToAttributes(PointAdjustment adjustment)
    {
        var item = DynamoDbMapper.ToItem(adjustment);
        return new Dictionary<string, AttributeValue>
        {
            ["PK"] = new(item.PK),
            ["SK"] = new(item.SK),
            ["userId"] = new(item.UserId),
            ["seasonId"] = new(item.SeasonId),
            ["adjustmentId"] = new(item.AdjustmentId),
            ["displayName"] = new(item.DisplayName),
            ["points"] = new() { N = item.Points.ToString() },
            ["reason"] = new(item.Reason),
            ["createdBy"] = new(item.CreatedBy),
            ["createdAt"] = new(Timestamp(item.CreatedAt))
        };
    }

    private static PointAdjustment MapAdjustment(Dictionary<string, AttributeValue> attrs) =>
        DynamoDbMapper.ToDomain(new PointAdjustmentItem
        {
            PK = attrs["PK"].S,
            SK = attrs["SK"].S,
            UserId = attrs["userId"].S,
            SeasonId = attrs["seasonId"].S,
            AdjustmentId = attrs["adjustmentId"].S,
            DisplayName = attrs["displayName"].S,
            Points = int.Parse(attrs["points"].N),
            Reason = attrs["reason"].S,
            CreatedBy = attrs["createdBy"].S,
            CreatedAt = ParseTimestamp(attrs["createdAt"].S)
        });

    /// <summary>
    /// Round-trippable UTC. The older leaderboard item used a bare ToString()/Parse() pair, which
    /// loses DateTimeKind and depends on the host culture - not inherited here.
    /// </summary>
    private static string Timestamp(DateTime value) => value.ToString("O");

    private static DateTime ParseTimestamp(string value) =>
        DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);
}
