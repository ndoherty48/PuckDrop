namespace PuckDrop.Domain.Entities;

/// <summary>
/// The reason text attached to an admin sanction - a voided poll or a point adjustment.
/// </summary>
/// <remarks>
/// Shown publicly on the leaderboard, so it is required and trimmed: a blank or whitespace-only
/// reason renders as an unexplained penalty next to someone's name.
/// </remarks>
public static class SanctionReason
{
    public const int MaxLength = 200;

    /// <summary>
    /// Validates and trims a sanction reason.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the reason is blank or too long.</exception>
    public static string Normalise(string reason, string paramName)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required.", paramName);

        var trimmed = reason.Trim();
        if (trimmed.Length > MaxLength)
            throw new ArgumentException($"A reason cannot be longer than {MaxLength} characters.", paramName);

        return trimmed;
    }
}
