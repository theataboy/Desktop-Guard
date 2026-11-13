using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.State;

namespace DesktopGuard.Core.Shortcuts;

public static class ShortcutStateTransitions
{
    public static DesktopGuardState Approve(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Status != ShortcutStatus.Pending || shortcut.Operation != ShortcutOperation.None)
        {
            throw new InvalidOperationException("Only a pending shortcut can be approved.");
        }

        return Replace(state, shortcut with { Status = ShortcutStatus.ApprovedVisible });
    }

    public static DesktopGuardState BeginParking(DesktopGuardState state, Guid shortcutId, Guid recoveryFolderId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Status is not (ShortcutStatus.Pending or ShortcutStatus.ApprovedVisible)
            || shortcut.Operation != ShortcutOperation.None
            || recoveryFolderId == Guid.Empty)
        {
            throw new InvalidOperationException("This shortcut cannot be parked in its current state.");
        }

        return Replace(state, shortcut with
        {
            Operation = ShortcutOperation.Parking,
            RecoveryFolderId = recoveryFolderId,
            OperationId = recoveryFolderId,
        });
    }

    public static DesktopGuardState CompleteParking(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.Parking
            || shortcut.Status is not (ShortcutStatus.Pending or ShortcutStatus.ApprovedVisible)
            || shortcut.RecoveryFolderId is null)
        {
            throw new InvalidOperationException("No parking operation is in progress for this shortcut.");
        }

        return Replace(state, shortcut with
        {
            Status = ShortcutStatus.Parked,
            StatusBeforeParking = shortcut.Status,
            Operation = ShortcutOperation.None,
            OperationId = null,
        });
    }

    public static DesktopGuardState CancelParking(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.Parking)
        {
            throw new InvalidOperationException("No parking operation is in progress for this shortcut.");
        }

        return Replace(state, shortcut with
        {
            Operation = ShortcutOperation.None,
            RecoveryFolderId = null,
            OperationId = null,
        });
    }

    public static DesktopGuardState BeginRestore(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Status != ShortcutStatus.Parked
            || shortcut.Operation != ShortcutOperation.None
            || shortcut.RecoveryFolderId is null
            || shortcut.StatusBeforeParking is null)
        {
            throw new InvalidOperationException("Only a parked shortcut can be restored.");
        }

        return Replace(state, shortcut with
        {
            Operation = ShortcutOperation.Restoring,
            OperationId = Guid.NewGuid(),
        });
    }

    public static DesktopGuardState BeginMove(
        DesktopGuardState state,
        Guid shortcutId,
        DesktopRootKind targetRootKind,
        string targetRootPath)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRootPath);
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Status is not (ShortcutStatus.Pending or ShortcutStatus.ApprovedVisible)
            || shortcut.Operation != ShortcutOperation.None
            || shortcut.RootKind == targetRootKind
            || !Enum.IsDefined(targetRootKind)
            || !Path.IsPathFullyQualified(targetRootPath))
        {
            throw new InvalidOperationException("This shortcut cannot be moved in its current state.");
        }

        return Replace(state, shortcut with
        {
            Operation = ShortcutOperation.Moving,
            TargetRootKind = targetRootKind,
            TargetRootPath = Path.GetFullPath(targetRootPath),
            OperationId = Guid.NewGuid(),
        });
    }

    public static DesktopGuardState CompleteMove(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.Moving
            || shortcut.TargetRootKind is not DesktopRootKind targetRootKind
            || shortcut.TargetRootPath is null)
        {
            throw new InvalidOperationException("No move operation is in progress for this shortcut.");
        }

        string targetPath = Path.GetFullPath(Path.Combine(shortcut.TargetRootPath, shortcut.Name));
        return Replace(state, shortcut with
        {
            RootKind = targetRootKind,
            RootPath = shortcut.TargetRootPath,
            OriginalPath = targetPath,
            Status = targetRootKind == DesktopRootKind.Public
                ? ShortcutStatus.ApprovedVisible
                : ShortcutStatus.Pending,
            StatusBeforeParking = null,
            Operation = ShortcutOperation.None,
            RecoveryFolderId = null,
            TargetRootKind = null,
            TargetRootPath = null,
            OperationId = null,
        });
    }

    public static DesktopGuardState CancelMove(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.Moving)
        {
            throw new InvalidOperationException("No move operation is in progress for this shortcut.");
        }

        return Replace(state, shortcut with
        {
            Operation = ShortcutOperation.None,
            TargetRootKind = null,
            TargetRootPath = null,
            OperationId = null,
        });
    }

    public static DesktopGuardState CompleteRestore(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.Restoring
            || shortcut.Status != ShortcutStatus.Parked
            || shortcut.StatusBeforeParking is null)
        {
            throw new InvalidOperationException("No restore operation is in progress for this shortcut.");
        }

        return Replace(state, shortcut with
        {
            Status = shortcut.StatusBeforeParking.Value,
            StatusBeforeParking = null,
            Operation = ShortcutOperation.None,
            RecoveryFolderId = null,
            OperationId = null,
        });
    }

    public static DesktopGuardState CancelRestore(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.Restoring)
        {
            throw new InvalidOperationException("No restore operation is in progress for this shortcut.");
        }

        return Replace(state, shortcut with
        {
            Operation = ShortcutOperation.None,
            OperationId = null,
        });
    }

    public static DesktopGuardState SetStartAtSignIn(DesktopGuardState state, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with { StartAtSignIn = enabled };
    }

    private static TrackedShortcut Find(DesktopGuardState state, Guid shortcutId)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Shortcuts.FirstOrDefault(shortcut => shortcut.Id == shortcutId)
            ?? throw new KeyNotFoundException("The shortcut is no longer in the current inventory.");
    }

    private static DesktopGuardState Replace(DesktopGuardState state, TrackedShortcut updatedShortcut)
    {
        List<TrackedShortcut> shortcuts = state.Shortcuts
            .Select(shortcut => shortcut.Id == updatedShortcut.Id ? updatedShortcut : shortcut)
            .ToList();
        return state with { Shortcuts = shortcuts };
    }
}
