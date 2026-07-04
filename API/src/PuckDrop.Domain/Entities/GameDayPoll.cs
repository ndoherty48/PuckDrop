using PuckDrop.Domain.Enums;

namespace PuckDrop.Domain.Entities;

/// <summary>
/// A poll tied to a specific game day. Contains one or more questions.
/// </summary>
public class GameDayPoll
{
    /// <summary>
    /// Unique identifier (ULID).
    /// </summary>
    public required string PollId { get; set; }

    /// <summary>
    /// Which season this poll belongs to.
    /// </summary>
    public required string SeasonId { get; set; }

    /// <summary>
    /// The game date.
    /// </summary>
    public required DateOnly GameDate { get; set; }

    /// <summary>
    /// Poll title, e.g. "Belfast Giants vs Sheffield Steelers".
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Voting closes at this time (typically 19:00 local, stored as UTC).
    /// </summary>
    public required DateTime Deadline { get; set; }

    /// <summary>
    /// Current lifecycle status.
    /// </summary>
    public PollStatus Status { get; private set; } = PollStatus.Draft;

    /// <summary>
    /// Admin user ID who created this poll.
    /// </summary>
    public required string CreatedBy { get; set; }

    /// <summary>
    /// When the poll was created (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Returns true if answers can be submitted (poll is Open and deadline hasn't passed).
    /// </summary>
    public bool IsAcceptingAnswers(DateTime utcNow)
    {
        return Status == PollStatus.Open && utcNow < Deadline;
    }

    /// <summary>
    /// Publishes the poll (Draft → Open).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if poll is not in Draft status.</exception>
    public void Publish()
    {
        if (Status != PollStatus.Draft)
            throw new InvalidOperationException($"Cannot publish a poll with status '{Status}'. Poll must be in Draft status.");

        Status = PollStatus.Open;
    }

    /// <summary>
    /// Closes voting (Open → Closed).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if poll is not in Open status.</exception>
    public void Close()
    {
        if (Status != PollStatus.Open)
            throw new InvalidOperationException($"Cannot close a poll with status '{Status}'. Poll must be in Open status.");

        Status = PollStatus.Closed;
    }

    /// <summary>
    /// Marks the poll as scored (Closed → Scored, or Open → Scored if deadline has passed).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if poll cannot be scored from its current status.</exception>
    public void MarkScored(DateTime utcNow)
    {
        if (Status == PollStatus.Closed)
        {
            Status = PollStatus.Scored;
            return;
        }

        if (Status == PollStatus.Open && utcNow >= Deadline)
        {
            Status = PollStatus.Scored;
            return;
        }

        throw new InvalidOperationException(
            $"Cannot score a poll with status '{Status}'. Poll must be Closed, or Open with a passed deadline.");
    }
}
