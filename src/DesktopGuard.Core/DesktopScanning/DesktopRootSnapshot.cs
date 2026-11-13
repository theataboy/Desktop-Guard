namespace DesktopGuard.Core.DesktopScanning;

public sealed record DesktopRootSnapshot(
    DesktopRootKind RootKind,
    string RootPath,
    bool IsComplete,
    IReadOnlyList<DesktopInventoryEntry> Entries);
