using DesktopGuard.App.Application;
using DesktopGuard.App.DesktopScanning;
using DesktopGuard.App.Notifications;
using DesktopGuard.App.Persistence;
using DesktopGuard.App.ShortcutManagement;
using DesktopGuard.App.Startup;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;
using DesktopGuard.Core.State;

namespace DesktopGuard.App.Tests.Application;

[TestClass]
public sealed class DesktopGuardControllerTests
{
    [TestMethod]
    public void Initialize_BackfillsParkedShortcutIdentityWithoutMovingRecoveryFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string recovery = Path.Combine(data, "Recovery");
        string name = "Creality Print.url";
        Guid recoveryFolderId = Guid.NewGuid();
        string recoveryFolder = Directory.CreateDirectory(Path.Combine(recovery, recoveryFolderId.ToString("N"))).FullName;
        string recoveryPath = Path.Combine(recoveryFolder, name);
        File.WriteAllText(recoveryPath, "[InternetShortcut]\r\nURL=https://example.invalid/creality\r\n");
        TrackedShortcut parked = new(
            Guid.NewGuid(),
            DesktopRootKind.User,
            desktop,
            name,
            Path.Combine(desktop, name),
            ShortcutStatus.Parked,
            StatusBeforeParking: ShortcutStatus.Pending,
            Operation: ShortcutOperation.None,
            RecoveryFolderId: recoveryFolderId);
        DesktopGuardStateStore stateStore = new(data);
        stateStore.Save(new DesktopGuardState
        {
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.User, desktop, true, []),
                new DesktopRootCheckpoint(DesktopRootKind.Public, publicDesktop, true, []),
            ],
            Shortcuts = [parked],
        });
        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        using WindowsNotificationService notifications = new();
        ShortcutFileManager fileManager = new(rootProvider, stateStore, TimeSpan.Zero);
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            fileManager,
            new NoOpStartAtSignInManager(),
            notifications);

        controller.Initialize();

        TrackedShortcut updated = controller.State.Shortcuts.Single();
        Assert.IsNotNull(updated.ShortcutIdentityHash);
        Assert.HasCount(32, updated.ShortcutIdentityHash);
        Assert.IsTrue(File.Exists(recoveryPath));
        Assert.AreEqual(0, controller.ParkedShortcutsWithoutIdentityCount);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Initialize_RemovesOlderRecoveryDuplicateAndReconcilesInterruptedCleanup(bool interrupted, bool alreadyRemoved)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        DesktopGuardStateStore store = new(Path.Combine(temporaryDirectory.Path, "Data"));
        Guid oldFolder = Guid.NewGuid();
        Guid newFolder = Guid.NewGuid();
        string oldPath = Path.Combine(Directory.CreateDirectory(Path.Combine(store.RecoveryDirectoryPath, oldFolder.ToString("N"))).FullName, "Old.url");
        string newPath = Path.Combine(Directory.CreateDirectory(Path.Combine(store.RecoveryDirectoryPath, newFolder.ToString("N"))).FullName, "New.url");
        File.WriteAllText(oldPath, "[InternetShortcut]\r\nURL=https://example.invalid/app\r\nIconIndex=1");
        File.WriteAllText(newPath, "[InternetShortcut]\nURL=https://example.invalid/app\nIconIndex=2");
        File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-1));
        byte[] identity = new ShortcutIdentityReader().TryRead(newPath)!.IdentityHash;
        TrackedShortcut old = new(Guid.NewGuid(), DesktopRootKind.User, desktop, "Old.url", Path.Combine(desktop, "Old.url"),
            ShortcutStatus.Parked, ShortcutStatus.Pending, ShortcutOperation.None, oldFolder, ShortcutIdentityHash: identity);
        TrackedShortcut keeper = old with
        {
            Id = Guid.NewGuid(),
            Name = "New.url",
            OriginalPath = Path.Combine(desktop, "New.url"),
            RecoveryFolderId = newFolder,
        };
        DesktopGuardState state = new()
        {
            Roots = [new(DesktopRootKind.User, desktop, true, []), new(DesktopRootKind.Public, publicDesktop, true, [])],
            Shortcuts = [old, keeper],
        };
        if (interrupted)
        {
            state = ShortcutStateTransitions.BeginRecoveryDuplicateRemoval(state, old.Id, keeper.Id);
        }

        store.Save(state);
        if (alreadyRemoved)
        {
            File.Delete(oldPath);
            Directory.Delete(Path.GetDirectoryName(oldPath)!);
        }

        FixedDesktopRootProvider roots = new(desktop, publicDesktop);
        using WindowsNotificationService notifications = new();
        DesktopGuardController controller = new(new DesktopInventoryScanner(roots), new InventoryReconciler(), store,
            new ShortcutFileManager(roots, store, TimeSpan.Zero), new NoOpStartAtSignInManager(), notifications);

        controller.Initialize();

        Assert.AreEqual(keeper.Id, store.Load().Shortcuts.Single().Id);
        Assert.IsTrue(File.Exists(newPath));
        Assert.IsFalse(File.Exists(oldPath));
        Assert.AreEqual(0, controller.RecoveryDuplicateRemovalFailureCount);
    }

    [TestMethod]
    public async Task Refresh_RotatesRecoveryDuplicateReadBatchPastLockedShortcuts()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        DesktopGuardStateStore store = new(Path.Combine(temporaryDirectory.Path, "Data"));
        const int lockedCount = 64;
        const int accessibleCount = 2;
        const string url = "https://example.invalid/same-app";
        string identityFile = CreateIdentityFile(temporaryDirectory.Path, url);
        byte[] identity = new ShortcutIdentityReader().TryRead(identityFile)!.IdentityHash;
        File.Delete(identityFile);
        List<TrackedShortcut> shortcuts = [];
        List<string> accessiblePaths = [];

        for (int index = 0; index < lockedCount + accessibleCount; index++)
        {
            Guid folderId = Guid.NewGuid();
            string folder = Directory.CreateDirectory(
                Path.Combine(store.RecoveryDirectoryPath, folderId.ToString("N"))).FullName;
            string name = $"Copy {index:D2}.url";
            string recoveryPath = Path.Combine(folder, name);
            File.WriteAllText(recoveryPath, $"[InternetShortcut]\nURL={url}\nIconIndex={index}");
            File.SetLastWriteTimeUtc(recoveryPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index));
            if (index >= lockedCount)
            {
                accessiblePaths.Add(recoveryPath);
            }

            shortcuts.Add(new TrackedShortcut(Guid.NewGuid(), DesktopRootKind.User, desktop, name,
                Path.Combine(desktop, name), ShortcutStatus.Parked, ShortcutStatus.Pending,
                ShortcutOperation.None, folderId, ShortcutIdentityHash: identity));
        }

        List<FileStream> locks = [];
        try
        {
            foreach (TrackedShortcut shortcut in shortcuts.Take(lockedCount))
            {
                string path = Path.Combine(store.RecoveryDirectoryPath,
                    shortcut.RecoveryFolderId!.Value.ToString("N"), shortcut.Name);
                locks.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None));
            }

            store.Save(new DesktopGuardState
            {
                Roots =
                [
                    new(DesktopRootKind.User, desktop, true, []),
                    new(DesktopRootKind.Public, publicDesktop, true, []),
                ],
                Shortcuts = shortcuts,
            });
            FixedDesktopRootProvider roots = new(desktop, publicDesktop);
            using WindowsNotificationService notifications = new();
            DesktopGuardController controller = new(new DesktopInventoryScanner(roots), new InventoryReconciler(), store,
                new ShortcutFileManager(roots, store, TimeSpan.Zero), new NoOpStartAtSignInManager(), notifications);

            controller.Initialize();
            Assert.IsTrue(accessiblePaths.All(File.Exists));

            await controller.RefreshAsync(CancellationToken.None, showNotifications: false);

            Assert.IsFalse(File.Exists(accessiblePaths[0]));
            Assert.IsTrue(File.Exists(accessiblePaths[1]));
            Assert.HasCount(lockedCount + 1, controller.State.Shortcuts);
        }
        finally
        {
            foreach (FileStream fileLock in locks)
            {
                fileLock.Dispose();
            }
        }

        static string CreateIdentityFile(string root, string value)
        {
            string path = Path.Combine(root, $"identity-{Guid.NewGuid():N}.url");
            File.WriteAllText(path, $"[InternetShortcut]\nURL={value}\n");
            return path;
        }
    }

    [TestMethod]
    public async Task Park_ExistingFingerprintKeepsOneRecoveryCopy()
    {
        using TemporaryDirectory directory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(directory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(directory.Path, "Public")).FullName;
        DesktopGuardStateStore store = new(Path.Combine(directory.Path, "Data"));
        Guid recoveryId = Guid.NewGuid();
        string recoveryPath = Path.Combine(Directory.CreateDirectory(Path.Combine(store.RecoveryDirectoryPath, recoveryId.ToString("N"))).FullName, "Old.url");
        File.WriteAllText(recoveryPath, "[InternetShortcut]\nURL=https://example.invalid/app\n");
        File.SetLastWriteTimeUtc(recoveryPath, DateTime.UtcNow.AddDays(-1));
        string desktopPath = Path.Combine(desktop, "New.url");
        File.WriteAllText(desktopPath, "[InternetShortcut]\r\nURL=https://example.invalid/app\r\n");
        TrackedShortcut old = new(Guid.NewGuid(), DesktopRootKind.User, desktop, "Old.url", Path.Combine(desktop, "Old.url"),
            ShortcutStatus.Parked, ShortcutStatus.Pending, ShortcutOperation.None, recoveryId);
        TrackedShortcut pending = new(Guid.NewGuid(), DesktopRootKind.User, desktop, "New.url", desktopPath,
            ShortcutStatus.Pending, null, ShortcutOperation.None, null, AutoParkingEvaluated: true);
        store.Save(new DesktopGuardState
        {
            Roots = [new(DesktopRootKind.User, desktop, true, [desktopPath]), new(DesktopRootKind.Public, publicDesktop, true, [])],
            Shortcuts = [old, pending],
        });
        FixedDesktopRootProvider roots = new(desktop, publicDesktop);
        using WindowsNotificationService notifications = new();
        ShortcutFileManager manager = new(roots, store, new NoElevationMoveExecutor(), TimeSpan.Zero);
        DesktopGuardController controller = new(new DesktopInventoryScanner(roots), new InventoryReconciler(), store,
            manager, new NoOpStartAtSignInManager(), notifications);
        controller.Initialize();

        await controller.ParkAsync(pending.Id, CancellationToken.None);

        TrackedShortcut kept = store.Load().Shortcuts.Single();
        Assert.AreEqual(pending.Id, kept.Id);
        Assert.AreEqual(ShortcutStatus.Parked, kept.Status);
        Assert.IsFalse(File.Exists(recoveryPath));
        Assert.IsFalse(File.Exists(desktopPath));
        Assert.IsTrue(File.Exists(manager.GetRecoveryFilePath(kept)));
    }

    [TestMethod]
    public void Initialize_AutoParksMatureMatchingShortcutWithoutElevation()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string name = "Creality Print.url";
        string shortcutPath = Path.Combine(desktop, name);
        File.WriteAllText(shortcutPath, "[InternetShortcut]\r\nURL=https://example.invalid/creality\r\n");
        ShortcutIdentityReadResult identity = new ShortcutIdentityReader().TryRead(shortcutPath)!;
        Assert.IsNotNull(identity);

        Guid recoveryFolderId = Guid.NewGuid();
        string recoveryFolder = Directory.CreateDirectory(
            Path.Combine(data, "Recovery", recoveryFolderId.ToString("N"))).FullName;
        string recoveryPath = Path.Combine(recoveryFolder, "Previously Parked.url");
        File.WriteAllText(recoveryPath, "[InternetShortcut]\nURL=https://example.invalid/creality\n");
        File.SetLastWriteTimeUtc(recoveryPath, DateTime.UtcNow.AddDays(-1));
        TrackedShortcut parkedRule = new(
            Guid.NewGuid(),
            DesktopRootKind.User,
            desktop,
            "Previously Parked.url",
            Path.Combine(desktop, "Previously Parked.url"),
            ShortcutStatus.Parked,
            StatusBeforeParking: ShortcutStatus.Pending,
            Operation: ShortcutOperation.None,
            RecoveryFolderId: recoveryFolderId,
            ShortcutIdentityHash: identity.IdentityHash);
        TrackedShortcut scheduled = new(
            Guid.NewGuid(),
            DesktopRootKind.User,
            desktop,
            name,
            shortcutPath,
            ShortcutStatus.ScheduledForAutoParking,
            StatusBeforeParking: null,
            Operation: ShortcutOperation.None,
            RecoveryFolderId: null,
            ShortcutIdentityHash: identity.IdentityHash,
            AutoParkingSinceUtc: DateTimeOffset.UtcNow.AddMinutes(-3),
            AutoParkingContentHash: identity.SourceContentHash);
        DesktopGuardStateStore stateStore = new(data);
        stateStore.Save(new DesktopGuardState
        {
            AutoParkDelayMinutes = 2,
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.User, desktop, true, [shortcutPath]),
                new DesktopRootCheckpoint(DesktopRootKind.Public, publicDesktop, true, []),
            ],
            Shortcuts = [parkedRule, scheduled],
        });
        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        NoElevationMoveExecutor moveExecutor = new();
        using WindowsNotificationService notifications = new();
        ShortcutFileManager fileManager = new(rootProvider, stateStore, moveExecutor, TimeSpan.Zero);
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            fileManager,
            new NoOpStartAtSignInManager(),
            notifications);

        controller.Initialize();

        TrackedShortcut autoParked = controller.State.Shortcuts.Single(shortcut => shortcut.Id == scheduled.Id);
        Assert.AreEqual(ShortcutStatus.Parked, autoParked.Status);
        Assert.AreEqual(ShortcutStatus.Pending, autoParked.StatusBeforeParking);
        Assert.IsFalse(File.Exists(shortcutPath));
        Assert.IsTrue(File.Exists(fileManager.GetRecoveryFilePath(autoParked)));
        Assert.IsFalse(moveExecutor.AllowElevationWasRequested);
    }

    [TestMethod]
    public async Task Park_WhenCompletionSaveFails_ReconcilesMovedFileAndPreservesRecoveryRecord()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string shortcutPath = Path.Combine(desktop, "Program.lnk");
        File.WriteAllText(shortcutPath, "shortcut contents");

        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        FaultingStateStore stateStore = new(data);
        using WindowsNotificationService notifications = new();
        ShortcutFileManager fileManager = new(rootProvider, stateStore, TimeSpan.Zero);
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            fileManager,
            new NoOpStartAtSignInManager(),
            notifications);
        controller.Initialize();
        stateStore.FailOnSaveCalls(2);
        TrackedShortcut shortcut = controller.State.Shortcuts.Single();

        await controller.ParkAsync(shortcut.Id, CancellationToken.None);

        TrackedShortcut recovered = controller.State.Shortcuts.Single();
        Assert.AreEqual(ShortcutStatus.Parked, recovered.Status);
        Assert.AreEqual(ShortcutOperation.None, recovered.Operation);
        Assert.IsNotNull(recovered.RecoveryFolderId);
        Assert.IsFalse(File.Exists(shortcutPath));
        Assert.IsTrue(File.Exists(fileManager.GetRecoveryFilePath(recovered)));
        Assert.IsFalse(controller.IsOperationUnresolved(shortcut.Id));
        Assert.IsNull(controller.LastFailure);
    }

    [TestMethod]
    public async Task Park_WhenCompletionAndReconciliationSavesFail_PreservesDurableOperationIntent()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string shortcutPath = Path.Combine(desktop, "Program.lnk");
        File.WriteAllText(shortcutPath, "shortcut contents");

        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        FaultingStateStore stateStore = new(data);
        using WindowsNotificationService notifications = new();
        ShortcutFileManager fileManager = new(rootProvider, stateStore, TimeSpan.Zero);
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            fileManager,
            new NoOpStartAtSignInManager(),
            notifications);
        controller.Initialize();
        stateStore.FailOnSaveCalls(2, 3);
        TrackedShortcut shortcut = controller.State.Shortcuts.Single();

        await Assert.ThrowsAsync<StateStoreException>(
            () => controller.ParkAsync(shortcut.Id, CancellationToken.None));

        TrackedShortcut unresolved = controller.State.Shortcuts.Single();
        Assert.AreEqual(ShortcutOperation.Parking, unresolved.Operation);
        Assert.IsNotNull(unresolved.RecoveryFolderId);
        Assert.IsTrue(controller.IsOperationUnresolved(shortcut.Id));
        Assert.AreEqual(ShortcutOperation.Parking, stateStore.Load().Shortcuts.Single().Operation);
        Assert.IsFalse(File.Exists(shortcutPath));
        Assert.IsTrue(File.Exists(fileManager.GetRecoveryFilePath(unresolved)));
    }

    [TestMethod]
    public async Task Restore_WhenCompletionSaveFails_ReconcilesMovedFileAndClearsRecoveryRecord()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string shortcutPath = Path.Combine(desktop, "Program.url");
        File.WriteAllText(shortcutPath, "shortcut contents");

        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        FaultingStateStore stateStore = new(data);
        using WindowsNotificationService notifications = new();
        ShortcutFileManager fileManager = new(rootProvider, stateStore, TimeSpan.Zero);
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            fileManager,
            new NoOpStartAtSignInManager(),
            notifications);
        controller.Initialize();
        TrackedShortcut shortcut = controller.State.Shortcuts.Single();
        await controller.ParkAsync(shortcut.Id, CancellationToken.None);
        TrackedShortcut parked = controller.State.Shortcuts.Single();
        stateStore.FailOnSaveCalls(2);

        await controller.RestoreAsync(shortcut.Id, CancellationToken.None);

        TrackedShortcut restored = controller.State.Shortcuts.Single();
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, restored.Status);
        Assert.AreEqual(ShortcutOperation.None, restored.Operation);
        Assert.IsNull(restored.RecoveryFolderId);
        Assert.IsTrue(File.Exists(shortcutPath));
        Assert.IsFalse(File.Exists(fileManager.GetRecoveryFilePath(parked)));
        Assert.IsFalse(controller.IsOperationUnresolved(shortcut.Id));
        Assert.IsNull(controller.LastFailure);
    }

    [TestMethod]
    public async Task Approve_WhenStateSaveFails_RecordsFailureAndKeepsPendingStatus()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        FaultingStateStore stateStore = new(data);
        using WindowsNotificationService notifications = new();
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            new ShortcutFileManager(rootProvider, stateStore, TimeSpan.Zero),
            new NoOpStartAtSignInManager(),
            notifications);
        controller.Initialize();
        string shortcutPath = Path.Combine(desktop, "NewProgram.lnk");
        File.WriteAllText(shortcutPath, "shortcut contents");
        await controller.RefreshAsync(CancellationToken.None, showNotifications: false);
        TrackedShortcut pending = controller.State.Shortcuts.Single();
        stateStore.FailOnSaveCalls(1);

        await Assert.ThrowsAsync<StateStoreException>(
            () => controller.ApproveAsync(pending.Id, CancellationToken.None));

        Assert.IsInstanceOfType<StateStoreException>(controller.LastFailure);
        Assert.AreEqual(ShortcutStatus.Pending, controller.State.Shortcuts.Single().Status);
        Assert.AreEqual(ShortcutStatus.Pending, stateStore.Load().Shortcuts.Single().Status);
    }

    [TestMethod]
    public async Task PublicShortcuts_AreReviewableAndMovesPreserveStatus()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string baselinePublicPath = Path.Combine(publicDesktop, "ExistingShared.url");
        File.WriteAllText(baselinePublicPath, "baseline shortcut");

        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        DesktopGuardStateStore stateStore = new(data);
        using WindowsNotificationService notifications = new();
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            new ShortcutFileManager(rootProvider, stateStore, TimeSpan.Zero),
            new NoOpStartAtSignInManager(),
            notifications);
        controller.Initialize();
        TrackedShortcut existingPublic = controller.State.Shortcuts.Single();
        Assert.AreEqual(DesktopRootKind.Public, existingPublic.RootKind);
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, existingPublic.Status);

        string newPublicPath = Path.Combine(publicDesktop, "NewShared.lnk");
        File.WriteAllText(newPublicPath, "new public shortcut");
        await controller.RefreshAsync(CancellationToken.None, showNotifications: false);
        TrackedShortcut pendingPublic = controller.State.Shortcuts.Single(shortcut => shortcut.Name == "NewShared.lnk");
        Assert.AreEqual(ShortcutStatus.Pending, pendingPublic.Status);

        await controller.ApproveAsync(pendingPublic.Id, CancellationToken.None);
        Assert.IsTrue(File.Exists(newPublicPath));
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, controller.State.Shortcuts.Single(
            shortcut => shortcut.Id == pendingPublic.Id).Status);

        string userShortcutPath = Path.Combine(desktop, "UserProgram.lnk");
        File.WriteAllText(userShortcutPath, "user shortcut");
        await controller.RefreshAsync(CancellationToken.None, showNotifications: false);
        TrackedShortcut userShortcut = controller.State.Shortcuts.Single(shortcut => shortcut.Name == "UserProgram.lnk");
        await controller.ApproveAsync(userShortcut.Id, CancellationToken.None);
        await controller.MoveAsync(userShortcut.Id, DesktopRootKind.Public, CancellationToken.None);

        string sharedPath = Path.Combine(publicDesktop, "UserProgram.lnk");
        Assert.IsFalse(File.Exists(userShortcutPath));
        Assert.IsTrue(File.Exists(sharedPath));
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, controller.State.Shortcuts.Single(
            shortcut => shortcut.Id == userShortcut.Id).Status);

        await controller.MoveAsync(userShortcut.Id, DesktopRootKind.User, CancellationToken.None);
        Assert.IsFalse(File.Exists(sharedPath));
        Assert.IsTrue(File.Exists(userShortcutPath));
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, controller.State.Shortcuts.Single(
            shortcut => shortcut.Id == userShortcut.Id).Status);
    }

    [TestMethod]
    public async Task Move_WhenDestinationAppearsAfterScanLeavesNoStuckOperation()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string sourcePath = Path.Combine(desktop, "Program.lnk");
        string destinationPath = Path.Combine(publicDesktop, "Program.lnk");
        File.WriteAllText(sourcePath, "source shortcut");

        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        DesktopGuardStateStore stateStore = new(data);
        using WindowsNotificationService notifications = new();
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            new ShortcutFileManager(rootProvider, stateStore, TimeSpan.Zero),
            new NoOpStartAtSignInManager(),
            notifications);
        controller.Initialize();
        TrackedShortcut shortcut = controller.State.Shortcuts.Single();
        File.WriteAllText(destinationPath, "existing destination");

        ShortcutFileOperationException exception = await Assert.ThrowsAsync<ShortcutFileOperationException>(
            () => controller.MoveAsync(shortcut.Id, DesktopRootKind.Public, CancellationToken.None));

        Assert.AreEqual(ShortcutFileOperationFailure.DestinationConflict, exception.Failure);
        Assert.AreEqual(ShortcutOperation.None, controller.State.Shortcuts.Single().Operation);
        Assert.IsTrue(File.Exists(sourcePath));
        Assert.AreEqual("existing destination", File.ReadAllText(destinationPath));
        Assert.IsFalse(controller.IsOperationUnresolved(shortcut.Id));
    }

    [TestMethod]
    public async Task AddDesktopShortcut_CreatesApprovedLinkAndRecordsItInTheBaseline()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        DesktopGuardStateStore stateStore = new(data);
        using WindowsNotificationService notifications = new();
        ShortcutFileManager fileManager = new(
            rootProvider,
            stateStore,
            TimeSpan.Zero,
            new TestDesktopShortcutLinkWriter());
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            fileManager,
            new NoOpStartAtSignInManager(),
            notifications);
        controller.Initialize();
        Assert.IsTrue(controller.CanAddDesktopShortcut);

        await controller.AddDesktopShortcutAsync(CancellationToken.None);

        string shortcutPath = Path.Combine(desktop, DesktopShortcutCreator.LinkName);
        TrackedShortcut created = controller.State.Shortcuts.Single();
        Assert.IsTrue(File.Exists(shortcutPath));
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, created.Status);
        Assert.AreEqual(ShortcutOperation.None, created.Operation);
        Assert.IsTrue(controller.State.Roots.Single(root => root.RootKind == DesktopRootKind.User)
            .KnownPaths.Contains(shortcutPath, StringComparer.OrdinalIgnoreCase));
        Assert.IsFalse(controller.CanAddDesktopShortcut);

        await controller.RefreshAsync(CancellationToken.None);
        Assert.HasCount(0, controller.State.Events);
    }

    [TestMethod]
    public void AddDesktopShortcut_IsUnavailableWhenPublicDesktopAlreadyContainsDGLink()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string publicShortcut = Path.Combine(publicDesktop, DesktopShortcutCreator.LinkName);
        File.WriteAllText(publicShortcut, "existing Public link");
        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        DesktopGuardStateStore stateStore = new(Path.Combine(temporaryDirectory.Path, "AppData"));
        using WindowsNotificationService notifications = new();
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            new ShortcutFileManager(rootProvider, stateStore, TimeSpan.Zero, new TestDesktopShortcutLinkWriter()),
            new NoOpStartAtSignInManager(),
            notifications);

        controller.Initialize();

        Assert.IsFalse(controller.CanAddDesktopShortcut);
        Assert.IsFalse(File.Exists(Path.Combine(desktop, DesktopShortcutCreator.LinkName)));
        Assert.AreEqual("existing Public link", File.ReadAllText(publicShortcut));
    }

    [TestMethod]
    public void AddDesktopShortcut_RemainsUnavailableUntilPublicDesktopScanCompletes()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Path.Combine(temporaryDirectory.Path, "MissingPublicDesktop");
        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        DesktopGuardStateStore stateStore = new(Path.Combine(temporaryDirectory.Path, "AppData"));
        using WindowsNotificationService notifications = new();
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            new ShortcutFileManager(rootProvider, stateStore, TimeSpan.Zero, new TestDesktopShortcutLinkWriter()),
            new NoOpStartAtSignInManager(),
            notifications);

        controller.Initialize();

        Assert.IsFalse(controller.CanAddDesktopShortcut);
        Assert.IsFalse(File.Exists(Path.Combine(desktop, DesktopShortcutCreator.LinkName)));
    }

    [TestMethod]
    public async Task AddDesktopShortcut_RechecksPublicDesktopBeforeCreating()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        DesktopGuardStateStore stateStore = new(Path.Combine(temporaryDirectory.Path, "AppData"));
        using WindowsNotificationService notifications = new();
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            new ShortcutFileManager(rootProvider, stateStore, TimeSpan.Zero, new TestDesktopShortcutLinkWriter()),
            new NoOpStartAtSignInManager(),
            notifications);
        controller.Initialize();
        Assert.IsTrue(controller.CanAddDesktopShortcut);
        string publicShortcut = Path.Combine(publicDesktop, DesktopShortcutCreator.LinkName);
        File.WriteAllText(publicShortcut, "late Public link");

        ShortcutFileOperationException exception = await Assert.ThrowsAsync<ShortcutFileOperationException>(
            () => controller.AddDesktopShortcutAsync(CancellationToken.None));

        Assert.AreEqual(ShortcutFileOperationFailure.DesktopShortcutAlreadyExists, exception.Failure);
        Assert.IsFalse(File.Exists(Path.Combine(desktop, DesktopShortcutCreator.LinkName)));
        Assert.IsEmpty(controller.State.Shortcuts);
        Assert.AreEqual("late Public link", File.ReadAllText(publicShortcut));
    }

    [TestMethod]
    public async Task RestoreDesktopGuardShortcut_IsBlockedWhenTheOtherDesktopHasOne()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string userShortcut = Path.Combine(desktop, DesktopShortcutCreator.LinkName);
        File.WriteAllText(userShortcut, "original user DG link");
        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        DesktopGuardStateStore stateStore = new(Path.Combine(temporaryDirectory.Path, "AppData"));
        using WindowsNotificationService notifications = new();
        ShortcutFileManager fileManager = new(rootProvider, stateStore, TimeSpan.Zero);
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            fileManager,
            new NoOpStartAtSignInManager(),
            notifications);
        controller.Initialize();
        TrackedShortcut original = controller.State.Shortcuts.Single();
        await controller.ParkAsync(original.Id, CancellationToken.None);
        TrackedShortcut parked = controller.State.Shortcuts.Single();
        string recoveryPath = fileManager.GetRecoveryFilePath(parked);
        string publicShortcut = Path.Combine(publicDesktop, DesktopShortcutCreator.LinkName);
        File.WriteAllText(publicShortcut, "existing Public DG link");
        await controller.RefreshAsync(CancellationToken.None, showNotifications: false);

        ShortcutFileOperationException exception = await Assert.ThrowsAsync<ShortcutFileOperationException>(
            () => controller.RestoreAsync(parked.Id, CancellationToken.None));

        Assert.AreEqual(ShortcutFileOperationFailure.DesktopShortcutAlreadyExists, exception.Failure);
        Assert.IsFalse(File.Exists(userShortcut));
        Assert.IsTrue(File.Exists(recoveryPath));
        Assert.AreEqual("existing Public DG link", File.ReadAllText(publicShortcut));
        TrackedShortcut stillParked = controller.State.Shortcuts.Single(shortcut => shortcut.Id == parked.Id);
        Assert.AreEqual(ShortcutStatus.Parked, stillParked.Status);
        Assert.AreEqual(ShortcutOperation.None, stillParked.Operation);
    }

    [TestMethod]
    public async Task AddDesktopShortcut_WhenCreationFails_UpdatesListenersAfterReconcilingTheIntent()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        DesktopGuardStateStore stateStore = new(data);
        using WindowsNotificationService notifications = new();
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            new ShortcutFileManager(rootProvider, stateStore, TimeSpan.Zero, new FailingDesktopShortcutLinkWriter()),
            new NoOpStartAtSignInManager(),
            notifications);
        controller.Initialize();
        int stateChangedCount = 0;
        controller.StateChanged += () => stateChangedCount++;

        await Assert.ThrowsAsync<ShortcutFileOperationException>(
            () => controller.AddDesktopShortcutAsync(CancellationToken.None));

        Assert.AreEqual(1, stateChangedCount);
        Assert.IsTrue(controller.CanAddDesktopShortcut);
        Assert.IsEmpty(controller.State.Shortcuts);
        Assert.IsEmpty(Directory.GetDirectories(desktop, ".dg-create-*"));
    }

    [TestMethod]
    public void Initialize_CompletesHashMatchedDesktopShortcutStagingFromAnInterruptedCreation()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        DesktopGuardStateStore stateStore = new(data);
        DesktopGuardState initial = new()
        {
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.User, desktop, true, []),
                new DesktopRootCheckpoint(DesktopRootKind.Public, publicDesktop, true, []),
            ],
        };
        DesktopGuardState pending = ShortcutStateTransitions.BeginCreation(
            initial,
            desktop,
            DesktopShortcutCreator.LinkName);
        TrackedShortcut operation = pending.Shortcuts.Single();
        DesktopShortcutCreator creator = new(rootProvider, new TestDesktopShortcutLinkWriter());
        byte[]? contentHash = null;
        Assert.Throws<InterruptedCreationException>(() => creator.Create(
            operation,
            System.Windows.Forms.Application.ExecutablePath,
            hash =>
            {
                contentHash = hash;
                throw new InterruptedCreationException();
            }));
        TrackedShortcut interrupted = operation with { OperationContentHash = contentHash };
        stateStore.Save(pending with { Shortcuts = [interrupted] });

        using WindowsNotificationService notifications = new();
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            new ShortcutFileManager(rootProvider, stateStore, TimeSpan.Zero, new TestDesktopShortcutLinkWriter()),
            new NoOpStartAtSignInManager(),
            notifications);

        controller.Initialize();

        TrackedShortcut recovered = controller.State.Shortcuts.Single();
        Assert.IsTrue(File.Exists(recovered.OriginalPath));
        Assert.AreEqual(ShortcutOperation.None, recovered.Operation);
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, recovered.Status);
        Assert.IsFalse(controller.IsOperationUnresolved(recovered.Id));
        Assert.IsTrue(controller.State.Roots.Single(root => root.RootKind == DesktopRootKind.User)
            .KnownPaths.Contains(recovered.OriginalPath, StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task PublicShortcutManagement_IsDisabledUntilUserDesktopScanCompletes()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string publicShortcutPath = Path.Combine(publicDesktop, "Shared.lnk");
        File.WriteAllText(publicShortcutPath, "public shortcut");

        FixedDesktopRootProvider rootProvider = new(desktop, publicDesktop);
        DesktopGuardStateStore stateStore = new(data);
        using WindowsNotificationService notifications = new();
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            new ShortcutFileManager(rootProvider, stateStore, TimeSpan.Zero),
            new NoOpStartAtSignInManager(),
            notifications);
        controller.Initialize();
        TrackedShortcut publicShortcut = controller.State.Shortcuts.Single();

        rootProvider.UserDesktop = Path.Combine(temporaryDirectory.Path, "UnavailableDesktop");
        await controller.RefreshAsync(CancellationToken.None, showNotifications: false);

        Assert.IsFalse(controller.IsManageableShortcut(publicShortcut.Id));
        await Assert.ThrowsAsync<ShortcutFileOperationException>(
            () => controller.ApproveAsync(publicShortcut.Id, CancellationToken.None));
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, controller.State.Shortcuts.Single().Status);
        Assert.IsTrue(File.Exists(publicShortcutPath));

        rootProvider.UserDesktop = desktop;
        await controller.RefreshAsync(CancellationToken.None, showNotifications: false);

        Assert.IsTrue(controller.IsManageableShortcut(publicShortcut.Id));
    }

    private sealed class FixedDesktopRootProvider(string desktop, string publicDesktop) : IDesktopRootProvider
    {
        public string UserDesktop { get; set; } = desktop;

        public IReadOnlyList<DesktopRoot> GetRoots()
        {
            return
            [
                new DesktopRoot(DesktopRootKind.User, UserDesktop),
                new DesktopRoot(DesktopRootKind.Public, publicDesktop),
            ];
        }
    }

    private sealed class NoOpStartAtSignInManager : IStartAtSignInManager
    {
        public void SetEnabled(bool enabled, bool startMinimized)
        {
        }
    }

    private sealed class TestDesktopShortcutLinkWriter : IDesktopShortcutLinkWriter
    {
        public void Create(string linkPath, string targetPath)
        {
            File.WriteAllText(linkPath, targetPath);
        }
    }

    private sealed class FailingDesktopShortcutLinkWriter : IDesktopShortcutLinkWriter
    {
        public void Create(string linkPath, string targetPath)
        {
            throw new IOException("The test link writer failed.");
        }
    }

    private sealed class NoElevationMoveExecutor : IShortcutMoveExecutor
    {
        public bool AllowElevationWasRequested { get; private set; }

        public Task MoveAsync(
            string sourcePath,
            string destinationPath,
            Guid operationId,
            ElevatedShortcutOperationRequest elevatedRequest,
            CancellationToken cancellationToken,
            bool allowElevation = true,
            byte[]? expectedSourceContentHash = null)
        {
            AllowElevationWasRequested = allowElevation;
            ShortcutFileTransfer.Move(
                sourcePath,
                destinationPath,
                operationId,
                preserveHidden: elevatedRequest.Kind == ElevatedShortcutOperationKind.ParkToRecovery,
                expectedSourceContentHash: expectedSourceContentHash);
            return Task.CompletedTask;
        }

        public Task RetryAsync(
            string sourcePath,
            string destinationPath,
            Guid operationId,
            ElevatedShortcutOperationRequest elevatedRequest,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class InterruptedCreationException : Exception
    {
    }

    private sealed class FaultingStateStore(string dataDirectory) : IDesktopGuardStateStore
    {
        private readonly DesktopGuardStateStore _inner = new(dataDirectory);
        private HashSet<int> _failingSaveCalls = [];
        private int _saveCallCount;

        public string RecoveryDirectoryPath => _inner.RecoveryDirectoryPath;

        public DesktopGuardState Load()
        {
            return _inner.Load();
        }

        public void Save(DesktopGuardState state)
        {
            _saveCallCount++;
            if (_failingSaveCalls.Remove(_saveCallCount))
            {
                throw new StateStoreException(StateStoreFailureKind.Unavailable);
            }

            _inner.Save(state);
        }

        public void EnsureRecoveryDirectory()
        {
            _inner.EnsureRecoveryDirectory();
        }

        public void FailOnSaveCalls(params int[] calls)
        {
            _saveCallCount = 0;
            _failingSaveCalls = calls.ToHashSet();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DesktopGuard.App.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
