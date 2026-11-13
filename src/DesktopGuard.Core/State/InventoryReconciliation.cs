using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;

namespace DesktopGuard.Core.State;

public sealed record InventoryReconciliation(
    DesktopGuardState State,
    IReadOnlyList<TrackedShortcut> NewUserShortcuts,
    IReadOnlyList<TrackedShortcut> NewPublicShortcuts,
    IReadOnlyList<DesktopEvent> NewEvents);
