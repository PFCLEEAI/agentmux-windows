namespace AgentMux.Win.App.Notifications;

internal sealed class NullNativeToastService : INativeToastService
{
    public static NullNativeToastService Instance { get; } = new();

    private NullNativeToastService()
    {
    }

    public NativeToastResult TryShow(NativeToastRequest request) =>
        NativeToastResult.Skipped("native toast service disabled");
}
