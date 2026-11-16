using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;
using DesktopGuard.Core.State;

namespace DesktopGuard.Tests.Shortcuts;

[TestClass]
public sealed class ShortcutStateTransitionsTests
{
    [TestMethod]
    public void ParkAndRestore_PendingShortcutReturnsToPending()
    {
        DesktopGuardState initial = StateWithShortcut(ShortcutStatus.Pending);
        Guid id = initial.Shortcuts.Single().Id;

        DesktopGuardState parking = ShortcutStateTransitions.BeginParking(initial, id, Guid.NewGuid());
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(parking, id);
        DesktopGuardState restoring = ShortcutStateTransitions.BeginRestore(parked, id);
        DesktopGuardState restored = ShortcutStateTransitions.CompleteRestore(restoring, id);

        Assert.AreEqual(ShortcutStatus.Pending, restored.Shortcuts.Single().Status);
        Assert.AreEqual(ShortcutOperation.None, restored.Shortcuts.Single().Operation);
        Assert.IsNull(restored.Shortcuts.Single().RecoveryFolderId);
        Assert.IsTrue(DesktopStateValidator.IsValid(restored));
    }

    [TestMethod]
    public void ParkAndRestore_ApprovedShortcutReturnsToApprovedVisible()
    {
        DesktopGuardState initial = StateWithShortcut(ShortcutStatus.ApprovedVisible);
        Guid id = initial.Shortcuts.Single().Id;

        DesktopGuardState parking = ShortcutStateTransitions.BeginParking(initial, id, Guid.NewGuid());
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(parking, id);
        DesktopGuardState restoring = ShortcutStateTransitions.BeginRestore(parked, id);
        DesktopGuardState restored = ShortcutStateTransitions.CompleteRestore(restoring, id);

        Assert.AreEqual(ShortcutStatus.ApprovedVisible, restored.Shortcuts.Single().Status);
        Assert.IsTrue(DesktopStateValidator.IsValid(restored));
    }

    [TestMethod]
    public void CompleteParking_RemovesOriginalPathFromBaseline()
    {
        DesktopGuardState initial = StateWithShortcut(ShortcutStatus.Pending);
        TrackedShortcut shortcut = initial.Shortcuts.Single();

        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(
            ShortcutStateTransitions.BeginParking(initial, shortcut.Id, Guid.NewGuid()), shortcut.Id);

        Assert.IsEmpty(parked.Roots.Single().KnownPaths);
    }

    [TestMethod]
    public void BeginParking_AllowsPublicShortcut()
    {
        TrackedShortcut publicShortcut = CreateShortcut(DesktopRootKind.Public);
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(
                    DesktopRootKind.Public,
                    publicShortcut.RootPath,
                    IsBaselined: true,
                    KnownPaths: [publicShortcut.OriginalPath]),
            ],
            Shortcuts = [publicShortcut],
        };

        DesktopGuardState parking = ShortcutStateTransitions.BeginParking(state, publicShortcut.Id, Guid.NewGuid());
        Assert.AreEqual(ShortcutOperation.Parking, parking.Shortcuts.Single().Operation);
        Assert.IsTrue(DesktopStateValidator.IsValid(parking));
    }

    [TestMethod]
    public void MoveBetweenDesktops_PreservesApprovedStatus()
    {
        TrackedShortcut shortcut = CreateShortcut(DesktopRootKind.User) with
        {
            Status = ShortcutStatus.ApprovedVisible,
        };
        string publicRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopGuard.Tests", "Public"));
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(
                    DesktopRootKind.User,
                    shortcut.RootPath,
                    IsBaselined: true,
                    KnownPaths: [shortcut.OriginalPath]),
                new DesktopRootCheckpoint(DesktopRootKind.Public, publicRoot, IsBaselined: true, KnownPaths: []),
            ],
            Shortcuts = [shortcut],
        };

        DesktopGuardState moving = ShortcutStateTransitions.BeginMove(
            state,
            shortcut.Id,
            DesktopRootKind.Public,
            publicRoot);
        Assert.IsTrue(DesktopStateValidator.IsValid(moving));
        DesktopGuardState movedPublic = ShortcutStateTransitions.CompleteMove(moving, shortcut.Id);
        Assert.AreEqual(DesktopRootKind.Public, movedPublic.Shortcuts.Single().RootKind);
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, movedPublic.Shortcuts.Single().Status);
        Assert.IsEmpty(movedPublic.Roots.Single(root => root.RootKind == DesktopRootKind.User).KnownPaths);
        Assert.Contains(movedPublic.Shortcuts.Single().OriginalPath,
            movedPublic.Roots.Single(root => root.RootKind == DesktopRootKind.Public).KnownPaths);

        DesktopGuardState movingToUser = ShortcutStateTransitions.BeginMove(
            movedPublic,
            shortcut.Id,
            DesktopRootKind.User,
            shortcut.RootPath);
        DesktopGuardState movedUser = ShortcutStateTransitions.CompleteMove(movingToUser, shortcut.Id);
        Assert.AreEqual(DesktopRootKind.User, movedUser.Shortcuts.Single().RootKind);
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, movedUser.Shortcuts.Single().Status);
        Assert.IsTrue(DesktopStateValidator.IsValid(movedUser));
    }

    [TestMethod]
    public void BeginMove_RejectsTargetPathKnownToDestinationRoot()
    {
        TrackedShortcut source = CreateShortcut(DesktopRootKind.Public);
        string userRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopGuard.Tests", "User"));
        string targetPath = Path.Combine(userRoot, source.Name);
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.Public, source.RootPath, true, [source.OriginalPath]),
                new DesktopRootCheckpoint(DesktopRootKind.User, userRoot, true, [targetPath]),
            ],
            Shortcuts = [source],
        };

        Assert.Throws<InvalidOperationException>(() => ShortcutStateTransitions.BeginMove(
            state, source.Id, DesktopRootKind.User, userRoot));
    }

    [TestMethod]
    public void Validator_RejectsPersistedMoveTargetCollision()
    {
        TrackedShortcut source = CreateShortcut(DesktopRootKind.Public) with
        {
            Operation = ShortcutOperation.Moving,
            TargetRootKind = DesktopRootKind.User,
            TargetRootPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopGuard.Tests", "User")),
            OperationId = Guid.NewGuid(),
        };
        string targetPath = Path.Combine(source.TargetRootPath!, source.Name);
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.Public, source.RootPath, true, [source.OriginalPath]),
                new DesktopRootCheckpoint(DesktopRootKind.User, source.TargetRootPath!, true, [targetPath]),
            ],
            Shortcuts = [source],
        };

        Assert.IsFalse(DesktopStateValidator.IsValid(state));
    }

    [TestMethod]
    public void Validator_RejectsUnbaselinedRootWithTrackedShortcut()
    {
        TrackedShortcut shortcut = CreateShortcut(DesktopRootKind.User);
        DesktopGuardState state = new()
        {
            Roots = [new DesktopRootCheckpoint(DesktopRootKind.User, shortcut.RootPath, false, [])],
            Shortcuts = [shortcut],
        };

        Assert.IsFalse(DesktopStateValidator.IsValid(state));
    }

    [TestMethod]
    public void MoveBetweenDesktops_PreservesPendingStatus()
    {
        TrackedShortcut shortcut = CreateShortcut(DesktopRootKind.User);
        string publicRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopGuard.Tests", "Public"));
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.User, shortcut.RootPath, true, [shortcut.OriginalPath]),
                new DesktopRootCheckpoint(DesktopRootKind.Public, publicRoot, true, []),
            ],
            Shortcuts = [shortcut],
        };

        DesktopGuardState movingToPublic = ShortcutStateTransitions.BeginMove(
            state,
            shortcut.Id,
            DesktopRootKind.Public,
            publicRoot);
        DesktopGuardState movedPublic = ShortcutStateTransitions.CompleteMove(movingToPublic, shortcut.Id);
        Assert.AreEqual(ShortcutStatus.Pending, movedPublic.Shortcuts.Single().Status);

        DesktopGuardState movingToUser = ShortcutStateTransitions.BeginMove(
            movedPublic,
            shortcut.Id,
            DesktopRootKind.User,
            shortcut.RootPath);
        DesktopGuardState movedUser = ShortcutStateTransitions.CompleteMove(movingToUser, shortcut.Id);

        Assert.AreEqual(ShortcutStatus.Pending, movedUser.Shortcuts.Single().Status);
    }

    [TestMethod]
    public void MoveToPublic_RejectsDesktopGuardShortcutEvenAfterRename()
    {
        TrackedShortcut ordinaryShortcut = CreateShortcut(DesktopRootKind.User);
        TrackedShortcut desktopGuardShortcut = ordinaryShortcut with
        {
            Name = "Renamed DG.lnk",
            OriginalPath = Path.Combine(ordinaryShortcut.RootPath, "Renamed DG.lnk"),
            IsDesktopGuardShortcut = true,
        };
        string publicRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopGuard.Tests", "Public"));
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.User, desktopGuardShortcut.RootPath, true, [desktopGuardShortcut.OriginalPath]),
                new DesktopRootCheckpoint(DesktopRootKind.Public, publicRoot, true, []),
            ],
            Shortcuts = [desktopGuardShortcut],
        };

        Assert.IsTrue(DesktopStateValidator.IsValid(state));
        Assert.Throws<InvalidOperationException>(() => ShortcutStateTransitions.BeginMove(
            state,
            desktopGuardShortcut.Id,
            DesktopRootKind.Public,
            publicRoot));
    }

    [TestMethod]
    public void Validate_RejectsPersistedDesktopGuardMoveToPublic()
    {
        TrackedShortcut desktopGuardShortcut = CreateShortcut(DesktopRootKind.User) with
        {
            IsDesktopGuardShortcut = true,
            Operation = ShortcutOperation.Moving,
            TargetRootKind = DesktopRootKind.Public,
            TargetRootPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopGuard.Tests", "Public")),
            OperationId = Guid.NewGuid(),
        };
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.User, desktopGuardShortcut.RootPath, true, [desktopGuardShortcut.OriginalPath]),
                new DesktopRootCheckpoint(DesktopRootKind.Public, desktopGuardShortcut.TargetRootPath, true, []),
            ],
            Shortcuts = [desktopGuardShortcut],
        };

        Assert.IsFalse(DesktopStateValidator.IsValid(state));
    }

    [TestMethod]
    public void CreateDesktopShortcut_CompletesAsApprovedAndAddsItsPathToTheBaseline()
    {
        TrackedShortcut existing = CreateShortcut(DesktopRootKind.User);
        DesktopGuardState initial = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(
                    DesktopRootKind.User,
                    existing.RootPath,
                    IsBaselined: true,
                    KnownPaths: [existing.OriginalPath]),
            ],
            Shortcuts = [existing],
        };

        DesktopGuardState creating = ShortcutStateTransitions.BeginCreation(
            initial,
            existing.RootPath,
            "Desktop Guard.lnk");
        TrackedShortcut pending = creating.Shortcuts.Single(shortcut => shortcut.Operation == ShortcutOperation.Creating);
        Assert.IsTrue(pending.IsDesktopGuardShortcut);
        Assert.IsTrue(DesktopStateValidator.IsValid(creating));

        DesktopGuardState hashed = ShortcutStateTransitions.RecordCreationHash(
            creating,
            pending.Id,
            new byte[32]);
        Assert.IsTrue(DesktopStateValidator.IsValid(hashed));

        DesktopGuardState completed = ShortcutStateTransitions.CompleteCreation(hashed, pending.Id);
        TrackedShortcut created = completed.Shortcuts.Single(shortcut => shortcut.Id == pending.Id);
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, created.Status);
        Assert.AreEqual(ShortcutOperation.None, created.Operation);
        Assert.IsNull(created.OperationContentHash);
        Assert.IsTrue(completed.Roots.Single().KnownPaths.Contains(created.OriginalPath, StringComparer.OrdinalIgnoreCase));
        Assert.IsTrue(DesktopStateValidator.IsValid(completed));
    }

    [TestMethod]
    public void Rename_PreservesExtensionAndUpdatesDesktopCheckpoint()
    {
        DesktopGuardState initial = StateWithShortcut(ShortcutStatus.ApprovedVisible);
        TrackedShortcut original = initial.Shortcuts.Single() with { IsHidden = true };
        initial = initial with { Shortcuts = [original] };

        DesktopGuardState renaming = ShortcutStateTransitions.BeginRename(initial, original.Id, "Renamed.lnk");
        Assert.IsTrue(DesktopStateValidator.IsValid(renaming));

        DesktopGuardState renamed = ShortcutStateTransitions.CompleteRename(renaming, original.Id);
        TrackedShortcut result = renamed.Shortcuts.Single();
        string expectedPath = Path.Combine(original.RootPath, "Renamed.lnk");
        Assert.AreEqual("Renamed.lnk", result.Name);
        Assert.AreEqual(expectedPath, result.OriginalPath);
        Assert.IsTrue(result.IsHidden);
        CollectionAssert.AreEqual(
            new[] { expectedPath },
            renamed.Roots.Single().KnownPaths.ToArray());
        Assert.IsTrue(DesktopStateValidator.IsValid(renamed));
    }

    [TestMethod]
    public void Rename_CaseOnlyChangeRemainsAValidPendingOperation()
    {
        DesktopGuardState initial = StateWithShortcut(ShortcutStatus.ApprovedVisible);
        TrackedShortcut original = initial.Shortcuts.Single();
        string targetName = original.Name.ToUpperInvariant();

        DesktopGuardState pending = ShortcutStateTransitions.BeginRename(initial, original.Id, targetName);

        Assert.IsTrue(DesktopStateValidator.IsValid(pending));
        DesktopGuardState completed = ShortcutStateTransitions.CompleteRename(pending, original.Id);
        Assert.AreEqual(targetName, completed.Shortcuts.Single().Name);
        Assert.IsTrue(DesktopStateValidator.IsValid(completed));
    }

    [TestMethod]
    public void Rename_ParkedShortcutDoesNotRewriteReplacementPathInDesktopCheckpoint()
    {
        TrackedShortcut parked = CreateShortcut(DesktopRootKind.User) with
        {
            Status = ShortcutStatus.Parked,
            StatusBeforeParking = ShortcutStatus.ApprovedVisible,
            RecoveryFolderId = Guid.NewGuid(),
        };
        TrackedShortcut replacement = parked with
        {
            Id = Guid.NewGuid(),
            Status = ShortcutStatus.Pending,
            StatusBeforeParking = null,
            RecoveryFolderId = null,
        };
        DesktopGuardState initial = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(
                    DesktopRootKind.User,
                    parked.RootPath,
                    IsBaselined: true,
                    KnownPaths: [parked.OriginalPath]),
            ],
            Shortcuts = [parked, replacement],
        };

        DesktopGuardState renaming = ShortcutStateTransitions.BeginRename(initial, parked.Id, "Parked copy.lnk");

        Assert.IsTrue(DesktopStateValidator.IsValid(renaming));
        DesktopGuardState renamed = ShortcutStateTransitions.CompleteRename(renaming, parked.Id);
        Assert.AreEqual(parked.OriginalPath, renamed.Roots.Single().KnownPaths.Single());
        Assert.AreEqual("Parked copy.lnk", renamed.Shortcuts.Single(shortcut => shortcut.Id == parked.Id).Name);
        Assert.IsTrue(DesktopStateValidator.IsValid(renamed));
    }

    [TestMethod]
    public void Rename_RejectsTargetReservedByAnotherTrackedDesktopShortcut()
    {
        TrackedShortcut source = CreateShortcut(DesktopRootKind.User);
        TrackedShortcut existing = source with
        {
            Id = Guid.NewGuid(),
            Name = "Existing.lnk",
            OriginalPath = Path.Combine(source.RootPath, "Existing.lnk"),
        };
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(
                    DesktopRootKind.User,
                    source.RootPath,
                    IsBaselined: true,
                    KnownPaths: [source.OriginalPath, existing.OriginalPath]),
            ],
            Shortcuts = [source, existing],
        };

        Assert.Throws<InvalidOperationException>(
            () => ShortcutStateTransitions.BeginRename(state, source.Id, "Existing.lnk"));
    }

    [TestMethod]
    public void Restore_ClearsHiddenStateAfterHiddenShortcutWasParked()
    {
        TrackedShortcut hidden = CreateShortcut(DesktopRootKind.User) with { IsHidden = true };
        DesktopGuardState initial = StateWithShortcut(ShortcutStatus.ApprovedVisible) with
        {
            Shortcuts = [hidden],
        };

        DesktopGuardState parking = ShortcutStateTransitions.BeginParking(initial, hidden.Id, Guid.NewGuid());
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(parking, hidden.Id);
        Assert.IsTrue(parked.Shortcuts.Single().IsHidden);

        DesktopGuardState restoring = ShortcutStateTransitions.BeginRestore(parked, hidden.Id);
        DesktopGuardState restored = ShortcutStateTransitions.CompleteRestore(restoring, hidden.Id);

        Assert.IsFalse(restored.Shortcuts.Single().IsHidden);
        Assert.IsTrue(DesktopStateValidator.IsValid(restored));
    }

    [TestMethod]
    public void Delete_UsesOperationForLocationAndRemovesDesktopCheckpointPath()
    {
        DesktopGuardState initial = StateWithShortcut(ShortcutStatus.ApprovedVisible);
        TrackedShortcut shortcut = initial.Shortcuts.Single();

        DesktopGuardState deleting = ShortcutStateTransitions.BeginDelete(initial, shortcut.Id);
        Assert.AreEqual(ShortcutOperation.DeletingToRecycleBin, deleting.Shortcuts.Single().Operation);
        Assert.IsTrue(DesktopStateValidator.IsValid(deleting));

        DesktopGuardState deleted = ShortcutStateTransitions.CompleteDelete(deleting, shortcut.Id);
        Assert.IsEmpty(deleted.Shortcuts);
        Assert.IsEmpty(deleted.Roots.Single().KnownPaths);
        Assert.IsTrue(DesktopStateValidator.IsValid(deleted));
    }

    [TestMethod]
    public void BeginCreation_RejectsWhenPublicDesktopAlreadyTracksDesktopGuardShortcut()
    {
        string userRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopGuard.Tests", "User"));
        string publicRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopGuard.Tests", "Public"));
        string publicShortcutPath = Path.Combine(publicRoot, ShortcutStateTransitions.DesktopShortcutName);
        TrackedShortcut publicShortcut = CreateShortcut(DesktopRootKind.Public) with
        {
            Name = ShortcutStateTransitions.DesktopShortcutName,
            OriginalPath = publicShortcutPath,
            Status = ShortcutStatus.ApprovedVisible,
        };
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.User, userRoot, true, []),
                new DesktopRootCheckpoint(DesktopRootKind.Public, publicRoot, true, [publicShortcutPath]),
            ],
            Shortcuts = [publicShortcut],
        };

        Assert.Throws<InvalidOperationException>(() => ShortcutStateTransitions.BeginCreation(
            state,
            userRoot,
            ShortcutStateTransitions.DesktopShortcutName));
    }

    [TestMethod]
    public void DesktopStateValidator_RejectsNullAndTraversalData()
    {
        Assert.IsFalse(DesktopStateValidator.IsValid(new DesktopGuardState { Roots = null! }));

        TrackedShortcut shortcut = CreateShortcut(DesktopRootKind.User) with
        {
            OriginalPath = Path.Combine(Path.GetTempPath(), "outside", "Program.lnk"),
        };
        DesktopGuardState state = new() { Shortcuts = [shortcut] };

        Assert.IsFalse(DesktopStateValidator.IsValid(state));
    }

    [TestMethod]
    public void DesktopStateValidator_RejectsAlternateDataStreamNames()
    {
        TrackedShortcut original = CreateShortcut(DesktopRootKind.User);
        TrackedShortcut shortcut = original with
        {
            Name = "Program:stream.lnk",
            OriginalPath = Path.Combine(original.RootPath, "Program:stream.lnk"),
        };
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(
                    shortcut.RootKind,
                    shortcut.RootPath,
                    IsBaselined: true,
                    KnownPaths: []),
            ],
            Shortcuts = [shortcut],
        };

        Assert.IsFalse(DesktopStateValidator.IsValid(state));
    }

    [TestMethod]
    public void DesktopStateValidator_RejectsSharedRecoveryFolderIdentifiers()
    {
        TrackedShortcut first = CreateShortcut(DesktopRootKind.User) with
        {
            Status = ShortcutStatus.Parked,
            StatusBeforeParking = ShortcutStatus.ApprovedVisible,
            RecoveryFolderId = Guid.NewGuid(),
        };
        TrackedShortcut second = first with
        {
            Id = Guid.NewGuid(),
            Name = "Other.lnk",
            OriginalPath = Path.Combine(first.RootPath, "Other.lnk"),
        };
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(
                    first.RootKind,
                    first.RootPath,
                    IsBaselined: true,
                    KnownPaths: [first.OriginalPath, second.OriginalPath]),
            ],
            Shortcuts = [first, second],
        };

        Assert.IsFalse(DesktopStateValidator.IsValid(state));
    }

    [TestMethod]
    public void DesktopStateValidator_RejectsDuplicateTrackedShortcutPaths()
    {
        TrackedShortcut first = CreateShortcut(DesktopRootKind.User);
        TrackedShortcut duplicate = first with { Id = Guid.NewGuid() };
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(
                    first.RootKind,
                    first.RootPath,
                    IsBaselined: true,
                    KnownPaths: [first.OriginalPath]),
            ],
            Shortcuts = [first, duplicate],
        };

        Assert.IsFalse(DesktopStateValidator.IsValid(state));
    }

    [TestMethod]
    public void DesktopStateValidator_AllowsParkedShortcutAndNewShortcutAtSameOriginalPath()
    {
        TrackedShortcut parked = CreateShortcut(DesktopRootKind.User) with
        {
            Status = ShortcutStatus.Parked,
            StatusBeforeParking = ShortcutStatus.ApprovedVisible,
            RecoveryFolderId = Guid.NewGuid(),
        };
        TrackedShortcut replacement = parked with
        {
            Id = Guid.NewGuid(),
            Status = ShortcutStatus.Pending,
            StatusBeforeParking = null,
            RecoveryFolderId = null,
        };
        DesktopGuardState state = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(
                    parked.RootKind,
                    parked.RootPath,
                    IsBaselined: true,
                    KnownPaths: [parked.OriginalPath]),
            ],
            Shortcuts = [parked, replacement],
        };

        Assert.IsTrue(DesktopStateValidator.IsValid(state));
    }

    private static DesktopGuardState StateWithShortcut(ShortcutStatus status)
    {
        TrackedShortcut shortcut = CreateShortcut(DesktopRootKind.User) with { Status = status };
        return new DesktopGuardState
        {
            Roots =
            [
                new DesktopRootCheckpoint(
                    shortcut.RootKind,
                    shortcut.RootPath,
                    IsBaselined: true,
                    KnownPaths: [shortcut.OriginalPath]),
            ],
            Shortcuts = [shortcut],
        };
    }

    private static TrackedShortcut CreateShortcut(DesktopRootKind rootKind)
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopGuard.Tests", rootKind.ToString()));
        string path = Path.Combine(root, "Program.lnk");
        return new TrackedShortcut(
            Guid.NewGuid(),
            rootKind,
            root,
            "Program.lnk",
            path,
            ShortcutStatus.Pending,
            StatusBeforeParking: null,
            ShortcutOperation.None,
            RecoveryFolderId: null);
    }
}
