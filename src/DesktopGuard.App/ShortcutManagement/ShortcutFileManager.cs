using System.IO;
using System.Security;
using DesktopGuard.App.DesktopScanning;
using DesktopGuard.App.Persistence;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;

namespace DesktopGuard.App.ShortcutManagement;

internal sealed class ShortcutFileManager
{
    private static readonly TimeSpan RequiredStabilityInterval = TimeSpan.FromSeconds(1);
    private static readonly EnumerationOptions TopLevelEntries = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    private readonly IDesktopRootProvider _rootProvider;
    private readonly IDesktopGuardStateStore _stateStore;
    private readonly IShortcutMoveExecutor _moveExecutor;
    private readonly IShortcutActionExecutor _actionExecutor;
    private readonly DesktopShortcutCreator _desktopShortcutCreator;
    private readonly ShortcutIdentityReader _identityReader;
    private readonly TimeSpan _stabilityInterval;

    public ShortcutFileManager(IDesktopRootProvider rootProvider, IDesktopGuardStateStore stateStore)
        : this(rootProvider, stateStore, new ShortcutMoveExecutor(new WindowsShortcutElevationBroker()), RequiredStabilityInterval)
    {
    }

    internal ShortcutFileManager(
        IDesktopRootProvider rootProvider,
        IDesktopGuardStateStore stateStore,
        TimeSpan stabilityInterval,
        IDesktopShortcutLinkWriter? linkWriter = null)
        : this(
            rootProvider,
            stateStore,
            new ShortcutMoveExecutor(new WindowsShortcutElevationBroker()),
            stabilityInterval,
            linkWriter)
    {
    }

    internal ShortcutFileManager(
        IDesktopRootProvider rootProvider,
        IDesktopGuardStateStore stateStore,
        IShortcutMoveExecutor moveExecutor,
        TimeSpan stabilityInterval,
        IDesktopShortcutLinkWriter? linkWriter = null,
        IShortcutActionExecutor? actionExecutor = null,
        ShortcutIdentityReader? identityReader = null)
    {
        ArgumentNullException.ThrowIfNull(rootProvider);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(moveExecutor);
        if (stabilityInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stabilityInterval));
        }

        _rootProvider = rootProvider;
        _stateStore = stateStore;
        _moveExecutor = moveExecutor;
        _actionExecutor = actionExecutor ?? new WindowsShortcutActionExecutor(new WindowsShortcutElevationBroker());
        _identityReader = identityReader ?? new ShortcutIdentityReader();
        _desktopShortcutCreator = new DesktopShortcutCreator(
            rootProvider,
            linkWriter ?? new WindowsDesktopShortcutLinkWriter());
        _stabilityInterval = stabilityInterval;
    }

    public void EnsureDesktopShortcutAvailable(string userRootPath)
    {
        _desktopShortcutCreator.EnsureDestinationAvailable(userRootPath);
    }

    public string GetMoveTargetName(
        TrackedShortcut shortcut,
        DesktopRootKind targetRootKind,
        string targetRootPath)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (!Enum.IsDefined(targetRootKind) || targetRootKind == shortcut.RootKind)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        string root = GetCurrentDesktopRoot(targetRootKind, targetRootPath);
        return GetAvailableDesktopName(
            root,
            shortcut.Name,
            preserveDesktopShortcutNameOnConflict: false);
    }

    public string GetRestoreTargetName(TrackedShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (shortcut.Status != ShortcutStatus.Parked
            || shortcut.Operation != ShortcutOperation.None
            || shortcut.RecoveryFolderId is null)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        string root = GetCurrentDesktopRoot(shortcut.RootKind, shortcut.RootPath);
        return GetAvailableDesktopName(
            root,
            shortcut.Name,
            preserveDesktopShortcutNameOnConflict: true);
    }

    public void CreateDesktopShortcut(
        TrackedShortcut shortcut,
        string targetPath,
        Action<byte[]> contentHashPrepared)
    {
        _desktopShortcutCreator.Create(shortcut, targetPath, contentHashPrepared);
    }

    public Task ParkAsync(TrackedShortcut shortcut, CancellationToken cancellationToken)
    {
        return ParkCoreAsync(shortcut, allowExistingDestination: false, cancellationToken);
    }

    public Task ParkAutomaticallyAsync(
        TrackedShortcut shortcut,
        byte[] expectedSourceContentHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedSourceContentHash);
        return ParkCoreAsync(
            shortcut,
            allowExistingDestination: false,
            cancellationToken: cancellationToken,
            allowElevation: false,
            expectedSourceContentHash: expectedSourceContentHash);
    }

    public ShortcutIdentityReadResult? TryReadDesktopIdentity(DesktopInventoryEntry entry)
        => TryReadDesktopIdentity(entry, out _);

    public ShortcutIdentityReadResult? TryReadDesktopIdentity(DesktopInventoryEntry entry, out int chargedBytes)
    {
        ArgumentNullException.ThrowIfNull(entry);
        chargedBytes = GetIdentityReservation(entry.Name);
        if (entry.Kind != DesktopEntryKind.Shortcut
            || !DesktopCandidatePolicy.IsValidWindowsFileName(entry.Name)
            || !DesktopCandidatePolicy.IsDirectChildOf(entry.RootPath, entry.Path)
            || !string.Equals(Path.GetFileName(entry.Path), entry.Name, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string root = GetCurrentDesktopRoot(entry.RootKind, entry.RootPath);
        string path = Path.GetFullPath(entry.Path);
        ValidateRegularFile(path, root);
        return _identityReader.TryRead(path, out chargedBytes);
    }

    public ShortcutIdentityReadResult? TryReadParkedIdentity(TrackedShortcut shortcut)
        => TryReadParkedIdentity(shortcut, out _);

    public ShortcutIdentityReadResult? TryReadParkedIdentity(TrackedShortcut shortcut, out int chargedBytes)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        chargedBytes = GetIdentityReservation(shortcut.Name);
        if (shortcut.Status != ShortcutStatus.Parked || shortcut.Operation != ShortcutOperation.None)
        {
            return null;
        }

        string path = GetTrackedShortcutPath(shortcut);
        return _identityReader.TryRead(path, out chargedBytes);
    }

    internal static int GetIdentityReservation(string name) => ShortcutIdentityReader.GetReadReservation(name);

    public ShortcutIdentityReadResult? TryReadRecoveryIdentityForParking(TrackedShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (shortcut.Operation != ShortcutOperation.Parking || shortcut.RecoveryFolderId is null)
        {
            return null;
        }

        string recoveryFolder = GetExistingRecoveryFolder(shortcut);
        string path = BuildRecoveryFilePath(recoveryFolder, shortcut.Name);
        ValidateRegularFile(path, recoveryFolder);
        return _identityReader.TryRead(path);
    }

    public Task RestoreAsync(TrackedShortcut shortcut, CancellationToken cancellationToken)
    {
        return RestoreCoreAsync(shortcut, allowExistingDestination: false, cancellationToken);
    }

    public Task MoveBetweenDesktopsAsync(TrackedShortcut shortcut, CancellationToken cancellationToken)
    {
        return MoveBetweenDesktopsCoreAsync(shortcut, allowExistingDestination: false, cancellationToken);
    }

    public Task RenameAsync(TrackedShortcut shortcut, CancellationToken cancellationToken)
    {
        return RenameCoreAsync(shortcut, allowExistingDestination: false, cancellationToken);
    }

    public Task DeleteAsync(TrackedShortcut shortcut, CancellationToken cancellationToken)
    {
        return DeleteCoreAsync(shortcut, cancellationToken);
    }

    public void RemoveRecoveryDuplicate(
        TrackedShortcut duplicate, ShortcutIdentityReadResult duplicateIdentity,
        TrackedShortcut keeper, ShortcutIdentityReadResult keeperIdentity)
    {
        if (duplicate.Operation != ShortcutOperation.RemovingRecoveryDuplicate
            || duplicate.Status != ShortcutStatus.Parked || duplicate.OperationId is null
            || keeper.Operation != ShortcutOperation.None || keeper.Status != ShortcutStatus.Parked
            || duplicate.RootKind != keeper.RootKind || !PathsEqual(duplicate.RootPath, keeper.RootPath)
            || !duplicateIdentity.IdentityHash.AsSpan().SequenceEqual(keeperIdentity.IdentityHash))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        string duplicatePath = GetTrackedShortcutPath(duplicate);
        string keeperPath = GetTrackedShortcutPath(keeper);
        ShortcutFileTransfer.DeleteRecoveryDuplicate(
            duplicatePath, duplicateIdentity.SourceContentHash, keeperPath, keeperIdentity.SourceContentHash);
        DeleteEmptyRecoveryFolder(Path.GetDirectoryName(duplicatePath)!);
    }

    public Task MakeVisibleAsync(TrackedShortcut shortcut, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string sourcePath = GetTrackedShortcutPath(shortcut);
        if ((File.GetAttributes(sourcePath) & FileAttributes.Hidden) == 0)
        {
            return Task.CompletedTask;
        }

        ElevatedShortcutOperationRequest request = CreateActionRequest(
            ElevatedShortcutOperationKind.MakeVisible,
            shortcut);
        return _actionExecutor.MakeVisibleAsync(sourcePath, request, cancellationToken);
    }

    public void OpenProperties(TrackedShortcut shortcut)
    {
        string path = GetTrackedShortcutPath(shortcut);
        _actionExecutor.OpenProperties(path);
    }

    public void EnsureRenameDestinationAvailable(TrackedShortcut shortcut, string targetName)
    {
        if (!IsShortcutName(targetName)
            || !string.Equals(
                Path.GetExtension(shortcut.Name),
                Path.GetExtension(targetName),
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(shortcut.Name, targetName, StringComparison.Ordinal))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        string sourcePath = GetTrackedShortcutPath(shortcut);
        string root = Path.GetDirectoryName(sourcePath)
            ?? throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        if (IsCaseOnlyRename(shortcut.Name, targetName))
        {
            if (ResolveCaseOnlyRename(root, shortcut.Name, targetName)
                != PendingOperationResolution.OperationDidNotMoveFile)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.DestinationConflict);
            }
        }
        else
        {
            EnsureNoDestinationEntry(BuildDesktopPath(root, targetName));
        }
    }

    public bool IsHidden(TrackedShortcut shortcut)
    {
        string path = GetTrackedShortcutPath(shortcut);
        return (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
    }

    public Task RetryPendingOperationAsync(
        TrackedShortcut shortcut,
        CancellationToken cancellationToken,
        Action<byte[]>? contentHashPrepared = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return shortcut.Operation switch
        {
            ShortcutOperation.Parking => ParkCoreAsync(shortcut, allowExistingDestination: true, cancellationToken),
            ShortcutOperation.Restoring => RestoreCoreAsync(shortcut, allowExistingDestination: true, cancellationToken),
            ShortcutOperation.Moving => MoveBetweenDesktopsCoreAsync(
                shortcut,
                allowExistingDestination: true,
                cancellationToken),
            ShortcutOperation.Creating => RetryDesktopShortcutAsync(shortcut, contentHashPrepared, cancellationToken),
            ShortcutOperation.Renaming => RenameCoreAsync(shortcut, allowExistingDestination: true, cancellationToken),
            ShortcutOperation.DeletingToRecycleBin or ShortcutOperation.DeletingPermanently =>
                DeleteCoreAsync(shortcut, cancellationToken),
            _ => throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState),
        };
    }

    public PendingOperationResolution ResolveInterruptedOperation(TrackedShortcut shortcut)
    {
        if (shortcut.Operation == ShortcutOperation.Creating)
        {
            return _desktopShortcutCreator.ResolveInterruptedOperation(shortcut);
        }

        if (shortcut.Operation is ShortcutOperation.Renaming)
        {
            return ResolveInterruptedRename(shortcut);
        }

        if (shortcut.Operation is ShortcutOperation.DeletingToRecycleBin or ShortcutOperation.DeletingPermanently or ShortcutOperation.RemovingRecoveryDuplicate)
        {
            return ResolveInterruptedDelete(shortcut);
        }

        if (shortcut.Operation is not (ShortcutOperation.Parking or ShortcutOperation.Restoring or ShortcutOperation.Moving)
            || shortcut.OperationId is null
            || shortcut.OperationId == Guid.Empty)
        {
            return PendingOperationResolution.Ambiguous;
        }

        string sourcePath;
        string destinationPath;
        string sourceRoot;
        string destinationRoot;
        try
        {
            if (shortcut.Operation == ShortcutOperation.Moving)
            {
                if (shortcut.TargetRootKind is not DesktopRootKind targetKind
                    || shortcut.TargetRootPath is null)
                {
                    return PendingOperationResolution.Ambiguous;
                }

                sourceRoot = GetCurrentDesktopRoot(shortcut.RootKind, shortcut.RootPath);
                destinationRoot = GetCurrentDesktopRoot(targetKind, shortcut.TargetRootPath);
                sourcePath = ValidateDesktopPath(shortcut, shortcut.RootKind, sourceRoot);
                destinationPath = BuildDesktopPath(destinationRoot, shortcut.TargetName ?? shortcut.Name);
            }
            else
            {
                sourceRoot = GetCurrentDesktopRoot(shortcut.RootKind, shortcut.RootPath);
                string recoveryFolder = GetExistingOrExpectedRecoveryFolder(shortcut);
                string recoveryPath = BuildRecoveryFilePath(recoveryFolder, shortcut.Name);
                if (shortcut.Operation == ShortcutOperation.Parking)
                {
                    sourcePath = ValidateDesktopPath(shortcut, shortcut.RootKind, sourceRoot);
                    destinationPath = recoveryPath;
                    destinationRoot = recoveryFolder;
                }
                else
                {
                    sourcePath = recoveryPath;
                    sourceRoot = recoveryFolder;
                    string desktopRoot = GetCurrentDesktopRoot(shortcut.RootKind, shortcut.RootPath);
                    destinationPath = BuildDesktopPath(desktopRoot, shortcut.TargetName ?? shortcut.Name);
                    destinationRoot = shortcut.RootPath;
                }
            }

            if (HasOperationTemporaryFiles(destinationPath, shortcut.OperationId.Value))
            {
                return PendingOperationResolution.Ambiguous;
            }
        }
        catch (ShortcutFileOperationException)
        {
            return PendingOperationResolution.Ambiguous;
        }

        bool sourceExists = HasSafeFile(sourcePath, sourceRoot);
        bool destinationExists = HasSafeFile(destinationPath, destinationRoot);
        bool hasOtherDesktopDuplicate = false;
        if (shortcut.Operation == ShortcutOperation.Restoring
            && ShortcutStateTransitions.IsDesktopShortcutName(shortcut.Name))
        {
            try
            {
                hasOtherDesktopDuplicate = HasDesktopGuardShortcutOnOtherDesktop(shortcut.RootKind);
            }
            catch (ShortcutFileOperationException)
            {
                return PendingOperationResolution.Ambiguous;
            }
        }

        if (hasOtherDesktopDuplicate)
        {
            return sourceExists && !destinationExists
                ? PendingOperationResolution.OperationDidNotMoveFile
                : PendingOperationResolution.Ambiguous;
        }

        if (sourceExists && !destinationExists)
        {
            return PendingOperationResolution.OperationDidNotMoveFile;
        }

        if (!sourceExists && destinationExists)
        {
            return PendingOperationResolution.OperationMovedFile;
        }

        return PendingOperationResolution.Ambiguous;
    }

    private Task RetryDesktopShortcutAsync(
        TrackedShortcut shortcut,
        Action<byte[]>? contentHashPrepared,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (contentHashPrepared is null)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        string targetPath = System.Windows.Forms.Application.ExecutablePath;
        _desktopShortcutCreator.Retry(shortcut, targetPath, contentHashPrepared);
        return Task.CompletedTask;
    }

    public string GetRecoveryFilePath(TrackedShortcut shortcut)
    {
        if (shortcut.RecoveryFolderId is null)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        string folder = Path.Combine(_stateStore.RecoveryDirectoryPath, shortcut.RecoveryFolderId.Value.ToString("N"));
        return BuildRecoveryFilePath(folder, shortcut.Name);
    }

    private async Task RenameCoreAsync(
        TrackedShortcut shortcut,
        bool allowExistingDestination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePendingRename(shortcut);
        string sourcePath = GetTrackedShortcutPath(shortcut, validateFile: false);
        string sourceRoot = Path.GetDirectoryName(sourcePath)
            ?? throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        string destinationPath = BuildDesktopPath(sourceRoot, shortcut.TargetName!);
        bool caseOnlyRename = IsCaseOnlyRename(shortcut.Name, shortcut.TargetName!);
        if (caseOnlyRename)
        {
            PendingOperationResolution current = ResolveCaseOnlyRename(sourceRoot, shortcut.Name, shortcut.TargetName!);
            if (allowExistingDestination && current == PendingOperationResolution.OperationMovedFile)
            {
                return;
            }

            if (current != PendingOperationResolution.OperationDidNotMoveFile)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
            }
        }

        bool sourceExists = HasSafeFile(sourcePath, sourceRoot);
        bool destinationExists = !caseOnlyRename && HasSafeFile(destinationPath, sourceRoot);
        if (allowExistingDestination && !sourceExists && destinationExists)
        {
            return;
        }

        if (!sourceExists)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.SourceMissing);
        }

        if (destinationExists)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.DestinationConflict);
        }

        if (!caseOnlyRename)
        {
            EnsureNoDestinationEntry(destinationPath);
        }
        ElevatedShortcutOperationRequest request = CreateActionRequest(
            ElevatedShortcutOperationKind.RenameShortcut,
            shortcut,
            shortcut.TargetName,
            allowExistingDestination);
        await _actionExecutor.RenameAsync(sourcePath, destinationPath, request, cancellationToken)
            .ConfigureAwait(false);
        ValidateRegularFile(destinationPath, sourceRoot);
        if (caseOnlyRename
            ? ResolveCaseOnlyRename(sourceRoot, shortcut.Name, shortcut.TargetName!)
                != PendingOperationResolution.OperationMovedFile
            : HasSafeFile(sourcePath, sourceRoot))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
        }
    }

    private async Task DeleteCoreAsync(TrackedShortcut shortcut, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ShortcutOperation expectedOperation = shortcut.Status == ShortcutStatus.Parked
            ? ShortcutOperation.DeletingPermanently
            : ShortcutOperation.DeletingToRecycleBin;
        if (shortcut.Operation != expectedOperation
            || shortcut.OperationId is null
            || shortcut.OperationId == Guid.Empty)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        string sourcePath = GetTrackedShortcutPath(shortcut, validateFile: false);
        string sourceRoot = Path.GetDirectoryName(sourcePath)
            ?? throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        if (!HasSafeFile(sourcePath, sourceRoot))
        {
            return;
        }

        ElevatedShortcutOperationKind kind = shortcut.Status == ShortcutStatus.Parked
            ? ElevatedShortcutOperationKind.DeletePermanently
            : ElevatedShortcutOperationKind.DeleteToRecycleBin;
        ElevatedShortcutOperationRequest request = CreateActionRequest(kind, shortcut);
        if (kind == ElevatedShortcutOperationKind.DeletePermanently)
        {
            await _actionExecutor.DeletePermanentlyAsync(sourcePath, request, cancellationToken)
                .ConfigureAwait(false);
            DeleteEmptyRecoveryFolder(sourceRoot);
        }
        else
        {
            await _actionExecutor.DeleteToRecycleBinAsync(sourcePath, request, cancellationToken)
                .ConfigureAwait(false);
        }

        if (HasSafeFile(sourcePath, sourceRoot))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
        }
    }

    private PendingOperationResolution ResolveInterruptedRename(TrackedShortcut shortcut)
    {
        try
        {
            if (shortcut.OperationId is null
                || shortcut.OperationId == Guid.Empty
                || shortcut.TargetName is null
                || !IsShortcutName(shortcut.TargetName))
            {
                return PendingOperationResolution.Ambiguous;
            }

            string sourcePath = GetTrackedShortcutPath(shortcut, validateFile: false);
            string root = Path.GetDirectoryName(sourcePath)
                ?? throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            string destinationPath = BuildDesktopPath(root, shortcut.TargetName);
            if (IsCaseOnlyRename(shortcut.Name, shortcut.TargetName))
            {
                return ResolveCaseOnlyRename(root, shortcut.Name, shortcut.TargetName);
            }

            bool sourceExists = HasSafeFile(sourcePath, root);
            bool destinationExists = HasSafeFile(destinationPath, root);
            if (sourceExists && !destinationExists)
            {
                return PendingOperationResolution.OperationDidNotMoveFile;
            }

            if (!sourceExists && destinationExists)
            {
                return PendingOperationResolution.OperationMovedFile;
            }
        }
        catch (ShortcutFileOperationException)
        {
        }

        return PendingOperationResolution.Ambiguous;
    }

    private PendingOperationResolution ResolveInterruptedDelete(TrackedShortcut shortcut)
    {
        if (shortcut.OperationId is null || shortcut.OperationId == Guid.Empty)
        {
            return PendingOperationResolution.Ambiguous;
        }

        try
        {
            string sourcePath = GetTrackedShortcutPath(shortcut, validateFile: false);
            string root = Path.GetDirectoryName(sourcePath)
                ?? throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            return HasSafeFile(sourcePath, root)
                ? PendingOperationResolution.OperationDidNotMoveFile
                : PendingOperationResolution.OperationMovedFile;
        }
        catch (ShortcutFileOperationException exception)
            when (exception.Failure == ShortcutFileOperationFailure.SourceMissing)
        {
            return PendingOperationResolution.OperationMovedFile;
        }
        catch (ShortcutFileOperationException)
        {
            return PendingOperationResolution.Ambiguous;
        }
    }

    private async Task ParkCoreAsync(
        TrackedShortcut shortcut,
        bool allowExistingDestination,
        CancellationToken cancellationToken,
        bool allowElevation = true,
        byte[]? expectedSourceContentHash = null)
    {
        ValidatePendingOperation(shortcut, ShortcutOperation.Parking);
        string root = GetCurrentDesktopRoot(shortcut.RootKind, shortcut.RootPath);
        string sourcePath = ValidateDesktopSource(shortcut, shortcut.RootKind, root);
        await EnsureStableAsync(sourcePath, root, cancellationToken).ConfigureAwait(false);

        string recoveryFolder = EnsureRecoveryFolder(shortcut);
        string destinationPath = BuildRecoveryFilePath(recoveryFolder, shortcut.Name);
        ElevatedShortcutOperationRequest request = new(
            ElevatedShortcutOperationKind.ParkToRecovery,
            shortcut.Id,
            shortcut.OperationId!.Value,
            shortcut.RecoveryFolderId,
            shortcut.Name,
            shortcut.RootKind,
            DestinationRootKind: null);
        await MoveAsync(
                sourcePath,
                destinationPath,
                recoveryFolder,
                request,
                shortcut.OperationId.Value,
                allowExistingDestination,
                cancellationToken,
                allowElevation,
                expectedSourceContentHash)
            .ConfigureAwait(false);
    }

    private async Task RestoreCoreAsync(
        TrackedShortcut shortcut,
        bool allowExistingDestination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePendingOperation(shortcut, ShortcutOperation.Restoring);
        string root = GetCurrentDesktopRoot(shortcut.RootKind, shortcut.RootPath);
        string recoveryFolder = GetExistingRecoveryFolder(shortcut);
        string sourcePath = BuildRecoveryFilePath(recoveryFolder, shortcut.Name);
        ValidateRegularFile(sourcePath, recoveryFolder);
        string targetName = shortcut.TargetName ?? shortcut.Name;
        string destinationPath = BuildDesktopPath(root, targetName);
        if (ShortcutStateTransitions.IsDesktopShortcutName(shortcut.Name)
            && HasDesktopGuardShortcutOnOtherDesktop(shortcut.RootKind))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.DesktopShortcutAlreadyExists);
        }

        ElevatedShortcutOperationRequest request = new(
            ElevatedShortcutOperationKind.RestoreFromRecovery,
            shortcut.Id,
            shortcut.OperationId!.Value,
            shortcut.RecoveryFolderId,
            shortcut.Name,
            SourceRootKind: null,
            shortcut.RootKind,
            TargetName: targetName);
        await MoveAsync(sourcePath, destinationPath, root, request, shortcut.OperationId.Value, allowExistingDestination, cancellationToken)
            .ConfigureAwait(false);
        DeleteEmptyRecoveryFolder(recoveryFolder);
    }

    private async Task MoveBetweenDesktopsCoreAsync(
        TrackedShortcut shortcut,
        bool allowExistingDestination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePendingOperation(shortcut, ShortcutOperation.Moving);
        if (shortcut.TargetRootKind is not DesktopRootKind targetKind || shortcut.TargetRootPath is null)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        string sourceRoot = GetCurrentDesktopRoot(shortcut.RootKind, shortcut.RootPath);
        string destinationRoot = GetCurrentDesktopRoot(targetKind, shortcut.TargetRootPath);
        string sourcePath = ValidateDesktopSource(shortcut, shortcut.RootKind, sourceRoot);
        await EnsureStableAsync(sourcePath, sourceRoot, cancellationToken).ConfigureAwait(false);
        string targetName = shortcut.TargetName ?? shortcut.Name;
        string destinationPath = BuildDesktopPath(destinationRoot, targetName);
        ElevatedShortcutOperationRequest request = new(
            ElevatedShortcutOperationKind.MoveBetweenDesktops,
            shortcut.Id,
            shortcut.OperationId!.Value,
            RecoveryFolderId: null,
            shortcut.Name,
            shortcut.RootKind,
            targetKind,
            TargetName: targetName);
        await MoveAsync(
            sourcePath,
            destinationPath,
            destinationRoot,
            request,
            shortcut.OperationId.Value,
            allowExistingDestination,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task MoveAsync(
        string sourcePath,
        string destinationPath,
        string destinationRoot,
        ElevatedShortcutOperationRequest request,
        Guid operationId,
        bool allowExistingDestination,
        CancellationToken cancellationToken,
        bool allowElevation = true,
        byte[]? expectedSourceContentHash = null)
    {
        try
        {
            RevalidateRequestRoots(request, sourcePath, destinationPath);
            ValidateRegularFile(sourcePath, Path.GetDirectoryName(sourcePath)!);
            FileAttributes destinationDirectoryAttributes = File.GetAttributes(destinationRoot);
            if ((destinationDirectoryAttributes & FileAttributes.Directory) == 0
                || (destinationDirectoryAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            }

            if (allowExistingDestination)
            {
                await _moveExecutor.RetryAsync(
                    sourcePath,
                    destinationPath,
                    operationId,
                    request,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _moveExecutor.MoveAsync(
                    sourcePath,
                    destinationPath,
                    operationId,
                    request,
                    cancellationToken,
                    allowElevation,
                    expectedSourceContentHash).ConfigureAwait(false);
            }

            ValidateRegularFile(destinationPath, destinationRoot);
            if (HasSafeFile(sourcePath, Path.GetDirectoryName(sourcePath)!))
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
            }
        }
        catch (ShortcutFileOperationException exception)
            when (exception.Failure == ShortcutFileOperationFailure.DestinationConflict)
        {
            ShortcutFileTransfer.CleanOperationTemporaryFiles(destinationPath, operationId);
            throw;
        }
        catch (ShortcutFileOperationException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            ShortcutFileOperationException translated = TranslateMoveFailure(destinationPath, exception);
            if (translated.Failure == ShortcutFileOperationFailure.DestinationConflict)
            {
                ShortcutFileTransfer.CleanOperationTemporaryFiles(destinationPath, operationId);
            }

            throw translated;
        }
    }

    private void RevalidateRequestRoots(
        ElevatedShortcutOperationRequest request,
        string sourcePath,
        string destinationPath)
    {
        switch (request.Kind)
        {
            case ElevatedShortcutOperationKind.MoveBetweenDesktops:
                _ = GetCurrentDesktopRoot(request.SourceRootKind!.Value, Path.GetDirectoryName(sourcePath)!);
                _ = GetCurrentDesktopRoot(request.DestinationRootKind!.Value, Path.GetDirectoryName(destinationPath)!);
                break;
            case ElevatedShortcutOperationKind.ParkToRecovery:
                _ = GetCurrentDesktopRoot(request.SourceRootKind!.Value, Path.GetDirectoryName(sourcePath)!);
                ValidateRecoveryOperationPath(request, sourcePath, destinationPath, isSourceInRecovery: false);
                break;
            case ElevatedShortcutOperationKind.RestoreFromRecovery:
                _ = GetCurrentDesktopRoot(request.DestinationRootKind!.Value, Path.GetDirectoryName(destinationPath)!);
                ValidateRecoveryOperationPath(request, sourcePath, destinationPath, isSourceInRecovery: true);
                break;
            default:
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }
    }

    private void ValidateRecoveryOperationPath(
        ElevatedShortcutOperationRequest request,
        string sourcePath,
        string destinationPath,
        bool isSourceInRecovery)
    {
        if (request.RecoveryFolderId is not Guid recoveryFolderId || recoveryFolderId == Guid.Empty)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        string recoveryRoot = Path.GetFullPath(_stateStore.RecoveryDirectoryPath);
        string dataDirectory = Path.GetDirectoryName(recoveryRoot)
            ?? throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        string localAppData = Path.GetDirectoryName(dataDirectory)
            ?? throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        string recoveryFolder = GetRecoveryFolderPath(recoveryRoot, recoveryFolderId);
        ValidateDirectory(dataDirectory, localAppData);
        ValidateDirectory(recoveryRoot, dataDirectory);
        ValidateDirectory(recoveryFolder, recoveryRoot);

        string expectedRecoveryPath = BuildRecoveryFilePath(recoveryFolder, request.Name);
        string actualRecoveryPath = isSourceInRecovery ? sourcePath : destinationPath;
        if (!PathsEqual(expectedRecoveryPath, actualRecoveryPath))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }
    }

    private async Task EnsureStableAsync(string path, string root, CancellationToken cancellationToken)
    {
        FileStamp first = ReadFileStamp(path, root);
        await Task.Delay(_stabilityInterval, cancellationToken).ConfigureAwait(false);
        FileStamp second = ReadFileStamp(path, root);
        if (first != second)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.FileNotStable);
        }

        FileStamp final = ReadFileStamp(path, root);
        if (second != final)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.SourceChanged);
        }
    }

    private string GetCurrentDesktopRoot(DesktopRootKind kind, string expectedRoot)
    {
        DesktopRoot? root;
        try
        {
            root = _rootProvider.GetRoots().FirstOrDefault(item => item.Kind == kind);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath, exception);
        }

        if (root is null
            || string.IsNullOrWhiteSpace(root.Path)
            || !Path.IsPathFullyQualified(root.Path)
            || !PathsEqual(Path.GetFullPath(root.Path), Path.GetFullPath(expectedRoot)))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        return Path.GetFullPath(root.Path);
    }

    private bool HasDesktopGuardShortcutOnOtherDesktop(DesktopRootKind targetRootKind)
    {
        DesktopRootKind otherRootKind = targetRootKind == DesktopRootKind.User
            ? DesktopRootKind.Public
            : DesktopRootKind.User;
        DesktopRoot? otherRoot;
        try
        {
            otherRoot = _rootProvider.GetRoots().FirstOrDefault(root => root.Kind == otherRootKind);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw MapFileSystemFailure(exception, ShortcutFileOperationFailure.InvalidPath);
        }

        if (otherRoot is null || !Path.IsPathFullyQualified(otherRoot.Path))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        try
        {
            string root = GetCurrentDesktopRoot(otherRootKind, otherRoot.Path);
            string parent = Path.GetDirectoryName(root)
                ?? throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            ValidateDirectory(root, parent);
            string path = BuildDesktopPath(root, DesktopShortcutCreator.LinkName);
            return HasSafeFile(path, root);
        }
        catch (ShortcutFileOperationException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw MapFileSystemFailure(exception, ShortcutFileOperationFailure.InvalidPath);
        }
    }

    private static string ValidateDesktopSource(TrackedShortcut shortcut, DesktopRootKind expectedKind, string root)
    {
        string path = ValidateDesktopPath(shortcut, expectedKind, root);
        ValidateRegularFile(path, root);
        return path;
    }

    private static string ValidateDesktopPath(TrackedShortcut shortcut, DesktopRootKind expectedKind, string root)
    {
        if (shortcut.RootKind != expectedKind
            || !IsShortcutName(shortcut.Name)
            || !DesktopCandidatePolicy.IsDirectChildOf(root, shortcut.OriginalPath))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        return Path.GetFullPath(shortcut.OriginalPath);
    }

    private static string BuildDesktopPath(string root, string name)
    {
        if (!IsShortcutName(name))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        string path = Path.GetFullPath(Path.Combine(root, name));
        if (!DesktopCandidatePolicy.IsDirectChildOf(root, path))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        return path;
    }

    private static string GetAvailableDesktopName(
        string root,
        string sourceName,
        bool preserveDesktopShortcutNameOnConflict)
    {
        if (!IsShortcutName(sourceName))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        try
        {
            FileAttributes rootAttributes = File.GetAttributes(root);
            if ((rootAttributes & FileAttributes.Directory) == 0
                || (rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            }

            HashSet<string> existingNames = new(StringComparer.OrdinalIgnoreCase);
            int entryCount = 0;
            foreach (string entryPath in Directory.EnumerateFileSystemEntries(root, "*", TopLevelEntries))
            {
                if (++entryCount > DesktopInventoryScanner.MaximumEntriesPerRoot)
                {
                    throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
                }

                string entryName = Path.GetFileName(entryPath);
                if (string.IsNullOrEmpty(entryName))
                {
                    throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
                }

                existingNames.Add(entryName);
            }

            if (!existingNames.Contains(sourceName))
            {
                return sourceName;
            }

            if (preserveDesktopShortcutNameOnConflict
                && ShortcutStateTransitions.IsDesktopShortcutName(sourceName))
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.DestinationConflict);
            }

            string baseName = Path.GetFileNameWithoutExtension(sourceName);
            string extension = Path.GetExtension(sourceName);
            for (int suffix = 2; suffix <= entryCount + 2; suffix++)
            {
                string candidate = $"{baseName}({suffix}){extension}";
                if (!IsShortcutName(candidate))
                {
                    throw new ShortcutFileOperationException(ShortcutFileOperationFailure.DestinationConflict);
                }

                if (!existingNames.Contains(candidate))
                {
                    return candidate;
                }
            }

            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.DestinationConflict);
        }
        catch (ShortcutFileOperationException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw MapFileSystemFailure(exception, ShortcutFileOperationFailure.InvalidPath);
        }
    }

    private void ValidatePendingOperation(TrackedShortcut shortcut, ShortcutOperation operation)
    {
        bool isValid = shortcut.Operation == operation
            && shortcut.OperationId is not null
            && shortcut.OperationId != Guid.Empty
            && IsShortcutName(shortcut.Name);
        isValid &= operation switch
        {
            ShortcutOperation.Parking => shortcut.Status is ShortcutStatus.Pending or ShortcutStatus.ApprovedVisible
                && shortcut.RecoveryFolderId is not null
                && shortcut.OperationId == shortcut.RecoveryFolderId
                && shortcut.TargetRootKind is null
                && shortcut.TargetRootPath is null,
            ShortcutOperation.Restoring => shortcut.Status == ShortcutStatus.Parked
                && shortcut.RecoveryFolderId is not null
                && shortcut.StatusBeforeParking is not null
                && shortcut.TargetRootKind is null
                && shortcut.TargetRootPath is null
                && IsValidOperationTargetName(shortcut),
            ShortcutOperation.Moving => shortcut.Status is ShortcutStatus.Pending or ShortcutStatus.ApprovedVisible
                && shortcut.RecoveryFolderId is null
                && shortcut.TargetRootKind is DesktopRootKind targetKind
                && targetKind != shortcut.RootKind
                && shortcut.TargetRootPath is not null
                && IsValidOperationTargetName(shortcut),
            _ => false,
        };

        if (!isValid)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }
    }

    private static void ValidatePendingRename(TrackedShortcut shortcut)
    {
        if (shortcut.Operation != ShortcutOperation.Renaming
            || shortcut.OperationId is null
            || shortcut.OperationId == Guid.Empty
            || shortcut.TargetName is null
            || !IsShortcutName(shortcut.TargetName)
            || !string.Equals(
                Path.GetExtension(shortcut.Name),
                Path.GetExtension(shortcut.TargetName),
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(shortcut.Name, shortcut.TargetName, StringComparison.Ordinal))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }
    }

    private static bool IsValidOperationTargetName(TrackedShortcut shortcut)
    {
        string targetName = shortcut.TargetName ?? shortcut.Name;
        return IsShortcutName(targetName)
            && string.Equals(
                Path.GetExtension(shortcut.Name),
                Path.GetExtension(targetName),
                StringComparison.OrdinalIgnoreCase);
    }

    private string GetTrackedShortcutPath(TrackedShortcut shortcut, bool validateFile = true)
    {
        if (!IsShortcutName(shortcut.Name))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        string root;
        string path;
        if (shortcut.Status == ShortcutStatus.Parked)
        {
            string recoveryFolder = validateFile
                ? GetExistingRecoveryFolder(shortcut)
                : GetExistingOrExpectedRecoveryFolder(shortcut);
            root = recoveryFolder;
            path = BuildRecoveryFilePath(recoveryFolder, shortcut.Name);
        }
        else
        {
            root = GetCurrentDesktopRoot(shortcut.RootKind, shortcut.RootPath);
            path = ValidateDesktopPath(shortcut, shortcut.RootKind, root);
        }

        if (validateFile)
        {
            ValidateRegularFile(path, root);
        }

        return path;
    }

    private static ElevatedShortcutOperationRequest CreateActionRequest(
        ElevatedShortcutOperationKind kind,
        TrackedShortcut shortcut,
        string? targetName = null,
        bool isRetry = false)
    {
        return new ElevatedShortcutOperationRequest(
            kind,
            shortcut.Id,
            shortcut.OperationId is Guid operationId && operationId != Guid.Empty
                ? operationId
                : Guid.NewGuid(),
            shortcut.Status == ShortcutStatus.Parked ? shortcut.RecoveryFolderId : null,
            shortcut.Name,
            shortcut.Status == ShortcutStatus.Parked ? null : shortcut.RootKind,
            DestinationRootKind: null,
            IsRetry: isRetry,
            TargetName: targetName);
    }

    private static void EnsureNoDestinationEntry(string destinationPath)
    {
        try
        {
            _ = File.GetAttributes(destinationPath);
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.DestinationConflict);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private string EnsureRecoveryFolder(TrackedShortcut shortcut)
    {
        try
        {
            _stateStore.EnsureRecoveryDirectory();
            string recoveryRoot = Path.GetFullPath(_stateStore.RecoveryDirectoryPath);
            string recoveryFolder = GetRecoveryFolderPath(recoveryRoot, shortcut.RecoveryFolderId!.Value);
            Directory.CreateDirectory(recoveryFolder);
            ValidateDirectory(recoveryFolder, recoveryRoot);
            return recoveryFolder;
        }
        catch (StateStoreException exception)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.RecoveryUnavailable, exception);
        }
        catch (ShortcutFileOperationException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw MapFileSystemFailure(exception, ShortcutFileOperationFailure.RecoveryUnavailable);
        }
    }

    private string GetExistingRecoveryFolder(TrackedShortcut shortcut)
    {
        try
        {
            _stateStore.EnsureRecoveryDirectory();
            string recoveryRoot = Path.GetFullPath(_stateStore.RecoveryDirectoryPath);
            string recoveryFolder = GetRecoveryFolderPath(recoveryRoot, shortcut.RecoveryFolderId!.Value);
            ValidateDirectory(recoveryFolder, recoveryRoot);
            return recoveryFolder;
        }
        catch (StateStoreException exception)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.RecoveryUnavailable, exception);
        }
    }

    private string GetExistingOrExpectedRecoveryFolder(TrackedShortcut shortcut)
    {
        try
        {
            _stateStore.EnsureRecoveryDirectory();
            string recoveryRoot = Path.GetFullPath(_stateStore.RecoveryDirectoryPath);
            string recoveryFolder = GetRecoveryFolderPath(recoveryRoot, shortcut.RecoveryFolderId!.Value);
            try
            {
                ValidateDirectory(recoveryFolder, recoveryRoot);
            }
            catch (DirectoryNotFoundException)
            {
                // Parking can be interrupted before its per-shortcut folder is created.
            }
            catch (FileNotFoundException)
            {
                // Parking can be interrupted before its per-shortcut folder is created.
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                throw MapFileSystemFailure(exception, ShortcutFileOperationFailure.RecoveryUnavailable);
            }

            return recoveryFolder;
        }
        catch (StateStoreException exception)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.RecoveryUnavailable, exception);
        }
    }

    private static string GetRecoveryFolderPath(string recoveryRoot, Guid folderId)
    {
        string folder = Path.GetFullPath(Path.Combine(recoveryRoot, folderId.ToString("N")));
        if (folderId == Guid.Empty || !DesktopCandidatePolicy.IsDirectChildOf(recoveryRoot, folder))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        return folder;
    }

    private static string BuildRecoveryFilePath(string recoveryFolder, string name)
    {
        return BuildDesktopPath(recoveryFolder, name);
    }

    private static void ValidateDirectory(string path, string parent)
    {
        if (!DesktopCandidatePolicy.IsDirectChildOf(parent, path))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.RecoveryUnavailable);
        }
    }

    private static FileStamp ReadFileStamp(string path, string root)
    {
        ValidateRegularFile(path, root);
        try
        {
            FileInfo info = new(path);
            info.Refresh();
            return new FileStamp(info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw MapFileSystemFailure(exception, ShortcutFileOperationFailure.AccessDenied);
        }
    }

    private static void ValidateRegularFile(string path, string root)
    {
        if (!DesktopCandidatePolicy.IsDirectChildOf(root, path))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            }
        }
        catch (FileNotFoundException exception)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.SourceMissing, exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.SourceMissing, exception);
        }
        catch (ShortcutFileOperationException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw MapFileSystemFailure(exception, ShortcutFileOperationFailure.AccessDenied);
        }
    }

    private bool HasOperationTemporaryFiles(string destinationPath, Guid operationId)
    {
        string directory = ShortcutFileTransfer.GetOperationDirectory(destinationPath, operationId);
        try
        {
            _ = File.GetAttributes(directory);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw MapFileSystemFailure(exception, ShortcutFileOperationFailure.AmbiguousRecovery);
        }
    }

    private static bool HasSafeFile(string path, string root)
    {
        try
        {
            ValidateRegularFile(path, root);
            return true;
        }
        catch (ShortcutFileOperationException exception)
            when (exception.Failure == ShortcutFileOperationFailure.SourceMissing)
        {
            return false;
        }
        catch (ShortcutFileOperationException)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
        }
    }

    private static void DeleteEmptyRecoveryFolder(string recoveryFolder)
    {
        try
        {
            if (Directory.GetFileSystemEntries(recoveryFolder).Length == 0)
            {
                Directory.Delete(recoveryFolder, recursive: false);
            }
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            // The explicit file action is complete even if its empty recovery folder remains.
        }
    }

    private static ShortcutFileOperationException TranslateMoveFailure(string destinationPath, Exception exception)
    {
        if (ShortcutFileTransfer.IsFileLocked(exception))
        {
            return new ShortcutFileOperationException(ShortcutFileOperationFailure.FileLocked, exception);
        }

        if (ShortcutFileTransfer.IsAccessDenied(exception))
        {
            return new ShortcutFileOperationException(ShortcutFileOperationFailure.AccessDenied, exception);
        }

        try
        {
            _ = File.GetAttributes(destinationPath);
            return new ShortcutFileOperationException(ShortcutFileOperationFailure.DestinationConflict, exception);
        }
        catch (FileNotFoundException)
        {
            return new ShortcutFileOperationException(ShortcutFileOperationFailure.IoFailure, exception);
        }
        catch (DirectoryNotFoundException)
        {
            return new ShortcutFileOperationException(ShortcutFileOperationFailure.RecoveryUnavailable, exception);
        }
        catch (Exception lookupException) when (IsExpectedFileSystemException(lookupException))
        {
            return MapFileSystemFailure(lookupException, ShortcutFileOperationFailure.AccessDenied);
        }
    }

    private static ShortcutFileOperationException MapFileSystemFailure(
        Exception exception,
        ShortcutFileOperationFailure fallback)
    {
        if (ShortcutFileTransfer.IsFileLocked(exception))
        {
            return new ShortcutFileOperationException(ShortcutFileOperationFailure.FileLocked, exception);
        }

        return ShortcutFileTransfer.IsAccessDenied(exception)
            || exception is UnauthorizedAccessException or SecurityException
                ? new ShortcutFileOperationException(ShortcutFileOperationFailure.AccessDenied, exception)
                : new ShortcutFileOperationException(fallback, exception);
    }

    private static bool IsShortcutName(string name)
    {
        return DesktopCandidatePolicy.IsValidWindowsFileName(name)
            && string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
            && DesktopEntryClassifier.Classify(name) == DesktopEntryKind.Shortcut;
    }

    private static bool IsCaseOnlyRename(string sourceName, string targetName)
    {
        return !string.Equals(sourceName, targetName, StringComparison.Ordinal)
            && string.Equals(sourceName, targetName, StringComparison.OrdinalIgnoreCase);
    }

    private static PendingOperationResolution ResolveCaseOnlyRename(
        string directory,
        string sourceName,
        string targetName)
    {
        try
        {
            string[] matches = Directory.EnumerateFileSystemEntries(directory)
                .Where(path => string.Equals(Path.GetFileName(path), sourceName, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
            {
                return PendingOperationResolution.Ambiguous;
            }

            ValidateRegularFile(matches[0], directory);
            string actualName = Path.GetFileName(matches[0]);
            return string.Equals(actualName, sourceName, StringComparison.Ordinal)
                ? PendingOperationResolution.OperationDidNotMoveFile
                : string.Equals(actualName, targetName, StringComparison.Ordinal)
                    ? PendingOperationResolution.OperationMovedFile
                    : PendingOperationResolution.Ambiguous;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw MapFileSystemFailure(exception, ShortcutFileOperationFailure.AmbiguousRecovery);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left, right, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    private static bool IsExpectedFileSystemException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or SecurityException;
    }

    private readonly record struct FileStamp(long Length, DateTime LastWriteTimeUtc);
}
