using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.App.DesktopScanning;

internal sealed class DesktopInventoryScanner
{
    internal const int MaximumEntriesPerRoot = 4096;

    private static readonly EnumerationOptions TopLevelEnumerationOptions = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    private readonly IDesktopRootProvider _rootProvider;

    public DesktopInventoryScanner(IDesktopRootProvider rootProvider)
    {
        ArgumentNullException.ThrowIfNull(rootProvider);
        _rootProvider = rootProvider;
    }

    public DesktopScanResult Scan()
    {
        List<DesktopScanIssue> issues = [];
        IReadOnlyList<DesktopRoot> roots;

        try
        {
            roots = _rootProvider.GetRoots();
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            DesktopRootSnapshot user = new(DesktopRootKind.User, string.Empty, false, []);
            DesktopRootSnapshot common = new(DesktopRootKind.Public, string.Empty, false, []);
            issues.Add(new DesktopScanIssue(DesktopRootKind.User, DesktopScanIssueKind.RootUnavailable));
            issues.Add(new DesktopScanIssue(DesktopRootKind.Public, DesktopScanIssueKind.RootUnavailable));
            return new DesktopScanResult(new DesktopInventorySnapshot(new[] { user, common }), issues);
        }

        List<DesktopRootSnapshot> rootSnapshots = [];
        foreach (DesktopRoot root in roots)
        {
            rootSnapshots.Add(ScanRoot(root, issues));
        }

        return new DesktopScanResult(new DesktopInventorySnapshot(rootSnapshots), issues);
    }

    private static DesktopRootSnapshot ScanRoot(DesktopRoot root, ICollection<DesktopScanIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(root.Path) || !Path.IsPathFullyQualified(root.Path))
        {
            issues.Add(new DesktopScanIssue(root.Kind, DesktopScanIssueKind.RootUnavailable));
            return new DesktopRootSnapshot(root.Kind, root.Path, false, []);
        }

        string canonicalRoot;
        try
        {
            canonicalRoot = Path.GetFullPath(root.Path);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            issues.Add(new DesktopScanIssue(root.Kind, DesktopScanIssueKind.RootUnavailable));
            return new DesktopRootSnapshot(root.Kind, root.Path, false, []);
        }

        List<DesktopInventoryEntry> entries = [];
        bool isComplete = true;
        int inspectedEntryCount = 0;
        try
        {
            foreach (string candidatePath in Directory.EnumerateFileSystemEntries(
                canonicalRoot,
                "*",
                TopLevelEnumerationOptions))
            {
                inspectedEntryCount++;
                if (inspectedEntryCount > MaximumEntriesPerRoot)
                {
                    issues.Add(new DesktopScanIssue(root.Kind, DesktopScanIssueKind.EntryLimitReached));
                    isComplete = false;
                    break;
                }

                if (!TryInspectCandidate(root.Kind, canonicalRoot, candidatePath, entries, issues))
                {
                    isComplete = false;
                }
            }
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            issues.Add(new DesktopScanIssue(root.Kind, DesktopScanIssueKind.RootCouldNotBeRead));
            isComplete = false;
        }

        return new DesktopRootSnapshot(root.Kind, canonicalRoot, isComplete, entries);
    }

    private static bool TryInspectCandidate(
        DesktopRootKind rootKind,
        string canonicalRoot,
        string candidatePath,
        ICollection<DesktopInventoryEntry> entries,
        ICollection<DesktopScanIssue> issues)
    {
        if (!DesktopCandidatePolicy.IsDirectChildOf(canonicalRoot, candidatePath))
        {
            issues.Add(new DesktopScanIssue(rootKind, DesktopScanIssueKind.EntryOutsideRootSkipped));
            return false;
        }

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(candidatePath);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            issues.Add(new DesktopScanIssue(rootKind, DesktopScanIssueKind.EntryCouldNotBeInspected));
            return false;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            return true;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            issues.Add(new DesktopScanIssue(rootKind, DesktopScanIssueKind.ReparsePointSkipped));
            return false;
        }

        string name = Path.GetFileName(candidatePath);
        if (!DesktopCandidatePolicy.IsValidWindowsFileName(name))
        {
            issues.Add(new DesktopScanIssue(rootKind, DesktopScanIssueKind.EntryCouldNotBeInspected));
            return false;
        }

        entries.Add(new DesktopInventoryEntry(
            rootKind,
            canonicalRoot,
            Path.GetFullPath(candidatePath),
            name,
            DesktopEntryClassifier.Classify(name),
            IsHidden: (attributes & FileAttributes.Hidden) != 0));
        return true;
    }

    private static bool IsExpectedFileSystemException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or COMException
            or SecurityException;
    }
}
