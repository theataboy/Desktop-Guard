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
    public async Task PublicShortcuts_AreReviewableAndMoveBetweenRootsWithExpectedStatuses()
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
        Assert.AreEqual(ShortcutStatus.Pending, controller.State.Shortcuts.Single(
            shortcut => shortcut.Id == userShortcut.Id).Status);
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
        public void SetEnabled(bool enabled)
        {
        }
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
