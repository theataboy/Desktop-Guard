using System.Text.Json;
using System.Text.Json.Nodes;
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
    public void Load_VersionFourStateAddsDefaultsAndMarksInterruptedDesktopGuardCreation()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string shortcutPath = Path.Combine(desktop, "Program.lnk");
        string creatingPath = Path.Combine(desktop, "Desktop Guard.lnk");
        Guid creatingOperationId = Guid.NewGuid();
        DesktopGuardState versionFour = new()
        {
            SchemaVersion = 4,
            StartAtSignIn = false,
            StartMinimized = false,
            Roots = [new DesktopRootCheckpoint(DesktopRootKind.User, desktop, true, [shortcutPath])],
            Shortcuts =
            [
                new TrackedShortcut(
                    Guid.NewGuid(),
                    DesktopRootKind.User,
                    desktop,
                    "Program.lnk",
                    shortcutPath,
                    ShortcutStatus.Pending,
                    StatusBeforeParking: null,
                    Operation: ShortcutOperation.None,
                    RecoveryFolderId: null),
                new TrackedShortcut(
                    Guid.NewGuid(),
                    DesktopRootKind.User,
                    desktop,
                    "Desktop Guard.lnk",
                    creatingPath,
                    ShortcutStatus.ApprovedVisible,
                    StatusBeforeParking: null,
                    Operation: ShortcutOperation.Creating,
                    RecoveryFolderId: null,
                    OperationId: creatingOperationId),
            ],
        };
        JsonObject legacyDocument = JsonSerializer.SerializeToNode(versionFour)!.AsObject();
        foreach (JsonObject legacyShortcut in legacyDocument["Shortcuts"]!.AsArray().Select(token => token!.AsObject()))
        {
            legacyShortcut.Remove(nameof(TrackedShortcut.IsHidden));
            legacyShortcut.Remove(nameof(TrackedShortcut.TargetName));
            legacyShortcut.Remove(nameof(TrackedShortcut.IsDesktopGuardShortcut));
        }
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "state.json"), legacyDocument.ToJsonString());
        DesktopGuardStateStore store = new(temporaryDirectory.Path);

        DesktopGuardState migrated = store.Load();

        Assert.AreEqual(DesktopGuardState.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.IsFalse(migrated.StartAtSignIn);
        Assert.IsFalse(migrated.StartMinimized);
        TrackedShortcut migratedOrdinaryShortcut = migrated.Shortcuts.Single(shortcut => shortcut.Name == "Program.lnk");
        Assert.AreEqual(ShortcutStatus.Pending, migratedOrdinaryShortcut.Status);
        Assert.IsFalse(migratedOrdinaryShortcut.IsHidden);
        Assert.IsFalse(migratedOrdinaryShortcut.IsDesktopGuardShortcut);
        TrackedShortcut migratedCreation = migrated.Shortcuts.Single(shortcut => shortcut.Operation == ShortcutOperation.Creating);
        Assert.IsTrue(migratedCreation.IsDesktopGuardShortcut);
        Assert.AreEqual(creatingOperationId, migratedCreation.OperationId);
        Assert.AreEqual(shortcutPath, migrated.Roots.Single().KnownPaths.Single());
        Assert.IsTrue(DesktopStateValidator.IsValid(migrated));
    }

    [TestMethod]
    public void Load_VersionFiveMarksOnlyAnInterruptedDesktopGuardShortcutCreation()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string creatingPath = Path.Combine(desktop, "Desktop Guard.lnk");
        Guid creatingOperationId = Guid.NewGuid();
        DesktopGuardState versionFive = new()
        {
            SchemaVersion = 5,
            Roots = [new DesktopRootCheckpoint(DesktopRootKind.User, desktop, true, [])],
            Shortcuts =
            [
                new TrackedShortcut(
                    Guid.NewGuid(),
                    DesktopRootKind.User,
                    desktop,
                    "Desktop Guard.lnk",
                    creatingPath,
                    ShortcutStatus.ApprovedVisible,
                    StatusBeforeParking: null,
                    Operation: ShortcutOperation.Creating,
                    RecoveryFolderId: null,
                    OperationId: creatingOperationId),
            ],
        };
        JsonObject legacyDocument = JsonSerializer.SerializeToNode(versionFive)!.AsObject();
        foreach (JsonObject shortcut in legacyDocument["Shortcuts"]!.AsArray().Select(token => token!.AsObject()))
        {
            shortcut.Remove(nameof(TrackedShortcut.IsDesktopGuardShortcut));
        }

        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "state.json"), legacyDocument.ToJsonString());
        DesktopGuardStateStore store = new(temporaryDirectory.Path);

        DesktopGuardState migrated = store.Load();

        Assert.AreEqual(DesktopGuardState.CurrentSchemaVersion, migrated.SchemaVersion);
        TrackedShortcut migratedCreation = migrated.Shortcuts.Single(shortcut => shortcut.Operation == ShortcutOperation.Creating);
        Assert.IsTrue(migratedCreation.IsDesktopGuardShortcut);
        Assert.AreEqual(creatingOperationId, migratedCreation.OperationId);
        Assert.AreEqual(ShortcutStatus.ApprovedVisible, migratedCreation.Status);
        Assert.IsTrue(DesktopStateValidator.IsValid(migrated));

        store.Save(migrated);
        DesktopGuardState reloaded = store.Load();
        Assert.IsTrue(reloaded.Shortcuts.Single(shortcut => shortcut.Operation == ShortcutOperation.Creating).IsDesktopGuardShortcut);
    }

    [TestMethod]
    public void Load_VersionFiveDoesNotInferDesktopGuardMarkerFromExistingName()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string shortcutPath = Path.Combine(desktop, "Desktop Guard.lnk");
        DesktopGuardState versionFive = new()
        {
            SchemaVersion = 5,
            Roots = [new DesktopRootCheckpoint(DesktopRootKind.User, desktop, true, [shortcutPath])],
            Shortcuts =
            [
                new TrackedShortcut(
                    Guid.NewGuid(),
                    DesktopRootKind.User,
                    desktop,
                    "Desktop Guard.lnk",
                    shortcutPath,
                    ShortcutStatus.ApprovedVisible,
                    StatusBeforeParking: null,
                    Operation: ShortcutOperation.None,
                    RecoveryFolderId: null),
            ],
        };
        JsonObject legacyDocument = JsonSerializer.SerializeToNode(versionFive)!.AsObject();
        legacyDocument["Shortcuts"]!.AsArray().Single()!.AsObject().Remove(nameof(TrackedShortcut.IsDesktopGuardShortcut));
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "state.json"), legacyDocument.ToJsonString());
        DesktopGuardStateStore store = new(temporaryDirectory.Path);

        DesktopGuardState migrated = store.Load();

        Assert.IsFalse(migrated.Shortcuts.Single().IsDesktopGuardShortcut);
        Assert.IsTrue(DesktopStateValidator.IsValid(migrated));
    }

    [TestMethod]
    public void Load_VersionSixAddsDefaultAutoParkingDelayWithoutChangingDecisions()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string parkedPath = Path.Combine(desktop, "Creality Print.lnk");
        Guid recoveryFolderId = Guid.NewGuid();
        DesktopGuardState versionSix = new()
        {
            SchemaVersion = 6,
            StartAtSignIn = false,
            StartMinimized = false,
            Roots = [new DesktopRootCheckpoint(DesktopRootKind.User, desktop, true, [])],
            Shortcuts =
            [
                new TrackedShortcut(
                    Guid.NewGuid(),
                    DesktopRootKind.User,
                    desktop,
                    "Creality Print.lnk",
                    parkedPath,
                    ShortcutStatus.Parked,
                    StatusBeforeParking: ShortcutStatus.Pending,
                    Operation: ShortcutOperation.None,
                    RecoveryFolderId: recoveryFolderId),
            ],
        };
        JsonObject legacyDocument = JsonSerializer.SerializeToNode(versionSix)!.AsObject();
        legacyDocument.Remove(nameof(DesktopGuardState.AutoParkDelayMinutes));
        foreach (JsonObject shortcut in legacyDocument["Shortcuts"]!.AsArray().Select(token => token!.AsObject()))
        {
            shortcut.Remove(nameof(TrackedShortcut.ShortcutIdentityHash));
            shortcut.Remove(nameof(TrackedShortcut.AutoParkingSinceUtc));
        }

        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "state.json"), legacyDocument.ToJsonString());
        DesktopGuardStateStore store = new(temporaryDirectory.Path);

        DesktopGuardState migrated = store.Load();

        Assert.AreEqual(DesktopGuardState.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.AreEqual(5, migrated.AutoParkDelayMinutes);
        Assert.IsFalse(migrated.StartAtSignIn);
        Assert.IsFalse(migrated.StartMinimized);
        Assert.AreEqual(ShortcutStatus.Parked, migrated.Shortcuts.Single().Status);
        Assert.AreEqual(recoveryFolderId, migrated.Shortcuts.Single().RecoveryFolderId);
        Assert.IsTrue(DesktopStateValidator.IsValid(migrated));
    }

    [TestMethod]
    public void Load_VersionSixPreservesDesktopGuardLauncherMarker()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string launcherPath = Path.Combine(desktop, "Desktop Guard.lnk");
        Guid launcherId = Guid.NewGuid();
        DesktopGuardState versionSix = new()
        {
            SchemaVersion = 6,
            Roots = [new DesktopRootCheckpoint(DesktopRootKind.User, desktop, true, [launcherPath])],
            Shortcuts =
            [
                new TrackedShortcut(launcherId, DesktopRootKind.User, desktop, "Desktop Guard.lnk",
                    launcherPath, ShortcutStatus.ApprovedVisible, null, ShortcutOperation.None, null,
                    IsDesktopGuardShortcut: true),
            ],
        };
        JsonObject document = JsonSerializer.SerializeToNode(versionSix)!.AsObject();
        document.Remove(nameof(DesktopGuardState.AutoParkDelayMinutes));
        JsonObject launcher = document["Shortcuts"]!.AsArray().Single()!.AsObject();
        launcher.Remove(nameof(TrackedShortcut.ShortcutIdentityHash));
        launcher.Remove(nameof(TrackedShortcut.AutoParkingSinceUtc));
        launcher.Remove(nameof(TrackedShortcut.AutoParkingContentHash));
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "state.json"), document.ToJsonString());

        DesktopGuardState migrated = new DesktopGuardStateStore(temporaryDirectory.Path).Load();

        Assert.AreEqual(DesktopGuardState.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.AreEqual(launcherId, migrated.Shortcuts.Single().Id);
        Assert.IsTrue(migrated.Shortcuts.Single().IsDesktopGuardShortcut);
        Assert.IsTrue(DesktopStateValidator.IsValid(migrated));
    }

    [TestMethod]
    public void Load_VersionSevenPreservesFingerprintsAndAllowsExistingPendingComparison()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string path = Path.Combine(desktop, "App.lnk");
        TrackedShortcut pending = new(Guid.NewGuid(), DesktopRootKind.User, desktop, "App.lnk", path,
            ShortcutStatus.Pending, null, ShortcutOperation.None, null);
        DesktopGuardState legacy = new()
        {
            SchemaVersion = 7,
            Roots = [new(DesktopRootKind.User, desktop, true, [path])],
            Shortcuts = [pending],
        };
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "state.json"), JsonSerializer.Serialize(legacy));

        DesktopGuardState migrated = new DesktopGuardStateStore(temporaryDirectory.Path).Load();

        Assert.AreEqual(DesktopGuardState.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.IsFalse(migrated.Shortcuts.Single().AutoParkingEvaluated);
        Assert.AreEqual(ShortcutStatus.Pending, migrated.Shortcuts.Single().Status);
        Assert.IsTrue(DesktopStateValidator.IsValid(migrated));
    }

    [TestMethod]
    public void Load_VersionEightPreservesInterruptedRestoreWithOriginalTargetNameFallback()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string desktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "Desktop")).FullName;
        string originalPath = Path.Combine(desktop, "Program.url");
        TrackedShortcut restoring = new(
            Guid.NewGuid(),
            DesktopRootKind.User,
            desktop,
            "Program.url",
            originalPath,
            ShortcutStatus.Parked,
            ShortcutStatus.Pending,
            ShortcutOperation.Restoring,
            Guid.NewGuid(),
            OperationId: Guid.NewGuid());
        DesktopGuardState legacy = new()
        {
            SchemaVersion = 8,
            Roots = [new DesktopRootCheckpoint(DesktopRootKind.User, desktop, true, [])],
            Shortcuts = [restoring],
        };
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "state.json"), JsonSerializer.Serialize(legacy));

        DesktopGuardState migrated = new DesktopGuardStateStore(temporaryDirectory.Path).Load();

        Assert.AreEqual(DesktopGuardState.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.AreEqual(ShortcutOperation.Restoring, migrated.Shortcuts.Single().Operation);
        Assert.IsNull(migrated.Shortcuts.Single().TargetName);
        Assert.IsTrue(DesktopStateValidator.IsValid(migrated));
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
