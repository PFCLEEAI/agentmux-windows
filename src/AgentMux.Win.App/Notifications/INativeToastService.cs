using AgentMux.Core.Models;

namespace AgentMux.Win.App.Notifications;

internal interface INativeToastService
{
    NativeToastResult TryShow(NativeToastRequest request);
}

internal sealed record NativeToastRequest(
    string NotificationId,
    string WorkspaceId,
    string PaneId,
    string Title,
    string? Subtitle,
    string Body)
{
    public static NativeToastRequest FromNotification(TerminalNotification notification) =>
        new(
            notification.Id,
            notification.WorkspaceId,
            notification.PaneId,
            notification.Title,
            notification.Subtitle,
            notification.Body);
}

internal sealed record NativeToastResult(bool Attempted, bool Requested, string? Error = null)
{
    public static NativeToastResult Skipped(string reason) => new(false, false, reason);

    public static NativeToastResult Sent() => new(true, true);

    public static NativeToastResult Failed(string reason) => new(true, false, reason);
}
