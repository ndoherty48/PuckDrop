using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Pages.Admin;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests.Admin;

public class PollsTests : BunitContext
{
    private static SeasonModel BuildSeason() => new("2025-26", "2025/26 Season", new DateOnly(2025, 8, 1), new DateOnly(2026, 4, 30));

    private static PollModel BuildDraftPoll() => new(
        "poll-1", "2025-26", new DateOnly(2026, 1, 15), "Belfast Giants vs Sheffield Steelers",
        DateTime.UtcNow.AddDays(1), "Draft", "admin-user", DateTime.UtcNow);

    private IRenderedComponent<Polls> RenderWithHandler(RoutingHttpMessageHandler handler)
    {
        handler.MapJson(HttpMethod.Get, "seasons/current", BuildSeason());
        Services.AddSingleton(handler.BuildClient());
        return Render<Polls>();
    }

    [Fact]
    public void PublishButton_UserCancelsConfirm_DoesNotCallPublish()
    {
        var publishCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel> { BuildDraftPoll() })
            .Map(HttpMethod.Post, "polls/poll-1/publish", _ => { publishCalled = true; return new HttpResponseMessage(System.Net.HttpStatusCode.OK); });
        var cut = RenderWithHandler(handler);

        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);

        cut.WaitForAssertion(() => cut.Find("button.btn-success"));
        cut.Find("button.btn-success").Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("confirm"));
        Assert.False(publishCalled);
    }

    [Fact]
    public void PublishButton_UserConfirms_CallsPublishAndReloads()
    {
        var publishCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel> { BuildDraftPoll() })
            .Map(HttpMethod.Post, "polls/poll-1/publish", _ =>
            {
                publishCalled = true;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = System.Net.Http.Json.JsonContent.Create(BuildDraftPoll() with { Status = "Open" })
                };
            });
        var cut = RenderWithHandler(handler);

        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);

        cut.WaitForAssertion(() => cut.Find("button.btn-success"));
        cut.Find("button.btn-success").Click();

        cut.WaitForAssertion(() => Assert.True(publishCalled));
    }

    [Fact]
    public void CloseButton_UserCancelsConfirm_DoesNotCallClose()
    {
        var closeCalled = false;
        var openPoll = BuildDraftPoll() with { Status = "Open" };
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel> { openPoll })
            .Map(HttpMethod.Post, "polls/poll-1/close", _ => { closeCalled = true; return new HttpResponseMessage(System.Net.HttpStatusCode.OK); });
        var cut = RenderWithHandler(handler);

        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);

        cut.WaitForAssertion(() => cut.Find("button.btn-warning"));
        cut.Find("button.btn-warning").Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("confirm"));
        Assert.False(closeCalled);
    }
}
