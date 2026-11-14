using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.State;

namespace DesktopGuard.Core.Shortcuts;

public static class ShortcutStateTransitions
{
    public const string DesktopShortcutName = "Desktop Guard.lnk";

    public static bool IsDesktopShortcutName(string? name)
    {
        return string.Equals(name, DesktopShortcutName, StringComparison.OrdinalIgnoreCase);
    }

    public static DesktopGuardState BeginCreation(DesktopGuardState state, string userRootPath, string name)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(userRootPath);
        if (state.SchemaVersion != DesktopGuardState.CurrentSchemaVersion
            || !Path.IsPathFullyQualified(userRootPath)
            || !DesktopCandidatePolicy.IsValidWindowsFileName(name)
            || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
            || !string.Equals(name, DesktopShortcutName, StringComparison.Ordinal)
            || DesktopEntryClassifier.Classify(name) != DesktopEntryKind.Shortcut)
        {
            throw new InvalidOperationException("The desktop shortcut cannot be created in the current state.");
        }

        string root = Path.GetFullPath(userRootPath);
        DesktopRootCheckpoint? checkpoint = state.Roots.FirstOrDefault(item =>
            item.RootKind == DesktopRootKind.User && PathsEqual(item.RootPath, root));
        DesktopRootCheckpoint? publicCheckpoint = state.Roots.FirstOrDefault(item =>
            item.RootKind == DesktopRootKind.Public);
        string path = Path.GetFullPath(Path.Combine(root, name));
        if (checkpoint is not { IsBaselined: true }
            || !DesktopCandidatePolicy.IsDirectChildOf(root, path)
            || checkpoint.KnownPaths.Any(knownPath => PathsEqual(knownPath, path))
            || state.Shortcuts.Any(shortcut => shortcut.RootKind == DesktopRootKind.User
                && PathsEqual(shortcut.RootPath, root)
                && PathsEqual(shortcut.OriginalPath, path))
            || state.Shortcuts.Any(shortcut => IsDesktopShortcutName(shortcut.Name))
            || publicCheckpoint is not null
                && publicCheckpoint.KnownPaths.Any(knownPath => PathsEqual(
                    knownPath,
                    Path.GetFullPath(Path.Combine(publicCheckpoint.RootPath, name)))))
        {
            throw new InvalidOperationException("The desktop shortcut cannot be created in the current state.");
        }

        TrackedShortcut shortcut = new(
            Id: Guid.NewGuid(),
            RootKind: DesktopRootKind.User,
            RootPath: root,
            Name: name,
            OriginalPath: path,
            Status: ShortcutStatus.ApprovedVisible,
            StatusBeforeParking: null,
            Operation: ShortcutOperation.Creating,
            RecoveryFolderId: null,
            OperationId: Guid.NewGuid());
        return state with { Shortcuts = [.. state.Shortcuts, shortcut] };
    }

    public static DesktopGuardState RecordCreationHash(DesktopGuardState state, Guid shortcutId, byte[] contentHash)
    {
        ArgumentNullException.ThrowIfNull(contentHash);
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.Creating
            || shortcut.OperationId is null
            || contentHash.Length != 32)
        {
            throw new InvalidOperationException("No valid desktop shortcut creation is in progress.");
        }

        return Replace(state, shortcut with { OperationContentHash = [.. contentHash] });
    }

    public static DesktopGuardState CompleteCreation(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.Creating
            || shortcut.RootKind != DesktopRootKind.User
            || shortcut.Status != ShortcutStatus.ApprovedVisible)
        {
            throw new InvalidOperationException("No desktop shortcut creation is in progress.");
        }

        List<DesktopRootCheckpoint> roots = state.Roots
            .Select(root => root.RootKind == DesktopRootKind.User && PathsEqual(root.RootPath, shortcut.RootPath)
                ? root with
                {
                    KnownPaths = root.KnownPaths
                        .Append(shortcut.OriginalPath)
                        .Distinct(GetPathComparer())
                        .ToArray(),
                }
                : root)
            .ToList();
        return Replace(state with { Roots = roots }, shortcut with
        {
            Operation = ShortcutOperation.None,
            OperationId = null,
            OperationContentHash = null,
            IsHidden = false,
        });
    }

    public static DesktopGuardState CancelCreation(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.Creating)
        {
            throw new InvalidOperationException("No desktop shortcut creation is in progress.");
        }

        return state with { Shortcuts = state.Shortcuts.Where(item => item.Id != shortcutId).ToList() };
    }

    public static DesktopGuardState Approve(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Status != ShortcutStatus.Pending || shortcut.Operation != ShortcutOperation.None)
        {
            throw new InvalidOperationException("Only a pending shortcut can be approved.");
        }

        return Replace(state, shortcut with { Status = ShortcutStatus.ApprovedVisible });
    }

    public static DesktopGuardState BeginRename(DesktopGuardState state, Guid shortcutId, string targetName)
    {
        ArgumentNullException.ThrowIfNull(state);
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.None
            || !IsValidName(targetName)
            || !string.Equals(Path.GetExtension(shortcut.Name), Path.GetExtension(targetName), StringComparison.OrdinalIgnoreCase)
            || PathsEqual(shortcut.Name, targetName))
        {
            throw new InvalidOperationException("This shortcut cannot be renamed in its current state.");
        }

        string targetPath = Path.GetFullPath(Path.Combine(shortcut.RootPath, targetName));
        if (shortcut.Status != ShortcutStatus.Parked && state.Shortcuts.Any(other =>
                other.Id != shortcutId
                    && (other.Status != ShortcutStatus.Parked || other.Operation == ShortcutOperation.Restoring)
                    && PathsEqual(other.OriginalPath, targetPath)))
        {
            throw new InvalidOperationException("This shortcut cannot be renamed in its current state.");
        }

        return Replace(state, shortcut with
        {
            Operation = ShortcutOperation.Renaming,
            OperationId = Guid.NewGuid(),
            TargetName = targetName,
        });
    }

    public static DesktopGuardState CompleteRename(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.Renaming || shortcut.TargetName is null)
        {
            throw new InvalidOperationException("No rename operation is in progress for this shortcut.");
        }

        string targetPath = Path.GetFullPath(Path.Combine(shortcut.RootPath, shortcut.TargetName));
        List<DesktopRootCheckpoint> roots = shortcut.Status == ShortcutStatus.Parked
            ? state.Roots
            : state.Roots
                .Select(root => root.RootKind == shortcut.RootKind && PathsEqual(root.RootPath, shortcut.RootPath)
                    ? root with
                    {
                        KnownPaths = root.KnownPaths
                            .Select(path => PathsEqual(path, shortcut.OriginalPath) ? targetPath : path)
                            .Distinct(GetPathComparer())
                            .ToArray(),
                    }
                    : root)
                .ToList();

        return Replace(state with { Roots = roots }, shortcut with
        {
            Name = shortcut.TargetName,
            OriginalPath = targetPath,
            Operation = ShortcutOperation.None,
            OperationId = null,
            TargetName = null,
        });
    }

    public static DesktopGuardState CancelRename(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.Renaming)
        {
            throw new InvalidOperationException("No rename operation is in progress for this shortcut.");
        }

        return Replace(state, shortcut with
        {
            Operation = ShortcutOperation.None,
            OperationId = null,
            TargetName = null,
        });
    }

    public static DesktopGuardState BeginDelete(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.None)
        {
            throw new InvalidOperationException("This shortcut cannot be deleted in its current state.");
        }

        return Replace(state, shortcut with
        {
            Operation = shortcut.Status == ShortcutStatus.Parked
                ? ShortcutOperation.DeletingPermanently
                : ShortcutOperation.DeletingToRecycleBin,
            OperationId = Guid.NewGuid(),
        });
    }

    public static DesktopGuardState CompleteDelete(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation is not (ShortcutOperation.DeletingToRecycleBin or ShortcutOperation.DeletingPermanently))
        {
            throw new InvalidOperationException("No delete operation is in progress for this shortcut.");
        }

        List<DesktopRootCheckpoint> roots = state.Roots;
        if (shortcut.Operation == ShortcutOperation.DeletingToRecycleBin)
        {
            roots = state.Roots
                .Select(root => root.RootKind == shortcut.RootKind && PathsEqual(root.RootPath, shortcut.RootPath)
                    ? root with { KnownPaths = root.KnownPaths.Where(path => !PathsEqual(path, shortcut.OriginalPath)).ToArray() }
                    : root)
                .ToList();
        }

        return state with
        {
            Roots = roots,
            Shortcuts = state.Shortcuts.Where(item => item.Id != shortcutId).ToList(),
        };
    }

    public static DesktopGuardState CancelDelete(DesktopGuardState state, Guid shortcutId)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation is not (ShortcutOperation.DeletingToRecycleBin or ShortcutOperation.DeletingPermanently))
        {
            throw new InvalidOperationException("No delete operation is in progress for this shortcut.");
        }

        return Replace(state, shortcut with
        {
            Operation = ShortcutOperation.None,
            OperationId = null,
        });
    }

    public static DesktopGuardState SetHidden(DesktopGuardState state, Guid shortcutId, bool isHidden)
    {
        TrackedShortcut shortcut = Find(state, shortcutId);
        if (shortcut.Operation != ShortcutOperation.None)
        {
            throw new InvalidOperationException("This shortcut cannot be updated in its current state.");
        }

        return Replace(state, shortcut with { IsHidden = isHidden });
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
            IsHidden = false,
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
            IsHidden = false,
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

    public static DesktopGuardState SetStartMinimized(DesktopGuardState state, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with { StartMinimized = enabled };
    }

    private static TrackedShortcut Find(DesktopGuardState state, Guid shortcutId)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Shortcuts.FirstOrDefault(shortcut => shortcut.Id == shortcutId)
            ?? throw new KeyNotFoundException("The shortcut is no longer in the current inventory.");
    }

    private static bool IsValidName(string? name)
    {
        return name is not null
            && DesktopCandidatePolicy.IsValidWindowsFileName(name)
            && string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
            && DesktopEntryClassifier.Classify(name) == DesktopEntryKind.Shortcut;
    }

    private static DesktopGuardState Replace(DesktopGuardState state, TrackedShortcut updatedShortcut)
    {
        List<TrackedShortcut> shortcuts = state.Shortcuts
            .Select(shortcut => shortcut.Id == updatedShortcut.Id ? updatedShortcut : shortcut)
            .ToList();
        return state with { Shortcuts = shortcuts };
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left, right, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }
}
