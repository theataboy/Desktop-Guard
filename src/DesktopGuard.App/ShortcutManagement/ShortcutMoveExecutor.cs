using System.IO;
using System.Security;

namespace DesktopGuard.App.ShortcutManagement;

internal sealed class ShortcutMoveExecutor : IShortcutMoveExecutor
{
    private readonly IShortcutElevationBroker _elevationBroker;
    private readonly Action<string, string, Guid, bool> _move;

    public ShortcutMoveExecutor(IShortcutElevationBroker elevationBroker)
        : this(elevationBroker, ShortcutFileTransfer.Move)
    {
    }

    internal ShortcutMoveExecutor(
        IShortcutElevationBroker elevationBroker,
        Action<string, string, Guid, bool> move)
    {
        ArgumentNullException.ThrowIfNull(elevationBroker);
        ArgumentNullException.ThrowIfNull(move);
        _elevationBroker = elevationBroker;
        _move = move;
    }

    public async Task MoveAsync(
        string sourcePath,
        string destinationPath,
        Guid operationId,
        ElevatedShortcutOperationRequest elevatedRequest,
        CancellationToken cancellationToken)
    {
        await ExecuteMoveAsync(
            sourcePath,
            destinationPath,
            operationId,
            elevatedRequest,
            allowExistingDestination: false,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RetryAsync(
        string sourcePath,
        string destinationPath,
        Guid operationId,
        ElevatedShortcutOperationRequest elevatedRequest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ExecuteMoveAsync(
            sourcePath,
            destinationPath,
            operationId,
            elevatedRequest with { IsRetry = true },
            allowExistingDestination: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteMoveAsync(
        string sourcePath,
        string destinationPath,
        Guid operationId,
        ElevatedShortcutOperationRequest elevatedRequest,
        bool allowExistingDestination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _move(sourcePath, destinationPath, operationId, allowExistingDestination);
        }
        catch (Exception exception) when (IsAccessDenied(exception))
        {
            await _elevationBroker.ExecuteAsync(elevatedRequest, cancellationToken).ConfigureAwait(false);
            ShortcutFileTransfer.ValidateCompletedMove(sourcePath, destinationPath);
        }
    }

    private static bool IsAccessDenied(Exception exception)
    {
        return ShortcutFileTransfer.IsAccessDenied(exception)
            || exception is ShortcutFileOperationException
            { Failure: ShortcutFileOperationFailure.AccessDenied };
    }
}

internal static class ShortcutFileTransfer
{
    private const int BufferSize = 64 * 1024;
    private const string TemporaryFileName = "transfer.tmp";

    public static void Move(
        string sourcePath,
        string destinationPath,
        Guid operationId,
        bool allowExistingDestination = false)
    {
        if (operationId == Guid.Empty)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        string operationDirectory = GetOperationDirectory(destinationPath, operationId);
        if (File.Exists(destinationPath))
        {
            if ((allowExistingDestination || IsEmptyOperationDirectory(operationDirectory))
                && File.Exists(sourcePath)
                && FilesAreEqual(sourcePath, destinationPath))
            {
                File.Delete(sourcePath);
                DeleteEmptyOperationDirectory(destinationPath, operationId);
                return;
            }

            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.DestinationConflict);
        }

        ValidateRegularFile(sourcePath);

        if (Directory.Exists(operationDirectory))
        {
            CleanOperationTemporaryFiles(destinationPath, operationId);
        }

        Directory.CreateDirectory(operationDirectory);
        FileAttributes directoryAttributes = File.GetAttributes(operationDirectory);
        if ((directoryAttributes & FileAttributes.ReparsePoint) != 0
            || (directoryAttributes & FileAttributes.Directory) == 0)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        File.SetAttributes(operationDirectory, directoryAttributes | FileAttributes.Hidden);

        string temporaryPath = Path.Combine(operationDirectory, TemporaryFileName);
        CopyToNewFile(sourcePath, temporaryPath);
        if (!FilesAreEqual(sourcePath, temporaryPath))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.SourceChanged);
        }

        EnsureNoDestinationEntry(destinationPath);
        File.Move(temporaryPath, destinationPath);
        if (!FilesAreEqual(sourcePath, destinationPath))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.SourceChanged);
        }

        File.Delete(sourcePath);
        DeleteEmptyOperationDirectory(destinationPath, operationId);
    }

    public static void ValidateCompletedMove(string sourcePath, string destinationPath)
    {
        ValidateRegularFile(destinationPath);
        try
        {
            _ = File.GetAttributes(sourcePath);
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    public static bool IsAccessDenied(Exception exception)
    {
        return (exception is UnauthorizedAccessException or IOException or SecurityException)
            && (exception.HResult & 0xFFFF) == 5;
    }

    public static bool IsFileLocked(Exception exception)
    {
        int errorCode = exception.HResult & 0xFFFF;
        return exception is IOException && errorCode is 32 or 33;
    }

    public static bool FilesAreEqual(string firstPath, string secondPath)
    {
        try
        {
            ValidateRegularFile(firstPath);
            ValidateRegularFile(secondPath);
            FileInfo firstInfo = new(firstPath);
            FileInfo secondInfo = new(secondPath);
            firstInfo.Refresh();
            secondInfo.Refresh();
            if (!firstInfo.Exists || !secondInfo.Exists || firstInfo.Length != secondInfo.Length)
            {
                return false;
            }

            using FileStream first = new(firstPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using FileStream second = new(secondPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] firstBuffer = new byte[BufferSize];
            byte[] secondBuffer = new byte[BufferSize];
            while (true)
            {
                int firstRead = first.Read(firstBuffer, 0, firstBuffer.Length);
                int secondRead = second.Read(secondBuffer, 0, secondBuffer.Length);
                if (firstRead != secondRead
                    || !firstBuffer.AsSpan(0, firstRead).SequenceEqual(secondBuffer.AsSpan(0, secondRead)))
                {
                    return false;
                }

                if (firstRead == 0)
                {
                    return true;
                }
            }
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    public static void CleanOperationTemporaryFiles(string destinationPath, Guid operationId)
    {
        string operationDirectory = GetOperationDirectory(destinationPath, operationId);
        if (!Directory.Exists(operationDirectory))
        {
            return;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(operationDirectory);
            if ((attributes & FileAttributes.Directory) == 0
                || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
            }

            string[] entries = Directory.GetFileSystemEntries(operationDirectory);
            if (entries.Length > 1
                || entries.Length == 1
                    && !PathsEqual(Path.GetFileName(entries[0]), TemporaryFileName))
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
            }

            if (entries.Length == 1)
            {
                ValidateRegularFile(entries[0]);
                File.Delete(entries[0]);
            }

            Directory.Delete(operationDirectory, recursive: false);
        }
        catch (ShortcutFileOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new ShortcutFileOperationException(
                IsFileLocked(exception)
                    ? ShortcutFileOperationFailure.FileLocked
                    : IsAccessDenied(exception)
                        ? ShortcutFileOperationFailure.AccessDenied
                        : ShortcutFileOperationFailure.AmbiguousRecovery,
                exception);
        }
    }

    public static string GetOperationDirectory(string destinationPath, Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        return Path.GetFullPath(Path.Combine(directory, $".desktopguard-{operationId:N}"));
    }

    private static void CopyToNewFile(string sourcePath, string temporaryPath)
    {
        try
        {
            using FileStream source = new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.SequentialScan);
            using FileStream destination = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.WriteThrough);
            source.CopyTo(destination, BufferSize);
            destination.Flush(flushToDisk: true);
        }
        catch (ShortcutFileOperationException)
        {
            throw;
        }
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

    private static bool IsEmptyOperationDirectory(string operationDirectory)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(operationDirectory);
            if ((attributes & FileAttributes.Directory) == 0
                || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
            }

            return Directory.GetFileSystemEntries(operationDirectory).Length == 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void ValidateRegularFile(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }
    }

    private static void DeleteEmptyOperationDirectory(string destinationPath, Guid operationId)
    {
        string operationDirectory = GetOperationDirectory(destinationPath, operationId);
        try
        {
            if (Directory.Exists(operationDirectory)
                && Directory.GetFileSystemEntries(operationDirectory).Length == 0)
            {
                Directory.Delete(operationDirectory, recursive: false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // The move completed; an empty hidden operation folder does not affect the shortcut or recovery data.
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left, right, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }
}
