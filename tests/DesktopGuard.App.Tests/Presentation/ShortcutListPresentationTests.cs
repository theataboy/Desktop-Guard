using DesktopGuard.App;
using DesktopGuard.App.Application;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;

namespace DesktopGuard.App.Tests.Presentation;

[TestClass]
public sealed class ShortcutListPresentationTests
{
    [TestMethod]
    public void CountForAndGetVisibleRows_ApplySearchAndSelectedFilter()
    {
        ShortcutListPresentation presentation = new();
        presentation.SetRows(
        [
            CreateRow("Approved.lnk", DesktopRootKind.User, "User Desktop", "Approved and visible", approved: true),
            CreateRow("New.url", DesktopRootKind.Public, "Public Desktop", "Review required", needsReview: true),
            CreateRow("Parked.lnk", DesktopRootKind.User, "Recovery folder", "Parked in recovery", recovery: true),
        ]);

        Assert.AreEqual(3, presentation.CountFor(ShortcutListFilter.All));
        Assert.AreEqual(1, presentation.CountFor(ShortcutListFilter.NeedsReview));
        Assert.AreEqual(1, presentation.CountFor(ShortcutListFilter.Approved));
        Assert.AreEqual(1, presentation.CountFor(ShortcutListFilter.Recovery));

        presentation.SetFilter(ShortcutListFilter.NeedsReview);
        Assert.AreEqual("New.url", presentation.GetVisibleRows().Single().Entry.Name);

        presentation.SetSearchText("PARKED");
        Assert.AreEqual(0, presentation.CountFor(ShortcutListFilter.NeedsReview));
        presentation.SetFilter(ShortcutListFilter.All);
        Assert.AreEqual("Parked.lnk", presentation.GetVisibleRows().Single().Entry.Name);
    }

    [TestMethod]
    public void SortBy_UsesNameAscendingByDefaultAndTogglesTheSelectedColumn()
    {
        ShortcutListPresentation presentation = new();
        presentation.SetRows(
        [
            CreateRow("zeta.lnk", DesktopRootKind.User, "User Desktop", "Approved"),
            CreateRow("Alpha.lnk", DesktopRootKind.Public, "Public Desktop", "Review"),
            CreateRow("beta.lnk", DesktopRootKind.User, "Recovery folder", "Parked"),
        ]);

        CollectionAssert.AreEqual(
            new[] { "Alpha.lnk", "beta.lnk", "zeta.lnk" },
            presentation.GetVisibleRows().Select(row => row.Entry.Name).ToArray());

        presentation.SortBy(ShortcutListSortColumn.Name);
        CollectionAssert.AreEqual(
            new[] { "zeta.lnk", "beta.lnk", "Alpha.lnk" },
            presentation.GetVisibleRows().Select(row => row.Entry.Name).ToArray());

        presentation.SortBy(ShortcutListSortColumn.Location);
        Assert.IsFalse(presentation.SortDescending);
        CollectionAssert.AreEqual(
            new[] { "Public Desktop", "Recovery folder", "User Desktop" },
            presentation.GetVisibleRows().Select(row => row.Location).ToArray());

        presentation.SortBy(ShortcutListSortColumn.Location);
        Assert.IsTrue(presentation.SortDescending);
        CollectionAssert.AreEqual(
            new[] { "User Desktop", "Recovery folder", "Public Desktop" },
            presentation.GetVisibleRows().Select(row => row.Location).ToArray());

        presentation.SortBy(ShortcutListSortColumn.Status);
        CollectionAssert.AreEqual(
            new[] { "Approved", "Parked", "Review" },
            presentation.GetVisibleRows().Select(row => row.Status).ToArray());

        presentation.SortBy(ShortcutListSortColumn.Status);
        CollectionAssert.AreEqual(
            new[] { "Review", "Parked", "Approved" },
            presentation.GetVisibleRows().Select(row => row.Status).ToArray());
    }

    [TestMethod]
    public void HiddenShortcut_RemainsInItsNormalStatusFilter()
    {
        ShortcutListPresentation presentation = new();
        presentation.SetRows(
        [
            CreateRow(
                "Hidden.lnk",
                DesktopRootKind.User,
                "User Desktop",
                "Review required",
                needsReview: true,
                isHidden: true),
        ]);
        presentation.SetFilter(ShortcutListFilter.NeedsReview);

        ShortcutListPresentationRow row = presentation.GetVisibleRows().Single();

        Assert.AreEqual("Hidden.lnk", row.Entry.Name);
        Assert.IsTrue(row.Entry.IsHidden);
        Assert.AreEqual(1, presentation.CountFor(ShortcutListFilter.NeedsReview));
    }

    private static ShortcutListPresentationRow CreateRow(
        string name,
        DesktopRootKind rootKind,
        string location,
        string status,
        bool needsReview = false,
        bool approved = false,
        bool recovery = false,
        bool isHidden = false)
    {
        ShortcutListEntry entry = new(name, rootKind, Status: null, Shortcut: null, IsHidden: isHidden);
        return new ShortcutListPresentationRow(entry, location, status, needsReview, approved, recovery, isHidden);
    }
}
