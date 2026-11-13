namespace DesktopGuard.App.Persistence;

internal enum StateStoreFailureKind
{
    Unavailable,
    Corrupt,
    TooLarge,
    UnsafePath,
}
