using System.Security.Cryptography;
using System.Text;
using DesktopGuard.App.DesktopScanning;
using DesktopGuard.App.ShortcutManagement;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;
using DesktopGuard.Core.State;

namespace DesktopGuard.App.Tests.ShortcutManagement;

[TestClass]
public sealed class DesktopShortcutCreatorTests
{
    [TestMethod]
    public void Create_PersistsHashBeforePublishingTheShortcut()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string targetPath = Path.Combine(temporaryDirectory.Path, "DesktopGuard.App.exe");
        File.WriteAllText(targetPath, "application bytes");
        DesktopShortcutCreator creator = new(new FixedDesktopRootProvider(userRoot, publicRoot), new TestLinkWriter());
        TrackedShortcut shortcut = BeginCreation(userRoot);
        string destination = Path.Combine(userRoot, DesktopShortcutCreator.LinkName);
        byte[]? persistedHash = null;

        creator.Create(shortcut, targetPath, hash =>
        {
            persistedHash = hash;
            Assert.IsFalse(File.Exists(destination));
        });

        Assert.IsTrue(File.Exists(destination));
        CollectionAssert.AreEqual(SHA256.HashData(File.ReadAllBytes(destination)), persistedHash);
        Assert.HasCount(0, Directory.GetDirectories(userRoot, ".dg-create-*"));
    }

    [TestMethod]
    public void ResolveInterruptedOperation_PublishesOnlyHashMatchedStagingContent()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string targetPath = Path.Combine(temporaryDirectory.Path, "DesktopGuard.App.exe");
        File.WriteAllText(targetPath, "application bytes");
        DesktopShortcutCreator creator = new(new FixedDesktopRootProvider(userRoot, publicRoot), new TestLinkWriter());
        TrackedShortcut shortcut = BeginCreation(userRoot);
        byte[]? expectedHash = null;

        Assert.Throws<HashPersistedException>(() => creator.Create(shortcut, targetPath, hash =>
        {
            expectedHash = hash;
            throw new HashPersistedException();
        }));
        TrackedShortcut persisted = shortcut with { OperationContentHash = expectedHash };

        PendingOperationResolution resolution = creator.ResolveInterruptedOperation(persisted);

        Assert.AreEqual(PendingOperationResolution.OperationMovedFile, resolution);
        Assert.IsTrue(File.Exists(persisted.OriginalPath));
        CollectionAssert.AreEqual(expectedHash, SHA256.HashData(File.ReadAllBytes(persisted.OriginalPath)));
    }

    [TestMethod]
    public void ResolveInterruptedOperation_RecognizesPublishedLinkAndRemovesItsEmptyStageDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string targetPath = Path.Combine(temporaryDirectory.Path, "DesktopGuard.App.exe");
        File.WriteAllText(targetPath, "application bytes");
        DesktopShortcutCreator creator = new(new FixedDesktopRootProvider(userRoot, publicRoot), new TestLinkWriter());
        TrackedShortcut shortcut = BeginCreation(userRoot);
        byte[]? expectedHash = null;
        creator.Create(shortcut, targetPath, hash => expectedHash = hash);
        string stageDirectory = Path.Combine(userRoot, ".dg-create-" + shortcut.OperationId!.Value.ToString("N"));
        Directory.CreateDirectory(stageDirectory);
        new DirectoryInfo(stageDirectory).Attributes |= FileAttributes.Hidden;

        PendingOperationResolution resolution = creator.ResolveInterruptedOperation(
            shortcut with { OperationContentHash = expectedHash });

        Assert.AreEqual(PendingOperationResolution.OperationMovedFile, resolution);
        Assert.IsTrue(File.Exists(shortcut.OriginalPath));
        Assert.IsFalse(Directory.Exists(stageDirectory));
    }

    [TestMethod]
    public void Retry_CleansOnlyItsSingleStagingFileAndRecreatesTheShortcut()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string targetPath = Path.Combine(temporaryDirectory.Path, "DesktopGuard.App.exe");
        File.WriteAllText(targetPath, "application bytes");
        TestLinkWriter writer = new();
        DesktopShortcutCreator creator = new(new FixedDesktopRootProvider(userRoot, publicRoot), writer);
        TrackedShortcut shortcut = BeginCreation(userRoot);

        Assert.Throws<HashPersistedException>(() => creator.Create(shortcut, targetPath, _ => throw new HashPersistedException()));
        Assert.AreEqual(PendingOperationResolution.Ambiguous, creator.ResolveInterruptedOperation(shortcut));
        byte[]? persistedHash = null;

        creator.Retry(shortcut, targetPath, hash => persistedHash = hash);

        Assert.AreEqual(2, writer.CreateCount);
        Assert.IsTrue(File.Exists(shortcut.OriginalPath));
        CollectionAssert.AreEqual(persistedHash, SHA256.HashData(File.ReadAllBytes(shortcut.OriginalPath)));
        Assert.HasCount(0, Directory.GetDirectories(userRoot, ".dg-create-*"));
    }

    [TestMethod]
    public void Retry_PreservesUnexpectedStagingContents()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string targetPath = Path.Combine(temporaryDirectory.Path, "DesktopGuard.App.exe");
        File.WriteAllText(targetPath, "application bytes");
        TestLinkWriter writer = new();
        DesktopShortcutCreator creator = new(new FixedDesktopRootProvider(userRoot, publicRoot), writer);
        TrackedShortcut shortcut = BeginCreation(userRoot);
        Assert.Throws<HashPersistedException>(() => creator.Create(shortcut, targetPath, _ => throw new HashPersistedException()));
        string operationDirectory = Directory.GetDirectories(userRoot, ".dg-create-*").Single();
        string extraPath = Path.Combine(operationDirectory, "user-data.bin");
        File.WriteAllText(extraPath, "preserve me");

        Assert.Throws<ShortcutFileOperationException>(() => creator.Retry(shortcut, targetPath, _ => { }));

        Assert.AreEqual("preserve me", File.ReadAllText(extraPath));
        Assert.IsFalse(File.Exists(shortcut.OriginalPath));
        Assert.AreEqual(1, writer.CreateCount);
    }

    [TestMethod]
    public void EnsureDestinationAvailable_ReportsCollisionWithoutChangingExistingFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string destination = Path.Combine(userRoot, DesktopShortcutCreator.LinkName);
        File.WriteAllText(destination, "keep this link");
        DesktopShortcutCreator creator = new(new FixedDesktopRootProvider(userRoot, publicRoot), new TestLinkWriter());

        ShortcutFileOperationException exception = Assert.Throws<ShortcutFileOperationException>(
            () => creator.EnsureDestinationAvailable(userRoot));

        Assert.AreEqual(ShortcutFileOperationFailure.DestinationConflict, exception.Failure);
        Assert.AreEqual("keep this link", File.ReadAllText(destination));
    }

    [TestMethod]
    public void EnsureDestinationAvailable_RejectsDesktopGuardShortcutOnPublicDesktop()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string publicShortcut = Path.Combine(publicRoot, DesktopShortcutCreator.LinkName);
        File.WriteAllText(publicShortcut, "keep this public link");
        DesktopShortcutCreator creator = new(new FixedDesktopRootProvider(userRoot, publicRoot), new TestLinkWriter());

        ShortcutFileOperationException exception = Assert.Throws<ShortcutFileOperationException>(
            () => creator.EnsureDestinationAvailable(userRoot));

        Assert.AreEqual(ShortcutFileOperationFailure.DesktopShortcutAlreadyExists, exception.Failure);
        Assert.IsFalse(File.Exists(Path.Combine(userRoot, DesktopShortcutCreator.LinkName)));
        Assert.AreEqual("keep this public link", File.ReadAllText(publicShortcut));
    }

    [TestMethod]
    public void Create_RechecksPublicDesktopBeforePublishing()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string targetPath = Path.Combine(temporaryDirectory.Path, "DesktopGuard.App.exe");
        string publicShortcut = Path.Combine(publicRoot, DesktopShortcutCreator.LinkName);
        File.WriteAllText(targetPath, "application bytes");
        DesktopShortcutCreator creator = new(
            new FixedDesktopRootProvider(userRoot, publicRoot),
            new PublicShortcutOnWriteLinkWriter(publicShortcut));
        TrackedShortcut shortcut = BeginCreation(userRoot);

        ShortcutFileOperationException exception = Assert.Throws<ShortcutFileOperationException>(
            () => creator.Create(shortcut, targetPath, _ => { }));

        Assert.AreEqual(ShortcutFileOperationFailure.DesktopShortcutAlreadyExists, exception.Failure);
        Assert.IsFalse(File.Exists(shortcut.OriginalPath));
        Assert.AreEqual("external Public link", File.ReadAllText(publicShortcut));
    }

    [TestMethod]
    public void WindowsShortcutCreation_PublishesLinkThroughTheStagingPath()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string targetPath = Path.Combine(temporaryDirectory.Path, "DesktopGuard.App.exe");
        File.WriteAllText(targetPath, "application bytes");
        DesktopShortcutCreator creator = new(new FixedDesktopRootProvider(userRoot, publicRoot), new WindowsDesktopShortcutLinkWriter());
        TrackedShortcut shortcut = BeginCreation(userRoot);
        byte[]? persistedHash = null;

        creator.Create(shortcut, targetPath, hash => persistedHash = hash);

        Assert.IsTrue(File.Exists(shortcut.OriginalPath));
        Assert.AreEqual(DesktopEntryKind.Shortcut, DesktopEntryClassifier.Classify(Path.GetFileName(shortcut.OriginalPath)));
        CollectionAssert.AreEqual(persistedHash, SHA256.HashData(File.ReadAllBytes(shortcut.OriginalPath)));
        Assert.HasCount(0, Directory.GetDirectories(userRoot, ".dg-create-*"));
    }

    private static TrackedShortcut BeginCreation(string userRoot)
    {
        DesktopGuardState state = new()
        {
            Roots = [new DesktopRootCheckpoint(DesktopRootKind.User, userRoot, true, [])],
        };
        return ShortcutStateTransitions.BeginCreation(state, userRoot, DesktopShortcutCreator.LinkName).Shortcuts.Single();
    }

    private sealed class FixedDesktopRootProvider(string userRoot, string publicRoot) : IDesktopRootProvider
    {
        public IReadOnlyList<DesktopRoot> GetRoots()
        {
            return
            [
                new DesktopRoot(DesktopRootKind.User, userRoot),
                new DesktopRoot(DesktopRootKind.Public, publicRoot),
            ];
        }
    }

    private sealed class TestLinkWriter : IDesktopShortcutLinkWriter
    {
        public int CreateCount { get; private set; }

        public void Create(string linkPath, string targetPath)
        {
            CreateCount++;
            File.WriteAllBytes(linkPath, Encoding.UTF8.GetBytes(targetPath));
        }
    }

    private sealed class PublicShortcutOnWriteLinkWriter(string publicShortcutPath) : IDesktopShortcutLinkWriter
    {
        public void Create(string linkPath, string targetPath)
        {
            File.WriteAllBytes(linkPath, Encoding.UTF8.GetBytes(targetPath));
            File.WriteAllText(publicShortcutPath, "external Public link");
        }
    }

    private sealed class HashPersistedException : Exception
    {
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
