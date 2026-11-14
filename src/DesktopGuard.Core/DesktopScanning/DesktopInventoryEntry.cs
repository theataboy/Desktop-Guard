namespace DesktopGuard.Core.DesktopScanning;

public sealed record DesktopInventoryEntry(
    DesktopRootKind RootKind,
    string RootPath,
    string Path,
    string Name,
    DesktopEntryKind Kind,
    bool IsHidden = false);
