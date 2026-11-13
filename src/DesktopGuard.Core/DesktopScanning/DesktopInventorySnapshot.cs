namespace DesktopGuard.Core.DesktopScanning;

public sealed record DesktopInventorySnapshot(IReadOnlyList<DesktopRootSnapshot> Roots);
