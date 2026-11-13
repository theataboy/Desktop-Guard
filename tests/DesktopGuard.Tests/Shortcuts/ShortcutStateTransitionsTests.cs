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
    public void MoveBetweenDesktops_ChangesRootAndAppliesDestinationStatus()
    {
        TrackedShortcut shortcut = CreateShortcut(DesktopRootKind.User);
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

        DesktopGuardState movingToUser = ShortcutStateTransitions.BeginMove(
            movedPublic,
            shortcut.Id,
            DesktopRootKind.User,
            shortcut.RootPath);
        DesktopGuardState movedUser = ShortcutStateTransitions.CompleteMove(movingToUser, shortcut.Id);
        Assert.AreEqual(DesktopRootKind.User, movedUser.Shortcuts.Single().RootKind);
        Assert.AreEqual(ShortcutStatus.Pending, movedUser.Shortcuts.Single().Status);
        Assert.IsTrue(DesktopStateValidator.IsValid(movedUser));
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
