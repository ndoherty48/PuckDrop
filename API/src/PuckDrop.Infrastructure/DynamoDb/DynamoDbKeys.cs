namespace PuckDrop.Infrastructure.DynamoDb;

/// <summary>
/// Constants and helpers for DynamoDB key construction.
/// </summary>
public static class DynamoDbKeys
{
    public const string TableName = "PuckDrop";
    public const string GSI1IndexName = "GSI1";
    public const string GSI2IndexName = "GSI2";

    // Key prefixes
    public static string SeasonPK(string seasonId) => $"SEASON#{seasonId}";
    public const string SeasonSK = "SEASON";
    public const string SeasonsCollectionPK = "SEASONS";
    public static string SeasonCollectionSK(string seasonId) => $"SEASON#{seasonId}";

    public static string PollPK(string pollId) => $"POLL#{pollId}";
    public const string PollSK = "POLL";
    public static string PollSeasonSK(DateOnly gameDate, string pollId) => $"POLL#{gameDate:yyyy-MM-dd}#{pollId}";

    public static string QuestionSK(int sortOrder, string questionId) => $"Q#{sortOrder:D4}#{questionId}";
    public const string QuestionSKPrefix = "Q#";

    public static string OptionSK(string questionId, int sortOrder) => $"OPT#{questionId}#{sortOrder:D4}";
    public const string OptionSKPrefix = "OPT#";

    public static string UserAnswerPK(string userId, string pollId) => $"USERANSWER#{userId}#{pollId}";
    public static string UserAnswerSK(string questionId) => $"Q#{questionId}";
    public static string UserAnswerGSI1SK(string userId, string questionId) => $"ANSWER#{userId}#{questionId}";
    public const string AnswerGSI1SKPrefix = "ANSWER#";

    public static string LeaderboardPK(string seasonId) => $"LEADERBOARD#{seasonId}";
    // Scoring facts all live in the season's leaderboard partition under a shared U#{userId}#
    // prefix, so one begins_with query returns everything needed to fold one player's total -
    // or, unprefixed, the whole season in a single query.
    public const string UserFactSKPrefix = "U#";
    public static string UserFactSKPrefixFor(string userId) => $"U#{userId}#";
    public static string PollScoreSK(string userId, string pollId) => $"U#{userId}#POLL#{pollId}";
    public static string PollVoidSK(string userId, string pollId) => $"U#{userId}#VOID#{pollId}";
    public static string PointAdjustmentSK(string userId, string adjustmentId) => $"U#{userId}#ADJ#{adjustmentId}";

    public static string GSI2PK_PollStatus(string seasonId, string status) => $"SEASON#{seasonId}#STATUS#{status}";
    public static string GSI2SK_Deadline(DateTime deadline) => $"DEADLINE#{deadline:O}";
}
