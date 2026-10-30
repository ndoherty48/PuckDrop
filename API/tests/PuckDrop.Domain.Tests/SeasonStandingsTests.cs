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

    // ─── Tie-break ──────────────────────────────────────────────────────────

    private static IReadOnlyList<(string UserId, int Rank, bool Shared)> Ranks(IReadOnlyList<LeaderboardEntry> entries) =>
        entries.Select(e => (e.UserId, e.Rank, e.IsRankShared)).ToList();

    private static LeaderboardEntry For(IReadOnlyList<LeaderboardEntry> entries, string userId) =>
        entries.Single(e => e.UserId == userId);

    [Fact]
    public void Build_NoOneElseOnTheSameTotal_HasAnUnsharedRank()
    {
        var entries = SeasonStandings.Build(
            SeasonId, [Score("a", "p1", 2, 2), Score("b", "p1", 1, 2), Score("c", "p1", 0, 2)], [], []);

        Assert.Equal([("a", 1, false), ("b", 2, false), ("c", 3, false)], Ranks(entries));
    }

    [Fact]
    public void Build_LevelOnPoints_MoreGameDaysPlayedRanksHigher()
    {
        // The same 2 points, but "regular" turned up for three game days and "occasional" for two.
        var entries = SeasonStandings.Build(SeasonId, [
            Score("occasional", "p1", 1, 1), Score("occasional", "p2", 1, 1),
            Score("regular", "p1", 1, 1), Score("regular", "p2", 0, 1), Score("regular", "p3", 1, 1)
        ], [], []);

        Assert.Equal([("regular", 1, false), ("occasional", 2, false)], Ranks(entries));
        Assert.Equal([3, 2], entries.Select(e => e.GameDaysPlayed));
    }

    [Fact]
    public void Build_LevelOnPointsAndGameDays_HeadToHeadDecides()
    {
        // Both on 2 points from 4 game days, three of them shared. On those three Sam took p1, and
        // p2 and p3 were draws - so Sam is ahead 1-0. p4 and p5 weren't shared, so don't count.
        var entries = SeasonStandings.Build(SeasonId, [
            Score("nick", "p1", 0, 1), Score("nick", "p2", 0, 1), Score("nick", "p3", 1, 1), Score("nick", "p4", 1, 1),
            Score("sam", "p1", 1, 1), Score("sam", "p2", 0, 1), Score("sam", "p3", 1, 1), Score("sam", "p5", 0, 1)
        ], [], []);

        Assert.Equal([("sam", 1, false), ("nick", 2, false)], Ranks(entries));
    }

    [Fact]
    public void Build_LevelOnPoints_WithExactlyTheSameGameDays_StaysLevel()
    {
        // The accepted limitation: one point per game day, the same days played and the same total
        // means every day one of them took is matched by one the other took.
        var entries = SeasonStandings.Build(SeasonId, [
            Score("nick", "p1", 1, 1), Score("nick", "p2", 0, 1),
            Score("sam", "p1", 0, 1), Score("sam", "p2", 1, 1)
        ], [], []);

        Assert.Equal([("nick", 1, true), ("sam", 1, true)], Ranks(entries));
    }

    [Fact]
    public void Build_ThreeWayMiniLeague_SeparatesEveryone()
    {
        // All on 2 points from 3 game days. a beats b and c; b beats c.
        var entries = SeasonStandings.Build(SeasonId, [
            Score("a", "ab", 1, 1), Score("a", "ac", 1, 1), Score("a", "a-only", 0, 1),
            Score("b", "ab", 0, 1), Score("b", "bc", 1, 1), Score("b", "b-only", 1, 1),
            Score("c", "ac", 0, 1), Score("c", "bc", 0, 1), Score("c", "c-only", 2, 2)
        ], [], []);

        Assert.Equal([("a", 1, false), ("b", 2, false), ("c", 3, false)], Ranks(entries));
    }

    [Fact]
    public void Build_MiniLeagueThatOnlyPartlySeparates_LeavesTheRestSharingARank()
    {
        // a beats both; b and c draw with each other. No second mini-league between b and c.
        var entries = SeasonStandings.Build(SeasonId, [
            Score("a", "ab", 1, 1), Score("a", "ac", 1, 1), Score("a", "a-only", 0, 1),
            Score("b", "ab", 0, 1), Score("b", "bc", 1, 1), Score("b", "b-only", 1, 1),
            Score("c", "ac", 0, 1), Score("c", "bc", 1, 1), Score("c", "c-only", 1, 1)
        ], [], []);

        Assert.Equal([("a", 1, false), ("b", 2, true), ("c", 2, true)], Ranks(entries));
    }

    [Fact]
    public void Build_CircularHeadToHead_EveryoneSharesARank()
    {
        // a beats b, b beats c, c beats a. A per-pair comparer would give an unstable sort here.
        var entries = SeasonStandings.Build(SeasonId, [
            Score("a", "ab", 1, 1), Score("a", "ca", 0, 1), Score("a", "a-only", 1, 1),
            Score("b", "ab", 0, 1), Score("b", "bc", 1, 1), Score("b", "b-only", 1, 1),
            Score("c", "bc", 0, 1), Score("c", "ca", 1, 1), Score("c", "c-only", 1, 1)
        ], [], []);

        Assert.Equal([("a", 1, true), ("b", 1, true), ("c", 1, true)], Ranks(entries));
    }

    [Fact]
    public void Build_LevelPlayersWhoNeverSharedAGameDay_ShareARank()
    {
        var entries = SeasonStandings.Build(SeasonId, [Score("x", "p1", 1, 1), Score("y", "p2", 1, 1)], [], []);

        Assert.Equal([("x", 1, true), ("y", 1, true)], Ranks(entries));
    }

    [Fact]
    public void Build_SharedRank_UsesCompetitionNumbering()
    {
        var entries = SeasonStandings.Build(SeasonId, [
            Score("top", "p1", 3, 3),
            Score("x", "p1", 1, 3), Score("y", "p1", 1, 3),
            Score("last", "p1", 0, 3)
        ], [], []);

        Assert.Equal([("top", 1, false), ("x", 2, true), ("y", 2, true), ("last", 4, false)], Ranks(entries));
    }

    [Fact]
    public void Build_VoidedGameDay_IsNotAGameDayPlayed()
    {
        // Both on 1 point. "voided" played two game days, but one was voided, so it counts one.
        var entries = SeasonStandings.Build(
            SeasonId,
            [
                Score("voided", "p1", 1, 1), Score("voided", "p2", 0, 1),
                Score("clean", "p1", 1, 1), Score("clean", "p3", 0, 1)
            ],
            [Void("voided", "p2")],
            []);

        Assert.Equal(1, For(entries, "voided").GameDaysPlayed);
        Assert.Equal([("clean", 1, false), ("voided", 2, false)], Ranks(entries));
    }

    [Fact]
    public void Build_VoidedGameDay_DoesNotCountInHeadToHead()
    {
        // a beat b on p1, but a's p1 was voided - so the only shared day left is p2, a draw, and
        // they stay level. Counting p1 would have put a ahead.
        var entries = SeasonStandings.Build(
            SeasonId,
            [
                Score("a", "p1", 1, 1), Score("a", "p2", 0, 1), Score("a", "p3", 1, 1), Score("a", "p5", 0, 1),
                Score("b", "p1", 0, 1), Score("b", "p2", 0, 1), Score("b", "p4", 1, 1)
            ],
            [Void("a", "p1")],
            []);

        Assert.Equal([("a", 1, true), ("b", 1, true)], Ranks(entries));
    }

    [Fact]
    public void Build_Adjustment_MovesTheTotalButNotTheTieBreak()
    {
        // Sanctioned earned 3 from 3 game days and was docked 1; clean earned 2 from 2. Level on 2,
        // and the sanctioned player's playing record still wins the tie-break.
        var entries = SeasonStandings.Build(
            SeasonId,
            [
                Score("sanctioned", "p1", 1, 1), Score("sanctioned", "p2", 1, 1), Score("sanctioned", "p3", 1, 1),
                Score("clean", "p1", 1, 1), Score("clean", "p2", 1, 1)
            ],
            [],
            [Adjustment("sanctioned", -1)]);

        Assert.Equal(3, For(entries, "sanctioned").GameDaysPlayed);
        Assert.Equal([("sanctioned", 1, false), ("clean", 2, false)], Ranks(entries));
    }

    [Fact]
    public void Build_GameDayWithNothingGraded_IsNotAGameDayPlayed()
    {
        var entries = SeasonStandings.Build(
            SeasonId, [Score("u1", "ungraded", 0, 0), Score("u1", "p2", 1, 1)], [], []);

        Assert.Equal(1, Single(entries).GameDaysPlayed);
    }

    [Fact]
    public void Build_LevelOnANegativeTotal_TieBreakStillApplies()
    {
        var entries = SeasonStandings.Build(
            SeasonId,
            [Score("more", "p1", 1, 1), Score("more", "p2", 0, 1), Score("fewer", "p1", 1, 1)],
            [],
            [Adjustment("more", -3), Adjustment("fewer", -3)]);

        Assert.All(entries, e => Assert.Equal(-2, e.TotalPoints));
        Assert.Equal([("more", 1, false), ("fewer", 2, false)], Ranks(entries));
    }

    [Fact]
    public void Build_TieBreak_IsIndependentOfTheOrderFactsArriveIn()
    {
        PollScore[] scores =
        [
            Score("a", "ab", 1, 1), Score("a", "ac", 1, 1), Score("a", "a-only", 0, 1),
            Score("b", "ab", 0, 1), Score("b", "bc", 1, 1), Score("b", "b-only", 1, 1),
            Score("c", "ac", 0, 1), Score("c", "bc", 1, 1), Score("c", "c-only", 1, 1),
            Score("d", "p9", 2, 2)
        ];

        var forwards = SeasonStandings.Build(SeasonId, scores, [], []);
        var backwards = SeasonStandings.Build(SeasonId, scores.Reverse(), [], []);

        Assert.Equal(Ranks(forwards), Ranks(backwards));
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
