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
    public void Reconcile_UnresolvedRestoreTracksReplacementAndDefersSuffixedTarget()
    {
        string userRoot = CreateRootPath("unresolved-restore-suffixed-target");
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
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Restore.lnk"))]),
            now.AddMinutes(1)).State;
        TrackedShortcut replacement = afterPark.Shortcuts.Single(shortcut => shortcut.Id != original.Id);
        DesktopGuardState restoring = ShortcutStateTransitions.BeginRestore(
            afterPark,
            original.Id,
            "Restore(2).lnk");

        InventoryReconciliation result = _reconciler.Reconcile(
            restoring,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Restore.lnk"),
                Entry(DesktopRootKind.User, userRoot, "Restore(2).lnk"))]),
            now.AddMinutes(2));

        Assert.HasCount(2, result.State.Shortcuts);
        Assert.AreEqual(ShortcutOperation.Restoring, result.State.Shortcuts.Single(item => item.Id == original.Id).Operation);
        Assert.AreEqual(ShortcutStatus.Pending, result.State.Shortcuts.Single(item => item.Id == replacement.Id).Status);
        Assert.Contains(Path.Combine(userRoot, "Restore.lnk"), result.State.Roots.Single().KnownPaths);
        Assert.DoesNotContain(Path.Combine(userRoot, "Restore(2).lnk"), result.State.Roots.Single().KnownPaths);
        Assert.IsTrue(DesktopStateValidator.IsValid(result.State));
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
    public void Reconcile_RecreatedShortcutWithMatchingIdentityIsScheduledForAutoParking()
    {
        string userRoot = CreateRootPath("recreated-shortcut-identity");
        byte[] identityHash = System.Security.Cryptography.SHA256.HashData("creality-launch"u8);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(
            new DesktopGuardState(),
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Creality Print 7.3.lnk"))]),
            now).State;
        TrackedShortcut original = baseline.Shortcuts.Single();
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(
            ShortcutStateTransitions.BeginParking(baseline, original.Id, Guid.NewGuid()),
            original.Id,
            identityHash);

        InventoryReconciliation result = _reconciler.Reconcile(
            parked,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Creality Print.lnk", identityHash: identityHash))]),
            now.AddMinutes(1));

        TrackedShortcut replacement = result.State.Shortcuts.Single(shortcut => shortcut.Id != original.Id);
        Assert.AreEqual(ShortcutStatus.ScheduledForAutoParking, replacement.Status);
        Assert.AreEqual(now.AddMinutes(1), replacement.AutoParkingSinceUtc);
        Assert.AreEqual("Creality Print.lnk", replacement.Name);
        Assert.IsEmpty(result.NewUserShortcuts);
        Assert.IsTrue(DesktopStateValidator.IsValid(result.State));
    }

    [TestMethod]
    public void Reconcile_ChangedBytesWithSameIdentityReturnToReview()
    {
        string root = CreateRootPath("changed-scheduled-shortcut");
        byte[] identity = System.Security.Cryptography.SHA256.HashData("identity"u8);
        byte[] changedBytes = System.Security.Cryptography.SHA256.HashData("changed bytes"u8);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(new DesktopGuardState(),
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true,
                Entry(DesktopRootKind.User, root, "Original.lnk"))]), now).State;
        Guid originalId = baseline.Shortcuts.Single().Id;
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(
            ShortcutStateTransitions.BeginParking(baseline, originalId, Guid.NewGuid()), originalId, identity);
        DesktopInventoryEntry replacement = Entry(DesktopRootKind.User, root, "Copy.lnk", identityHash: identity);
        DesktopGuardState scheduled = _reconciler.Reconcile(parked,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true, replacement)]), now).State;
        InventoryReconciliation result = _reconciler.Reconcile(scheduled,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true,
                replacement with { SourceContentHash = changedBytes })]), now.AddMinutes(5));
        TrackedShortcut copy = result.State.Shortcuts.Single(item => item.Id != originalId);
        Assert.AreEqual(ShortcutStatus.Pending, copy.Status);
        Assert.IsNull(copy.ShortcutIdentityHash);
        Assert.HasCount(1, result.NewUserShortcuts);

        DesktopGuardState pending = ShortcutStateTransitions.BeginParking(result.State, copy.Id, Guid.NewGuid());
        TrackedShortcut parkedCopy = ShortcutStateTransitions.CompleteParking(pending, copy.Id).Shortcuts
            .Single(item => item.Id == copy.Id);
        Assert.IsNull(parkedCopy.ShortcutIdentityHash);
    }

    [TestMethod]
    public void RenameParkedRule_WithScheduledCopyProducesValidState()
    {
        string root = CreateRootPath("rename-parked-rule");
        byte[] identity = System.Security.Cryptography.SHA256.HashData("identity"u8);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(new DesktopGuardState(),
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true,
                Entry(DesktopRootKind.User, root, "Original.lnk"))]), now).State;
        Guid originalId = baseline.Shortcuts.Single().Id;
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(
            ShortcutStateTransitions.BeginParking(baseline, originalId, Guid.NewGuid()), originalId, identity);
        DesktopGuardState scheduled = _reconciler.Reconcile(parked,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true,
                Entry(DesktopRootKind.User, root, "Copy.lnk", identityHash: identity))]), now).State;
        DesktopGuardState renaming = ShortcutStateTransitions.BeginRename(scheduled, originalId, "Renamed.lnk");
        Assert.IsTrue(DesktopStateValidator.IsValid(renaming));
        Assert.IsTrue(DesktopStateValidator.IsValid(ShortcutStateTransitions.CompleteRename(renaming, originalId)));
    }

    [TestMethod]
    [DataRow(ShortcutStatus.Pending, ShortcutStatus.ScheduledForAutoParking)]
    [DataRow(ShortcutStatus.ApprovedVisible, ShortcutStatus.ApprovedVisible)]
    public void Reconcile_ExistingPendingMatchIsScheduledOnceWhileApprovedDecisionIsPreserved(
        ShortcutStatus initialStatus, ShortcutStatus expectedStatus)
    {
        string root = CreateRootPath("existing-pending-rule");
        byte[] identity = System.Security.Cryptography.SHA256.HashData("identity"u8);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(new DesktopGuardState(),
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true,
                Entry(DesktopRootKind.User, root, "Original.lnk"), Entry(DesktopRootKind.User, root, "Copy.lnk"))]), now).State;
        Guid originalId = baseline.Shortcuts.Single(shortcut => shortcut.Name == "Original.lnk").Id;
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(
            ShortcutStateTransitions.BeginParking(baseline, originalId, Guid.NewGuid()), originalId, identity);
        parked = parked with
        {
            Shortcuts = parked.Shortcuts.Select(shortcut => shortcut.Id == originalId ? shortcut
                : shortcut with { Status = initialStatus, AutoParkingEvaluated = false }).ToList(),
        };
        DesktopInventoryEntry copy = Entry(DesktopRootKind.User, root, "Copy.lnk", identityHash: identity);
        InventoryReconciliation result = _reconciler.Reconcile(parked,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true, copy)]), now);
        Assert.AreEqual(expectedStatus, result.State.Shortcuts.Single(shortcut => shortcut.Id != originalId).Status);
        Assert.IsEmpty(result.NewUserShortcuts);

        if (initialStatus == ShortcutStatus.Pending)
        {
            DesktopGuardState changed = _reconciler.Reconcile(result.State,
                new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true,
                    copy with { SourceContentHash = new byte[32] })]), now.AddMinutes(1)).State;
            DesktopGuardState rescanned = _reconciler.Reconcile(changed,
                new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true, copy)]), now.AddMinutes(2)).State;
            Assert.AreEqual(ShortcutStatus.Pending, rescanned.Shortcuts.Single(shortcut => shortcut.Id != originalId).Status);
        }
    }

    [TestMethod]
    public void Reconcile_ExistingPendingShortcutDefersUntilParkedIdentityCanBeCompared()
    {
        string root = CreateRootPath("pending-identity-deferred");
        byte[] identity = System.Security.Cryptography.SHA256.HashData("identity"u8);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(new DesktopGuardState(),
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true,
                Entry(DesktopRootKind.User, root, "Original.lnk"), Entry(DesktopRootKind.User, root, "Copy.lnk"))]), now).State;
        Guid originalId = baseline.Shortcuts.Single(shortcut => shortcut.Name == "Original.lnk").Id;
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(
            ShortcutStateTransitions.BeginParking(baseline, originalId, Guid.NewGuid()), originalId, identity);
        parked = parked with
        {
            Shortcuts = parked.Shortcuts.Select(shortcut => shortcut.Id == originalId ? shortcut
                : shortcut with { Status = ShortcutStatus.Pending, AutoParkingEvaluated = false }).ToList(),
        };

        DesktopInventoryEntry copyWithoutIdentity = Entry(DesktopRootKind.User, root, "Copy.lnk");
        InventoryReconciliation deferred = _reconciler.Reconcile(parked,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true, copyWithoutIdentity)]), now);
        TrackedShortcut stillPending = deferred.State.Shortcuts.Single(shortcut => shortcut.Id != originalId);
        Assert.AreEqual(ShortcutStatus.Pending, stillPending.Status);
        Assert.IsFalse(stillPending.AutoParkingEvaluated);

        InventoryReconciliation compared = _reconciler.Reconcile(deferred.State,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true,
                copyWithoutIdentity with { ShortcutIdentityHash = identity, SourceContentHash = identity })]),
            now.AddMinutes(1));
        TrackedShortcut scheduled = compared.State.Shortcuts.Single(shortcut => shortcut.Id != originalId);
        Assert.AreEqual(ShortcutStatus.ScheduledForAutoParking, scheduled.Status);
        Assert.IsTrue(scheduled.AutoParkingEvaluated);
    }

    [TestMethod]
    public void Reconcile_ExistingPendingShortcutWithoutParkedRuleCompletesComparisonWithoutIdentity()
    {
        string root = CreateRootPath("pending-without-rule");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(new DesktopGuardState(),
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true,
                Entry(DesktopRootKind.User, root, "Existing.lnk"))]), now).State;
        TrackedShortcut pending = baseline.Shortcuts.Single() with
        {
            Status = ShortcutStatus.Pending,
            AutoParkingEvaluated = false,
        };

        InventoryReconciliation result = _reconciler.Reconcile(
            baseline with { Shortcuts = [pending] },
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, root, true,
                Entry(DesktopRootKind.User, root, "Existing.lnk"))]),
            now.AddMinutes(1));

        Assert.IsTrue(result.State.Shortcuts.Single().AutoParkingEvaluated);
        Assert.AreEqual(ShortcutStatus.Pending, result.State.Shortcuts.Single().Status);
    }

    [TestMethod]
    public void Reconcile_SameNameWithDifferentIdentityRemainsPending()
    {
        string userRoot = CreateRootPath("same-name-different-identity");
        byte[] parkedIdentity = System.Security.Cryptography.SHA256.HashData("old-target"u8);
        byte[] newIdentity = System.Security.Cryptography.SHA256.HashData("new-target"u8);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DesktopGuardState baseline = _reconciler.Reconcile(
            new DesktopGuardState(),
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Creality Print.lnk"))]),
            now).State;
        TrackedShortcut original = baseline.Shortcuts.Single();
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(
            ShortcutStateTransitions.BeginParking(baseline, original.Id, Guid.NewGuid()),
            original.Id,
            parkedIdentity);

        InventoryReconciliation result = _reconciler.Reconcile(
            parked,
            new DesktopInventorySnapshot([Root(DesktopRootKind.User, userRoot, complete: true,
                Entry(DesktopRootKind.User, userRoot, "Creality Print.lnk", identityHash: newIdentity))]),
            now.AddMinutes(1));

        TrackedShortcut replacement = result.State.Shortcuts.Single(shortcut => shortcut.Id != original.Id);
        Assert.AreEqual(ShortcutStatus.Pending, replacement.Status);
        Assert.IsNull(replacement.ShortcutIdentityHash);
        Assert.HasCount(1, result.NewUserShortcuts);
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
        bool isHidden = false,
        byte[]? identityHash = null)
    {
        return new DesktopInventoryEntry(
            kind,
            rootPath,
            Path.Combine(rootPath, name),
            name,
            DesktopEntryClassifier.Classify(name),
            IsHidden: isHidden,
            ShortcutIdentityHash: identityHash,
            SourceContentHash: identityHash);
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
