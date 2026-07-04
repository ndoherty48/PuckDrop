namespace PuckDrop.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of a game day poll.
/// Transitions: Draft → Open → Closed → Scored (or Open → Scored).
/// </summary>
public enum PollStatus
{
    Draft,
    Open,
    Closed,
    Scored
}
