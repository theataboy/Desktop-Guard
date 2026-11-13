using DesktopGuard.App.DesktopScanning;
using DesktopGuard.App.Persistence;
using DesktopGuard.App.ShortcutManagement;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;
using DesktopGuard.Core.State;

namespace DesktopGuard.App.Tests.ShortcutManagement;

[TestClass]
public sealed class ShortcutFileManagerTests
{
    [TestMethod]
    public async Task ParkAndRestore_MovesOnlyTheShortcutAndPreservesItsContents()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string shortcutPath = Path.Combine(desktop, "Program.lnk");
        const string contents = "untrusted shortcut bytes";
        File.WriteAllText(shortcutPath, contents);
        ShortcutFileManager manager = CreateManager(desktop, data);
        TrackedShortcut shortcut = CreateShortcut(desktop, "Program.lnk");
        DesktopGuardState parking = ShortcutStateTransitions.BeginParking(
            new DesktopGuardState { Shortcuts = [shortcut] },
            shortcut.Id,
            Guid.NewGuid());

        await manager.ParkAsync(parking.Shortcuts.Single(), CancellationToken.None);

        string recoveryPath = manager.GetRecoveryFilePath(parking.Shortcuts.Single());
        Assert.IsFalse(File.Exists(shortcutPath));
        Assert.IsTrue(File.Exists(recoveryPath));
        Assert.AreEqual(contents, File.ReadAllText(recoveryPath));
        Assert.AreEqual(
            PendingOperationResolution.OperationMovedFile,
            manager.ResolveInterruptedOperation(parking.Shortcuts.Single()));

        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(parking, shortcut.Id);
        DesktopGuardState restoring = ShortcutStateTransitions.BeginRestore(parked, shortcut.Id);
        await manager.RestoreAsync(restoring.Shortcuts.Single(), CancellationToken.None);

        Assert.IsTrue(File.Exists(shortcutPath));
        Assert.AreEqual(contents, File.ReadAllText(shortcutPath));
        Assert.IsFalse(File.Exists(recoveryPath));
        Assert.AreEqual(
            PendingOperationResolution.OperationMovedFile,
            manager.ResolveInterruptedOperation(restoring.Shortcuts.Single()));
    }

    [TestMethod]
    public async Task PublicShortcut_CanBeParkedAndRestoredWithoutChangingItsDecision()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string publicPath = Path.Combine(publicDesktop, "Shared.url");
        const string contents = "untrusted public shortcut bytes";
        File.WriteAllText(publicPath, contents);
        ShortcutFileManager manager = CreateManager(userDesktop, publicDesktop, data);
        TrackedShortcut shortcut = CreateShortcut(publicDesktop, "Shared.url", DesktopRootKind.Public) with
        {
            Status = ShortcutStatus.ApprovedVisible,
        };
        DesktopGuardState baseline = StateWithRoots(shortcut, userDesktop, publicDesktop);
        DesktopGuardState parking = ShortcutStateTransitions.BeginParking(baseline, shortcut.Id, Guid.NewGuid());

        await manager.ParkAsync(parking.Shortcuts.Single(), CancellationToken.None);
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(parking, shortcut.Id);
        DesktopGuardState restoring = ShortcutStateTransitions.BeginRestore(parked, shortcut.Id);
        await manager.RestoreAsync(restoring.Shortcuts.Single(), CancellationToken.None);

        Assert.IsTrue(File.Exists(publicPath));
        Assert.AreEqual(contents, File.ReadAllText(publicPath));
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, ShortcutStateTransitions.CompleteRestore(
            restoring,
            shortcut.Id).Shortcuts.Single().Status);
    }

    [TestMethod]
    public async Task MoveBetweenDesktops_UsesDestinationAndReturnsPendingFromPublic()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string publicPath = Path.Combine(publicDesktop, "Shared.lnk");
        string userPath = Path.Combine(userDesktop, "Shared.lnk");
        const string contents = "opaque shortcut bytes";
        File.WriteAllText(publicPath, contents);
        ShortcutFileManager manager = CreateManager(userDesktop, publicDesktop, data);
        TrackedShortcut shortcut = CreateShortcut(publicDesktop, "Shared.lnk", DesktopRootKind.Public);
        DesktopGuardState baseline = StateWithRoots(shortcut, userDesktop, publicDesktop);
        DesktopGuardState movingToUser = ShortcutStateTransitions.BeginMove(
            baseline,
            shortcut.Id,
            DesktopRootKind.User,
            userDesktop);

        await manager.MoveBetweenDesktopsAsync(movingToUser.Shortcuts.Single(), CancellationToken.None);
        DesktopGuardState moved = ShortcutStateTransitions.CompleteMove(movingToUser, shortcut.Id);

        Assert.IsFalse(File.Exists(publicPath));
        Assert.IsTrue(File.Exists(userPath));
        Assert.AreEqual(contents, File.ReadAllText(userPath));
        Assert.AreEqual(ShortcutStatus.Pending, moved.Shortcuts.Single().Status);
        Assert.AreEqual(DesktopRootKind.User, moved.Shortcuts.Single().RootKind);
    }

    [TestMethod]
    public async Task RetryPendingOperation_CleansOnlyItsOwnIncompleteStagingFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string sourcePath = Path.Combine(desktop, "Program.lnk");
        File.WriteAllText(sourcePath, "shortcut bytes");
        ShortcutFileManager manager = CreateManager(desktop, data);
        TrackedShortcut shortcut = CreateShortcut(desktop, "Program.lnk");
        DesktopGuardState parking = ShortcutStateTransitions.BeginParking(
            new DesktopGuardState { Shortcuts = [shortcut] },
            shortcut.Id,
            Guid.NewGuid());
        string recoveryPath = manager.GetRecoveryFilePath(parking.Shortcuts.Single());
        string operationDirectory = ShortcutFileTransfer.GetOperationDirectory(
            recoveryPath,
            parking.Shortcuts.Single().OperationId!.Value);
        Directory.CreateDirectory(operationDirectory);
        File.WriteAllText(Path.Combine(operationDirectory, "transfer.tmp"), "partial");

        Assert.AreEqual(
            PendingOperationResolution.Ambiguous,
            manager.ResolveInterruptedOperation(parking.Shortcuts.Single()));
        await manager.RetryPendingOperationAsync(parking.Shortcuts.Single(), CancellationToken.None);

        Assert.IsFalse(File.Exists(sourcePath));
        Assert.AreEqual("shortcut bytes", File.ReadAllText(recoveryPath));
        Assert.IsFalse(Directory.Exists(operationDirectory));
    }

    [TestMethod]
    public async Task Restore_WhenOriginalPathIsOccupied_PreservesBothFiles()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string shortcutPath = Path.Combine(desktop, "Program.url");
        File.WriteAllText(shortcutPath, "parked file");
        ShortcutFileManager manager = CreateManager(desktop, data);
        TrackedShortcut shortcut = CreateShortcut(desktop, "Program.url");
        DesktopGuardState parking = ShortcutStateTransitions.BeginParking(
            new DesktopGuardState { Shortcuts = [shortcut] },
            shortcut.Id,
            Guid.NewGuid());
        await manager.ParkAsync(parking.Shortcuts.Single(), CancellationToken.None);
        string recoveryPath = manager.GetRecoveryFilePath(parking.Shortcuts.Single());
        File.WriteAllText(shortcutPath, "replacement at original path");
        DesktopGuardState parked = ShortcutStateTransitions.CompleteParking(parking, shortcut.Id);
        DesktopGuardState restoring = ShortcutStateTransitions.BeginRestore(parked, shortcut.Id);

        ShortcutFileOperationException exception = await Assert.ThrowsAsync<ShortcutFileOperationException>(
            () => manager.RestoreAsync(restoring.Shortcuts.Single(), CancellationToken.None));

        Assert.AreEqual(ShortcutFileOperationFailure.DestinationConflict, exception.Failure);
        Assert.AreEqual("replacement at original path", File.ReadAllText(shortcutPath));
        Assert.AreEqual("parked file", File.ReadAllText(recoveryPath));
    }

    [TestMethod]
    public void ResolveInterruptedParking_WhenOnlyOriginalFileExists_ReportsNoMove()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        File.WriteAllText(Path.Combine(desktop, "Program.lnk"), "shortcut");
        ShortcutFileManager manager = CreateManager(desktop, Path.Combine(temporaryDirectory.Path, "AppData"));
        TrackedShortcut shortcut = CreateShortcut(desktop, "Program.lnk");
        DesktopGuardState parking = ShortcutStateTransitions.BeginParking(
            new DesktopGuardState { Shortcuts = [shortcut] },
            shortcut.Id,
            Guid.NewGuid());

        PendingOperationResolution result = manager.ResolveInterruptedOperation(parking.Shortcuts.Single());

        Assert.AreEqual(PendingOperationResolution.OperationDidNotMoveFile, result);
    }

    [TestMethod]
    public void ResolveInterruptedParking_WhenRecoveryFolderIsUnsafe_ReportsAmbiguous()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string recoveryRoot = Directory.CreateDirectory(Path.Combine(data, "Recovery")).FullName;
        File.WriteAllText(Path.Combine(desktop, "Program.lnk"), "shortcut");
        TrackedShortcut shortcut = CreateShortcut(desktop, "Program.lnk");
        DesktopGuardState parking = ShortcutStateTransitions.BeginParking(
            new DesktopGuardState { Shortcuts = [shortcut] },
            shortcut.Id,
            Guid.NewGuid());
        File.WriteAllText(
            Path.Combine(recoveryRoot, parking.Shortcuts.Single().RecoveryFolderId!.Value.ToString("N")),
            "unexpected file instead of recovery directory");
        ShortcutFileManager manager = CreateManager(desktop, data);

        PendingOperationResolution result = manager.ResolveInterruptedOperation(parking.Shortcuts.Single());

        Assert.AreEqual(PendingOperationResolution.Ambiguous, result);
    }

    [TestMethod]
    public async Task Park_RejectsNonShortcutAndTraversalPaths()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        ShortcutFileManager manager = CreateManager(desktop, data);
        TrackedShortcut nonShortcut = CreateShortcut(desktop, "Notes.txt");
        DesktopGuardState nonShortcutParking = ShortcutStateTransitions.BeginParking(
            new DesktopGuardState { Shortcuts = [nonShortcut] },
            nonShortcut.Id,
            Guid.NewGuid());

        ShortcutFileOperationException nonShortcutException = await Assert.ThrowsAsync<ShortcutFileOperationException>(
            () => manager.ParkAsync(nonShortcutParking.Shortcuts.Single(), CancellationToken.None));
        Assert.AreEqual(ShortcutFileOperationFailure.InvalidState, nonShortcutException.Failure);

        TrackedShortcut shortcut = CreateShortcut(desktop, "Program.lnk") with
        {
            OriginalPath = Path.Combine(temporaryDirectory.Path, "outside.lnk"),
        };
        DesktopGuardState traversalParking = ShortcutStateTransitions.BeginParking(
            new DesktopGuardState { Shortcuts = [shortcut] },
            shortcut.Id,
            Guid.NewGuid());
        ShortcutFileOperationException pathException = await Assert.ThrowsAsync<ShortcutFileOperationException>(
            () => manager.ParkAsync(traversalParking.Shortcuts.Single(), CancellationToken.None));
        Assert.AreEqual(ShortcutFileOperationFailure.InvalidPath, pathException.Failure);
    }

    [TestMethod]
    public async Task Park_WhenSourceChangesDuringStabilityCheck_LeavesItOnDesktop()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string data = Path.Combine(temporaryDirectory.Path, "AppData");
        string shortcutPath = Path.Combine(desktop, "Program.lnk");
        File.WriteAllText(shortcutPath, "initial");
        IDesktopRootProvider provider = new FixedDesktopRootProvider(desktop);
        ShortcutFileManager manager = new(
            provider,
            new DesktopGuardStateStore(data),
            TimeSpan.FromMilliseconds(150));
        TrackedShortcut shortcut = CreateShortcut(desktop, "Program.lnk");
        DesktopGuardState parking = ShortcutStateTransitions.BeginParking(
            new DesktopGuardState { Shortcuts = [shortcut] },
            shortcut.Id,
            Guid.NewGuid());

        Task parkTask = manager.ParkAsync(parking.Shortcuts.Single(), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(25));
        File.AppendAllText(shortcutPath, " changed");

        ShortcutFileOperationException exception = await Assert.ThrowsAsync<ShortcutFileOperationException>(
            () => parkTask);

        Assert.AreEqual(ShortcutFileOperationFailure.FileNotStable, exception.Failure);
        Assert.IsTrue(File.Exists(shortcutPath));
        Assert.AreEqual("initial changed", File.ReadAllText(shortcutPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(data, "Recovery")));
    }

    private static ShortcutFileManager CreateManager(string desktop, string dataDirectory)
    {
        IDesktopRootProvider provider = new FixedDesktopRootProvider(desktop);
        DesktopGuardStateStore store = new(dataDirectory);
        return new ShortcutFileManager(provider, store, TimeSpan.Zero);
    }

    private static ShortcutFileManager CreateManager(string userDesktop, string publicDesktop, string dataDirectory)
    {
        IDesktopRootProvider provider = new FixedDesktopRootProvider(userDesktop, publicDesktop);
        DesktopGuardStateStore store = new(dataDirectory);
        return new ShortcutFileManager(provider, store, TimeSpan.Zero);
    }

    private static TrackedShortcut CreateShortcut(
        string desktop,
        string name,
        DesktopRootKind rootKind = DesktopRootKind.User)
    {
        return new TrackedShortcut(
            Guid.NewGuid(),
            rootKind,
            desktop,
            name,
            Path.Combine(desktop, name),
            ShortcutStatus.Pending,
            StatusBeforeParking: null,
            Operation: ShortcutOperation.None,
            RecoveryFolderId: null);
    }

    private static DesktopGuardState StateWithRoots(
        TrackedShortcut shortcut,
        string userDesktop,
        string publicDesktop)
    {
        return new DesktopGuardState
        {
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.User, userDesktop, true, []),
                new DesktopRootCheckpoint(DesktopRootKind.Public, publicDesktop, true, [shortcut.OriginalPath]),
            ],
            Shortcuts = [shortcut],
        };
    }

    private sealed class FixedDesktopRootProvider(string desktop, string? publicDesktop = null) : IDesktopRootProvider
    {
        public IReadOnlyList<DesktopRoot> GetRoots()
        {
            return publicDesktop is null
                ? [new DesktopRoot(DesktopRootKind.User, desktop)]
                : [
                    new DesktopRoot(DesktopRootKind.User, desktop),
                    new DesktopRoot(DesktopRootKind.Public, publicDesktop),
                ];
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
