namespace DesktopGuard.Core.Shortcuts;

public static class RecoveryDuplicatePolicy
{
    public static IReadOnlyList<(Guid DuplicateId, Guid KeeperId)> FindDuplicates(
        IReadOnlyList<TrackedShortcut> shortcuts,
        IReadOnlyList<RecoveryShortcutSnapshot> snapshots)
    {
        Dictionary<Guid, RecoveryShortcutSnapshot> byId = snapshots.ToDictionary(snapshot => snapshot.ShortcutId);
        return shortcuts
            .Where(shortcut => shortcut.Status == ShortcutStatus.Parked
                && shortcut.Operation == ShortcutOperation.None
                && shortcut.ShortcutIdentityHash is { Length: 32 } hash
                && byId.TryGetValue(shortcut.Id, out RecoveryShortcutSnapshot? snapshot)
                && hash.AsSpan().SequenceEqual(snapshot.IdentityHash))
            .GroupBy(shortcut => new
            {
                shortcut.RootKind,
                RootPath = OperatingSystem.IsWindows() ? shortcut.RootPath.ToUpperInvariant() : shortcut.RootPath,
                Identity = Convert.ToHexString(shortcut.ShortcutIdentityHash!),
                shortcut.IsDesktopGuardShortcut,
            })
            .SelectMany(group =>
            {
                // OrderBy is stable: equal timestamps retain the first stored record.
                TrackedShortcut[] ordered = group.OrderByDescending(shortcut => byId[shortcut.Id].LastWriteUtc).ToArray();
                return ordered.Skip(1).Select(shortcut => (DuplicateId: shortcut.Id, KeeperId: ordered[0].Id));
            })
            .ToArray();
    }
}
