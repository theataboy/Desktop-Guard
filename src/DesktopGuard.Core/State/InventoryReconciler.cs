using System.IO;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;

namespace DesktopGuard.Core.State;

public sealed class InventoryReconciler
{
    public const int MaximumEventCount = 5000;
    public static readonly TimeSpan EventRetention = TimeSpan.FromDays(90);

    public InventoryReconciliation Reconcile(
        DesktopGuardState state,
        DesktopInventorySnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);

        nowUtc = nowUtc.ToUniversalTime();
        List<DesktopRootCheckpoint> roots = [.. state.Roots];
        List<TrackedShortcut> shortcuts = [.. state.Shortcuts];
        List<DesktopEvent> events = state.Events
            .Where(item => item.TimestampUtc >= nowUtc - EventRetention && item.TimestampUtc <= nowUtc)
            .ToList();
        List<TrackedShortcut> newUserShortcuts = [];
        List<TrackedShortcut> newPublicShortcuts = [];
        List<DesktopEvent> newEvents = [];

        foreach (DesktopRootSnapshot rootSnapshot in snapshot.Roots)
        {
            if (!rootSnapshot.IsComplete
                || !Enum.IsDefined(rootSnapshot.RootKind)
                || !Path.IsPathFullyQualified(rootSnapshot.RootPath))
            {
                continue;
            }

            List<DesktopInventoryEntry> currentEntries = rootSnapshot.Entries
                .Where(entry => IsValidEntry(rootSnapshot, entry))
                .OrderBy(entry => entry.Path, GetPathComparer())
                .ToList();
            DesktopRootCheckpoint? existingRoot = roots.FirstOrDefault(
                root => root.RootKind == rootSnapshot.RootKind);
            bool sameRoot = existingRoot is not null
                && PathsEqual(existingRoot.RootPath, rootSnapshot.RootPath);
            if (existingRoot is not null && !sameRoot)
            {
                // A changed known-folder location is ambiguous. Preserve its prior checkpoint and decisions.
                continue;
            }

            bool needsBaseline = !sameRoot || existingRoot is not { IsBaselined: true };

            if (needsBaseline)
            {
                roots.RemoveAll(root => root.RootKind == rootSnapshot.RootKind);
                roots.Add(new DesktopRootCheckpoint(
                    rootSnapshot.RootKind,
                    rootSnapshot.RootPath,
                    IsBaselined: true,
                    currentEntries.Select(entry => entry.Path).Distinct(GetPathComparer()).ToArray()));

                if (rootSnapshot.RootKind is DesktopRootKind.User or DesktopRootKind.Public)
                {
                    shortcuts.RemoveAll(shortcut => shortcut.RootKind == rootSnapshot.RootKind
                            && PathsEqual(shortcut.RootPath, rootSnapshot.RootPath)
                            && shortcut.Operation == ShortcutOperation.None
                            && shortcut.Status != ShortcutStatus.Parked);

                    foreach (DesktopInventoryEntry entry in currentEntries.Where(
                        entry => entry.Kind == DesktopEntryKind.Shortcut))
                    {
                        if (FindActiveShortcut(shortcuts, entry) is null)
                        {
                            shortcuts.Add(CreateShortcut(entry, ShortcutStatus.ApprovedVisible));
                        }
                    }
                }

                continue;
            }

            HashSet<string> previouslyKnownPaths = existingRoot!.KnownPaths.ToHashSet(GetPathComparer());
            HashSet<string> currentPaths = currentEntries.Select(entry => entry.Path).ToHashSet(GetPathComparer());

            foreach (DesktopInventoryEntry entry in currentEntries)
            {
                bool isNew = !previouslyKnownPaths.Contains(entry.Path);
                TrackedShortcut? activeShortcut = FindActiveShortcut(shortcuts, entry);

                if (isNew)
                {
                    DesktopEvent desktopEvent = new(entry.Name, entry.Path, nowUtc);
                    events.Add(desktopEvent);
                    newEvents.Add(desktopEvent);

                    if (entry.Kind == DesktopEntryKind.Shortcut)
                    {
                        if (activeShortcut is null)
                        {
                            TrackedShortcut shortcut = CreateShortcut(entry, ShortcutStatus.Pending);
                            shortcuts.Add(shortcut);
                            if (rootSnapshot.RootKind == DesktopRootKind.User)
                            {
                                newUserShortcuts.Add(shortcut);
                            }
                            else
                            {
                                newPublicShortcuts.Add(shortcut);
                            }
                        }
                    }
                }
                else if (entry.Kind == DesktopEntryKind.Shortcut
                    && activeShortcut is null)
                {
                    // A path in the saved baseline is approved; an untracked path needs review.
                    TrackedShortcut shortcut = CreateShortcut(entry, ShortcutStatus.ApprovedVisible);
                    shortcuts.Add(shortcut);
                }
            }

            shortcuts.RemoveAll(shortcut =>
                shortcut.RootKind == rootSnapshot.RootKind
                    && PathsEqual(shortcut.RootPath, rootSnapshot.RootPath)
                    && shortcut.Status != ShortcutStatus.Parked
                    && shortcut.Operation == ShortcutOperation.None
                    && !currentPaths.Contains(shortcut.OriginalPath));

            roots.Remove(existingRoot);
            roots.Add(existingRoot with
            {
                IsBaselined = true,
                KnownPaths = currentPaths.ToArray(),
            });
        }

        if (events.Count > MaximumEventCount)
        {
            events = events
                .OrderByDescending(item => item.TimestampUtc)
                .Take(MaximumEventCount)
                .OrderBy(item => item.TimestampUtc)
                .ToList();
        }

        DesktopGuardState nextState = state with
        {
            Roots = roots,
            Shortcuts = shortcuts,
            Events = events,
        };

        return new InventoryReconciliation(nextState, newUserShortcuts, newPublicShortcuts, newEvents);
    }

    private static TrackedShortcut CreateShortcut(DesktopInventoryEntry entry, ShortcutStatus status)
    {
        return new TrackedShortcut(
            Id: Guid.NewGuid(),
            RootKind: entry.RootKind,
            RootPath: entry.RootPath,
            Name: entry.Name,
            OriginalPath: entry.Path,
            Status: status,
            StatusBeforeParking: null,
            Operation: ShortcutOperation.None,
            RecoveryFolderId: null);
    }

    private static TrackedShortcut? FindActiveShortcut(
        IEnumerable<TrackedShortcut> shortcuts,
        DesktopInventoryEntry entry)
    {
        return shortcuts.FirstOrDefault(shortcut =>
            shortcut.Status != ShortcutStatus.Parked
                && shortcut.RootKind == entry.RootKind
                && PathsEqual(shortcut.RootPath, entry.RootPath)
                && PathsEqual(shortcut.OriginalPath, entry.Path)
            || shortcut.Operation == ShortcutOperation.Moving
                && shortcut.TargetRootKind == entry.RootKind
                && PathsEqual(shortcut.TargetRootPath!, entry.RootPath)
                && PathsEqual(Path.Combine(shortcut.TargetRootPath!, shortcut.Name), entry.Path));
    }

    private static bool IsValidEntry(DesktopRootSnapshot rootSnapshot, DesktopInventoryEntry entry)
    {
        return entry.RootKind == rootSnapshot.RootKind
            && PathsEqual(entry.RootPath, rootSnapshot.RootPath)
            && DesktopCandidatePolicy.IsValidWindowsFileName(entry.Name)
            && PathsEqual(Path.GetFileName(entry.Path), entry.Name)
            && DesktopCandidatePolicy.IsDirectChildOf(rootSnapshot.RootPath, entry.Path)
            && DesktopEntryClassifier.Classify(entry.Name) == entry.Kind;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left, right, GetPathComparison());
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}
