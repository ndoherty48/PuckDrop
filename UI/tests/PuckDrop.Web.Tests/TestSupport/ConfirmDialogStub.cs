using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Services;

namespace PuckDrop.Web.Tests.TestSupport;

/// <summary>
/// Stands in for ConfirmDialogHost: answers every ConfirmDialogService.ShowAsync call immediately
/// with a fixed result, and records the request so a test can assert on what was shown.
/// </summary>
internal sealed class ConfirmDialogStub
{
    public ConfirmDialogRequest? LastRequest { get; private set; }

    public static ConfirmDialogStub Register(BunitContext context, bool result)
    {
        var stub = new ConfirmDialogStub();
        var service = new ConfirmDialogService();
        service.Requested += request =>
        {
            stub.LastRequest = request;
            request.Completion.TrySetResult(result);
        };
        context.Services.AddSingleton(service);
        return stub;
    }
}
