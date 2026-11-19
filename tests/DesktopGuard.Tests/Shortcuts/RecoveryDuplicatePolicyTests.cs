using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;

namespace DesktopGuard.Tests.Shortcuts;

[TestClass]
public sealed class RecoveryDuplicatePolicyTests
{
    [TestMethod]
    public void FindDuplicates_KeepsNewestAndUsesFirstRecordForEqualDates()
    {
        TrackedShortcut first = Shortcut("First.lnk");
        TrackedShortcut second = Shortcut("Second.lnk");
        TrackedShortcut third = Shortcut("Third.lnk");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RecoveryShortcutSnapshot[] snapshots =
        [
            new(first.Id, new byte[32], now.AddDays(-1)),
            new(second.Id, new byte[32], now),
            new(third.Id, new byte[32], now),
        ];
        IReadOnlyList<(Guid DuplicateId, Guid KeeperId)> result = RecoveryDuplicatePolicy.FindDuplicates(
            [first, second, third], snapshots);
        Assert.HasCount(2, result);
        Assert.IsTrue(result.All(pair => pair.KeeperId == second.Id));
        Assert.IsTrue(result.Any(pair => pair.DuplicateId == third.Id));
    }

    [TestMethod]
    public void FindDuplicates_DoesNotMergeDifferentDesktopsOrChangedIdentity()
    {
        TrackedShortcut first = Shortcut("First.lnk");
        TrackedShortcut otherDesktop = Shortcut("Other.lnk") with { RootKind = DesktopRootKind.Public };
        TrackedShortcut changed = Shortcut("Changed.lnk");
        byte[] changedIdentity = new byte[32];
        changedIdentity[0] = 1;
        RecoveryShortcutSnapshot[] snapshots =
        [
            new(first.Id, new byte[32], DateTimeOffset.UtcNow),
            new(otherDesktop.Id, new byte[32], DateTimeOffset.UtcNow),
            new(changed.Id, changedIdentity, DateTimeOffset.UtcNow),
        ];
        Assert.IsEmpty(RecoveryDuplicatePolicy.FindDuplicates([first, otherDesktop, changed], snapshots));
    }

    private static TrackedShortcut Shortcut(string name)
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DG-policy-tests"));
        return new TrackedShortcut(Guid.NewGuid(), DesktopRootKind.User, root, name, Path.Combine(root, name),
            ShortcutStatus.Parked, ShortcutStatus.Pending, ShortcutOperation.None, Guid.NewGuid(),
            ShortcutIdentityHash: new byte[32]);
    }
}
