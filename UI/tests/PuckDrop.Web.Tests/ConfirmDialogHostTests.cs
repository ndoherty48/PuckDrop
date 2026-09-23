using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Components;
using PuckDrop.Web.Services;
using Xunit;

namespace PuckDrop.Web.Tests;

/// <summary>
/// ConfirmDialogHost + Modal, the design-system replacement for window.confirm(). The dialog
/// itself is a native &lt;dialog&gt;, shown via JS interop (mocked here) rather than a real
/// browser, so this covers the wiring - what's rendered, and what a button click resolves - not
/// Escape/backdrop dismissal, which is native &lt;dialog&gt; behaviour with nothing left to test.
/// </summary>
public class ConfirmDialogHostTests : BunitContext
{
    private (IRenderedComponent<ConfirmDialogHost> Cut, ConfirmDialogService Service) RenderHost()
    {
        JSInterop.SetupVoid("puckDropDialog.show", _ => true);
        JSInterop.SetupVoid("puckDropDialog.close", _ => true);

        var service = new ConfirmDialogService();
        Services.AddSingleton(service);

        return (Render<ConfirmDialogHost>(), service);
    }

    [Fact]
    public void ShowAsync_RendersTheTitleMessageAndButtonLabels()
    {
        var (cut, service) = RenderHost();

        _ = service.ShowAsync("Are you sure you want to do this?", title: "Do the thing?",
            confirmLabel: "Do it", cancelLabel: "Never mind");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Do the thing?", cut.Find(".pd-dialog-title").TextContent);
            Assert.Contains("Are you sure you want to do this?", cut.Find(".pd-dialog-body").TextContent);
            Assert.Contains("Do it", cut.Find(".pd-dialog-actions").TextContent);
            Assert.Contains("Never mind", cut.Find(".pd-dialog-actions").TextContent);
        });
    }

    [Fact]
    public void Danger_UsesTheDangerButtonStyle()
    {
        var (cut, service) = RenderHost();

        _ = service.ShowAsync("This cannot be undone.", danger: true);

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".pd-dialog-actions button.pd-btn-danger")));
    }

    [Fact]
    public void NonDanger_UsesThePrimaryButtonStyle()
    {
        var (cut, service) = RenderHost();

        _ = service.ShowAsync("Just a heads up.");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".pd-dialog-actions button.pd-btn-primary")));
    }

    [Fact]
    public async Task ClickingConfirm_ResolvesTrue()
    {
        var (cut, service) = RenderHost();

        var task = service.ShowAsync("Delete it?", confirmLabel: "Delete", danger: true);
        cut.WaitForAssertion(() => cut.Find(".pd-dialog-actions button.pd-btn-danger"));

        cut.Find(".pd-dialog-actions button.pd-btn-danger").Click();

        Assert.True(await task);
    }

    [Fact]
    public async Task ClickingCancel_ResolvesFalse()
    {
        var (cut, service) = RenderHost();

        var task = service.ShowAsync("Delete it?");
        cut.WaitForAssertion(() => cut.Find(".pd-dialog-actions button.pd-btn-quiet"));

        cut.Find(".pd-dialog-actions button.pd-btn-quiet").Click();

        Assert.False(await task);
    }

    [Fact]
    public async Task ResolvingOneRequest_LeavesTheHostReadyForTheNext()
    {
        var (cut, service) = RenderHost();

        var first = service.ShowAsync("First?");
        cut.WaitForAssertion(() => cut.Find(".pd-dialog-actions button.pd-btn-quiet"));
        cut.Find(".pd-dialog-actions button.pd-btn-quiet").Click();
        Assert.False(await first);

        var second = service.ShowAsync("Second?", confirmLabel: "Yes");
        cut.WaitForAssertion(() => Assert.Contains("Second?", cut.Find(".pd-dialog-body").TextContent));
        cut.Find(".pd-dialog-actions button.pd-btn-primary").Click();

        Assert.True(await second);
    }
}
