using System.Text;
using DesktopGuard.App.ShortcutManagement;

namespace DesktopGuard.App.Tests.ShortcutManagement;

[TestClass]
public sealed class ShortcutIdentityReaderTests
{
    [TestMethod]
    public void TryRead_InternetShortcutUsesUrlWithoutContactingTheTarget()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string firstPath = Path.Combine(temporaryDirectory.Path, "first.url");
        string secondPath = Path.Combine(temporaryDirectory.Path, "second.url");
        File.WriteAllText(firstPath, "[InternetShortcut]\r\nURL=https://Example.invalid/a/path?mode=1\r\n", Encoding.UTF8);
        File.WriteAllText(secondPath, "[InternetShortcut]\nURL=https://example.invalid/a/path?mode=1\n", Encoding.UTF8);
        ShortcutIdentityReader reader = new();

        ShortcutIdentityReadResult? first = reader.TryRead(firstPath);
        ShortcutIdentityReadResult? second = reader.TryRead(secondPath);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        CollectionAssert.AreEqual(first.IdentityHash, second.IdentityHash);
        Assert.HasCount(32, first.SourceContentHash);
    }

    [TestMethod]
    public void TryRead_ShellLinkReadsRawTargetEvenWhenTargetDoesNotExist()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string linkPath = Path.Combine(temporaryDirectory.Path, "missing-target.lnk");
        string missingTarget = Path.Combine(temporaryDirectory.Path, "missing", "CrealityPrint.exe");
        new WindowsDesktopShortcutLinkWriter().Create(linkPath, missingTarget);

        ShortcutIdentityReadResult? identity = new ShortcutIdentityReader().TryRead(linkPath);

        Assert.IsNotNull(identity);
        Assert.HasCount(32, identity.IdentityHash);
        Assert.IsFalse(File.Exists(missingTarget));
    }

    [TestMethod]
    public void TryRead_ChargesBytesForMalformedAndOversizedShortcuts()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string malformed = Path.Combine(temporaryDirectory.Path, "malformed.url");
        string oversized = Path.Combine(temporaryDirectory.Path, "oversized.url");
        File.WriteAllText(malformed, "[InternetShortcut]\r\nURL=\r\n");
        File.WriteAllBytes(oversized, new byte[64 * 1024 + 1]);
        ShortcutIdentityReader reader = new();

        Assert.IsNull(reader.TryRead(malformed, out int malformedCharge));
        Assert.AreEqual(new FileInfo(malformed).Length + 1, malformedCharge);
        Assert.IsNull(reader.TryRead(oversized, out int oversizedCharge));
        Assert.AreEqual(64 * 1024 + 1, oversizedCharge);
    }

    [TestMethod]
    public void TryRead_MalformedOrOversizedInputDoesNotProduceAnIdentity()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string malformedPath = Path.Combine(temporaryDirectory.Path, "malformed.url");
        string oversizedPath = Path.Combine(temporaryDirectory.Path, "oversized.url");
        File.WriteAllText(malformedPath, "[InternetShortcut]\r\nURL=\r\n");
        File.WriteAllBytes(oversizedPath, new byte[1024 * 1024 + 1]);
        ShortcutIdentityReader reader = new();

        Assert.IsNull(reader.TryRead(malformedPath));
        Assert.IsNull(reader.TryRead(oversizedPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"DesktopGuard-identity-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
