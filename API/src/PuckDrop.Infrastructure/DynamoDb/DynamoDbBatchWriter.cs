using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace PuckDrop.Infrastructure.DynamoDb;

/// <summary>
/// Writes items in batches, retrying anything DynamoDB hands back unprocessed.
/// </summary>
/// <remarks>
/// BatchWriteItem reports throttled writes in UnprocessedItems instead of throwing, so ignoring
/// them silently drops data - during scoring that means an answer keeping its old grade, or a
/// missing score fact, and a leaderboard that is quietly wrong. Retried with exponential backoff,
/// as AWS recommends.
/// </remarks>
internal static class DynamoDbBatchWriter
{
    /// <summary>BatchWriteItem accepts at most 25 items per request.</summary>
    private const int MaxItemsPerBatch = 25;

    private const int MaxAttempts = 5;

    public static async Task WriteAllAsync(
        IAmazonDynamoDB dynamoDb,
        IReadOnlyList<WriteRequest> writeRequests,
        CancellationToken cancellationToken)
    {
        foreach (var batch in writeRequests.Chunk(MaxItemsPerBatch))
            await WriteBatchAsync(dynamoDb, batch.ToList(), cancellationToken);
    }

    private static async Task WriteBatchAsync(
        IAmazonDynamoDB dynamoDb,
        List<WriteRequest> batch,
        CancellationToken cancellationToken)
    {
        var pending = batch;

        for (var attempt = 1; ; attempt++)
        {
            var response = await dynamoDb.BatchWriteItemAsync(new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, List<WriteRequest>>
                {
                    [DynamoDbKeys.TableName] = pending
                }
            }, cancellationToken);

            // The SDK leaves UnprocessedItems null when everything succeeded.
            if (response.UnprocessedItems is null
                || !response.UnprocessedItems.TryGetValue(DynamoDbKeys.TableName, out var unprocessed)
                || unprocessed.Count == 0)
            {
                return;
            }

            if (attempt == MaxAttempts)
                throw new InvalidOperationException(
                    $"DynamoDB left {unprocessed.Count} write(s) unprocessed after {MaxAttempts} attempts.");

            pending = unprocessed;
            await Task.Delay(TimeSpan.FromMilliseconds(50 * Math.Pow(2, attempt - 1)), cancellationToken);
        }
    }
}
