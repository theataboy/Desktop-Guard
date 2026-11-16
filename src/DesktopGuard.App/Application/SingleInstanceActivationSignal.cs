namespace DesktopGuard.App.Application;

internal static class SingleInstanceActivationSignal
{
    public static EventWaitHandle Create(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            $"Local\\DesktopGuard-Activate-{userId}");
    }
}
