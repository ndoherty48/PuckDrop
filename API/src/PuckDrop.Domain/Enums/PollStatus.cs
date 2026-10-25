namespace PuckDrop.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of a game day poll.
/// Transitions: Draft → Open → Closed → Scored (or Open → Scored). Draft, Open and Closed can
/// also go to Cancelled, and back to whichever of them it was cancelled from.
/// </summary>
public enum PollStatus
{
    Draft,
    Open,
    Closed,
    Scored,
    Cancelled
}
