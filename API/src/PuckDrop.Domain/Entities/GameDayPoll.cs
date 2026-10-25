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
    /// The status the poll was in when it was cancelled - what <see cref="Restore"/> returns it to.
    /// Null unless the poll is Cancelled.
    /// </summary>
    public PollStatus? CancelledFromStatus { get; private set; }

    /// <summary>
    /// Why the game day was cancelled. Shown to every player. Null unless the poll is Cancelled.
    /// </summary>
    public string? CancellationReason { get; private set; }

    /// <summary>
    /// Admin user ID who cancelled the poll. Null unless the poll is Cancelled.
    /// </summary>
    public string? CancelledBy { get; private set; }

    /// <summary>
    /// The canceller's display name as it was when they cancelled, so admins can see who did it
    /// without a user directory - the same way answers capture the picker's name. Null unless the
    /// poll is Cancelled.
    /// </summary>
    public string? CancelledByName { get; private set; }

    /// <summary>
    /// When the poll was cancelled (UTC). Null unless the poll is Cancelled.
    /// </summary>
    public DateTime? CancelledAt { get; private set; }

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
    /// Checks the poll can be scored - or re-scored - without changing its status.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="MarkScored"/> so scoring can validate before writing anything and
    /// apply the status change only once every write has landed. A pass that dies halfway then
    /// leaves the poll Closed and re-runnable, rather than Scored with half the work done.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if poll cannot be scored from its current status.</exception>
    public void EnsureCanBeScored(DateTime utcNow)
    {
        // Already Scored is allowed: that is a re-score. It used to be blocked only to stop the old
        // running-sum leaderboard double-counting, which no longer applies now totals are folded
        // from facts - re-scoring simply overwrites the poll's facts and the totals follow.
        if (Status is PollStatus.Closed or PollStatus.Scored)
            return;

        if (Status == PollStatus.Open && utcNow >= Deadline)
            return;

        throw new InvalidOperationException(
            $"Cannot score a poll with status '{Status}'. Poll must be Closed or Scored, or Open with a passed deadline.");
    }

    /// <summary>
    /// Marks the poll as scored (Closed → Scored, Open → Scored if the deadline has passed, or
    /// Scored → Scored when an admin re-scores).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if poll cannot be scored from its current status.</exception>
    public void MarkScored(DateTime utcNow)
    {
        EnsureCanBeScored(utcNow);
        Status = PollStatus.Scored;
    }

    /// <summary>
    /// Changes the game date and deadline. Draft only - once picks are open, moving the game
    /// underneath players would be confusing (and a game date already published is what other
    /// players' calendars/expectations are set by).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if poll is not in Draft status.</exception>
    public void Reschedule(DateOnly gameDate, DateTime deadline)
    {
        if (Status != PollStatus.Draft)
            throw new InvalidOperationException($"Cannot reschedule a poll with status '{Status}'. Poll must be in Draft status.");

        GameDate = gameDate;
        Deadline = deadline;
    }

    /// <summary>
    /// Cancels the poll because its game didn't go ahead. It will never be scored, so it never
    /// reaches the leaderboard.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the poll is Scored or already Cancelled.</exception>
    /// <exception cref="ArgumentException">Thrown if the reason is blank or too long.</exception>
    public void Cancel(string reason, string cancelledBy, string cancelledByName, DateTime utcNow)
    {
        // Not once Scored: a game that didn't count must be cancelled before it's scored, which is
        // what keeps a cancel from ever changing anyone's total. Not twice either, or the status
        // Restore goes back to would be overwritten with Cancelled.
        if (Status is PollStatus.Scored or PollStatus.Cancelled)
            throw new InvalidOperationException(
                $"Cannot cancel a poll with status '{Status}'. Poll must be Draft, Open or Closed.");

        var normalisedReason = SanctionReason.Normalise(reason, nameof(reason));

        CancelledFromStatus = Status;
        CancellationReason = normalisedReason;
        CancelledBy = cancelledBy;
        CancelledByName = cancelledByName;
        CancelledAt = utcNow;
        Status = PollStatus.Cancelled;
    }

    /// <summary>
    /// Undoes <see cref="Cancel"/>, returning the poll to exactly the status it was cancelled from.
    /// Its questions and picks were never touched, so nothing else needs putting back.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the poll is not Cancelled.</exception>
    public void Restore()
    {
        if (Status != PollStatus.Cancelled || CancelledFromStatus is not { } previousStatus)
            throw new InvalidOperationException(
                $"Cannot restore a poll with status '{Status}'. Poll must be Cancelled.");

        Status = previousStatus;
        CancelledFromStatus = null;
        CancellationReason = null;
        CancelledBy = null;
        CancelledByName = null;
        CancelledAt = null;
    }
}
