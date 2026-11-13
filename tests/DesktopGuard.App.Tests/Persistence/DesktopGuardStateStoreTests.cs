using System.Text.Json;
using DesktopGuard.App.Persistence;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;
using DesktopGuard.Core.State;

namespace DesktopGuard.App.Tests.Persistence;

[TestClass]
public sealed class DesktopGuardStateStoreTests
{
    [TestMethod]
    public void Load_WhenStateDoesNotExist_ReturnsDefaultState()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DesktopGuardStateStore store = new(temporaryDirectory.Path);

        DesktopGuardState state = store.Load();

        Assert.AreEqual(DesktopGuardState.CurrentSchemaVersion, state.SchemaVersion);
        Assert.IsTrue(state.StartAtSignIn);
        Assert.IsTrue(state.StartMinimized);
        Assert.IsEmpty(state.Roots);
        Assert.IsEmpty(state.Shortcuts);
        Assert.IsEmpty(state.Events);
    }

    [TestMethod]
    public void SaveAndLoad_RoundTripsState()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DesktopGuardStateStore store = new(temporaryDirectory.Path);
        DesktopGuardState expected = new() { StartAtSignIn = false };

        store.Save(expected);

        DesktopGuardState actual = store.Load();
        Assert.IsFalse(actual.StartAtSignIn);
        Assert.AreEqual(expected.SchemaVersion, actual.SchemaVersion);
    }

    [TestMethod]
    public void Load_VersionOneStatePreservesPublicCheckpointAndMigratesPendingOperation()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string shortcutPath = Path.Combine(userRoot, "Program.lnk");
        string publicBaselinePath = Path.Combine(publicRoot, "Shared.url");
        Guid recoveryFolderId = Guid.NewGuid();
        DesktopGuardState versionOne = new()
        {
            SchemaVersion = 1,
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.User, userRoot, true, [shortcutPath]),
                new DesktopRootCheckpoint(DesktopRootKind.Public, publicRoot, true, [publicBaselinePath]),
            ],
            Shortcuts =
            [
                new TrackedShortcut(
                    Guid.NewGuid(),
                    DesktopRootKind.User,
                    userRoot,
                    "Program.lnk",
                    shortcutPath,
                    ShortcutStatus.Pending,
                    StatusBeforeParking: null,
                    Operation: ShortcutOperation.Parking,
                    RecoveryFolderId: recoveryFolderId),
            ],
        };
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "state.json"), JsonSerializer.Serialize(versionOne));
        DesktopGuardStateStore store = new(temporaryDirectory.Path);

        DesktopGuardState migrated = store.Load();

        Assert.AreEqual(DesktopGuardState.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.IsTrue(migrated.StartMinimized);
        Assert.AreEqual(publicBaselinePath, migrated.Roots.Single(root => root.RootKind == DesktopRootKind.Public).KnownPaths.Single());
        Assert.AreEqual(recoveryFolderId, migrated.Shortcuts.Single().OperationId);
        Assert.AreEqual(ShortcutOperation.Parking, migrated.Shortcuts.Single().Operation);
    }

    [TestMethod]
    public void Load_VersionTwoStateMigratesWithoutChangingDecisionsOrCheckpoints()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string userPath = Path.Combine(userRoot, "Program.lnk");
        string publicPath = Path.Combine(publicRoot, "Shared.url");
        DesktopGuardState versionTwo = new()
        {
            SchemaVersion = 2,
            StartAtSignIn = false,
            Roots =
            [
                new DesktopRootCheckpoint(DesktopRootKind.User, userRoot, true, [userPath]),
                new DesktopRootCheckpoint(DesktopRootKind.Public, publicRoot, true, [publicPath]),
            ],
            Shortcuts =
            [
                new TrackedShortcut(
                    Guid.NewGuid(),
                    DesktopRootKind.User,
                    userRoot,
                    "Program.lnk",
                    userPath,
                    ShortcutStatus.Pending,
                    StatusBeforeParking: null,
                    Operation: ShortcutOperation.None,
                    RecoveryFolderId: null),
            ],
        };
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "state.json"), JsonSerializer.Serialize(versionTwo));
        DesktopGuardStateStore store = new(temporaryDirectory.Path);

        DesktopGuardState migrated = store.Load();

        Assert.AreEqual(DesktopGuardState.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.IsFalse(migrated.StartAtSignIn);
        Assert.IsTrue(migrated.StartMinimized);
        Assert.AreEqual(userPath, migrated.Roots.Single(root => root.RootKind == DesktopRootKind.User).KnownPaths.Single());
        Assert.AreEqual(publicPath, migrated.Roots.Single(root => root.RootKind == DesktopRootKind.Public).KnownPaths.Single());
        Assert.AreEqual(ShortcutStatus.Pending, migrated.Shortcuts.Single().Status);
    }

    [TestMethod]
    public void Load_VersionThreeStateEnablesStartMinimizedWithoutChangingExistingPreferences()
    {
        using TemporaryDirectory temporaryDirectory = new();
        const string versionThreeState = """
            {
              "SchemaVersion": 3,
              "StartAtSignIn": false,
              "Roots": [],
              "Shortcuts": [],
              "Events": []
            }
            """;
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "state.json"), versionThreeState);
        DesktopGuardStateStore store = new(temporaryDirectory.Path);

        DesktopGuardState migrated = store.Load();

        Assert.AreEqual(DesktopGuardState.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.IsFalse(migrated.StartAtSignIn);
        Assert.IsTrue(migrated.StartMinimized);
    }

    [TestMethod]
    public void Load_WhenStateIsCorrupt_PreservesTheFileAndFailsClosed()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string statePath = Path.Combine(temporaryDirectory.Path, "state.json");
        const string contents = "{ this is not valid json";
        File.WriteAllText(statePath, contents);
        DesktopGuardStateStore store = new(temporaryDirectory.Path);

        StateStoreException exception = Assert.Throws<StateStoreException>(() => store.Load());

        Assert.AreEqual(StateStoreFailureKind.Corrupt, exception.Kind);
        Assert.AreEqual(contents, File.ReadAllText(statePath));
    }

    [TestMethod]
    public void Save_WhenSerializedStateExceedsLimit_PreservesPriorState()
    {
        using TemporaryDirectory temporaryDirectory = new();
        DesktopGuardStateStore store = new(temporaryDirectory.Path);
        store.Save(new DesktopGuardState { StartAtSignIn = false });
        string root = @"C:\" + string.Join("\\", Enumerable.Repeat("d", 300));
        string name = new string('x', 251) + ".lnk";
        string filePath = Path.Combine(root, name);
        DesktopEvent repeatedEvent = new(name, filePath, DateTimeOffset.UtcNow);
        DesktopGuardState oversized = new()
        {
            Events = Enumerable.Repeat(repeatedEvent, InventoryReconciler.MaximumEventCount).ToList(),
        };

        StateStoreException exception = Assert.Throws<StateStoreException>(() => store.Save(oversized));

        Assert.AreEqual(StateStoreFailureKind.TooLarge, exception.Kind);
        Assert.IsFalse(store.Load().StartAtSignIn);
        Assert.HasCount(1, Directory.GetFiles(temporaryDirectory.Path));
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
