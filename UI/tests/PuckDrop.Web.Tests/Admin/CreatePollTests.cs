using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Pages.Admin;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests.Admin;

public class CreatePollTests : BunitContext
{
    private IRenderedComponent<CreatePoll> RenderCreatePoll(RoutingHttpMessageHandler handler)
    {
        Services.AddSingleton(handler.BuildClient());
        return Render<CreatePoll>();
    }

    [Fact]
    public void SubmitWithoutTitle_ShowsErrorSummaryAndFieldError_WithoutCreating()
    {
        // No POST route is registered, so a create call would fail and show "Failed to create poll".
        var cut = RenderCreatePoll(new RoutingHttpMessageHandler());

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Enter a title for the poll", cut.Find(".pd-error-summary").TextContent);
            Assert.Equal("true", cut.Find("#title").GetAttribute("aria-invalid"));
            Assert.Contains("title-error", cut.Find("#title").GetAttribute("aria-describedby"));
            Assert.DoesNotContain("Failed to create poll", cut.Markup);
        });
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void ValidSubmit_CreatesTheDraftAndOpensItsEditPage()
    {
        string? submittedBody = null;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, "polls", request =>
            {
                submittedBody = request.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PollModel("poll-9", "2026-27", new DateOnly(2026, 9, 23),
                        "Belfast Giants vs Guildford Flames", DateTime.UtcNow.AddDays(9), "Draft", "admin", DateTime.UtcNow))
                };
            });
        var cut = RenderCreatePoll(handler);

        cut.Find("#title").Change("  Belfast Giants vs Guildford Flames ");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith("admin/polls/poll-9/edit", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Contains("\"Belfast Giants vs Guildford Flames\"", submittedBody);
        Assert.Empty(cut.FindAll(".pd-error-summary"));
    }

    [Fact]
    public void CreateFails_ShowsAnError_AndStaysOnThePage()
    {
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, "polls", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = RenderCreatePoll(handler);

        cut.Find("#title").Change("Belfast Giants vs Guildford Flames");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.Contains("Failed to create poll. Please try again.", cut.Find("[role=alert]").TextContent));
        Assert.DoesNotContain("edit", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
