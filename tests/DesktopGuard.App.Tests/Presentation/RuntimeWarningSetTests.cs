using DesktopGuard.App.Presentation;

namespace DesktopGuard.App.Tests.Presentation;

[TestClass]
public sealed class RuntimeWarningSetTests
{
    [TestMethod]
    public void Clear_RemovesRecoveredSourceAndKeepsPersistentWarning()
    {
        RuntimeWarningSet warnings = new();
        warnings.Set("scan", "Scan failed.");
        warnings.Set("notifications", "Notifications are unavailable.");

        Assert.IsTrue(warnings.Clear("scan"));

        CollectionAssert.AreEqual(new[] { "Notifications are unavailable." }, warnings.Messages.ToArray());
        Assert.AreEqual(1, warnings.Count);
    }

    [TestMethod]
    public void Set_ReplacesWarningForTheSameSource()
    {
        RuntimeWarningSet warnings = new();
        warnings.Set("watch:user", "The user Desktop watcher failed.");

        Assert.IsTrue(warnings.Set("watch:user", "The user Desktop watcher was recreated."));

        CollectionAssert.AreEqual(new[] { "The user Desktop watcher was recreated." }, warnings.Messages.ToArray());
    }
}
