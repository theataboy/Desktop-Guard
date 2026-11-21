using System.Collections.Concurrent;
using DesktopGuard.App.DesktopScanning;
using DesktopGuard.App.Notifications;
using DesktopGuard.App.Persistence;
using DesktopGuard.App.ShortcutManagement;
using DesktopGuard.App.Startup;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;
using DesktopGuard.Core.State;

namespace DesktopGuard.App.Application;

internal sealed class DesktopGuardController
{
    private const int MaximumNotificationsPerScan = 5;
    private const long MaximumIdentityReadBytesPerScan = 64 * 1024 * 1024;
    private const long MaximumIdentityBackfillBytes = 64 * 1024 * 1024;

    private readonly DesktopInventoryScanner _scanner;
    private readonly InventoryReconciler _reconciler;
    private readonly IDesktopGuardStateStore _stateStore;
    private readonly ShortcutFileManager _fileManager;
    private readonly IStartAtSignInManager _startAtSignInManager;
    private readonly WindowsNotificationService _notifications;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, byte> _unresolvedOperationIds = new();

    private DesktopGuardState _state = new();
    private DesktopInventorySnapshot _inventory = new([]);
    private IReadOnlyList<DesktopScanIssue> _scanIssues = [];
    private Exception? _lastFailure;
    private Exception? _startupFailure;
    private Guid? _nextRecoveryDuplicateCandidateId;

    public DesktopGuardController(
        DesktopInventoryScanner scanner,
        InventoryReconciler reconciler,
        IDesktopGuardStateStore stateStore,
        ShortcutFileManager fileManager,
        IStartAtSignInManager startAtSignInManager,
        WindowsNotificationService notifications)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(reconciler);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(fileManager);
        ArgumentNullException.ThrowIfNull(startAtSignInManager);
        ArgumentNullException.ThrowIfNull(notifications);
        _scanner = scanner;
        _reconciler = reconciler;
        _stateStore = stateStore;
        _fileManager = fileManager;
        _startAtSignInManager = startAtSignInManager;
        _notifications = notifications;
    }

    public event Action? StateChanged;

    public DesktopGuardState State => _state;

    public IReadOnlyList<DesktopScanIssue> ScanIssues => _scanIssues;

    public Exception? LastFailure => _lastFailure;

    public Exception? StartupFailure => _startupFailure;

    public bool ContainsShortcut(Guid shortcutId)
    {
        return shortcutId != Guid.Empty && _state.Shortcuts.Any(shortcut => shortcut.Id == shortcutId);
    }

    public bool IsOperationUnresolved(Guid shortcutId)
    {
        return _unresolvedOperationIds.ContainsKey(shortcutId);
    }

    public bool IsManageableShortcut(Guid shortcutId)
    {
        TrackedShortcut? shortcut = _state.Shortcuts.FirstOrDefault(item => item.Id == shortcutId);
        DesktopRootCheckpoint? userRoot = _state.Roots.FirstOrDefault(root => root.RootKind == DesktopRootKind.User);
        if (shortcut is null
            || userRoot is null
            || !IsRootCurrentAndComplete(DesktopRootKind.User, userRoot.RootPath)
            || !IsRootCurrentAndComplete(shortcut.RootKind, shortcut.RootPath))
        {
            return false;
        }

        if (ShortcutStateTransitions.IsDesktopShortcutName(shortcut.Name)
            && (_state.Roots.FirstOrDefault(root => root.RootKind == DesktopRootKind.Public) is not DesktopRootCheckpoint publicRoot
                || !IsRootCurrentAndComplete(DesktopRootKind.Public, publicRoot.RootPath)))
        {
            return false;
        }

        return shortcut.Operation != ShortcutOperation.Moving
            || shortcut.TargetRootKind is DesktopRootKind targetKind
                && shortcut.TargetRootPath is not null
                && IsRootCurrentAndComplete(targetKind, shortcut.TargetRootPath);
    }

    public bool IsCurrentUserShortcut(Guid shortcutId)
    {
        TrackedShortcut? shortcut = _state.Shortcuts.FirstOrDefault(item => item.Id == shortcutId);
        return shortcut is not null
            && shortcut.RootKind == DesktopRootKind.User
            && IsManageableShortcut(shortcutId);
    }

    public string RecoveryDirectoryPath => _stateStore.RecoveryDirectoryPath;

    public int ParkedShortcutsWithoutIdentityCount => _state.Shortcuts.Count(shortcut =>
        shortcut.Status == ShortcutStatus.Parked && shortcut.ShortcutIdentityHash is null);

    public int RecoveryDuplicateRemovalFailureCount { get; private set; }

    public bool CanAddDesktopShortcut
    {
        get
        {
            DesktopRootCheckpoint? userRoot = _state.Roots.FirstOrDefault(root => root.RootKind == DesktopRootKind.User);
            DesktopRootCheckpoint? publicRoot = _state.Roots.FirstOrDefault(root => root.RootKind == DesktopRootKind.Public);
            return userRoot is not null
                && publicRoot is not null
                && IsRootCurrentAndComplete(DesktopRootKind.User, userRoot.RootPath)
                && IsRootCurrentAndComplete(DesktopRootKind.Public, publicRoot.RootPath)
                && !_state.Shortcuts.Any(shortcut => ShortcutStateTransitions.IsDesktopShortcutName(shortcut.Name))
                && !_inventory.Roots.Any(root => root.Entries.Any(entry =>
                    ShortcutStateTransitions.IsDesktopShortcutName(entry.Name)));
        }
    }

    public IReadOnlyList<ShortcutListEntry> GetShortcutList()
    {
        List<ShortcutListEntry> entries = _state.Shortcuts
            .Select(shortcut => new ShortcutListEntry(
                shortcut.Name,
                shortcut.RootKind,
                shortcut.Status,
                shortcut,
                TryReadHidden(shortcut)))
            .ToList();

        foreach (DesktopRootSnapshot publicRoot in _inventory.Roots.Where(
            root => root.RootKind == DesktopRootKind.Public && root.IsComplete))
        {
            DesktopRootCheckpoint? publicCheckpoint = _state.Roots.FirstOrDefault(
                root => root.RootKind == DesktopRootKind.Public);
            entries.AddRange(publicRoot.Entries
                .Where(entry => entry.Kind == DesktopEntryKind.Shortcut
                    && (publicCheckpoint is null
                        || !PathsEqual(publicCheckpoint.RootPath, publicRoot.RootPath)
                        || publicCheckpoint.KnownPaths.Any(path => PathsEqual(path, entry.Path)))
                    && (publicCheckpoint is null || !PathsEqual(publicCheckpoint.RootPath, publicRoot.RootPath)
                        || !_state.Shortcuts.Any(shortcut => shortcut.RootKind == DesktopRootKind.Public
                            && PathsEqual(shortcut.RootPath, publicRoot.RootPath)
                            && PathsEqual(shortcut.OriginalPath, entry.Path))))
                .Select(entry => new ShortcutListEntry(
                    entry.Name,
                    DesktopRootKind.Public,
                    Status: null,
                    Shortcut: null,
                    IsHidden: entry.IsHidden)));
        }

        return entries
            .OrderBy(entry => entry.RootKind)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool TryReadHidden(TrackedShortcut shortcut)
    {
        try
        {
            return _fileManager.IsHidden(shortcut);
        }
        catch (Exception exception) when (exception is ShortcutFileOperationException
            or StateStoreException
            or IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return shortcut.IsHidden;
        }
    }

    public void Initialize()
    {
        _state = _stateStore.Load();
        ResolveInterruptedOperations();
        BackfillParkedShortcutIdentities();

        RefreshAsync(CancellationToken.None, showNotifications: true).GetAwaiter().GetResult();

        try
        {
            _startAtSignInManager.SetEnabled(_state.StartAtSignIn, _state.StartMinimized);
        }
        catch (Exception exception) when (IsExpectedStartupException(exception))
        {
            _startupFailure = exception;
        }

        StateChanged?.Invoke();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken, bool showNotifications = true)
    {
        InventoryReconciliation? reconciliation = null;
        List<TrackedShortcut> autoParkingFailures = [];
        int removedDuplicates = 0;
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DesktopScanResult scanResult = await Task.Run(_scanner.Scan, cancellationToken).ConfigureAwait(false);
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            (DesktopInventorySnapshot inventory, Dictionary<string, ShortcutIdentityReadResult> identities) =
                EnrichSnapshotWithShortcutIdentities(scanResult.Inventory);
            reconciliation = _reconciler.Reconcile(_state, inventory, nowUtc);
            _stateStore.Save(reconciliation.State);
            _state = reconciliation.State;
            _inventory = inventory;
            autoParkingFailures = await AutoParkDueShortcutsAsync(identities, nowUtc, cancellationToken)
                .ConfigureAwait(false);
            removedDuplicates = RemoveRecoveryDuplicates();
            List<DesktopScanIssue> issues = [.. scanResult.Issues];
            foreach (DesktopRootSnapshot scannedRoot in scanResult.Inventory.Roots)
            {
                DesktopRootCheckpoint? savedRoot = _state.Roots.FirstOrDefault(
                    root => root.RootKind == scannedRoot.RootKind);
                if (savedRoot is not null
                    && scannedRoot.IsComplete
                    && !PathsEqual(savedRoot.RootPath, scannedRoot.RootPath))
                {
                    issues.Add(new DesktopScanIssue(scannedRoot.RootKind, DesktopScanIssueKind.RootPathChanged));
                }
            }

            _scanIssues = issues;
            _lastFailure = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _lastFailure = exception;
            throw;
        }
        finally
        {
            _stateGate.Release();
        }

        StateChanged?.Invoke();
        if (showNotifications && reconciliation is not null)
        {
            ShowNewItemNotifications(reconciliation, autoParkingFailures, removedDuplicates);
        }
    }

    public async Task ApproveAsync(Guid shortcutId, CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureManageableShortcut(shortcutId);
            DesktopGuardState updated = ShortcutStateTransitions.Approve(_state, shortcutId);
            _stateStore.Save(updated);
            _state = updated;
            _lastFailure = null;
        }
        catch (Exception exception)
        {
            if (exception is not OperationCanceledException)
            {
                _lastFailure = exception;
            }

            throw;
        }
        finally
        {
            _stateGate.Release();
        }

        StateChanged?.Invoke();
    }

    public async Task RenameAsync(Guid shortcutId, string baseName, CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureManageableShortcut(shortcutId);
            TrackedShortcut shortcut = FindShortcut(shortcutId);
            string targetName = baseName + Path.GetExtension(shortcut.Name);
            _fileManager.EnsureRenameDestinationAvailable(shortcut, targetName);
            bool isHidden = _fileManager.IsHidden(shortcut);
            DesktopGuardState prepared = ShortcutStateTransitions.SetHidden(_state, shortcutId, isHidden);
            DesktopGuardState pending = ShortcutStateTransitions.BeginRename(prepared, shortcutId, targetName);
            _stateStore.Save(pending);
            _state = pending;

            try
            {
                await _fileManager.RenameAsync(FindShortcut(pending, shortcutId), cancellationToken)
                    .ConfigureAwait(false);
                DesktopGuardState completed = ShortcutStateTransitions.CompleteRename(_state, shortcutId);
                _stateStore.Save(completed);
                _state = completed;
                _unresolvedOperationIds.TryRemove(shortcutId, out _);
                _lastFailure = null;
            }
            catch
            {
                if (TryReconcileFailedOperation(shortcutId) != PendingOperationResolution.OperationMovedFile)
                {
                    throw;
                }
            }
        }
        catch (Exception exception)
        {
            if (exception is not OperationCanceledException)
            {
                _lastFailure = exception;
            }

            throw;
        }
        finally
        {
            _stateGate.Release();
        }

        StateChanged?.Invoke();
    }

    public async Task DeleteAsync(Guid shortcutId, CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureManageableShortcut(shortcutId);
            DesktopGuardState pending = ShortcutStateTransitions.BeginDelete(_state, shortcutId);
            _stateStore.Save(pending);
            _state = pending;

            try
            {
                await _fileManager.DeleteAsync(FindShortcut(pending, shortcutId), cancellationToken)
                    .ConfigureAwait(false);
                DesktopGuardState completed = ShortcutStateTransitions.CompleteDelete(_state, shortcutId);
                _stateStore.Save(completed);
                _state = completed;
                _unresolvedOperationIds.TryRemove(shortcutId, out _);
                _lastFailure = null;
            }
            catch
            {
                if (TryReconcileFailedOperation(shortcutId) != PendingOperationResolution.OperationMovedFile)
                {
                    throw;
                }
            }
        }
        catch (Exception exception)
        {
            if (exception is not OperationCanceledException)
            {
                _lastFailure = exception;
            }

            throw;
        }
        finally
        {
            _stateGate.Release();
        }

        StateChanged?.Invoke();
    }

    public async Task MakeVisibleAsync(Guid shortcutId, CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureManageableShortcut(shortcutId);
            await _fileManager.MakeVisibleAsync(FindShortcut(shortcutId), cancellationToken).ConfigureAwait(false);
            DesktopGuardState updated = ShortcutStateTransitions.SetHidden(_state, shortcutId, false);
            _stateStore.Save(updated);
            _state = updated;
            _lastFailure = null;
        }
        catch (Exception exception)
        {
            if (exception is not OperationCanceledException)
            {
                _lastFailure = exception;
            }

            throw;
        }
        finally
        {
            _stateGate.Release();
        }

        StateChanged?.Invoke();
    }

    public async Task OpenPropertiesAsync(Guid shortcutId, CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureManageableShortcut(shortcutId);
            _fileManager.OpenProperties(FindShortcut(shortcutId));
            _lastFailure = null;
        }
        catch (Exception exception)
        {
            if (exception is not OperationCanceledException)
            {
                _lastFailure = exception;
            }

            throw;
        }
        finally
        {
            _stateGate.Release();
        }

        StateChanged?.Invoke();
    }

    public async Task SetStartupPreferencesAsync(
        bool startAtSignIn,
        bool startMinimized,
        int autoParkDelayMinutes,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DesktopGuardState updated = ShortcutStateTransitions.SetStartMinimized(
                ShortcutStateTransitions.SetStartAtSignIn(
                    ShortcutStateTransitions.SetAutoParkDelayMinutes(_state, autoParkDelayMinutes),
                    startAtSignIn),
                startMinimized);
            _startAtSignInManager.SetEnabled(updated.StartAtSignIn, updated.StartMinimized);
            _stateStore.Save(updated);
            _state = updated;
            _startupFailure = null;
            _lastFailure = null;
        }
        catch (Exception exception)
        {
            _lastFailure = exception;
            try
            {
                _startAtSignInManager.SetEnabled(_state.StartAtSignIn, _state.StartMinimized);
            }
            catch (Exception rollbackException) when (IsExpectedStartupException(rollbackException))
            {
                _startupFailure = rollbackException;
            }

            throw;
        }
        finally
        {
            _stateGate.Release();
        }

        StateChanged?.Invoke();
    }

    public async Task AddDesktopShortcutAsync(CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool stateMayHaveChanged = false;
        try
        {
            if (!CanAddDesktopShortcut)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            }

            DesktopRootCheckpoint userRoot = _state.Roots.Single(root => root.RootKind == DesktopRootKind.User);
            _fileManager.EnsureDesktopShortcutAvailable(userRoot.RootPath);
            DesktopGuardState pending = ShortcutStateTransitions.BeginCreation(
                _state,
                userRoot.RootPath,
                DesktopShortcutCreator.LinkName);
            Guid shortcutId = pending.Shortcuts[^1].Id;
            stateMayHaveChanged = true;
            _stateStore.Save(pending);
            _state = pending;

            try
            {
                _fileManager.CreateDesktopShortcut(
                    FindShortcut(pending, shortcutId),
                    System.Windows.Forms.Application.ExecutablePath,
                    contentHash =>
                    {
                        DesktopGuardState hashed = ShortcutStateTransitions.RecordCreationHash(
                            _state,
                            shortcutId,
                            contentHash);
                        _stateStore.Save(hashed);
                        _state = hashed;
                    });
                DesktopGuardState completed = ShortcutStateTransitions.CompleteCreation(_state, shortcutId);
                _stateStore.Save(completed);
                _state = completed;
                _unresolvedOperationIds.TryRemove(shortcutId, out _);
                _lastFailure = null;
            }
            catch
            {
                if (TryReconcileFailedOperation(shortcutId) != PendingOperationResolution.OperationMovedFile)
                {
                    throw;
                }
            }
        }
        catch (Exception exception)
        {
            _lastFailure = exception;
            throw;
        }
        finally
        {
            _stateGate.Release();
            if (stateMayHaveChanged)
            {
                StateChanged?.Invoke();
            }
        }
    }

    public async Task ParkAsync(Guid shortcutId, CancellationToken cancellationToken)
    {
        int removedDuplicates = 0;
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureManageableShortcut(shortcutId);
            bool isHidden = _fileManager.IsHidden(FindShortcut(shortcutId));
            DesktopGuardState prepared = ShortcutStateTransitions.SetHidden(_state, shortcutId, isHidden);
            DesktopGuardState pending = ShortcutStateTransitions.BeginParking(prepared, shortcutId, Guid.NewGuid());
            _stateStore.Save(pending);
            _state = pending;

            try
            {
                TrackedShortcut pendingShortcut = FindShortcut(pending, shortcutId);
                await _fileManager.ParkAsync(pendingShortcut, cancellationToken).ConfigureAwait(false);
                byte[]? identityHash = TryReadRecoveryIdentityForParking(pendingShortcut)?.IdentityHash;
                DesktopGuardState completed = ShortcutStateTransitions.CompleteParking(
                    _state,
                    shortcutId,
                    identityHash);
                _stateStore.Save(completed);
                _state = completed;
                _unresolvedOperationIds.TryRemove(shortcutId, out _);
                _lastFailure = null;
            }
            catch
            {
                if (TryReconcileFailedOperation(shortcutId) != PendingOperationResolution.OperationMovedFile)
                {
                    throw;
                }
            }

            removedDuplicates = RemoveRecoveryDuplicates();
        }
        catch (Exception exception)
        {
            _lastFailure = exception;
            throw;
        }
        finally
        {
            _stateGate.Release();
        }

        StateChanged?.Invoke();
        if (removedDuplicates > 0)
        {
            _notifications.ShowRecoveryDuplicatesRemoved(removedDuplicates);
        }
    }

    public async Task RestoreAsync(Guid shortcutId, CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureManageableShortcut(shortcutId);
            TrackedShortcut shortcutToRestore = FindShortcut(_state, shortcutId);
            string targetName = _fileManager.GetRestoreTargetName(shortcutToRestore);
            DesktopGuardState pending = ShortcutStateTransitions.BeginRestore(_state, shortcutId, targetName);
            _stateStore.Save(pending);
            _state = pending;

            try
            {
                await _fileManager.RestoreAsync(FindShortcut(pending, shortcutId), cancellationToken)
                    .ConfigureAwait(false);
                DesktopGuardState completed = ShortcutStateTransitions.CompleteRestore(_state, shortcutId);
                _stateStore.Save(completed);
                _state = completed;
                _unresolvedOperationIds.TryRemove(shortcutId, out _);
                _lastFailure = null;
            }
            catch (ShortcutFileOperationException exception)
                when (exception.Failure == ShortcutFileOperationFailure.DestinationConflict)
            {
                CancelDesktopOperationAfterDestinationConflict(shortcutId);
                throw;
            }
            catch
            {
                if (TryReconcileFailedOperation(shortcutId) != PendingOperationResolution.OperationMovedFile)
                {
                    throw;
                }
            }
        }
        catch (Exception exception)
        {
            _lastFailure = exception;
            throw;
        }
        finally
        {
            _stateGate.Release();
        }

        StateChanged?.Invoke();
    }

    public async Task MoveAsync(
        Guid shortcutId,
        DesktopRootKind destinationRootKind,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureManageableShortcut(shortcutId);
            if (!Enum.IsDefined(destinationRootKind)
                || destinationRootKind == FindShortcut(shortcutId).RootKind)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            }

            DesktopRootCheckpoint? targetRoot = _state.Roots.FirstOrDefault(root =>
                root.RootKind == destinationRootKind
                    && IsRootCurrentAndComplete(destinationRootKind, root.RootPath));
            if (targetRoot is null)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            }

            TrackedShortcut shortcutToMove = FindShortcut(shortcutId);
            string targetName = _fileManager.GetMoveTargetName(
                shortcutToMove,
                destinationRootKind,
                targetRoot.RootPath);
            DesktopGuardState pending = ShortcutStateTransitions.BeginMove(
                _state,
                shortcutId,
                destinationRootKind,
                targetRoot.RootPath,
                targetName);
            _stateStore.Save(pending);
            _state = pending;

            try
            {
                await _fileManager.MoveBetweenDesktopsAsync(FindShortcut(pending, shortcutId), cancellationToken)
                    .ConfigureAwait(false);
                DesktopGuardState completed = ShortcutStateTransitions.CompleteMove(_state, shortcutId);
                _stateStore.Save(completed);
                _state = completed;
                _unresolvedOperationIds.TryRemove(shortcutId, out _);
                _lastFailure = null;
            }
            catch (ShortcutFileOperationException exception)
                when (exception.Failure == ShortcutFileOperationFailure.DestinationConflict)
            {
                CancelDesktopOperationAfterDestinationConflict(shortcutId);
                throw;
            }
            catch
            {
                if (TryReconcileFailedOperation(shortcutId) != PendingOperationResolution.OperationMovedFile)
                {
                    throw;
                }
            }
        }
        catch (Exception exception)
        {
            _lastFailure = exception;
            throw;
        }
        finally
        {
            _stateGate.Release();
        }

        StateChanged?.Invoke();
    }

    public async Task RetryPendingOperationAsync(Guid shortcutId, CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureManageableShortcut(shortcutId);
            if (!_unresolvedOperationIds.ContainsKey(shortcutId))
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
            }

            TrackedShortcut pendingShortcut = FindShortcut(shortcutId);
            if (pendingShortcut.Operation == ShortcutOperation.RemovingRecoveryDuplicate)
            {
                if (TryReconcileFailedOperation(shortcutId) == PendingOperationResolution.Ambiguous)
                {
                    throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
                }

                // A later scan may retry only after reading and validating both copies again.
                return;
            }

            await _fileManager.RetryPendingOperationAsync(
                pendingShortcut,
                cancellationToken,
                contentHash =>
                {
                    DesktopGuardState hashed = ShortcutStateTransitions.RecordCreationHash(
                        _state,
                        shortcutId,
                        contentHash);
                    _stateStore.Save(hashed);
                    _state = hashed;
                }).ConfigureAwait(false);
            DesktopGuardState completed = pendingShortcut.Operation switch
            {
                ShortcutOperation.Parking => ShortcutStateTransitions.CompleteParking(_state, shortcutId),
                ShortcutOperation.Restoring => ShortcutStateTransitions.CompleteRestore(_state, shortcutId),
                ShortcutOperation.Moving => ShortcutStateTransitions.CompleteMove(_state, shortcutId),
                ShortcutOperation.Creating => ShortcutStateTransitions.CompleteCreation(_state, shortcutId),
                ShortcutOperation.Renaming => ShortcutStateTransitions.CompleteRename(_state, shortcutId),
                ShortcutOperation.DeletingToRecycleBin or ShortcutOperation.DeletingPermanently =>
                    ShortcutStateTransitions.CompleteDelete(_state, shortcutId),
                _ => throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState),
            };
            _stateStore.Save(completed);
            _state = completed;
            _unresolvedOperationIds.TryRemove(shortcutId, out _);
            _lastFailure = null;
        }
        catch (ShortcutFileOperationException exception)
            when (exception.Failure == ShortcutFileOperationFailure.DestinationConflict)
        {
            _lastFailure = exception;
            if (!CancelDesktopOperationAfterDestinationConflict(shortcutId))
            {
                _ = TryReconcileFailedOperation(shortcutId);
            }

            throw;
        }
        catch (Exception exception)
        {
            _lastFailure = exception;
            if (exception is not OperationCanceledException)
            {
                _ = TryReconcileFailedOperation(shortcutId);
            }

            throw;
        }
        finally
        {
            _stateGate.Release();
            StateChanged?.Invoke();
        }
    }

    public void EnsureRecoveryDirectory()
    {
        _stateStore.EnsureRecoveryDirectory();
    }

    private void ResolveInterruptedOperations()
    {
        DesktopGuardState working = _state;
        foreach (TrackedShortcut shortcut in working.Shortcuts.Where(
            item => item.Operation is ShortcutOperation.Parking
                or ShortcutOperation.Restoring
                or ShortcutOperation.Moving
                or ShortcutOperation.Creating
                or ShortcutOperation.Renaming
                or ShortcutOperation.DeletingToRecycleBin
                or ShortcutOperation.DeletingPermanently
                or ShortcutOperation.RemovingRecoveryDuplicate).ToArray())
        {
            PendingOperationResolution resolution;
            try
            {
                resolution = _fileManager.ResolveInterruptedOperation(shortcut);
            }
            catch (ShortcutFileOperationException)
            {
                _unresolvedOperationIds.TryAdd(shortcut.Id, 0);
                continue;
            }

            DesktopGuardState? recovered = ApplyOperationResolution(working, shortcut.Id, resolution);

            if (recovered is not null)
            {
                _stateStore.Save(recovered);
                working = recovered;
            }
            else
            {
                _unresolvedOperationIds.TryAdd(shortcut.Id, 0);
            }
        }

        _state = working;
    }

    private int RemoveRecoveryDuplicates()
    {
        // Use saved identities only to select candidates. Removal requires fresh identities and locked byte checks.
        RecoveryShortcutSnapshot[] saved = _state.Shortcuts
            .Where(shortcut => shortcut.ShortcutIdentityHash is { Length: 32 })
            .Select(shortcut => new RecoveryShortcutSnapshot(shortcut.Id, shortcut.ShortcutIdentityHash!, DateTimeOffset.MinValue))
            .ToArray();
        HashSet<Guid> candidateIds = RecoveryDuplicatePolicy.FindDuplicates(_state.Shortcuts, saved)
            .SelectMany(pair => new[] { pair.DuplicateId, pair.KeeperId }).ToHashSet();
        TrackedShortcut[] candidates = _state.Shortcuts
            .Where(shortcut => candidateIds.Contains(shortcut.Id))
            .ToArray();
        int firstCandidateIndex = _nextRecoveryDuplicateCandidateId is Guid nextId
            ? Array.FindIndex(candidates, shortcut => shortcut.Id == nextId)
            : -1;
        if (firstCandidateIndex < 0)
        {
            firstCandidateIndex = 0;
        }

        Dictionary<Guid, ShortcutIdentityReadResult> identities = [];
        List<RecoveryShortcutSnapshot> snapshots = [];
        int failures = 0;
        int candidateCount = Math.Min(candidates.Length, (int)(MaximumIdentityBackfillBytes / (1024 * 1024)));
        for (int offset = 0; offset < candidateCount; offset++)
        {
            TrackedShortcut shortcut = candidates[(firstCandidateIndex + offset) % candidates.Length];
            ShortcutIdentityReadResult? identity;
            try
            {
                identity = _fileManager.TryReadParkedIdentity(shortcut);
            }
            catch (Exception exception) when (IsExpectedIdentityReadException(exception))
            {
                identity = null;
            }

            if (identity is null || !identity.IdentityHash.AsSpan().SequenceEqual(shortcut.ShortcutIdentityHash))
            {
                failures++;
                continue;
            }

            identities.Add(shortcut.Id, identity);
            snapshots.Add(new RecoveryShortcutSnapshot(shortcut.Id, identity.IdentityHash, identity.LastWriteUtc));
        }

        _nextRecoveryDuplicateCandidateId = candidates.Length == 0
            ? null
            : candidates[(firstCandidateIndex + candidateCount) % candidates.Length].Id;

        int removed = 0;
        foreach ((Guid duplicateId, Guid keeperId) in RecoveryDuplicatePolicy.FindDuplicates(_state.Shortcuts, snapshots))
        {
            DesktopGuardState pending = ShortcutStateTransitions.BeginRecoveryDuplicateRemoval(_state, duplicateId, keeperId);
            _stateStore.Save(pending);
            _state = pending;
            try
            {
                _fileManager.RemoveRecoveryDuplicate(FindShortcut(duplicateId), identities[duplicateId],
                    FindShortcut(keeperId), identities[keeperId]);
                DesktopGuardState completed = ShortcutStateTransitions.CompleteDelete(_state, duplicateId);
                _stateStore.Save(completed);
                _state = completed;
                _unresolvedOperationIds.TryRemove(duplicateId, out _);
                removed++;
            }
            catch (Exception exception) when (IsExpectedIdentityReadException(exception))
            {
                if (TryReconcileFailedOperation(duplicateId) == PendingOperationResolution.OperationMovedFile)
                {
                    removed++;
                }
                else
                {
                    failures++;
                }
            }
        }

        RecoveryDuplicateRemovalFailureCount = failures;
        return removed;
    }

    private void BackfillParkedShortcutIdentities()
    {
        DesktopGuardState updated = _state;
        long totalReadBytes = 0;
        foreach (TrackedShortcut shortcut in _state.Shortcuts.Where(shortcut =>
                     shortcut.Status == ShortcutStatus.Parked && shortcut.ShortcutIdentityHash is null))
        {
            if (MaximumIdentityBackfillBytes - totalReadBytes < 1024 * 1024)
            {
                break;
            }

            ShortcutIdentityReadResult? identity;
            try
            {
                identity = _fileManager.TryReadParkedIdentity(shortcut);
            }
            catch (Exception exception) when (IsExpectedIdentityReadException(exception))
            {
                continue;
            }

            if (identity is null)
            {
                continue;
            }

            totalReadBytes += identity.SourceLength;
            if (totalReadBytes > MaximumIdentityBackfillBytes)
            {
                break;
            }

            updated = ShortcutStateTransitions.SetShortcutIdentityHash(
                updated,
                shortcut.Id,
                identity.IdentityHash);
        }

        if (!ReferenceEquals(updated, _state))
        {
            _stateStore.Save(updated);
            _state = updated;
        }
    }

    private (DesktopInventorySnapshot Inventory, Dictionary<string, ShortcutIdentityReadResult> Identities)
        EnrichSnapshotWithShortcutIdentities(DesktopInventorySnapshot inventory)
    {
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        Dictionary<string, ShortcutIdentityReadResult> identities = new(pathComparer);
        List<DesktopRootSnapshot> enrichedRoots = [];
        long totalReadBytes = 0;

        foreach (DesktopRootSnapshot root in inventory.Roots)
        {
            DesktopRootCheckpoint? checkpoint = _state.Roots.FirstOrDefault(item => item.RootKind == root.RootKind);
            bool sameRoot = checkpoint is { IsBaselined: true }
                && PathsEqual(checkpoint.RootPath, root.RootPath);
            bool hasParkedRules = sameRoot && _state.Shortcuts.Any(shortcut =>
                shortcut.Status == ShortcutStatus.Parked
                    && shortcut.RootKind == root.RootKind
                    && PathsEqual(shortcut.RootPath, root.RootPath)
                    && shortcut.ShortcutIdentityHash is { Length: 32 });
            if (!root.IsComplete || !hasParkedRules)
            {
                enrichedRoots.Add(root);
                continue;
            }

            HashSet<string> knownPaths = checkpoint!.KnownPaths.ToHashSet(pathComparer);
            HashSet<string> scheduledPaths = _state.Shortcuts
                .Where(shortcut => (shortcut.Status == ShortcutStatus.ScheduledForAutoParking
                        || shortcut.Status == ShortcutStatus.Pending && !shortcut.AutoParkingEvaluated)
                    && shortcut.RootKind == root.RootKind
                    && PathsEqual(shortcut.RootPath, root.RootPath))
                .Select(shortcut => shortcut.OriginalPath)
                .ToHashSet(pathComparer);
            List<DesktopInventoryEntry> entries = [];
            foreach (DesktopInventoryEntry entry in root.Entries)
            {
                if (entry.Kind != DesktopEntryKind.Shortcut
                    || knownPaths.Contains(entry.Path) && !scheduledPaths.Contains(entry.Path)
                    || MaximumIdentityReadBytesPerScan - totalReadBytes < 1024 * 1024)
                {
                    entries.Add(entry);
                    continue;
                }

                ShortcutIdentityReadResult? identity;
                try
                {
                    identity = _fileManager.TryReadDesktopIdentity(entry);
                }
                catch (Exception exception) when (IsExpectedIdentityReadException(exception))
                {
                    identity = null;
                }

                if (identity is null)
                {
                    entries.Add(entry);
                    continue;
                }

                totalReadBytes += identity.SourceLength;
                identities[entry.Path] = identity;
                entries.Add(entry with { ShortcutIdentityHash = identity.IdentityHash, SourceContentHash = identity.SourceContentHash });
            }

            enrichedRoots.Add(root with { Entries = entries });
        }

        return (new DesktopInventorySnapshot(enrichedRoots), identities);
    }

    private async Task<List<TrackedShortcut>> AutoParkDueShortcutsAsync(
        IReadOnlyDictionary<string, ShortcutIdentityReadResult> identities,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        List<TrackedShortcut> failedShortcuts = [];
        DateTimeOffset dueBefore = nowUtc - TimeSpan.FromMinutes(_state.AutoParkDelayMinutes);
        foreach (TrackedShortcut scheduled in _state.Shortcuts
                     .Where(shortcut => shortcut.Status == ShortcutStatus.ScheduledForAutoParking
                         && shortcut.AutoParkingSinceUtc is DateTimeOffset sinceUtc
                         && sinceUtc <= dueBefore)
                     .ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scheduled.AutoParkingSinceUtc is not DateTimeOffset sinceUtc
                || !identities.TryGetValue(scheduled.OriginalPath, out ShortcutIdentityReadResult? identity)
                || scheduled.ShortcutIdentityHash is not { Length: 32 } expectedIdentity
                || !expectedIdentity.AsSpan().SequenceEqual(identity.IdentityHash)
                || !HasMatchingParkedRule(_state, scheduled))
            {
                continue;
            }

            DesktopRootSnapshot? root = _inventory.Roots.FirstOrDefault(item =>
                item.RootKind == scheduled.RootKind
                    && item.IsComplete
                    && PathsEqual(item.RootPath, scheduled.RootPath));
            if (root is null || !root.Entries.Any(entry => PathsEqual(entry.Path, scheduled.OriginalPath)))
            {
                continue;
            }

            DesktopGuardState pending = ShortcutStateTransitions.BeginAutomaticParking(
                _state,
                scheduled.Id,
                Guid.NewGuid());
            _stateStore.Save(pending);
            _state = pending;

            try
            {
                TrackedShortcut pendingShortcut = FindShortcut(pending, scheduled.Id);
                await _fileManager.ParkAutomaticallyAsync(
                    pendingShortcut,
                    identity.SourceContentHash,
                    cancellationToken).ConfigureAwait(false);
                byte[]? recoveryIdentityHash = TryReadRecoveryIdentityForParking(pendingShortcut)?.IdentityHash;
                DesktopGuardState completed = ShortcutStateTransitions.CompleteParking(
                    _state,
                    scheduled.Id,
                    recoveryIdentityHash);
                _stateStore.Save(completed);
                _state = completed;
                _unresolvedOperationIds.TryRemove(scheduled.Id, out _);
            }
            catch (Exception exception)
            {
                PendingOperationResolution resolution = TryReconcileFailedOperation(scheduled.Id);
                if (resolution == PendingOperationResolution.OperationDidNotMoveFile)
                {
                    TrackedShortcut pendingShortcut = FindShortcut(scheduled.Id);
                    if (pendingShortcut.Status == ShortcutStatus.Pending
                        && pendingShortcut.Operation == ShortcutOperation.None)
                    {
                        failedShortcuts.Add(pendingShortcut);
                    }
                }

                if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
            }
        }

        return failedShortcuts;
    }

    private ShortcutIdentityReadResult? TryReadRecoveryIdentityForParking(TrackedShortcut shortcut)
    {
        try
        {
            return _fileManager.TryReadRecoveryIdentityForParking(shortcut);
        }
        catch (Exception exception) when (IsExpectedIdentityReadException(exception))
        {
            return null;
        }
    }

    private static bool HasMatchingParkedRule(DesktopGuardState state, TrackedShortcut scheduled)
    {
        return state.Shortcuts.Any(rule => rule.Status == ShortcutStatus.Parked
            && rule.Operation == ShortcutOperation.None
            && rule.RootKind == scheduled.RootKind
            && PathsEqual(rule.RootPath, scheduled.RootPath)
            && rule.ShortcutIdentityHash is { Length: 32 } ruleHash
            && scheduled.ShortcutIdentityHash is { Length: 32 } scheduledHash
            && ruleHash.AsSpan().SequenceEqual(scheduledHash));
    }

    private static bool IsExpectedIdentityReadException(Exception exception)
    {
        return exception is ShortcutFileOperationException
            or StateStoreException
            or IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or System.Runtime.InteropServices.COMException;
    }

    private void ShowNewItemNotifications(
        InventoryReconciliation reconciliation,
        IReadOnlyList<TrackedShortcut> autoParkingFailures,
        int removedDuplicates)
    {
        // Windows may suppress notifications. The pending list and Public Desktop list remain authoritative.
        int shown = 0;
        if (removedDuplicates > 0)
        {
            _notifications.ShowRecoveryDuplicatesRemoved(removedDuplicates);
            shown++;
        }
        foreach (TrackedShortcut shortcut in reconciliation.NewUserShortcuts)
        {
            if (shown++ >= MaximumNotificationsPerScan)
            {
                return;
            }

            _notifications.ShowNewShortcut(shortcut, shortcut.Name, isPublic: false);
        }

        foreach (TrackedShortcut shortcut in reconciliation.NewPublicShortcuts)
        {
            if (shown++ >= MaximumNotificationsPerScan)
            {
                return;
            }

            _notifications.ShowNewShortcut(shortcut, shortcut.Name, isPublic: true);
        }

        foreach (TrackedShortcut shortcut in autoParkingFailures)
        {
            if (shown++ >= MaximumNotificationsPerScan)
            {
                return;
            }

            _notifications.ShowNewShortcut(
                shortcut,
                shortcut.Name,
                isPublic: shortcut.RootKind == DesktopRootKind.Public);
        }
    }

    private PendingOperationResolution TryReconcileFailedOperation(Guid shortcutId)
    {
        try
        {
            TrackedShortcut shortcut = FindShortcut(shortcutId);
            PendingOperationResolution resolution = _fileManager.ResolveInterruptedOperation(shortcut);
            DesktopGuardState? reconciled = ApplyOperationResolution(_state, shortcutId, resolution);
            if (reconciled is null)
            {
                _unresolvedOperationIds.TryAdd(shortcutId, 0);
                return PendingOperationResolution.Ambiguous;
            }

            _stateStore.Save(reconciled);
            _state = reconciled;
            _unresolvedOperationIds.TryRemove(shortcutId, out _);
            _lastFailure = null;
            return resolution;
        }
        catch (Exception)
        {
            // Keep the durable operation intent if its file state or new state cannot be saved safely.
            _unresolvedOperationIds.TryAdd(shortcutId, 0);
            return PendingOperationResolution.Ambiguous;
        }
    }

    private bool CancelDesktopOperationAfterDestinationConflict(Guid shortcutId)
    {
        TrackedShortcut shortcut = FindShortcut(_state, shortcutId);
        DesktopGuardState? canceled = shortcut.Operation switch
        {
            ShortcutOperation.Restoring => ShortcutStateTransitions.CancelRestore(_state, shortcutId),
            ShortcutOperation.Moving => ShortcutStateTransitions.CancelMove(_state, shortcutId),
            _ => null,
        };
        if (canceled is null)
        {
            return false;
        }

        try
        {
            _stateStore.Save(canceled);
        }
        catch
        {
            _unresolvedOperationIds.TryAdd(shortcutId, 0);
            throw;
        }

        _state = canceled;
        _unresolvedOperationIds.TryRemove(shortcutId, out _);
        return true;
    }

    private static DesktopGuardState? ApplyOperationResolution(
        DesktopGuardState state,
        Guid shortcutId,
        PendingOperationResolution resolution)
    {
        TrackedShortcut shortcut = FindShortcut(state, shortcutId);
        return shortcut.Operation switch
        {
            ShortcutOperation.Parking when resolution == PendingOperationResolution.OperationDidNotMoveFile =>
                ShortcutStateTransitions.CancelParking(state, shortcutId),
            ShortcutOperation.Parking when resolution == PendingOperationResolution.OperationMovedFile =>
                ShortcutStateTransitions.CompleteParking(state, shortcutId),
            ShortcutOperation.Restoring when resolution == PendingOperationResolution.OperationDidNotMoveFile =>
                ShortcutStateTransitions.CancelRestore(state, shortcutId),
            ShortcutOperation.Restoring when resolution == PendingOperationResolution.OperationMovedFile =>
                ShortcutStateTransitions.CompleteRestore(state, shortcutId),
            ShortcutOperation.Moving when resolution == PendingOperationResolution.OperationDidNotMoveFile =>
                ShortcutStateTransitions.CancelMove(state, shortcutId),
            ShortcutOperation.Moving when resolution == PendingOperationResolution.OperationMovedFile =>
                ShortcutStateTransitions.CompleteMove(state, shortcutId),
            ShortcutOperation.Creating when resolution == PendingOperationResolution.OperationDidNotMoveFile =>
                ShortcutStateTransitions.CancelCreation(state, shortcutId),
            ShortcutOperation.Creating when resolution == PendingOperationResolution.OperationMovedFile =>
                ShortcutStateTransitions.CompleteCreation(state, shortcutId),
            ShortcutOperation.Renaming when resolution == PendingOperationResolution.OperationDidNotMoveFile =>
                ShortcutStateTransitions.CancelRename(state, shortcutId),
            ShortcutOperation.Renaming when resolution == PendingOperationResolution.OperationMovedFile =>
                ShortcutStateTransitions.CompleteRename(state, shortcutId),
            ShortcutOperation.DeletingToRecycleBin or ShortcutOperation.DeletingPermanently or ShortcutOperation.RemovingRecoveryDuplicate
                when resolution == PendingOperationResolution.OperationDidNotMoveFile =>
                ShortcutStateTransitions.CancelDelete(state, shortcutId),
            ShortcutOperation.DeletingToRecycleBin or ShortcutOperation.DeletingPermanently or ShortcutOperation.RemovingRecoveryDuplicate
                when resolution == PendingOperationResolution.OperationMovedFile =>
                ShortcutStateTransitions.CompleteDelete(state, shortcutId),
            _ => null,
        };
    }

    private TrackedShortcut FindShortcut(Guid shortcutId)
    {
        return FindShortcut(_state, shortcutId);
    }

    private void EnsureManageableShortcut(Guid shortcutId)
    {
        if (!IsManageableShortcut(shortcutId))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        _ = FindShortcut(shortcutId);
    }

    private bool IsRootCurrentAndComplete(DesktopRootKind kind, string path)
    {
        DesktopRootSnapshot? currentRoot = _inventory.Roots.FirstOrDefault(root => root.RootKind == kind);
        DesktopRootCheckpoint? checkpoint = _state.Roots.FirstOrDefault(root => root.RootKind == kind);
        return currentRoot is not null
            && currentRoot.IsComplete
            && checkpoint is not null
            && PathsEqual(currentRoot.RootPath, path)
            && PathsEqual(checkpoint.RootPath, path);
    }

    private static TrackedShortcut FindShortcut(DesktopGuardState state, Guid shortcutId)
    {
        return state.Shortcuts.FirstOrDefault(shortcut => shortcut.Id == shortcutId)
            ?? throw new KeyNotFoundException("The shortcut is no longer in the current inventory.");
    }

    private static bool IsExpectedStartupException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
