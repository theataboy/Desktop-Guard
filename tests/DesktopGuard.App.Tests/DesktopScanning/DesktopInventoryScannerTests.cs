using DesktopGuard.App.DesktopScanning;
using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.App.Tests.DesktopScanning;

[TestClass]
public sealed class DesktopInventoryScannerTests
{
    [TestMethod]
    public void Scan_ListsDirectFilesFromBothRootsWithoutChangingThem()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userDesktopPath = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicDesktopPath = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string nestedDirectoryPath = Directory.CreateDirectory(Path.Combine(userDesktopPath, "Nested")).FullName;
        string linkContents = "Untrusted shortcut contents that must not be opened or resolved.";
        string urlContents = "[InternetShortcut]\r\nURL=file:///untrusted-target";
        string otherFileContents = "Other files are observed without modification.";
        File.WriteAllText(Path.Combine(userDesktopPath, "Program.lnk"), linkContents);
        File.WriteAllText(Path.Combine(userDesktopPath, "Website.URL"), urlContents);
        File.WriteAllText(Path.Combine(userDesktopPath, "Notes.txt"), otherFileContents);
        File.WriteAllText(Path.Combine(nestedDirectoryPath, "Nested.lnk"), "Nested file.");
        File.WriteAllText(Path.Combine(publicDesktopPath, "Shared.lnk"), linkContents);

        DesktopInventoryScanner scanner = CreateScanner(userDesktopPath, publicDesktopPath);

        DesktopScanResult result = scanner.Scan();

        Assert.HasCount(4, GetEntries(result));
        Assert.IsEmpty(result.Issues);
        AssertEntry(result, "Program.lnk", DesktopRootKind.User, DesktopEntryKind.Shortcut);
        AssertEntry(result, "Website.URL", DesktopRootKind.User, DesktopEntryKind.Shortcut);
        AssertEntry(result, "Notes.txt", DesktopRootKind.User, DesktopEntryKind.OtherFile);
        AssertEntry(result, "Shared.lnk", DesktopRootKind.Public, DesktopEntryKind.Shortcut);
        Assert.IsFalse(GetEntries(result).Any(entry => entry.Name == "Nested.lnk"));
        Assert.AreEqual(linkContents, File.ReadAllText(Path.Combine(userDesktopPath, "Program.lnk")));
        Assert.AreEqual(urlContents, File.ReadAllText(Path.Combine(userDesktopPath, "Website.URL")));
        Assert.AreEqual(otherFileContents, File.ReadAllText(Path.Combine(userDesktopPath, "Notes.txt")));
    }

    [TestMethod]
    public void Scan_WhenOneRootIsUnavailable_ReportsIssueAndKeepsOtherRootResults()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string missingUserDesktopPath = Path.Combine(temporaryDirectory.Path, "MissingUserDesktop");
        string publicDesktopPath = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        File.WriteAllText(Path.Combine(publicDesktopPath, "Shared.url"), "Untrusted URL content.");

        DesktopInventoryScanner scanner = CreateScanner(missingUserDesktopPath, publicDesktopPath);

        DesktopScanResult result = scanner.Scan();

        Assert.HasCount(1, GetEntries(result));
        Assert.AreEqual(DesktopRootKind.Public, GetEntries(result)[0].RootKind);
        Assert.AreEqual("Shared.url", GetEntries(result)[0].Name);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.RootKind == DesktopRootKind.User
                && issue.Kind == DesktopScanIssueKind.RootCouldNotBeRead));
    }

    [TestMethod]
    public void Scan_RejectsRelativeRootPaths()
    {
        DesktopInventoryScanner scanner = new(new FixedDesktopRootProvider(
        [
            new DesktopRoot(DesktopRootKind.User, "."),
        ]));

        DesktopScanResult result = scanner.Scan();

        Assert.IsEmpty(GetEntries(result));
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual(DesktopScanIssueKind.RootUnavailable, result.Issues[0].Kind);
    }

    private static DesktopInventoryScanner CreateScanner(string userRootPath, string publicRootPath)
    {
        return new DesktopInventoryScanner(new FixedDesktopRootProvider(
        [
            new DesktopRoot(DesktopRootKind.User, userRootPath),
            new DesktopRoot(DesktopRootKind.Public, publicRootPath),
        ]));
    }

    private static void AssertEntry(
        DesktopScanResult result,
        string name,
        DesktopRootKind rootKind,
        DesktopEntryKind entryKind)
    {
        DesktopInventoryEntry entry = GetEntries(result).Single(candidate => candidate.Name == name);
        Assert.AreEqual(rootKind, entry.RootKind);
        Assert.AreEqual(entryKind, entry.Kind);
    }

    private static DesktopInventoryEntry[] GetEntries(DesktopScanResult result)
    {
        return result.Inventory.Roots.SelectMany(root => root.Entries).ToArray();
    }

    private sealed class FixedDesktopRootProvider(IReadOnlyList<DesktopRoot> roots) : IDesktopRootProvider
    {
        public IReadOnlyList<DesktopRoot> GetRoots()
        {
            return roots;
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
