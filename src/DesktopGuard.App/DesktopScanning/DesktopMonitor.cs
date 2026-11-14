using System.IO;
using System.Threading.Channels;
using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.App.DesktopScanning;

internal sealed class DesktopMonitor : IDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ChangeDebounce = TimeSpan.FromMilliseconds(250);

    private readonly IDesktopRootProvider _rootProvider;
    private readonly Func<CancellationToken, Task> _scanAsync;
    private readonly object _watcherGate = new();
    private readonly Channel<byte> _scanSignals = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly Dictionary<DesktopRootKind, (string Path, FileSystemWatcher Watcher)> _watchers = [];
    private readonly CancellationTokenSource _shutdown = new();
    private System.Threading.Timer? _periodicScanTimer;
    private Task? _worker;
    private int _watchersNeedRefresh;
    private volatile bool _disposed;

    public DesktopMonitor(IDesktopRootProvider rootProvider, Func<CancellationToken, Task> scanAsync)
    {
        ArgumentNullException.ThrowIfNull(rootProvider);
        ArgumentNullException.ThrowIfNull(scanAsync);
        _rootProvider = rootProvider;
        _scanAsync = scanAsync;
    }

    public event Action<Exception>? ScanFailed;

    public event Action<DesktopRootKind>? WatchFailed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_worker is not null)
        {
            return;
        }

        RefreshWatchers();
        _periodicScanTimer = new System.Threading.Timer(
            _ => RequestScan(),
            null,
            ScanInterval,
            ScanInterval);
        _worker = ProcessSignalsAsync(_shutdown.Token);
        RequestScan();
    }

    public void RequestScan()
    {
        if (!_disposed)
        {
            _scanSignals.Writer.TryWrite(0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_watcherGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (FileSystemWatcher watcher in _watchers.Values.Select(item => item.Watcher))
            {
                watcher.Dispose();
            }

            _watchers.Clear();
        }

        _periodicScanTimer?.Dispose();
        _shutdown.Cancel();
        _scanSignals.Writer.TryComplete();
        _shutdown.Dispose();
    }

    private async Task ProcessSignalsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _scanSignals.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                DrainSignals();
                await Task.Delay(ChangeDebounce, cancellationToken).ConfigureAwait(false);
                DrainSignals();

                if (Interlocked.Exchange(ref _watchersNeedRefresh, 0) != 0)
                {
                    RefreshWatchers();
                }

                try
                {
                    await _scanAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    ScanFailed?.Invoke(exception);
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    RefreshWatchers();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private void RefreshWatchers()
    {
        lock (_watcherGate)
        {
            if (_disposed)
            {
                return;
            }

            IReadOnlyList<DesktopRoot> roots;
            try
            {
                roots = _rootProvider.GetRoots();
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                WatchFailed?.Invoke(DesktopRootKind.User);
                WatchFailed?.Invoke(DesktopRootKind.Public);
                return;
            }

            HashSet<DesktopRootKind> retainedRoots = [];
            foreach (DesktopRoot root in roots)
            {
                if (!Enum.IsDefined(root.Kind)
                    || string.IsNullOrWhiteSpace(root.Path)
                    || !Path.IsPathFullyQualified(root.Path))
                {
                    continue;
                }

                string canonicalPath;
                try
                {
                    canonicalPath = Path.GetFullPath(root.Path);
                }
                catch (Exception exception) when (IsExpectedFileSystemException(exception))
                {
                    WatchFailed?.Invoke(root.Kind);
                    continue;
                }

                retainedRoots.Add(root.Kind);
                if (_watchers.TryGetValue(root.Kind, out (string Path, FileSystemWatcher Watcher) current)
                    && PathsEqual(current.Path, canonicalPath))
                {
                    continue;
                }

                RemoveWatcher(root.Kind);
                if (!Directory.Exists(canonicalPath))
                {
                    continue;
                }

                try
                {
                    FileSystemWatcher watcher = new(canonicalPath, "*")
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName
                            | NotifyFilters.LastWrite
                            | NotifyFilters.Size
                            | NotifyFilters.Attributes,
                        EnableRaisingEvents = false,
                    };
                    watcher.Created += OnFileSystemChanged;
                    watcher.Changed += OnFileSystemChanged;
                    watcher.Deleted += OnFileSystemChanged;
                    watcher.Renamed += OnFileSystemRenamed;
                    watcher.Error += OnWatcherError;
                    watcher.EnableRaisingEvents = true;
                    _watchers[root.Kind] = (canonicalPath, watcher);
                }
                catch (Exception exception) when (IsExpectedFileSystemException(exception))
                {
                    WatchFailed?.Invoke(root.Kind);
                }
            }

            foreach (DesktopRootKind kind in _watchers.Keys.ToArray())
            {
                if (!retainedRoots.Contains(kind))
                {
                    RemoveWatcher(kind);
                }
            }
        }
    }

    private void RemoveWatcher(DesktopRootKind kind)
    {
        if (_watchers.Remove(kind, out (string Path, FileSystemWatcher Watcher) watcher))
        {
            watcher.Watcher.Dispose();
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs args)
    {
        RequestScan();
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs args)
    {
        RequestScan();
    }

    internal void OnWatcherError(object sender, ErrorEventArgs args)
    {
        Interlocked.Exchange(ref _watchersNeedRefresh, 1);
        RequestScan();
    }

    private void DrainSignals()
    {
        while (_scanSignals.Reader.TryRead(out _))
        {
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool IsExpectedFileSystemException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException;
    }
}
