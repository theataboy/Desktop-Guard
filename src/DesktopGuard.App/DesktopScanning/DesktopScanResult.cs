using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.App.DesktopScanning;

internal sealed record DesktopScanResult(
    DesktopInventorySnapshot Inventory,
    IReadOnlyList<DesktopScanIssue> Issues);
