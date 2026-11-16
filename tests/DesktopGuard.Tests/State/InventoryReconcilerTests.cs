using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;
using DesktopGuard.Core.State;

namespace DesktopGuard.Tests.State;

[TestClass]
public sealed class InventoryReconcilerTests
{
    private readonly InventoryReconciler _reconciler = new();

    [TestMethod]
    public void Reconcile_FirstCompleteScanCreatesBaselineWithoutNewEvents()
    {
        string userRoot = CreateRootPath("user");
        string publicRoot = CreateRootPath("public");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopInventorySnapshot snapshot = new(
        [
            Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Existing.lnk"),
                Entry(DesktopRootKind.User, userRoot, "Notes.txt")),
            Root(DesktopRootKind.Public, publicRoot, complete: true,
                Entry(DesktopRootKind.Public, publicRoot, "Shared.url")),
        ]);

        InventoryReconciliation result = _reconciler.Reconcile(new DesktopGuardState(), snapshot, now);

        TrackedShortcut shortcut = result.State.Shortcuts.Single(item => item.RootKind == DesktopRootKind.User);
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, shortcut.Status);
        Assert.AreEqual(DesktopRootKind.User, shortcut.RootKind);
        TrackedShortcut publicShortcut = result.State.Shortcuts.Single(item => item.RootKind == DesktopRootKind.Public);
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, publicShortcut.Status);
        Assert.IsEmpty(result.NewUserShortcuts);
        Assert.IsEmpty(result.NewPublicShortcuts);
        Assert.IsEmpty(result.NewEvents);
        Assert.HasCount(2, result.State.Roots);
        Assert.IsTrue(result.State.Roots.All(root => root.IsBaselined));
    }

    [TestMethod]
    public void Reconcile_UpdatesHiddenStateForTrackedShortcut()
    {
        string userRoot = CreateRootPath("hidden-shortcut");
        DesktopInventoryEntry hiddenEntry = Entry(DesktopRootKind.User, userRoot, "Hidden.lnk", isHidden: true);
        DesktopGuardState baseline = _reconciler.Reconcile(
            new DesktopGuardState(),
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true, hiddenEntry)]),
            DateTimeOffset.UtcNow).State;
        TrackedShortcut original = baseline.Shortcuts.Single();
        DesktopInventorySnapshot visibleSnapshot = new(
        [
            Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Hidden.lnk", isHidden: false)),
        ]);

        InventoryReconciliation result = _reconciler.Reconcile(
            baseline,
            visibleSnapshot,
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.AreNotEqual(false, original.IsHidden);
        Assert.IsFalse(result.State.Shortcuts.Single().IsHidden);
    }

    [TestMethod]
    public void Reconcile_ExternalCaseOnlyRenameKeepsDecisionAndUpdatesExactName()
    {
        string userRoot = CreateRootPath("case-only-rename");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(
            new DesktopGuardState(),
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.User, userRoot, complete: true,
                    Entry(DesktopRootKind.User, userRoot, "Program.lnk")),
            ]),
            now).State;
        TrackedShortcut original = baseline.Shortcuts.Single() with { Status = ShortcutStatus.Pending };
        baseline = baseline with { Shortcuts = [original] };

        InventoryReconciliation result = _reconciler.Reconcile(
            baseline,
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.User, userRoot, complete: true,
                    Entry(DesktopRootKind.User, userRoot, "PROGRAM.lnk")),
            ]),
            now.AddMinutes(1));

        TrackedShortcut updated = result.State.Shortcuts.Single();
        Assert.AreEqual(original.Id, updated.Id);
        Assert.AreEqual(ShortcutStatus.Pending, updated.Status);
        Assert.AreEqual("PROGRAM.lnk", updated.Name);
        Assert.AreEqual(Path.Combine(userRoot, "PROGRAM.lnk"), updated.OriginalPath);
        Assert.IsEmpty(result.NewEvents);
        Assert.IsEmpty(result.NewUserShortcuts);
        Assert.IsTrue(DesktopStateValidator.IsValid(result.State));
        DesktopGuardState renaming = ShortcutStateTransitions.BeginRename(result.State, updated.Id, "program.lnk");
        Assert.IsTrue(DesktopStateValidator.IsValid(renaming));
    }

    [TestMethod]
    public void Reconcile_NewUserAndPublicShortcutsArePendingAndAllNewEntriesAreLogged()
    {
        string userRoot = CreateRootPath("user");
        string publicRoot = CreateRootPath("public");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(
            new DesktopGuardState(),
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.User, userRoot, complete: true,
                    Entry(DesktopRootKind.User, userRoot, "Existing.lnk")),
                Root(DesktopRootKind.Public, publicRoot, complete: true,
                    Entry(DesktopRootKind.Public, publicRoot, "Shared.url")),
            ]),
            now).State;

        InventoryReconciliation result = _reconciler.Reconcile(
            baseline,
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.User, userRoot, complete: true,
                    Entry(DesktopRootKind.User, userRoot, "Existing.lnk"),
                    Entry(DesktopRootKind.User, userRoot, "New.url"),
                    Entry(DesktopRootKind.User, userRoot, "New note.txt")),
                Root(DesktopRootKind.Public, publicRoot, complete: true,
                    Entry(DesktopRootKind.Public, publicRoot, "Shared.url"),
                    Entry(DesktopRootKind.Public, publicRoot, "New shared.lnk")),
            ]),
            now.AddMinutes(1));

        Assert.AreEqual(ShortcutStatus.Pending, result.NewUserShortcuts.Single().Status);
        Assert.AreEqual("New.url", result.NewUserShortcuts.Single().Name);
        Assert.AreEqual("New shared.lnk", result.NewPublicShortcuts.Single().Name);
        Assert.AreEqual(ShortcutStatus.Pending, result.NewPublicShortcuts.Single().Status);
        Assert.HasCount(4, result.State.Shortcuts);
        Assert.HasCount(3, result.NewEvents);
        Assert.IsTrue(result.NewEvents.Any(item => item.FileName == "New note.txt"));
        Assert.HasCount(2, result.State.Shortcuts.Where(item => item.RootKind == DesktopRootKind.Public));
    }

    [TestMethod]
    public void Reconcile_RestoredShortcutKeepsItsExistingDecisionAndCheckpointAfterRestore()
    {
        string userRoot = CreateRootPath("restored-shortcut");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(
            new DesktopGuardState(),
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.User, userRoot, complete: true,
                    Entry(DesktopRootKind.User, userRoot, "Existing.lnk")),
            ]),
            now).State;
        TrackedShortcut original = baseline.Shortcuts.Single();
        DesktopGuardState parking = ShortcutStateTransitions.BeginParking(baseline, original.Id, Guid.NewGuid());
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(parking, original.Id);
        DesktopGuardState afterParkedScan = _reconciler.Reconcile(
            parked,
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.User, userRoot, complete: true),
            ]),
            now.AddMinutes(1)).State;
        DesktopGuardState restoring = ShortcutStateTransitions.BeginRestore(afterParkedScan, original.Id);
        DesktopGuardState restored = ShortcutStateTransitions.CompleteRestore(restoring, original.Id);

        InventoryReconciliation result = _reconciler.Reconcile(
            restored,
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.User, userRoot, complete: true,
                    Entry(DesktopRootKind.User, userRoot, "Existing.lnk")),
            ]),
            now.AddMinutes(2));

        Assert.AreEqual(original.Id, result.State.Shortcuts.Single().Id);
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, result.State.Shortcuts.Single().Status);
        Assert.IsEmpty(result.NewUserShortcuts);
        Assert.IsEmpty(result.NewEvents);
        Assert.Contains(Path.Combine(userRoot, "Existing.lnk"), result.State.Roots.Single().KnownPaths);
    }

    [TestMethod]
    public void Reconcile_UnresolvedRestoreClaimsItsOriginalPathWithoutCreatingDuplicate()
    {
        string userRoot = CreateRootPath("unresolved-restore");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(
            new DesktopGuardState(),
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Restore.lnk"))]),
            now).State;
        TrackedShortcut original = baseline.Shortcuts.Single();
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(
            ShortcutStateTransitions.BeginParking(baseline, original.Id, Guid.NewGuid()), original.Id);
        DesktopGuardState afterPark = _reconciler.Reconcile(
            parked,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true)]),
            now.AddMinutes(1)).State;
        DesktopGuardState restoring = ShortcutStateTransitions.BeginRestore(afterPark, original.Id);

        InventoryReconciliation result = _reconciler.Reconcile(
            restoring,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Restore.lnk"))]),
            now.AddMinutes(2));

        Assert.AreEqual(original.Id, result.State.Shortcuts.Single().Id);
        Assert.AreEqual(ShortcutOperation.Restoring, result.State.Shortcuts.Single().Operation);
        Assert.IsTrue(DesktopStateValidator.IsValid(result.State));
        Assert.DoesNotContain(Path.Combine(userRoot, "Restore.lnk"), result.State.Roots.Single().KnownPaths);
    }

    [TestMethod]
    public void Reconcile_UnresolvedRenameClaimsItsTargetPathWithoutCreatingDuplicate()
    {
        string userRoot = CreateRootPath("unresolved-rename");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(
            new DesktopGuardState(),
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Before.lnk"))]),
            now).State;
        TrackedShortcut original = baseline.Shortcuts.Single();
        DesktopGuardState renaming = ShortcutStateTransitions.BeginRename(baseline, original.Id, "After.lnk");

        InventoryReconciliation result = _reconciler.Reconcile(
            renaming,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Before.lnk"),
                Entry(DesktopRootKind.User, userRoot, "After.lnk"))]),
            now.AddMinutes(1));

        Assert.HasCount(1, result.State.Shortcuts);
        Assert.AreEqual(original.Id, result.State.Shortcuts.Single().Id);
        Assert.AreEqual(ShortcutOperation.Renaming, result.State.Shortcuts.Single().Operation);
        Assert.IsTrue(DesktopStateValidator.IsValid(result.State));
        Assert.DoesNotContain(Path.Combine(userRoot, "After.lnk"), result.State.Roots.Single().KnownPaths);
    }

    [TestMethod]
    public void Reconcile_UnresolvedMoveTargetRemainsUnbaselinedUntilOperationIsResolved()
    {
        string userRoot = CreateRootPath("unresolved-move-user");
        string publicRoot = CreateRootPath("unresolved-move-public");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopInventorySnapshot initialSnapshot = new([
            Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Moving.lnk")),
            Root(DesktopRootKind.Public, publicRoot, complete: true),
        ]);
        DesktopGuardState baseline = _reconciler.Reconcile(new DesktopGuardState(), initialSnapshot, now).State;
        TrackedShortcut original = baseline.Shortcuts.Single();
        DesktopGuardState moving = ShortcutStateTransitions.BeginMove(
            baseline, original.Id, DesktopRootKind.Public, publicRoot);
        DesktopInventorySnapshot conflictingSnapshot = new([
            Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Moving.lnk")),
            Root(DesktopRootKind.Public, publicRoot, complete: true,
                Entry(DesktopRootKind.Public, publicRoot, "Moving.lnk")),
        ]);

        InventoryReconciliation unresolved = _reconciler.Reconcile(
            moving, conflictingSnapshot, now.AddMinutes(1));

        Assert.IsTrue(DesktopStateValidator.IsValid(unresolved.State));
        Assert.HasCount(1, unresolved.State.Shortcuts);
        Assert.IsEmpty(unresolved.NewPublicShortcuts);
        Assert.IsEmpty(unresolved.NewEvents);
        Assert.DoesNotContain(Path.Combine(publicRoot, "Moving.lnk"),
            unresolved.State.Roots.Single(root => root.RootKind == DesktopRootKind.Public).KnownPaths);

        DesktopGuardState canceled = ShortcutStateTransitions.CancelMove(unresolved.State, original.Id);
        InventoryReconciliation afterCancel = _reconciler.Reconcile(
            canceled, conflictingSnapshot, now.AddMinutes(2));

        Assert.AreEqual(ShortcutStatus.Pending, afterCancel.NewPublicShortcuts.Single().Status);
        Assert.IsTrue(DesktopStateValidator.IsValid(afterCancel.State));
    }

    [TestMethod]
    public void Reconcile_RecreatedShortcutAfterParkingIsPending()
    {
        string userRoot = CreateRootPath("recreated-after-parking");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(
            new DesktopGuardState(),
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Parked.lnk"))]),
            now).State;
        TrackedShortcut original = baseline.Shortcuts.Single();
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(
            ShortcutStateTransitions.BeginParking(baseline, original.Id, Guid.NewGuid()), original.Id);

        InventoryReconciliation result = _reconciler.Reconcile(
            parked,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Parked.lnk"))]),
            now.AddMinutes(1));

        TrackedShortcut replacement = result.State.Shortcuts.Single(shortcut => shortcut.Id != original.Id);
        Assert.AreEqual(ShortcutStatus.Pending, replacement.Status);
        Assert.Contains(Path.Combine(userRoot, "Parked.lnk"), result.NewUserShortcuts.Select(item => item.OriginalPath));
    }

    [TestMethod]
    public void Reconcile_IncompleteRootDoesNotChangeItsBaselineOrDecisions()
    {
        string userRoot = CreateRootPath("user");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(
            new DesktopGuardState(),
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.User, userRoot, complete: true,
                    Entry(DesktopRootKind.User, userRoot, "Existing.lnk")),
            ]),
            now).State;
        DesktopRootCheckpoint originalRoot = baseline.Roots.Single();

        InventoryReconciliation result = _reconciler.Reconcile(
            baseline,
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.User, userRoot, complete: false,
                    Entry(DesktopRootKind.User, userRoot, "Existing.lnk"),
                    Entry(DesktopRootKind.User, userRoot, "Unverified.lnk")),
            ]),
            now.AddMinutes(1));

        Assert.AreEqual(originalRoot, result.State.Roots.Single());
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, result.State.Shortcuts.Single().Status);
        Assert.IsEmpty(result.NewUserShortcuts);
        Assert.IsEmpty(result.NewEvents);
    }

    [TestMethod]
    public void Reconcile_ChangedUserDesktopPathPreservesPriorBaselineAndDecisions()
    {
        string originalRoot = CreateRootPath("user-original");
        string changedRoot = CreateRootPath("user-changed");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(
            new DesktopGuardState(),
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.User, originalRoot, complete: true,
                    Entry(DesktopRootKind.User, originalRoot, "Existing.lnk")),
            ]),
            now).State;

        InventoryReconciliation result = _reconciler.Reconcile(
            baseline,
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.User, changedRoot, complete: true,
                    Entry(DesktopRootKind.User, changedRoot, "Unreviewed.lnk")),
            ]),
            now.AddMinutes(1));

        Assert.AreEqual(originalRoot, result.State.Roots.Single().RootPath);
        Assert.AreEqual("Existing.lnk", result.State.Shortcuts.Single().Name);
        Assert.IsEmpty(result.NewUserShortcuts);
        Assert.IsEmpty(result.NewEvents);
    }

    [TestMethod]
    public void Reconcile_ChangedPublicDesktopPathPreservesCheckpointAndBlocksNewManagement()
    {
        string originalRoot = CreateRootPath("public-original");
        string changedRoot = CreateRootPath("public-changed");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(
            new DesktopGuardState(),
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.Public, originalRoot, complete: true,
                    Entry(DesktopRootKind.Public, originalRoot, "Existing.url")),
            ]),
            now).State;

        InventoryReconciliation result = _reconciler.Reconcile(
            baseline,
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.Public, changedRoot, complete: true,
                    Entry(DesktopRootKind.Public, changedRoot, "Unreviewed.lnk")),
            ]),
            now.AddMinutes(1));

        Assert.AreEqual(originalRoot, result.State.Roots.Single().RootPath);
        Assert.AreEqual("Existing.url", result.State.Shortcuts.Single().Name);
        Assert.IsEmpty(result.NewPublicShortcuts);
        Assert.IsEmpty(result.NewEvents);
    }

    [TestMethod]
    public void Reconcile_WhenTrackingCapacityWouldBeExceededDoesNotDiscardParkedRecoveryRecords()
    {
        string root = CreateRootPath("capacity");
        List<TrackedShortcut> parkedShortcuts = Enumerable.Range(0, 8192)
            .Select(index =>
            {
                string name = $"Parked-{index}.lnk";
                return new TrackedShortcut(
                    Guid.NewGuid(),
                    DesktopRootKind.User,
                    root,
                    name,
                    Path.Combine(root, name),
                    ShortcutStatus.Parked,
                    StatusBeforeParking: ShortcutStatus.ApprovedVisible,
                    Operation: ShortcutOperation.None,
                    RecoveryFolderId: Guid.NewGuid());
            })
            .ToList();
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.User, root, IsBaselined: true, KnownPaths: []),
            ],
            Shortcuts = parkedShortcuts,
        };

        InventoryReconciliation result = _reconciler.Reconcile(
            state,
            new DesktopInventorySnapshot(
            [
                Root(DesktopRootKind.User, root, complete: true,
                    Entry(DesktopRootKind.User, root, "New.lnk")),
            ]),
            DateTimeOffset.UtcNow);

        Assert.HasCount(8193, result.State.Shortcuts);
        Assert.IsTrue(result.State.Shortcuts.Any(shortcut => shortcut.Id == parkedShortcuts[0].Id));
    }

    [TestMethod]
    public void Reconcile_PrunesEventsOutsideNinetyDayRetentionAndFutureTimestamps()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string root = CreateRootPath("events");
        DesktopGuardState state = new()
        {
            Events =
            [
                Event(root, "Old.lnk", now.AddDays(-91)),
                Event(root, "Boundary.lnk", now.AddDays(-90)),
                Event(root, "Future.lnk", now.AddSeconds(1)),
            ],
        };

        InventoryReconciliation result = _reconciler.Reconcile(state, new DesktopInventorySnapshot([]), now);

        Assert.AreEqual("Boundary.lnk", result.State.Events.Single().FileName);
    }

    private static DesktopRootSnapshot Root(
        DesktopRootKind kind,
        string rootPath,
        bool complete,
        params DesktopInventoryEntry[] entries)
    {
        return new DesktopRootSnapshot(kind, rootPath, complete, entries);
    }

    private static DesktopInventoryEntry Entry(
        DesktopRootKind kind,
        string rootPath,
        string name,
        bool isHidden = false)
    {
        return new DesktopInventoryEntry(
            kind,
            rootPath,
            Path.Combine(rootPath, name),
            name,
            DesktopEntryClassifier.Classify(name),
            IsHidden: isHidden);
    }

    private static DesktopEvent Event(string rootPath, string name, DateTimeOffset timestamp)
    {
        return new DesktopEvent(name, Path.Combine(rootPath, name), timestamp);
    }

    private static string CreateRootPath(string name)
    {
        return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopGuard.Core.Tests", name));
    }
}
