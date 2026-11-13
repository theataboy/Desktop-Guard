using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.App.DesktopScanning;

internal sealed record DesktopScanIssue(DesktopRootKind RootKind, DesktopScanIssueKind Kind);
