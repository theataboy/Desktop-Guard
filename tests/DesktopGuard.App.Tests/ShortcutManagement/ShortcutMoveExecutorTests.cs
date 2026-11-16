using DesktopGuard.App.ShortcutManagement;
using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.App.Tests.ShortcutManagement;

[TestClass]
public sealed class ShortcutMoveExecutorTests
{
    [TestMethod]
    public async Task Move_AccessDeniedInvokesOneOperationElevationBroker()
    {
        RecordingElevationBroker broker = new();
        ShortcutMoveExecutor executor = new(
            broker,
            (_, _, _, _, _) => throw new IOException("access denied", unchecked((int)0x80070005)));
        ElevatedShortcutOperationRequest request = CreateRequest();

        ShortcutFileOperationException exception = await Assert.ThrowsAsync<ShortcutFileOperationException>(
            () => executor.MoveAsync(
                "source.lnk",
                "destination.lnk",
                request.OperationId,
                request,
                CancellationToken.None));

        Assert.AreEqual(ShortcutFileOperationFailure.ElevationCancelled, exception.Failure);
        Assert.AreEqual(1, broker.CallCount);
        Assert.AreSame(request, broker.LastRequest);
    }

    [TestMethod]
    public void RecycleBinFileOperation_MapsShellAccessDeniedSourceToWin32AccessDenied()
    {
        IOException exception = RecycleBinFileOperation.CreateFailure(0x78);

        Assert.AreEqual(5, exception.HResult & 0xFFFF);
        Assert.IsTrue(ShortcutFileTransfer.IsAccessDenied(exception));
    }

    [TestMethod]
    public async Task Move_FileLockDoesNotInvokeElevationBroker()
    {
        RecordingElevationBroker broker = new();
        ShortcutMoveExecutor executor = new(
            broker,
            (_, _, _, _, _) => throw new IOException("sharing violation", unchecked((int)0x80070020)));
        ElevatedShortcutOperationRequest request = CreateRequest();

        IOException exception = await Assert.ThrowsAsync<IOException>(
            () => executor.MoveAsync(
                "source.lnk",
                "destination.lnk",
                request.OperationId,
                request,
                CancellationToken.None));

        Assert.AreEqual(32, exception.HResult & 0xFFFF);
        Assert.AreEqual(0, broker.CallCount);
    }

    [TestMethod]
    public async Task Retry_AccessDeniedMarksElevatedRequestAsRetry()
    {
        RecordingElevationBroker broker = new();
        ShortcutMoveExecutor executor = new(
            broker,
            (_, _, _, _, _) => throw new IOException("access denied", unchecked((int)0x80070005)));
        ElevatedShortcutOperationRequest request = CreateRequest();

        await Assert.ThrowsAsync<ShortcutFileOperationException>(
            () => executor.RetryAsync(
                "missing-source.lnk",
                "destination.lnk",
                request.OperationId,
                request,
                CancellationToken.None));

        Assert.IsNotNull(broker.LastRequest);
        Assert.IsTrue(broker.LastRequest.IsRetry);
    }

    [TestMethod]
    public void Move_IdenticalExistingDestinationRemainsAConflict()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string sourcePath = Path.Combine(temporaryDirectory.Path, "source.lnk");
        string destinationPath = Path.Combine(temporaryDirectory.Path, "destination.lnk");
        File.WriteAllText(sourcePath, "same shortcut bytes");
        File.WriteAllText(destinationPath, "same shortcut bytes");

        ShortcutFileOperationException exception = Assert.Throws<ShortcutFileOperationException>(
            () => ShortcutFileTransfer.Move(sourcePath, destinationPath, Guid.NewGuid()));

        Assert.AreEqual(ShortcutFileOperationFailure.DestinationConflict, exception.Failure);
        Assert.IsTrue(File.Exists(sourcePath));
        Assert.IsTrue(File.Exists(destinationPath));
    }

    [TestMethod]
    public void RetryMove_IdenticalExistingDestinationCompletesPendingMove()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string sourcePath = Path.Combine(temporaryDirectory.Path, "source.lnk");
        string destinationPath = Path.Combine(temporaryDirectory.Path, "destination.lnk");
        File.WriteAllText(sourcePath, "same shortcut bytes");
        File.WriteAllText(destinationPath, "same shortcut bytes");

        ShortcutFileTransfer.Move(sourcePath, destinationPath, Guid.NewGuid(), allowExistingDestination: true);

        Assert.IsFalse(File.Exists(sourcePath));
        Assert.AreEqual("same shortcut bytes", File.ReadAllText(destinationPath));
    }

    [TestMethod]
    public void RetryMove_ReparseDestinationNeverRemovesTheSource()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string sourcePath = Path.Combine(temporaryDirectory.Path, "source.lnk");
        string destinationPath = Path.Combine(temporaryDirectory.Path, "destination.lnk");
        string targetPath = Path.Combine(temporaryDirectory.Path, "target.lnk");
        File.WriteAllText(sourcePath, "same shortcut bytes");
        File.WriteAllText(targetPath, "same shortcut bytes");
        try
        {
            File.CreateSymbolicLink(destinationPath, targetPath);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            || exception is IOException && (exception.HResult & 0xFFFF) == 1314)
        {
            Assert.Inconclusive("Creating a test symbolic link requires Windows Developer Mode or link privileges.");
        }

        ShortcutFileOperationException failure = Assert.Throws<ShortcutFileOperationException>(() =>
            ShortcutFileTransfer.Move(sourcePath, destinationPath, Guid.NewGuid(), allowExistingDestination: true));

        Assert.AreEqual(ShortcutFileOperationFailure.InvalidPath, failure.Failure);
        Assert.AreEqual("same shortcut bytes", File.ReadAllText(sourcePath));
        Assert.AreNotEqual((FileAttributes)0, File.GetAttributes(destinationPath) & FileAttributes.ReparsePoint);
        Assert.AreEqual("same shortcut bytes", File.ReadAllText(targetPath));
    }

    [TestMethod]
    public void Move_SourceAndDestinationCannotBeReplacedBeforeVerifiedSourceIsRemoved()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string sourcePath = Path.Combine(temporaryDirectory.Path, "source.lnk");
        string destinationPath = Path.Combine(temporaryDirectory.Path, "destination.lnk");
        File.WriteAllText(sourcePath, "original shortcut bytes");
        bool sourceWasProtected = false;
        bool destinationWasProtected = false;

        ShortcutFileTransfer.Move(
            sourcePath,
            destinationPath,
            Guid.NewGuid(),
            beforeSourceDeletion: () =>
            {
                sourceWasProtected = Assert.Throws<IOException>(() =>
                    File.Move(sourcePath, Path.Combine(temporaryDirectory.Path, "replacement.lnk"))) is not null;
                destinationWasProtected = Assert.Throws<IOException>(() =>
                    File.Move(destinationPath, Path.Combine(temporaryDirectory.Path, "other.lnk"))) is not null;
            });

        Assert.IsTrue(sourceWasProtected);
        Assert.IsTrue(destinationWasProtected);
        Assert.IsFalse(File.Exists(sourcePath));
        Assert.AreEqual("original shortcut bytes", File.ReadAllText(destinationPath));
    }

    [TestMethod]
    public void Move_PosixDeleteRemovesSourceNameWhileExistingSharedHandleRemainsOpen()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string sourcePath = Path.Combine(temporaryDirectory.Path, "source.lnk");
        string destinationPath = Path.Combine(temporaryDirectory.Path, "Desktop", "source.lnk");
        File.WriteAllText(sourcePath, "shortcut bytes");
        using FileStream existingReader = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        ShortcutFileTransfer.Move(sourcePath, destinationPath, Guid.NewGuid());

        Assert.IsFalse(File.Exists(sourcePath));
        ShortcutFileTransfer.ValidateCompletedMove(sourcePath, destinationPath);
        existingReader.Position = 0;
        using StreamReader reader = new(existingReader, leaveOpen: true);
        Assert.AreEqual("shortcut bytes", reader.ReadToEnd());
    }

    [TestMethod]
    public void ParkTransfer_PreservesHiddenAttributeInRecovery()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string sourcePath = Path.Combine(temporaryDirectory.Path, "source.lnk");
        string destinationPath = Path.Combine(temporaryDirectory.Path, "Recovery", "source.lnk");
        File.WriteAllText(sourcePath, "shortcut bytes");
        File.SetAttributes(sourcePath, File.GetAttributes(sourcePath) | FileAttributes.Hidden);

        ShortcutFileTransfer.Move(sourcePath, destinationPath, Guid.NewGuid(), preserveHidden: true);

        Assert.AreNotEqual((FileAttributes)0, File.GetAttributes(destinationPath) & FileAttributes.Hidden);
    }

    [TestMethod]
    public void Move_ReadOnlySourceIsRemovedAfterDestinationIsPublished()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string sourcePath = Path.Combine(temporaryDirectory.Path, "source.lnk");
        string destinationPath = Path.Combine(temporaryDirectory.Path, "Desktop", "source.lnk");
        File.WriteAllText(sourcePath, "read-only shortcut bytes");
        File.SetAttributes(sourcePath, File.GetAttributes(sourcePath) | FileAttributes.ReadOnly);

        ShortcutFileTransfer.Move(sourcePath, destinationPath, Guid.NewGuid());

        Assert.IsFalse(File.Exists(sourcePath));
        Assert.AreEqual("read-only shortcut bytes", File.ReadAllText(destinationPath));
    }

    [TestMethod]
    public void DesktopTransfer_ClearsHiddenAttributeWhenPublishingShortcut()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string sourcePath = Path.Combine(temporaryDirectory.Path, "source.lnk");
        string destinationPath = Path.Combine(temporaryDirectory.Path, "Desktop", "source.lnk");
        File.WriteAllText(sourcePath, "shortcut bytes");
        File.SetAttributes(sourcePath, File.GetAttributes(sourcePath) | FileAttributes.Hidden);

        ShortcutFileTransfer.Move(sourcePath, destinationPath, Guid.NewGuid());

        Assert.AreEqual((FileAttributes)0, File.GetAttributes(destinationPath) & FileAttributes.Hidden);
    }

    [TestMethod]
    public void SetHiddenAttribute_ClearsOnlyHiddenAndLeavesFileWithValidAttributes()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string path = Path.Combine(temporaryDirectory.Path, "shortcut.lnk");
        File.WriteAllText(path, "shortcut bytes");
        File.SetAttributes(path, FileAttributes.Hidden);

        ShortcutFileTransfer.SetHiddenAttribute(path, isHidden: false);

        FileAttributes attributes = File.GetAttributes(path);
        Assert.AreEqual((FileAttributes)0, attributes & FileAttributes.Hidden);
        Assert.AreNotEqual((FileAttributes)0, attributes);
    }

    [TestMethod]
    public void Move_IdenticalDestinationWithEmptyOperationDirectoryResumesCurrentOperation()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string sourcePath = Path.Combine(temporaryDirectory.Path, "source.lnk");
        string destinationPath = Path.Combine(temporaryDirectory.Path, "destination.lnk");
        Guid operationId = Guid.NewGuid();
        Directory.CreateDirectory(ShortcutFileTransfer.GetOperationDirectory(destinationPath, operationId));
        File.WriteAllText(sourcePath, "same shortcut bytes");
        File.WriteAllText(destinationPath, "same shortcut bytes");

        ShortcutFileTransfer.Move(sourcePath, destinationPath, operationId);

        Assert.IsFalse(File.Exists(sourcePath));
        Assert.AreEqual("same shortcut bytes", File.ReadAllText(destinationPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"DesktopGuard-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private static ElevatedShortcutOperationRequest CreateRequest()
    {
        return new ElevatedShortcutOperationRequest(
            ElevatedShortcutOperationKind.MoveBetweenDesktops,
            Guid.NewGuid(),
            Guid.NewGuid(),
            RecoveryFolderId: null,
            Name: "Program.lnk",
            SourceRootKind: DesktopRootKind.User,
            DestinationRootKind: DesktopRootKind.Public);
    }

    private sealed class RecordingElevationBroker : IShortcutElevationBroker
    {
        public int CallCount { get; private set; }

        public ElevatedShortcutOperationRequest? LastRequest { get; private set; }

        public Task ExecuteAsync(ElevatedShortcutOperationRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.ElevationCancelled);
        }
    }
}
