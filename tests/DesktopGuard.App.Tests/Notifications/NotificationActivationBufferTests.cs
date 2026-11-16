using DesktopGuard.App.Notifications;

namespace DesktopGuard.App.Tests.Notifications;

[TestClass]
public sealed class NotificationActivationBufferTests
{
    [TestMethod]
    public void ColdStartActivation_IsKeptUntilControllerInitializationCompletes()
    {
        NotificationActivationBuffer buffer = new();
        Guid requestedId = Guid.NewGuid();

        Assert.IsFalse(buffer.TryGetReadyShortcutId(requestedId, out Guid? readyShortcutId));
        Assert.IsNull(readyShortcutId);

        Assert.IsTrue(buffer.MarkReady(out Guid? pendingShortcutId));
        Assert.AreEqual(requestedId, pendingShortcutId);
    }

    [TestMethod]
    public void InitializedActivation_IsReturnedImmediately()
    {
        NotificationActivationBuffer buffer = new();
        Assert.IsFalse(buffer.MarkReady(out _));

        Guid requestedId = Guid.NewGuid();
        Assert.IsTrue(buffer.TryGetReadyShortcutId(requestedId, out Guid? readyShortcutId));

        Assert.AreEqual(requestedId, readyShortcutId);
    }
}
