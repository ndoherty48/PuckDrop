using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Standings;
using Xunit;

namespace PuckDrop.Domain.Tests;

public class SeasonStandingsTests
{
    private const string SeasonId = "2025-26";

    private static readonly DateTime Jan1 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PollScore Score(
        string userId, string pollId, int points, int answered, string? displayName = null, DateTime? scoredAt = null) => new()
        {
            SeasonId = SeasonId,
            PollId = pollId,
            UserId = userId,
            DisplayName = displayName ?? userId,
            Points = points,
            Answered = answered,
            ScoredAt = scoredAt ?? Jan1
        };

    private static PollVoid Void(string userId, string pollId, string reason = "No-show", DateTime? voidedAt = null)
    {
        var pollVoid = PollVoid.Create(SeasonId, pollId, userId, $"Game day {pollId}", reason, "admin-user");
        if (voidedAt is not null) pollVoid.VoidedAt = voidedAt.Value;
        return pollVoid;
    }

    private static PointAdjustment Adjustment(
        string userId, int points, string reason = "Picked after puck drop", DateTime? createdAt = null, string? displayName = null)
    {
        var adjustment = PointAdjustment.Create(
            SeasonId, $"adj-{userId}-{points}", userId, displayName ?? userId, points, reason, "admin-user");
        if (createdAt is not null) adjustment.CreatedAt = createdAt.Value;
        return adjustment;
    }

    private static LeaderboardEntry Single(IReadOnlyList<LeaderboardEntry> entries)
    {
        Assert.Single(entries);
        return entries[0];
    }

    // ─── Totals ─────────────────────────────────────────────────────────────

    [Fact]
    public void Build_SumsPointsAndAnsweredAcrossPolls()
    {
        var entry = Single(SeasonStandings.Build(
            SeasonId, [Score("u1", "p1", 2, 3), Score("u1", "p2", 1, 2)], [], []));

        Assert.Equal(3, entry.EarnedPoints);
        Assert.Equal(3, entry.TotalPoints);
        Assert.Equal(5, entry.TotalAnswered);
    }

    [Fact]
    public void Build_NoFacts_ReturnsEmpty()
    {
        Assert.Empty(SeasonStandings.Build(SeasonId, [], [], []));
    }

    // ─── Voids ──────────────────────────────────────────────────────────────

    [Fact]
    public void Build_VoidedPoll_ExcludedFromBothPointsAndAnswered()
    {
        // A void takes the whole game day off the player's record, not just the points - otherwise
        // voiding would quietly wreck their accuracy instead of erasing the day.
        var entry = Single(SeasonStandings.Build(
            SeasonId, [Score("u1", "p1", 2, 3), Score("u1", "p2", 1, 2)], [Void("u1", "p2")], []));

        Assert.Equal(2, entry.EarnedPoints);
        Assert.Equal(3, entry.TotalAnswered);
    }

    [Fact]
    public void Build_VoidedPoll_IsListedForDisplayWithItsReason()
    {
        var entry = Single(SeasonStandings.Build(
            SeasonId, [Score("u1", "p1", 2, 3)], [Void("u1", "p1", "Picked after puck drop")], []));

        var shown = Assert.Single(entry.Voids);
        Assert.Equal("Picked after puck drop", shown.Reason);
        Assert.Equal("p1", shown.PollId);
    }

    [Fact]
    public void Build_VoidForAPollThatIsNotScoredYet_IsNotShown()
    {
        // Voiding before scoring is allowed, and the void still applies once the poll is scored.
        // Until then there are no points missing, so there is nothing to explain on the leaderboard.
        var entry = Single(SeasonStandings.Build(
            SeasonId, [Score("u1", "p1", 2, 3)], [Void("u1", "p-not-scored-yet")], []));

        Assert.Empty(entry.Voids);
        Assert.Equal(2, entry.EarnedPoints);
    }

    [Fact]
    public void Build_PlayerWithOnlyAVoid_DoesNotAppearAtAll()
    {
        Assert.Empty(SeasonStandings.Build(SeasonId, [], [Void("u1", "p1")], []));
    }

    [Fact]
    public void Build_EveryPollVoided_LeavesZeroedTotalsRatherThanDivideByZeroAccuracy()
    {
        var entry = Single(SeasonStandings.Build(
            SeasonId, [Score("u1", "p1", 2, 3)], [Void("u1", "p1")], []));

        Assert.Equal(0, entry.EarnedPoints);
        Assert.Equal(0, entry.TotalAnswered);
        Assert.Equal(0, entry.Accuracy);
    }

    [Fact]
    public void Build_UnvoidingAfterARescore_ReflectsTheNewScoreNotTheOld()
    {
        // The payoff of keeping the void fact separate from the score fact: re-scoring rewrites the
        // score while the void is in place, and removing the void then reveals the CURRENT score.
        var rescored = Score("u1", "p1", points: 3, answered: 3);

        var whileVoided = Single(SeasonStandings.Build(SeasonId, [rescored], [Void("u1", "p1")], []));
        var afterRestore = Single(SeasonStandings.Build(SeasonId, [rescored], [], []));

        Assert.Equal(0, whileVoided.TotalPoints);
        Assert.Equal(3, afterRestore.TotalPoints);
    }

    // ─── Adjustments ────────────────────────────────────────────────────────

    [Fact]
    public void Build_Adjustment_ChangesTheTotalButNotTheAccuracy()
    {
        // A deduction is a sanction, not a wrong answer: 2 of 4 stays 50%.
        var entry = Single(SeasonStandings.Build(
            SeasonId, [Score("u1", "p1", 2, 4)], [], [Adjustment("u1", -1)]));

        Assert.Equal(2, entry.EarnedPoints);
        Assert.Equal(-1, entry.AdjustmentPoints);
        Assert.Equal(1, entry.TotalPoints);
        Assert.Equal(50, entry.Accuracy);
    }

    [Fact]
    public void Build_DeductionLargerThanPointsEarned_GoesNegativeRatherThanClamping()
    {
        var entry = Single(SeasonStandings.Build(
            SeasonId, [Score("u1", "p1", 2, 4)], [], [Adjustment("u1", -5)]));

        Assert.Equal(-3, entry.TotalPoints);
    }

    [Fact]
    public void Build_MultipleAdjustments_AreSummedAndListedOldestFirst()
    {
        var entry = Single(SeasonStandings.Build(SeasonId, [Score("u1", "p1", 5, 5)], [], [
            Adjustment("u1", -2, "Late pick", Jan1.AddDays(2)),
            Adjustment("u1", 1, "Bonus", Jan1.AddDays(1))
        ]));

        Assert.Equal(-1, entry.AdjustmentPoints);
        Assert.Equal(["Bonus", "Late pick"], entry.Adjustments.Select(a => a.Reason));
    }

    [Fact]
    public void Build_PlayerWithAnAdjustmentButNoScores_StillAppears_NamedFromTheAdjustment()
    {
        // There is no user directory - a name is only ever captured from a player's own answers -
        // so an adjustment carries its own denormalised name or the row would be nameless.
        var entry = Single(SeasonStandings.Build(
            SeasonId, [], [], [Adjustment("u1", -3, displayName: "Nathan")]));

        Assert.Equal("Nathan", entry.DisplayName);
        Assert.Equal(-3, entry.TotalPoints);
    }

    // ─── Ordering ───────────────────────────────────────────────────────────

    [Fact]
    public void Build_OrdersByEffectivePointsDescending_NotByEarnedPoints()
    {
        // The deduction must actually move someone down the table, not just annotate their row.
        var entries = SeasonStandings.Build(
            SeasonId,
            [Score("top", "p1", 10, 10), Score("dropped", "p1", 9, 10), Score("steady", "p1", 5, 10)],
            [],
            [Adjustment("dropped", -8)]);

        Assert.Equal(["top", "steady", "dropped"], entries.Select(e => e.UserId));
    }

    [Fact]
    public void Build_NegativeTotals_RankBelowEveryoneOnZeroOrMore()
    {
        var entries = SeasonStandings.Build(
            SeasonId,
            [Score("zero", "p1", 0, 3), Score("negative", "p1", 1, 3)],
            [],
            [Adjustment("negative", -5)]);

        Assert.Equal(["zero", "negative"], entries.Select(e => e.UserId));
        Assert.Equal(-4, entries[1].TotalPoints);
    }

    [Fact]
    public void Build_Ties_BreakOnUserIdSoOrderIsStable()
    {
        var entries = SeasonStandings.Build(
            SeasonId, [Score("charlie", "p1", 5, 5), Score("alice", "p1", 5, 5), Score("bob", "p1", 5, 5)], [], []);

        Assert.Equal(["alice", "bob", "charlie"], entries.Select(e => e.UserId));
    }

    [Fact]
    public void Build_IsIndependentOfTheOrderFactsArriveIn()
    {
        // The property the whole design rests on: facts can be written in any order - a void before
        // its poll is scored, a re-score after an adjustment - and the fold lands in the same place.
        PollScore[] scores = [Score("u1", "p1", 2, 3), Score("u1", "p2", 1, 2), Score("u2", "p1", 3, 3)];
        PollVoid[] voids = [Void("u1", "p2")];
        PointAdjustment[] adjustments = [Adjustment("u1", -1), Adjustment("u2", 2)];

        var forwards = SeasonStandings.Build(SeasonId, scores, voids, adjustments);
        var backwards = SeasonStandings.Build(
            SeasonId, scores.Reverse(), voids.Reverse(), adjustments.Reverse());

        Assert.Equal(
            forwards.Select(e => (e.UserId, e.TotalPoints, e.TotalAnswered)),
            backwards.Select(e => (e.UserId, e.TotalPoints, e.TotalAnswered)));
    }

    // ─── Display name and timestamps ────────────────────────────────────────

    [Fact]
    public void Build_DisplayName_ComesFromTheMostRecentlyScoredPoll()
    {
        // Self-healing, as the old running-sum leaderboard did on every scoring pass.
        var entry = Single(SeasonStandings.Build(SeasonId, [
            Score("u1", "p1", 1, 1, displayName: "Stale Old Name", scoredAt: Jan1),
            Score("u1", "p2", 1, 1, displayName: "Nathan", scoredAt: Jan1.AddDays(1))
        ], [], []));

        Assert.Equal("Nathan", entry.DisplayName);
    }

    [Fact]
    public void Build_LastUpdated_IsTheMostRecentContributingFact()
    {
        var entry = Single(SeasonStandings.Build(
            SeasonId,
            [Score("u1", "p1", 1, 1, scoredAt: Jan1)],
            [],
            [Adjustment("u1", -1, createdAt: Jan1.AddDays(3))]));

        Assert.Equal(Jan1.AddDays(3), entry.LastUpdated);
    }
}
