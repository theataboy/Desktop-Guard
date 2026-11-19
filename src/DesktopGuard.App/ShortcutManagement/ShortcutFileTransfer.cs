using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace DesktopGuard.App.ShortcutManagement;

internal static class ShortcutFileTransfer
{
    private const int BufferSize = 64 * 1024;
    private const string TemporaryFileName = "transfer.tmp";
    private const uint GenericRead = 0x80000000;
    private const uint DeleteAccess = 0x00010000;
    private const uint WriteAttributes = 0x00000100;
    private const uint ShareRead = 0x00000001;
    private const uint OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000;
    private const int FileDispositionInfoEx = 21;
    private const int FileAttributeTagInfo = 9;
    private const uint DeleteFile = 0x00000001;
    private const uint PosixSemantics = 0x00000002;
    private const uint IgnoreReadOnlyAttribute = 0x00000010;

    public static void Move(
        string sourcePath,
        string destinationPath,
        Guid operationId,
        bool allowExistingDestination = false,
        bool preserveHidden = false,
        Action? beforeSourceDeletion = null,
        byte[]? expectedSourceContentHash = null)
    {
        if (operationId == Guid.Empty)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        if (expectedSourceContentHash is not null && expectedSourceContentHash.Length != 32)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        using FileStream source = OpenVerifiedFile(
            sourcePath,
            GenericRead | DeleteAccess | WriteAttributes,
            out FileAttributes sourceAttributes);
        DateTime sourceLastWriteUtc = File.GetLastWriteTimeUtc(source.SafeFileHandle);
        if (expectedSourceContentHash is not null)
        {
            if (source.Length is < 1 or > 1024 * 1024)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.SourceChanged);
            }

            source.Position = 0;
            byte[] actualHash = SHA256.HashData(source);
            source.Position = 0;
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedSourceContentHash))
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.SourceChanged);
            }
        }

        string operationDirectory = GetOperationDirectory(destinationPath, operationId);
        if (File.Exists(destinationPath))
        {
            using FileStream existingDestination = OpenVerifiedFile(
                destinationPath,
                GenericRead | WriteAttributes,
                out _);
            if ((allowExistingDestination || IsEmptyOperationDirectory(operationDirectory))
                && FilesAreEqual(source, existingDestination))
            {
                File.SetLastWriteTimeUtc(existingDestination.SafeFileHandle, sourceLastWriteUtc);
                UpdateHiddenAttribute(destinationPath, sourceAttributes, preserveHidden);
                beforeSourceDeletion?.Invoke();
                DeleteSourceByHandle(source.SafeFileHandle);
                DeleteEmptyOperationDirectory(destinationPath, operationId);
                return;
            }

            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.DestinationConflict);
        }

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
        CopyToNewFile(source, temporaryPath, sourceLastWriteUtc);
        using (FileStream temporary = OpenComparisonFile(temporaryPath))
        {
            if (!FilesAreEqual(source, temporary))
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.SourceChanged);
            }
        }

        UpdateHiddenAttribute(temporaryPath, sourceAttributes, preserveHidden);

        EnsureNoDestinationEntry(destinationPath);
        File.Move(temporaryPath, destinationPath);
        using (FileStream destination = OpenComparisonFile(destinationPath))
        {
            if (!FilesAreEqual(source, destination))
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.SourceChanged);
            }

            beforeSourceDeletion?.Invoke();
            DeleteSourceByHandle(source.SafeFileHandle);
        }

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

    internal static byte[]? ReadBoundedContent(string path, int maximumLength, out DateTimeOffset lastWriteUtc)
    {
        if (maximumLength is < 1 or > 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        using FileStream source = OpenVerifiedFile(path, GenericRead, out _);
        lastWriteUtc = File.GetLastWriteTimeUtc(source.SafeFileHandle);
        if (source.Length is < 1 || source.Length > maximumLength)
        {
            return null;
        }

        byte[] bytes = GC.AllocateUninitializedArray<byte>((int)source.Length);
        source.Position = 0;
        source.ReadExactly(bytes);
        if (source.ReadByte() != -1 || source.Length != bytes.Length)
        {
            return null;
        }

        return bytes;
    }

    internal static void DeleteRecoveryDuplicate(
        string duplicatePath,
        byte[] expectedDuplicateHash,
        string keeperPath,
        byte[] expectedKeeperHash)
    {
        if (PathsEqual(duplicatePath, keeperPath)
            || expectedDuplicateHash.Length != 32 || expectedKeeperHash.Length != 32)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        // Lock the retained copy first. Both hashes must still describe the freshly parsed files.
        using FileStream keeper = OpenVerifiedFile(keeperPath, GenericRead, out _);
        using FileStream duplicate = OpenVerifiedFile(duplicatePath, GenericRead | DeleteAccess, out _);
        if (keeper.Length is < 1 or > 1024 * 1024 || duplicate.Length is < 1 or > 1024 * 1024
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(keeper), expectedKeeperHash)
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(duplicate), expectedDuplicateHash))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.SourceChanged);
        }

        DeleteSourceByHandle(duplicate.SafeFileHandle);
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

    private static bool FilesAreEqual(FileStream source, FileStream second)
    {
        if (source.Length != second.Length)
        {
            return false;
        }

        source.Position = 0;
        second.Position = 0;
        byte[] firstBuffer = new byte[BufferSize];
        byte[] secondBuffer = new byte[BufferSize];
        while (true)
        {
            int firstRead = source.Read(firstBuffer, 0, firstBuffer.Length);
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

    private static FileStream OpenComparisonFile(string path)
    {
        return OpenVerifiedFile(path, GenericRead, out _);
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

    public static void SetHiddenAttribute(string path, bool isHidden)
    {
        FileAttributes attributes = File.GetAttributes(path);
        attributes = isHidden
            ? attributes | FileAttributes.Hidden
            : attributes & ~FileAttributes.Hidden;
        if (attributes == 0)
        {
            attributes = FileAttributes.Normal;
        }

        File.SetAttributes(path, attributes);
    }

    private static void CopyToNewFile(FileStream source, string temporaryPath, DateTime sourceLastWriteUtc)
    {
        try
        {
            source.Position = 0;
            using FileStream destination = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.WriteThrough);
            source.CopyTo(destination, BufferSize);
            destination.Flush(flushToDisk: true);
            File.SetLastWriteTimeUtc(destination.SafeFileHandle, sourceLastWriteUtc);
            destination.Flush(flushToDisk: true);
        }
        catch (ShortcutFileOperationException)
        {
            throw;
        }
    }

    private static FileStream OpenVerifiedFile(string path, uint desiredAccess, out FileAttributes attributes)
    {
        SafeFileHandle handle = CreateFileW(
            path,
            desiredAccess,
            ShareRead,
            IntPtr.Zero,
            OpenExisting,
            OpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException("The source shortcut could not be locked for transfer.",
                unchecked((int)(0x80070000u | (uint)error)));
        }

        try
        {
            if (!GetFileInformationByHandleEx(
                handle,
                FileAttributeTagInfo,
                out FileAttributeTagInformation information,
                (uint)Marshal.SizeOf<FileAttributeTagInformation>()))
            {
                throw new IOException("The shortcut attributes could not be checked.",
                    unchecked((int)(0x80070000u | (uint)Marshal.GetLastWin32Error())));
            }

            attributes = information.Attributes;
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            }

            return new FileStream(handle, FileAccess.Read, BufferSize);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static void DeleteSourceByHandle(SafeFileHandle handle)
    {
        FileDispositionInformationEx disposition = new()
        {
            Flags = DeleteFile | PosixSemantics | IgnoreReadOnlyAttribute,
        };
        if (!SetFileInformationByHandle(
            handle,
            FileDispositionInfoEx,
            ref disposition,
            (uint)Marshal.SizeOf<FileDispositionInformationEx>()))
        {
            throw new IOException("The verified source shortcut could not be removed.",
                unchecked((int)(0x80070000u | (uint)Marshal.GetLastWin32Error())));
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle fileHandle,
        int fileInformationClass,
        out FileAttributeTagInformation fileInformation,
        uint bufferSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInformation
    {
        public FileAttributes Attributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInformationEx
    {
        public uint Flags;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle fileHandle,
        int fileInformationClass,
        ref FileDispositionInformationEx fileInformation,
        uint bufferSize);

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

    private static void UpdateHiddenAttribute(string path, FileAttributes sourceAttributes, bool preserveHidden)
    {
        bool shouldBeHidden = preserveHidden && (sourceAttributes & FileAttributes.Hidden) != 0;
        SetHiddenAttribute(path, shouldBeHidden);
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
