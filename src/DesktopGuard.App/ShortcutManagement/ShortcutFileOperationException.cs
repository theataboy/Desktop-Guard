namespace DesktopGuard.App.ShortcutManagement;

internal sealed class ShortcutFileOperationException : Exception
{
    public ShortcutFileOperationException(ShortcutFileOperationFailure failure, Exception? innerException = null)
        : base("The shortcut file operation could not be completed safely.", innerException)
    {
        Failure = failure;
    }

    public ShortcutFileOperationFailure Failure { get; }
}
