using System.IO;
using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.Tests.DesktopScanning;

[TestClass]
public sealed class DesktopCandidatePolicyTests
{
    [TestMethod]
    public void IsDirectChildOf_RejectsNestedAndEscapingPaths()
    {
        string rootPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopGuardPolicyRoot"));
        string directChildPath = Path.Combine(rootPath, "shortcut.lnk");
        string nestedPath = Path.Combine(rootPath, "nested", "shortcut.lnk");
        string escapingPath = Path.Combine(rootPath, "..", "outside.lnk");

        Assert.IsTrue(DesktopCandidatePolicy.IsDirectChildOf(rootPath, directChildPath));
        Assert.IsFalse(DesktopCandidatePolicy.IsDirectChildOf(rootPath, nestedPath));
        Assert.IsFalse(DesktopCandidatePolicy.IsDirectChildOf(rootPath, escapingPath));
        Assert.IsFalse(DesktopCandidatePolicy.IsDirectChildOf(rootPath, "relative.lnk"));
    }

    [TestMethod]
    public void IsSafeDirectFile_RejectsDirectoriesAndReparsePoints()
    {
        string rootPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopGuardPolicyRoot"));
        string candidatePath = Path.Combine(rootPath, "shortcut.lnk");

        Assert.IsTrue(DesktopCandidatePolicy.IsSafeDirectFile(rootPath, candidatePath, FileAttributes.Normal));
        Assert.IsFalse(DesktopCandidatePolicy.IsSafeDirectFile(rootPath, candidatePath, FileAttributes.Directory));
        Assert.IsFalse(DesktopCandidatePolicy.IsSafeDirectFile(rootPath, candidatePath, FileAttributes.ReparsePoint));
        Assert.IsFalse(DesktopCandidatePolicy.IsSafeDirectFile(
            rootPath,
            Path.Combine(rootPath, "Program:stream.lnk"),
            FileAttributes.Normal));
    }

    [TestMethod]
    public void IsValidWindowsFileName_RejectsAlternateStreamsAndDeviceNames()
    {
        Assert.IsTrue(DesktopCandidatePolicy.IsValidWindowsFileName("Program.lnk"));
        Assert.IsFalse(DesktopCandidatePolicy.IsValidWindowsFileName("Program:stream.lnk"));
        Assert.IsFalse(DesktopCandidatePolicy.IsValidWindowsFileName("CON.lnk"));
        Assert.IsFalse(DesktopCandidatePolicy.IsValidWindowsFileName("COM0.lnk"));
        Assert.IsFalse(DesktopCandidatePolicy.IsValidWindowsFileName("LPT0.url"));
        Assert.IsFalse(DesktopCandidatePolicy.IsValidWindowsFileName("report.url."));
    }
}
