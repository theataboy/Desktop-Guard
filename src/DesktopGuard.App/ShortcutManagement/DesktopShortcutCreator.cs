using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using DesktopGuard.App.DesktopScanning;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;

namespace DesktopGuard.App.ShortcutManagement;

internal sealed class DesktopShortcutCreator
{
    public const string LinkName = ShortcutStateTransitions.DesktopShortcutName;

    private const string OperationDirectoryPrefix = ".dg-create-";
    private const int MaximumShortcutLength = 1024 * 1024;

    private readonly IDesktopRootProvider _rootProvider;
    private readonly IDesktopShortcutLinkWriter _linkWriter;

    public DesktopShortcutCreator(IDesktopRootProvider rootProvider)
        : this(rootProvider, new WindowsDesktopShortcutLinkWriter())
    {
    }

    internal DesktopShortcutCreator(
        IDesktopRootProvider rootProvider,
        IDesktopShortcutLinkWriter linkWriter)
    {
        ArgumentNullException.ThrowIfNull(rootProvider);
        ArgumentNullException.ThrowIfNull(linkWriter);
        _rootProvider = rootProvider;
        _linkWriter = linkWriter;
    }

    public void EnsureDestinationAvailable(string userRootPath)
    {
        string root = GetCurrentUserRoot(userRootPath);
        string destinationPath = BuildDestinationPath(root, LinkName);
        if (EntryExists(destinationPath))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.DestinationConflict);
        }

        EnsureNoPublicDesktopShortcut();
    }

    public void Create(
        TrackedShortcut shortcut,
        string targetPath,
        Action<byte[]> contentHashPrepared)
    {
        ArgumentNullException.ThrowIfNull(contentHashPrepared);
        string root = ValidatePendingCreation(shortcut);
        string destinationPath = BuildDestinationPath(root, shortcut.Name);
        string stagingDirectory = GetOperationDirectory(root, shortcut.OperationId!.Value);
        string stagingPath = Path.Combine(stagingDirectory, shortcut.Name);

        try
        {
            ValidateTargetPath(targetPath);
            EnsureNoPublicDesktopShortcut();
            EnsureEntryAbsent(destinationPath);
            EnsureEntryAbsent(stagingDirectory);
            Directory.CreateDirectory(stagingDirectory);
            ValidateDirectory(stagingDirectory, root);
            DirectoryInfo stagingInfo = new(stagingDirectory);
            stagingInfo.Attributes |= FileAttributes.Hidden;
            if (HasAnyEntries(stagingDirectory))
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
            }

            EnsureEntryAbsent(stagingPath);
            _linkWriter.Create(stagingPath, Path.GetFullPath(targetPath));
            ValidateRegularFile(stagingPath, stagingDirectory);
            byte[] contentHash = ComputeContentHash(stagingPath);
            contentHashPrepared(contentHash);
            Publish(root, stagingDirectory, stagingPath, destinationPath, contentHash);
        }
        catch (ShortcutFileOperationException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw TranslateFailure(exception, destinationPath);
        }
    }

    public void Retry(
        TrackedShortcut shortcut,
        string targetPath,
        Action<byte[]> contentHashPrepared)
    {
        ArgumentNullException.ThrowIfNull(contentHashPrepared);
        string root = ValidatePendingCreation(shortcut);
        string destinationPath = BuildDestinationPath(root, shortcut.Name);
        string stagingDirectory = GetOperationDirectory(root, shortcut.OperationId!.Value);
        string stagingPath = Path.Combine(stagingDirectory, shortcut.Name);
        EnsureNoPublicDesktopShortcut();
        bool destinationExists = EntryExists(destinationPath);
        bool stagingExists = EntryExists(stagingDirectory);

        if (destinationExists)
        {
            if (stagingExists)
            {
                bool stagingDirectoryIsEmpty = ValidateOperationDirectoryContents(stagingDirectory, stagingPath, root);
                if (stagingDirectoryIsEmpty
                    && shortcut.OperationContentHash is { Length: 32 } expectedHash
                    && IsRegularFile(destinationPath, root)
                    && HashMatches(destinationPath, expectedHash))
                {
                    Directory.Delete(stagingDirectory, recursive: false);
                    return;
                }

                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
            }

            if (shortcut.OperationContentHash is { Length: 32 } finalHash
                && IsRegularFile(destinationPath, root)
                && HashMatches(destinationPath, finalHash))
            {
                return;
            }

            throw new ShortcutFileOperationException(
                stagingExists
                    ? ShortcutFileOperationFailure.AmbiguousRecovery
                    : ShortcutFileOperationFailure.DestinationConflict);
        }

        if (stagingExists)
        {
            CleanupOperationDirectoryForRetry(stagingDirectory, stagingPath, root);
        }

        Create(shortcut with { OperationContentHash = null }, targetPath, contentHashPrepared);
    }

    public PendingOperationResolution ResolveInterruptedOperation(TrackedShortcut shortcut)
    {
        if (shortcut.Operation != ShortcutOperation.Creating
            || shortcut.OperationId is not Guid operationId
            || operationId == Guid.Empty)
        {
            return PendingOperationResolution.Ambiguous;
        }

        try
        {
            string root = ValidatePendingCreation(shortcut);
            EnsureNoPublicDesktopShortcut();
            string destinationPath = BuildDestinationPath(root, shortcut.Name);
            string stagingDirectory = GetOperationDirectory(root, operationId);
            string stagingPath = Path.Combine(stagingDirectory, shortcut.Name);
            bool destinationExists = EntryExists(destinationPath);
            bool stagingExists = EntryExists(stagingDirectory);

            if (destinationExists && stagingExists)
            {
                bool isEmptyStageDirectory = ValidateOperationDirectoryContents(stagingDirectory, stagingPath, root);
                if (!isEmptyStageDirectory)
                {
                    return PendingOperationResolution.Ambiguous;
                }

                if (shortcut.OperationContentHash is null)
                {
                    Directory.Delete(stagingDirectory, recursive: false);
                    return PendingOperationResolution.OperationDidNotMoveFile;
                }

                if (IsRegularFile(destinationPath, root)
                    && HashMatches(destinationPath, shortcut.OperationContentHash))
                {
                    Directory.Delete(stagingDirectory, recursive: false);
                    return PendingOperationResolution.OperationMovedFile;
                }

                return PendingOperationResolution.Ambiguous;
            }

            if (destinationExists)
            {
                if (shortcut.OperationContentHash is null)
                {
                    return PendingOperationResolution.OperationDidNotMoveFile;
                }

                return IsRegularFile(destinationPath, root)
                    && HashMatches(destinationPath, shortcut.OperationContentHash)
                        ? PendingOperationResolution.OperationMovedFile
                        : PendingOperationResolution.Ambiguous;
            }

            if (!stagingExists)
            {
                return PendingOperationResolution.OperationDidNotMoveFile;
            }

            bool stagingDirectoryIsEmpty = ValidateOperationDirectoryContents(stagingDirectory, stagingPath, root);
            if (stagingDirectoryIsEmpty)
            {
                Directory.Delete(stagingDirectory, recursive: false);
                return PendingOperationResolution.OperationDidNotMoveFile;
            }

            if (shortcut.OperationContentHash is null
                || !IsRegularFile(stagingPath, stagingDirectory)
                || !HashMatches(stagingPath, shortcut.OperationContentHash))
            {
                return PendingOperationResolution.Ambiguous;
            }

            Publish(root, stagingDirectory, stagingPath, destinationPath, shortcut.OperationContentHash);
            return PendingOperationResolution.OperationMovedFile;
        }
        catch (Exception exception) when (exception is ShortcutFileOperationException
            or IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException)
        {
            return PendingOperationResolution.Ambiguous;
        }
    }

    private string ValidatePendingCreation(TrackedShortcut shortcut)
    {
        if (shortcut.Operation != ShortcutOperation.Creating
            || shortcut.RootKind != DesktopRootKind.User
            || shortcut.Status != ShortcutStatus.ApprovedVisible
            || shortcut.OperationId is not Guid operationId
            || operationId == Guid.Empty
            || shortcut.RecoveryFolderId is not null
            || !string.Equals(shortcut.Name, LinkName, StringComparison.Ordinal)
            || shortcut.OperationContentHash is { Length: not 32 })
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        string root = GetCurrentUserRoot(shortcut.RootPath);
        string expectedPath = BuildDestinationPath(root, shortcut.Name);
        if (!PathsEqual(shortcut.OriginalPath, expectedPath))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        return root;
    }

    private string GetCurrentUserRoot(string expectedRoot)
    {
        try
        {
            DesktopRoot? userRoot = _rootProvider.GetRoots()
                .FirstOrDefault(root => root.Kind == DesktopRootKind.User);
            if (userRoot is null
                || !Path.IsPathFullyQualified(userRoot.Path)
                || !Path.IsPathFullyQualified(expectedRoot)
                || !PathsEqual(Path.GetFullPath(userRoot.Path), Path.GetFullPath(expectedRoot)))
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            }

            string root = Path.GetFullPath(userRoot.Path);
            ValidateRootDirectory(root);
            return root;
        }
        catch (ShortcutFileOperationException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw MapFileSystemFailure(exception);
        }
    }

    private string GetCurrentPublicRoot()
    {
        try
        {
            DesktopRoot? publicRoot = _rootProvider.GetRoots()
                .FirstOrDefault(root => root.Kind == DesktopRootKind.Public);
            if (publicRoot is null || !Path.IsPathFullyQualified(publicRoot.Path))
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            }

            string root = Path.GetFullPath(publicRoot.Path);
            ValidateRootDirectory(root);
            return root;
        }
        catch (ShortcutFileOperationException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw MapFileSystemFailure(exception);
        }
    }

    private void EnsureNoPublicDesktopShortcut()
    {
        string publicRoot = GetCurrentPublicRoot();
        string publicPath = BuildDestinationPath(publicRoot, LinkName);
        if (EntryExists(publicPath))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.DesktopShortcutAlreadyExists);
        }
    }

    private static string BuildDestinationPath(string root, string name)
    {
        if (!DesktopCandidatePolicy.IsValidWindowsFileName(name)
            || !string.Equals(name, LinkName, StringComparison.Ordinal))
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

    private static string GetOperationDirectory(string root, Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        string path = Path.GetFullPath(Path.Combine(root, OperationDirectoryPrefix + operationId.ToString("N")));
        if (!DesktopCandidatePolicy.IsDirectChildOf(root, path))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        return path;
    }

    private void Publish(
        string root,
        string stagingDirectory,
        string stagingPath,
        string destinationPath,
        byte[] expectedHash)
    {
        root = GetCurrentUserRoot(root);
        EnsureNoPublicDesktopShortcut();
        ValidateDirectory(stagingDirectory, root);
        ValidateRegularFile(stagingPath, stagingDirectory);
        EnsureEntryAbsent(destinationPath);
        if (!HashMatches(stagingPath, expectedHash))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
        }

        root = GetCurrentUserRoot(root);
        ValidateDirectory(stagingDirectory, root);
        ValidateRegularFile(stagingPath, stagingDirectory);
        File.Move(stagingPath, destinationPath, overwrite: false);
        ValidateRegularFile(destinationPath, root);
        if (!HashMatches(destinationPath, expectedHash))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
        }

        DeleteEmptyOperationDirectory(stagingDirectory, root);
    }

    private static void ValidateTargetPath(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)
            || !Path.IsPathFullyQualified(targetPath)
            || targetPath.Length > 32767
            || targetPath.Any(char.IsControl))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        string fullPath = Path.GetFullPath(targetPath);
        ValidateRegularFile(fullPath, Path.GetDirectoryName(fullPath)!);
    }

    private static void ValidateRootDirectory(string root)
    {
        FileAttributes attributes = File.GetAttributes(root);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }
    }

    private static void ValidateDirectory(string directory, string root)
    {
        if (!DesktopCandidatePolicy.IsDirectChildOf(root, directory))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        FileAttributes attributes = File.GetAttributes(directory);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }
    }

    private static void ValidateRegularFile(string path, string root)
    {
        if (!DesktopCandidatePolicy.IsDirectChildOf(root, path))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }
    }

    private static bool IsRegularFile(string path, string root)
    {
        try
        {
            ValidateRegularFile(path, root);
            return true;
        }
        catch (Exception exception) when (IsMissingPath(exception))
        {
            return false;
        }
    }

    private static bool ValidateOperationDirectoryContents(string directory, string expectedPath, string root)
    {
        ValidateDirectory(directory, root);
        using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
        if (!entries.MoveNext())
        {
            return true;
        }

        string firstEntry = entries.Current;
        if (entries.MoveNext() || !PathsEqual(firstEntry, expectedPath))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
        }

        ValidateRegularFile(expectedPath, directory);
        return false;
    }

    private static bool HasAnyEntries(string directory)
    {
        using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
        return entries.MoveNext();
    }

    private static void CleanupOperationDirectoryForRetry(string directory, string expectedPath, string root)
    {
        bool isEmpty = ValidateOperationDirectoryContents(directory, expectedPath, root);
        if (!isEmpty)
        {
            File.Delete(expectedPath);
        }

        Directory.Delete(directory, recursive: false);
    }

    private static void DeleteEmptyOperationDirectory(string directory, string root)
    {
        ValidateDirectory(directory, root);
        if (!HasAnyEntries(directory))
        {
            Directory.Delete(directory, recursive: false);
        }
    }

    private static byte[] ComputeContentHash(string path)
    {
        FileInfo info = new(path);
        info.Refresh();
        if (info.Length <= 0 || info.Length > MaximumShortcutLength)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.IoFailure);
        }

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SHA256.HashData(stream);
    }

    private static bool HashMatches(string path, byte[] expectedHash)
    {
        if (expectedHash.Length != 32)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(ComputeContentHash(path), expectedHash);
    }

    private static bool EntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception) when (IsMissingPath(exception))
        {
            return false;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw MapFileSystemFailure(exception);
        }
    }

    private static void EnsureEntryAbsent(string path)
    {
        if (EntryExists(path))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.DestinationConflict);
        }
    }

    private static ShortcutFileOperationException TranslateFailure(Exception exception, string destinationPath)
    {
        if (IsAccessDenied(exception))
        {
            return new ShortcutFileOperationException(ShortcutFileOperationFailure.AccessDenied, exception);
        }

        if (exception is IOException)
        {
            return EntryExists(destinationPath)
                ? new ShortcutFileOperationException(ShortcutFileOperationFailure.DestinationConflict, exception)
                : new ShortcutFileOperationException(ShortcutFileOperationFailure.IoFailure, exception);
        }

        return new ShortcutFileOperationException(ShortcutFileOperationFailure.IoFailure, exception);
    }

    private static ShortcutFileOperationException MapFileSystemFailure(Exception exception)
    {
        return IsAccessDenied(exception)
                ? new ShortcutFileOperationException(ShortcutFileOperationFailure.AccessDenied, exception)
                : new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath, exception);
    }

    private static bool IsAccessDenied(Exception exception)
    {
        return exception is UnauthorizedAccessException or SecurityException
            || exception is System.ComponentModel.Win32Exception { NativeErrorCode: 5 }
            || exception is COMException comException
                && (comException.ErrorCode == unchecked((int)0x80070005)
                    || (comException.ErrorCode & 0xFFFF) == 5);
    }

    private static bool IsMissingPath(Exception exception)
    {
        return exception is FileNotFoundException or DirectoryNotFoundException;
    }

    private static bool IsExpectedFileSystemException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or SecurityException
            or System.ComponentModel.Win32Exception
            or System.Runtime.InteropServices.COMException;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left, right, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }
}
