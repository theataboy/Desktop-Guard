using System.IO;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;

namespace DesktopGuard.Core.State;

public static class DesktopStateValidator
{
    private const int MaximumRoots = 2;
    private const int MaximumShortcuts = 8192;

    public static bool IsValid(DesktopGuardState state)
    {
        return IsValidForVersion(
            state,
            DesktopGuardState.CurrentSchemaVersion,
            legacyVersionOne: false,
            allowCreating: true);
    }

    public static bool IsValidVersionOne(DesktopGuardState state)
    {
        return IsValidForVersion(state, 1, legacyVersionOne: true, allowCreating: false);
    }

    public static bool IsValidVersionTwo(DesktopGuardState state)
    {
        return IsValidForVersion(state, 2, legacyVersionOne: false, allowCreating: false);
    }

    private static bool IsValidForVersion(
        DesktopGuardState state,
        int schemaVersion,
        bool legacyVersionOne,
        bool allowCreating)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Roots is null
            || state.Shortcuts is null
            || state.Events is null
            || state.SchemaVersion != schemaVersion
            || state.Roots.Count > MaximumRoots
            || state.Shortcuts.Count > MaximumShortcuts
            || state.Events.Count > InventoryReconciler.MaximumEventCount)
        {
            return false;
        }

        if (state.Roots.Any(root => root is null || !IsValidRoot(root))
            || state.Roots.Select(root => root.RootKind).Distinct().Count() != state.Roots.Count)
        {
            return false;
        }

        if (state.Shortcuts.Any(shortcut => shortcut is null || !IsValidShortcut(shortcut, legacyVersionOne, allowCreating))
            || state.Shortcuts.Select(shortcut => shortcut.Id).Distinct().Count() != state.Shortcuts.Count)
        {
            return false;
        }

        if (state.Shortcuts.Any(shortcut => !state.Roots.Any(root =>
                    root.RootKind == shortcut.RootKind && PathsEqual(root.RootPath, shortcut.RootPath))
                || shortcut.Operation == ShortcutOperation.Moving
                    && !state.Roots.Any(root =>
                        root.RootKind == shortcut.TargetRootKind
                            && PathsEqual(root.RootPath, shortcut.TargetRootPath!)))
            || state.Shortcuts
                .Where(shortcut => shortcut.Status != ShortcutStatus.Parked
                    || shortcut.Operation != ShortcutOperation.None)
                .Select(shortcut => shortcut.OriginalPath)
                .Distinct(GetPathComparer())
                .Count() != state.Shortcuts.Count(shortcut =>
                    shortcut.Status != ShortcutStatus.Parked
                        || shortcut.Operation != ShortcutOperation.None)
            || state.Shortcuts
                .Where(shortcut => shortcut.RecoveryFolderId is not null)
                .Select(shortcut => shortcut.RecoveryFolderId)
                .Distinct()
                .Count() != state.Shortcuts.Count(shortcut => shortcut.RecoveryFolderId is not null)
            || state.Shortcuts
                .Where(shortcut => shortcut.Operation == ShortcutOperation.Moving)
                .Select(shortcut => Path.Combine(shortcut.TargetRootPath!, shortcut.Name))
                .Distinct(GetPathComparer())
                .Count() != state.Shortcuts.Count(shortcut => shortcut.Operation == ShortcutOperation.Moving)
            || !legacyVersionOne && state.Shortcuts
                .Where(shortcut => shortcut.Operation != ShortcutOperation.None)
                .Select(shortcut => shortcut.OperationId)
                .Distinct()
                .Count() != state.Shortcuts.Count(shortcut => shortcut.Operation != ShortcutOperation.None))
        {
            return false;
        }

        return state.Events.All(desktopEvent => desktopEvent is not null && IsValidEvent(desktopEvent));
    }

    private static bool IsValidRoot(DesktopRootCheckpoint root)
    {
        return Enum.IsDefined(root.RootKind)
            && IsBoundedAbsolutePath(root.RootPath)
            && root.KnownPaths is not null
            && root.KnownPaths.Count <= 8192
            && root.KnownPaths.All(path =>
                IsBoundedAbsolutePath(path)
                    && DesktopCandidatePolicy.IsValidWindowsFileName(Path.GetFileName(path))
                    && DesktopCandidatePolicy.IsDirectChildOf(root.RootPath, path));
    }

    private static bool IsValidShortcut(TrackedShortcut shortcut, bool legacyVersionOne, bool allowCreating)
    {
        bool isParked = shortcut.Status == ShortcutStatus.Parked;
        bool hasRecoveryFolder = shortcut.RecoveryFolderId is not null;
        bool previousStatusIsValid = shortcut.StatusBeforeParking is null
            || shortcut.StatusBeforeParking is ShortcutStatus.Pending or ShortcutStatus.ApprovedVisible;
        bool operationIsValid = shortcut.Operation switch
        {
            ShortcutOperation.None => isParked == hasRecoveryFolder
                && (isParked == (shortcut.StatusBeforeParking is not null))
                && shortcut.TargetRootKind is null
                && shortcut.TargetRootPath is null
                && shortcut.OperationId is null
                && shortcut.OperationContentHash is null,
            ShortcutOperation.Parking => hasRecoveryFolder
                && !isParked
                && shortcut.StatusBeforeParking is null
                && shortcut.TargetRootKind is null
                && shortcut.TargetRootPath is null
                && shortcut.OperationContentHash is null
                && (legacyVersionOne
                    ? shortcut.OperationId is null
                    : shortcut.OperationId == shortcut.RecoveryFolderId),
            ShortcutOperation.Restoring => hasRecoveryFolder
                && isParked
                && shortcut.StatusBeforeParking is not null
                && shortcut.TargetRootKind is null
                && shortcut.TargetRootPath is null
                && shortcut.OperationContentHash is null
                && (legacyVersionOne
                    ? shortcut.OperationId is null
                    : shortcut.OperationId is not null && shortcut.OperationId != Guid.Empty),
            ShortcutOperation.Moving => !isParked
                && !hasRecoveryFolder
                && shortcut.StatusBeforeParking is null
                && shortcut.OperationId is not null
                && shortcut.OperationId != Guid.Empty
                && IsValidFileName(shortcut.Name)
                && shortcut.TargetRootKind is DesktopRootKind targetRootKind
                && Enum.IsDefined(targetRootKind)
                && targetRootKind != shortcut.RootKind
                && shortcut.OperationContentHash is null
                && IsBoundedAbsolutePath(shortcut.TargetRootPath)
                && DesktopCandidatePolicy.IsDirectChildOf(
                    shortcut.TargetRootPath!,
                    Path.Combine(shortcut.TargetRootPath!, shortcut.Name)),
            ShortcutOperation.Creating => allowCreating
                && shortcut.RootKind == DesktopRootKind.User
                && string.Equals(shortcut.Name, ShortcutStateTransitions.DesktopShortcutName, StringComparison.Ordinal)
                && shortcut.Status == ShortcutStatus.ApprovedVisible
                && !isParked
                && !hasRecoveryFolder
                && shortcut.StatusBeforeParking is null
                && shortcut.TargetRootKind is null
                && shortcut.TargetRootPath is null
                && shortcut.OperationId is not null
                && shortcut.OperationId != Guid.Empty
                && (shortcut.OperationContentHash is null || shortcut.OperationContentHash.Length == 32),
            _ => false,
        };

        return shortcut.Id != Guid.Empty
            && Enum.IsDefined(shortcut.RootKind)
            && (!legacyVersionOne || shortcut.RootKind == DesktopRootKind.User)
            && (!legacyVersionOne
                || shortcut.Operation != ShortcutOperation.Moving
                    && shortcut.TargetRootKind is null
                    && shortcut.TargetRootPath is null
                    && shortcut.OperationId is null)
            && (!legacyVersionOne || shortcut.OperationContentHash is null)
            && shortcut.Status is ShortcutStatus.Pending or ShortcutStatus.ApprovedVisible or ShortcutStatus.Parked
            && previousStatusIsValid
            && Enum.IsDefined(shortcut.Operation)
            && IsBoundedAbsolutePath(shortcut.RootPath)
            && IsValidFileName(shortcut.Name)
            && DesktopEntryClassifier.Classify(shortcut.Name) == DesktopEntryKind.Shortcut
            && IsBoundedAbsolutePath(shortcut.OriginalPath)
            && DesktopCandidatePolicy.IsDirectChildOf(shortcut.RootPath, shortcut.OriginalPath)
            && string.Equals(
                Path.GetFileName(shortcut.OriginalPath),
                shortcut.Name,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && operationIsValid;
    }

    private static bool IsValidEvent(DesktopEvent desktopEvent)
    {
        return IsValidFileName(desktopEvent.FileName)
            && IsBoundedAbsolutePath(desktopEvent.Path)
            && string.Equals(
                Path.GetFileName(desktopEvent.Path),
                desktopEvent.FileName,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && desktopEvent.TimestampUtc.Offset == TimeSpan.Zero;
    }

    private static bool IsValidFileName(string? name)
    {
        return DesktopCandidatePolicy.IsValidWindowsFileName(name)
            && string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal);
    }

    private static bool IsBoundedAbsolutePath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && path.Length <= 32767
            && !path.Any(char.IsControl)
            && Path.IsPathFullyQualified(path);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }
}
