namespace DesktopGuard.App.Persistence;

internal sealed class StateStoreException : Exception
{
    public StateStoreException(StateStoreFailureKind kind, Exception? innerException = null)
        : base("Desktop Guard could not safely access its local state.", innerException)
    {
        Kind = kind;
    }

    public StateStoreFailureKind Kind { get; }
}
