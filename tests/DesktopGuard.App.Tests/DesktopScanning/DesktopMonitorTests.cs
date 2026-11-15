using DesktopGuard.App.DesktopScanning;
using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.App.Tests.DesktopScanning;

[TestClass]
public sealed class DesktopMonitorTests
{
    [TestMethod]
    public async Task RequestScan_WhenFloodedDuringAScan_CoalescesToOneFollowUpScan()
    {
        using TemporaryDirectory temporaryDirectory = new();
        CountingDesktopRootProvider rootProvider = CreateRootProvider(temporaryDirectory.Path);
        TaskCompletionSource<bool> firstScanStarted = NewSignal();
        TaskCompletionSource<bool> releaseFirstScan = NewSignal();
        TaskCompletionSource<bool> secondScanCompleted = NewSignal();
        int scanCount = 0;
        using DesktopMonitor monitor = new(rootProvider, async cancellationToken =>
        {
            int currentCount = Interlocked.Increment(ref scanCount);
            if (currentCount == 1)
            {
                firstScanStarted.TrySetResult(true);
                await releaseFirstScan.Task.WaitAsync(cancellationToken);
            }
            else if (currentCount == 2)
            {
                secondScanCompleted.TrySetResult(true);
            }
        });

        monitor.Start();
        await firstScanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int index = 0; index < 10_000; index++)
        {
            monitor.RequestScan();
        }

        releaseFirstScan.TrySetResult(true);
        await secondScanCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        Assert.AreEqual(2, Volatile.Read(ref scanCount));
    }

    [TestMethod]
    public async Task WatcherError_QueuesAFullScanAndRefreshesWatchers()
    {
        using TemporaryDirectory temporaryDirectory = new();
        CountingDesktopRootProvider rootProvider = CreateRootProvider(temporaryDirectory.Path);
        TaskCompletionSource<bool> firstScanStarted = NewSignal();
        TaskCompletionSource<bool> releaseFirstScan = NewSignal();
        TaskCompletionSource<bool> secondScanCompleted = NewSignal();
        TaskCompletionSource<bool> thirdScanCompleted = NewSignal();
        int scanCount = 0;
        using DesktopMonitor monitor = new(rootProvider, async cancellationToken =>
        {
            int currentCount = Interlocked.Increment(ref scanCount);
            if (currentCount == 1)
            {
                firstScanStarted.TrySetResult(true);
                await releaseFirstScan.Task.WaitAsync(cancellationToken);
            }
            else if (currentCount == 2)
            {
                secondScanCompleted.TrySetResult(true);
            }
            else if (currentCount == 3)
            {
                thirdScanCompleted.TrySetResult(true);
            }
        });

        monitor.Start();
        await firstScanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        FileSystemWatcher failedWatcher = monitor.GetWatcher(DesktopRootKind.User)!;
        monitor.OnWatcherError(failedWatcher, new ErrorEventArgs(new IOException("Simulated watcher overflow.")));
        releaseFirstScan.TrySetResult(true);
        await secondScanCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        Assert.AreEqual(2, Volatile.Read(ref scanCount));
        Assert.IsGreaterThanOrEqualTo(
            3,
            rootProvider.GetRootsCount,
            "The watcher roots should be re-read after the watcher error.");
        Assert.AreNotSame(failedWatcher, monitor.GetWatcher(DesktopRootKind.User));
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "UserDesktop", "new.lnk"), "shortcut");
        await thirdScanCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static CountingDesktopRootProvider CreateRootProvider(string temporaryPath)
    {
        string userDesktop = Directory.CreateDirectory(Path.Combine(temporaryPath, "UserDesktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryPath, "PublicDesktop")).FullName;
        return new CountingDesktopRootProvider(
        [
            new DesktopRoot(DesktopRootKind.User, userDesktop),
            new DesktopRoot(DesktopRootKind.Public, publicDesktop),
        ]);
    }

    private static TaskCompletionSource<bool> NewSignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CountingDesktopRootProvider(IReadOnlyList<DesktopRoot> roots) : IDesktopRootProvider
    {
        private int _getRootsCount;

        public int GetRootsCount => Volatile.Read(ref _getRootsCount);

        public IReadOnlyList<DesktopRoot> GetRoots()
        {
            Interlocked.Increment(ref _getRootsCount);
            return roots;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DesktopGuard.App.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
