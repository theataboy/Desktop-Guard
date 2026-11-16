using System.Runtime.InteropServices;
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

    [TestMethod]
    [DataRow(false, 2)]
    [DataRow(true, 2)]
    [DataRow(true, 1)]
    public async Task RootProviderFailure_DoesNotStopTheMonitorWorker(bool unexpectedFailure, int failureCall)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        TaskCompletionSource<bool> thirdRootRefreshCompleted = NewSignal();
        SequenceDesktopRootProvider rootProvider = new(
        [
            new DesktopRoot(DesktopRootKind.User, userDesktop),
            new DesktopRoot(DesktopRootKind.Public, publicDesktop),
        ],
        thirdRootRefreshCompleted,
        unexpectedFailure,
        failureCall);
        TaskCompletionSource<bool> firstScanCompleted = NewSignal();
        TaskCompletionSource<bool> secondScanCompleted = NewSignal();
        TaskCompletionSource<bool> watchFailureReported = NewSignal();
        int scanCount = 0;
        using DesktopMonitor monitor = new(rootProvider, _ =>
        {
            if (Interlocked.Increment(ref scanCount) == 1)
            {
                firstScanCompleted.TrySetResult(true);
            }
            else
            {
                secondScanCompleted.TrySetResult(true);
            }

            return Task.CompletedTask;
        });
        monitor.WatchFailed += _ => watchFailureReported.TrySetResult(true);
        monitor.ScanFailed += _ => watchFailureReported.TrySetResult(true);

        monitor.Start();
        await firstScanCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await watchFailureReported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        monitor.RequestScan();
        await secondScanCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await thirdRootRefreshCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
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

    private sealed class SequenceDesktopRootProvider(
        IReadOnlyList<DesktopRoot> roots,
        TaskCompletionSource<bool> thirdRefreshCompleted,
        bool unexpectedFailure,
        int failureCall) : IDesktopRootProvider
    {
        private int _getRootsCount;

        public int GetRootsCount => Volatile.Read(ref _getRootsCount);

        public IReadOnlyList<DesktopRoot> GetRoots()
        {
            int count = Interlocked.Increment(ref _getRootsCount);
            if (count == failureCall)
            {
                if (unexpectedFailure)
                {
                    throw new InvalidOperationException("Unexpected root-provider failure.");
                }

                throw new COMException("Temporary known-folder failure.", unchecked((int)0x80004005));
            }

            if (count == 3)
            {
                thirdRefreshCompleted.TrySetResult(true);
            }

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
