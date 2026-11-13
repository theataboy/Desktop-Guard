namespace DesktopGuard.App.DesktopScanning;

internal enum DesktopScanIssueKind
{
    RootUnavailable,
    RootPathChanged,
    RootCouldNotBeRead,
    EntryCouldNotBeInspected,
    EntryOutsideRootSkipped,
    ReparsePointSkipped,
    EntryLimitReached,
}
