namespace DesktopGuard.App.ShortcutManagement;

internal enum ShortcutFileOperationFailure
{
    InvalidState,
    InvalidPath,
    SourceMissing,
    SourceChanged,
    FileNotStable,
    AccessDenied,
    FileLocked,
    ElevationCancelled,
    ElevationFailed,
    DestinationConflict,
    RecoveryUnavailable,
    IoFailure,
    AmbiguousRecovery,
}
