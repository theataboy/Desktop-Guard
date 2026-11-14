using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;

namespace DesktopGuard.App.Application;

internal sealed record ShortcutListEntry(
    string Name,
    DesktopRootKind RootKind,
    ShortcutStatus? Status,
    TrackedShortcut? Shortcut,
    bool IsHidden = false);
