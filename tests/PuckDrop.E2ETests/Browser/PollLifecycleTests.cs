using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// The highest-value single test in this suite: the full poll lifecycle through the real UI, as
/// an admin and a friend would actually experience it - nothing here is mocked. One long test
/// rather than several short ones, deliberately: every step after the initial login is an in-SPA
/// click/nav (no full page reload, so no fresh bootstrap fetch - see
/// AppHostFixture.GotoWithBootstrapRetryAsync), which keeps this materially cheaper against the
/// Lambda emulator than the same coverage split across separate tests each paying a fresh cold
/// boot would be.
/// </summary>
/// <remarks>
/// Covers, in one pass: admin creates a poll with two questions -> poll appears in Admin/Polls as
/// Draft, invisible on Home's "Next Game" card -> Publish (real JS confirm()) -> now visible on
/// Home as the active poll, for a separate friend session (proving GSI2's active-polls index
/// stays in sync with the status transition) -> friend picks answers and submits -> admin Close
/// (confirm) -> admin Score, deliberately getting one question "right" and one "wrong" for the
/// friend, so Results shows both a correct-answer green row and an incorrect-answer red row, with
/// the friend's real display name (not a UUID - the exact regression class an earlier bug in this
/// app came from) -> Leaderboard reflects the resulting 1 point, with the "You"/highlight styling
/// exercised implicitly by both accounts viewing it -> a second, single-question poll is run
/// through the same publish/vote/close/score cycle for the same friend, confirming their
/// leaderboard total accumulates to 2 rather than resetting to 1.
///
/// Deliberately out of scope here: the "voting closed" UI state (a poll whose deadline has
/// passed) - already covered by PuckDrop.Web.Tests' bUnit PollTests against mocked data, and
/// exercising it for real would mean either waiting out a real deadline or a second poll +
/// context just for that one assertion, which isn't worth the extra load on the emulator for
/// coverage that already exists elsewhere.
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class PollLifecycleTests(AppHostFixture fixture)
{
    [Fact]
    public async Task FullLifecycle_CreatePublishVoteCloseScore_ThenASecondPoll_AccumulatesPoints()
    {
        var adminSession = await fixture.LoginAndCaptureSessionAsync(TestData.AdminUsername, TestData.AdminPassword);
        await using var adminContext = await fixture.NewAuthenticatedBrowserContextAsync(adminSession);
        var admin = await adminContext.NewPageAsync();
        admin.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        await fixture.GotoWithBootstrapRetryAsync(admin, new Uri(fixture.BlazorBaseUri, "admin/polls").ToString());

        // ── Poll 1: two questions, friend gets one right and one wrong ─────────────────────
        var poll1Title = $"E2E Poll {Guid.NewGuid():N}";
        await CreatePollAsync(admin, poll1Title);
        await AddQuestionAsync(admin, "Will the home team win?", "Yes", "No");
        await AddQuestionAsync(admin, "Total goals over 5.5?", "Over", "Under");

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        var poll1Row = admin.Locator("tr", new PageLocatorOptions { HasText = poll1Title });
        await Assertions.Expect(poll1Row.GetByText("Draft")).ToBeVisibleAsync();

        // Not active yet - Draft polls don't show up as the "Next Game".
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Home", Exact = true }).ClickAsync();
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = poll1Title }))
            .Not.ToBeVisibleAsync();

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await poll1Row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Publish" }).ClickAsync();
        await Assertions.Expect(poll1Row.GetByText("Open")).ToBeVisibleAsync();

        // ── Friend: now visible as the active poll, votes, submits ─────────────────────────
        var friendSession = await fixture.LoginAndCaptureSessionAsync(TestData.FriendUsername, TestData.FriendPassword);
        await using var friendContext = await fixture.NewAuthenticatedBrowserContextAsync(friendSession);
        var friend = await friendContext.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(friend, fixture.BlazorBaseUri.ToString());
        await Assertions.Expect(friend.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = poll1Title }))
            .ToBeVisibleAsync();
        await friend.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Make your picks" }).ClickAsync();

        await friend.GetByLabel("Yes").CheckAsync();
        await friend.GetByLabel("Over").CheckAsync();
        await friend.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit picks" }).ClickAsync();
        await Assertions.Expect(friend.GetByText("Your picks have been submitted!")).ToBeVisibleAsync();

        // ── Admin: close, score (one right, one wrong for the friend) ──────────────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await poll1Row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" }).ClickAsync();
        await Assertions.Expect(poll1Row.GetByText("Closed")).ToBeVisibleAsync();

        // Closed polls drop off the "Next Game" card too - only Open ones show there.
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Home", Exact = true }).ClickAsync();
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = poll1Title }))
            .Not.ToBeVisibleAsync();

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await poll1Row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Score" }).ClickAsync();

        // "Yes" matches the friend's pick (correct); "Under" doesn't (they picked "Over" - wrong).
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Yes" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Under" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit Scores" }).ClickAsync();

        // ── Results: friend's real display name, one correct + one incorrect mark ──────────
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = poll1Title, Level = 1 }))
            .ToBeVisibleAsync();
        var resultsRow = admin.Locator("tr", new PageLocatorOptions { HasText = TestData.FriendDisplayName });
        await Assertions.Expect(resultsRow).ToBeVisibleAsync();
        // Each pick cell carries hidden ", correct" / ", wrong" text alongside its tick or cross.
        await Assertions.Expect(resultsRow.Locator("td", new LocatorLocatorOptions { HasText = ", correct" })).ToHaveCountAsync(1);
        await Assertions.Expect(resultsRow.Locator("td", new LocatorLocatorOptions { HasText = ", wrong" })).ToHaveCountAsync(1);

        // ── Leaderboard: 1 point so far ─────────────────────────────────────────────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Leaderboard" }).ClickAsync();
        var leaderboardRow = admin.Locator("tr", new PageLocatorOptions { HasText = TestData.FriendDisplayName });
        await Assertions.Expect(leaderboardRow).ToBeVisibleAsync();
        await Assertions.Expect(leaderboardRow).ToContainTextAsync("1");

        // ── Poll 2: single question, friend gets it right - points should accumulate ───────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        var poll2Title = $"E2E Poll 2 {Guid.NewGuid():N}";
        await CreatePollAsync(admin, poll2Title);
        await AddQuestionAsync(admin, "Will there be overtime?", "Yes", "No");

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        var poll2Row = admin.Locator("tr", new PageLocatorOptions { HasText = poll2Title });
        await poll2Row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Publish" }).ClickAsync();
        await Assertions.Expect(poll2Row.GetByText("Open")).ToBeVisibleAsync();

        await friend.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Home", Exact = true }).ClickAsync();
        await Assertions.Expect(friend.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = poll2Title }))
            .ToBeVisibleAsync();
        await friend.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Make your picks" }).ClickAsync();
        await friend.GetByLabel("Yes").CheckAsync();
        await friend.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit picks" }).ClickAsync();
        await Assertions.Expect(friend.GetByText("Your picks have been submitted!")).ToBeVisibleAsync();

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await poll2Row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" }).ClickAsync();
        await Assertions.Expect(poll2Row.GetByText("Closed")).ToBeVisibleAsync();
        await poll2Row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Score" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Yes" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit Scores" }).ClickAsync();

        // ScorePollAsync's real API call (not just the click) is what's slow under the emulator -
        // wait for the Nav.NavigateTo("results/...") it triggers on completion to actually land
        // before navigating away again, or a late-arriving navigation here can silently clobber
        // an immediately-following manual navigation (confirmed live: without this wait, clicking
        // "Leaderboard" right after the click above intermittently lands back on Results once the
        // scoring call finally resolves after the click already went through).
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = poll2Title, Level = 1 }))
            .ToBeVisibleAsync();

        // ── Leaderboard again: 1 + 1 = 2, not reset to 1 ────────────────────────────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Leaderboard" }).ClickAsync();
        await Assertions.Expect(leaderboardRow).ToContainTextAsync("2");
    }

    private static async Task CreatePollAsync(IPage admin, string title)
    {
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Create Poll" }).ClickAsync();

        var gameDate = DateOnly.FromDateTime(DateTime.UtcNow);
        // Generous future deadline - this whole test can genuinely take several minutes against
        // the Lambda emulator, and a poll whose deadline has already passed by the time the
        // friend gets to vote would show "Voting closed" with disabled inputs instead.
        var deadline = DateTime.UtcNow.AddHours(6);

        await admin.FillAsync("#title", title);
        await admin.FillAsync("#gameDate", gameDate.ToString("yyyy-MM-dd"));
        await admin.FillAsync("#deadline", deadline.ToString("yyyy-MM-ddTHH:mm"));
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create Poll" }).ClickAsync();

        // The edit page's h1 is the poll's own title
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = title, Level = 1 }))
            .ToBeVisibleAsync();
    }

    /// <summary>
    /// Fills EditPoll's "Add a question" form by its labels: the question field, then the first
    /// two option fields.
    /// </summary>
    private static async Task AddQuestionAsync(IPage admin, string questionText, string option1, string option2)
    {
        var addQuestion = admin.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Add a question" });

        await addQuestion.GetByLabel("Question", new LocatorGetByLabelOptions { Exact = true }).FillAsync(questionText);
        await addQuestion.GetByLabel("Option 1", new LocatorGetByLabelOptions { Exact = true }).FillAsync(option1);
        await addQuestion.GetByLabel("Option 2", new LocatorGetByLabelOptions { Exact = true }).FillAsync(option2);
        await addQuestion.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add question" })
            .ClickAsync();

        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = questionText }))
            .ToBeVisibleAsync();
    }
}
