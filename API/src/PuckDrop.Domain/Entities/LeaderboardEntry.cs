namespace PuckDrop.Domain.Entities;

/// <summary>
/// Aggregated score for a user within a season.
/// </summary>
public class LeaderboardEntry
{
    /// <summary>
    /// Cognito user ID.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Which season this entry belongs to.
    /// </summary>
    public required string SeasonId { get; set; }

    /// <summary>
    /// Denormalised display name for fast reads.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Running total of correct answers.
    /// </summary>
    public int TotalPoints { get; set; }

    /// <summary>
    /// Total questions answered across all polls.
    /// </summary>
    public int TotalAnswered { get; set; }

    /// <summary>
    /// Last time the entry was updated (UTC).
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Accuracy percentage (0-100). Returns 0 if no questions answered.
    /// </summary>
    public double Accuracy => TotalAnswered > 0
        ? Math.Round((double)TotalPoints / TotalAnswered * 100, 1)
        : 0;

    /// <summary>
    /// Adds points from a scored poll.
    /// </summary>
    /// <param name="correctAnswers">Number of correct answers in the poll.</param>
    /// <param name="totalAnswers">Total questions answered in the poll.</param>
    public void AddPollResults(int correctAnswers, int totalAnswers)
    {
        if (correctAnswers < 0)
            throw new ArgumentOutOfRangeException(nameof(correctAnswers), "Cannot be negative.");
        if (totalAnswers < 0)
            throw new ArgumentOutOfRangeException(nameof(totalAnswers), "Cannot be negative.");
        if (correctAnswers > totalAnswers)
            throw new ArgumentException("Correct answers cannot exceed total answers.");

        TotalPoints += correctAnswers;
        TotalAnswered += totalAnswers;
        LastUpdated = DateTime.UtcNow;
    }
}
