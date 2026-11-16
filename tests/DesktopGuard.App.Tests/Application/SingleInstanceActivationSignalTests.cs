using DesktopGuard.App.Application;

namespace DesktopGuard.App.Tests.Application;

[TestClass]
public sealed class SingleInstanceActivationSignalTests
{
    [TestMethod]
    public void SignalCreatedByAnotherHandleIsConsumedOnce()
    {
        string testUserId = $"test-{Guid.NewGuid():N}";
        using EventWaitHandle firstInstance = SingleInstanceActivationSignal.Create(testUserId);
        using EventWaitHandle secondInstance = SingleInstanceActivationSignal.Create(testUserId);

        secondInstance.Set();

        Assert.IsTrue(firstInstance.WaitOne(0));
        Assert.IsFalse(firstInstance.WaitOne(0));
    }
}
