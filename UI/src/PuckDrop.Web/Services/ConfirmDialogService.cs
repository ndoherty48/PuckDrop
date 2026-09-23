namespace PuckDrop.Web.Services;

/// <summary>
/// Replaces the browser's window.confirm() with a dialog styled to match the app. Pages call
/// ShowAsync the same way they'd call the old JS confirm(); ConfirmDialogHost - mounted once in
/// MainLayout - is the only listener, and renders the answer via Components/Modal.razor.
/// </summary>
public sealed class ConfirmDialogService
{
    public event Action<ConfirmDialogRequest>? Requested;

    public Task<bool> ShowAsync(
        string message,
        string title = "Are you sure?",
        string confirmLabel = "Confirm",
        string cancelLabel = "Cancel",
        bool danger = false)
    {
        var handler = Requested ?? throw new InvalidOperationException(
            $"{nameof(ConfirmDialogService)} has no listener - is ConfirmDialogHost mounted in the layout?");

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Invoke(new ConfirmDialogRequest(title, message, confirmLabel, cancelLabel, danger, completion));
        return completion.Task;
    }
}

public sealed record ConfirmDialogRequest(
    string Title,
    string Message,
    string ConfirmLabel,
    string CancelLabel,
    bool Danger,
    TaskCompletionSource<bool> Completion);
