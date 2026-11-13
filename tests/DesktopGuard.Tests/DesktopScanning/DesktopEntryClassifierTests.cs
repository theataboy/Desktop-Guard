using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.Tests.DesktopScanning;

[TestClass]
public sealed class DesktopEntryClassifierTests
{
    [TestMethod]
    [DataRow("setup.lnk")]
    [DataRow("website.url")]
    [DataRow("UPPERCASE.LNK")]
    [DataRow("UPPERCASE.URL")]
    public void Classify_SupportedShortcutExtension_ReturnsShortcut(string fileName)
    {
        Assert.AreEqual(DesktopEntryKind.Shortcut, DesktopEntryClassifier.Classify(fileName));
    }

    [TestMethod]
    [DataRow("readme.txt")]
    [DataRow("setup.exe")]
    [DataRow("setup.lnk.exe")]
    public void Classify_OtherExtension_ReturnsOtherFile(string fileName)
    {
        Assert.AreEqual(DesktopEntryKind.OtherFile, DesktopEntryClassifier.Classify(fileName));
    }
}
