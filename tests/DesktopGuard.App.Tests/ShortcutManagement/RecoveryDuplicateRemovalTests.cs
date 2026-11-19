using System.Security.Cryptography;
using DesktopGuard.App.ShortcutManagement;

namespace DesktopGuard.App.Tests.ShortcutManagement;

[TestClass]
public sealed class RecoveryDuplicateRemovalTests
{
    [TestMethod]
    public void DeleteDuplicate_PreservesKeeperEvenWhenFileBytesDiffer()
    {
        using TemporaryDirectory directory = new();
        string keeper = Path.Combine(directory.Path, "keeper.url");
        string duplicate = Path.Combine(directory.Path, "duplicate.url");
        File.WriteAllText(keeper, "[InternetShortcut]\nURL=https://example.invalid/a\nIconIndex=1");
        File.WriteAllText(duplicate, "[InternetShortcut]\r\nURL=https://example.invalid/a\r\nIconIndex=2");
        ShortcutIdentityReader reader = new();
        ShortcutIdentityReadResult kept = reader.TryRead(keeper)!;
        ShortcutIdentityReadResult removed = reader.TryRead(duplicate)!;
        CollectionAssert.AreEqual(kept.IdentityHash, removed.IdentityHash);

        ShortcutFileTransfer.DeleteRecoveryDuplicate(duplicate, removed.SourceContentHash, keeper, kept.SourceContentHash);

        Assert.IsFalse(File.Exists(duplicate));
        CollectionAssert.AreEqual(kept.SourceContentHash, SHA256.HashData(File.ReadAllBytes(keeper)));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void DeleteDuplicate_WhenEitherFileChanges_PreservesBoth(bool changeKeeper)
    {
        using TemporaryDirectory directory = new();
        string keeper = Path.Combine(directory.Path, "keeper.url");
        string duplicate = Path.Combine(directory.Path, "duplicate.url");
        File.WriteAllText(keeper, "first");
        File.WriteAllText(duplicate, "second");
        byte[] keeperHash = SHA256.HashData(File.ReadAllBytes(keeper));
        byte[] duplicateHash = SHA256.HashData(File.ReadAllBytes(duplicate));
        File.WriteAllText(changeKeeper ? keeper : duplicate, "changed");

        Assert.ThrowsExactly<ShortcutFileOperationException>(() => ShortcutFileTransfer.DeleteRecoveryDuplicate(
            duplicate, duplicateHash, keeper, keeperHash));

        Assert.IsTrue(File.Exists(keeper));
        Assert.IsTrue(File.Exists(duplicate));
    }

    [TestMethod]
    public void DeleteDuplicate_MissingKeeperPreservesDuplicate()
    {
        using TemporaryDirectory directory = new();
        string duplicate = Path.Combine(directory.Path, "duplicate.url");
        File.WriteAllText(duplicate, "copy");
        Assert.ThrowsExactly<IOException>(() => ShortcutFileTransfer.DeleteRecoveryDuplicate(duplicate,
            SHA256.HashData(File.ReadAllBytes(duplicate)), Path.Combine(directory.Path, "missing.url"), new byte[32]));
        Assert.IsTrue(File.Exists(duplicate));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DG-dedup-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
