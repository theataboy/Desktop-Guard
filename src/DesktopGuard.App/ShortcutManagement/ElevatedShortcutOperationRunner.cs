using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopGuard.App.DesktopScanning;
using DesktopGuard.Core.DesktopScanning;
using Microsoft.Win32.SafeHandles;

namespace DesktopGuard.App.ShortcutManagement;

internal static class ElevatedShortcutOperationRunner
{
    public const int SuccessExitCode = 0;
    private const int InvalidRequestExitCode = 10;
    private const int AccessDeniedExitCode = 20;
    private const int FileLockedExitCode = 21;
    private const int DestinationConflictExitCode = 22;
    private const int OperationFailureExitCode = 23;
    private const int MaximumPayloadLength = 4096;
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly Guid DesktopFolderId = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    private static readonly Guid PublicDesktopFolderId = new("C4AA340D-F20F-4863-AFEF-F87EF2E6BA25");
    private static readonly Guid LocalAppDataFolderId = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");

    public static int Run(string? encodedRequest)
    {
        ElevatedShortcutOperationRequest? request = DecodeRequest(encodedRequest);
        if (request is null || !IsValidRequest(request))
        {
            return InvalidRequestExitCode;
        }

        try
        {
            InteractiveUserFolders userFolders = InteractiveUserFolders.Resolve();
            (string sourcePath, string destinationPath) = ResolvePaths(request, userFolders);
            ValidateRegularShortcut(sourcePath);
            ValidateDestinationDirectory(Path.GetDirectoryName(destinationPath)!);
            ShortcutFileTransfer.Move(sourcePath, destinationPath, request.OperationId, request.IsRetry);
            ShortcutFileTransfer.ValidateCompletedMove(sourcePath, destinationPath);
            return SuccessExitCode;
        }
        catch (ShortcutFileOperationException exception)
        {
            return exception.Failure switch
            {
                ShortcutFileOperationFailure.DestinationConflict => DestinationConflictExitCode,
                ShortcutFileOperationFailure.FileLocked => FileLockedExitCode,
                ShortcutFileOperationFailure.AccessDenied => AccessDeniedExitCode,
                _ => OperationFailureExitCode,
            };
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException
            or Win32Exception
            or COMException)
        {
            return ShortcutFileTransfer.IsFileLocked(exception)
                ? FileLockedExitCode
                : ShortcutFileTransfer.IsAccessDenied(exception)
                    ? AccessDeniedExitCode
                    : OperationFailureExitCode;
        }
    }

    public static ShortcutFileOperationException CreateFailure(int exitCode)
    {
        ShortcutFileOperationFailure failure = exitCode switch
        {
            AccessDeniedExitCode => ShortcutFileOperationFailure.AccessDenied,
            FileLockedExitCode => ShortcutFileOperationFailure.FileLocked,
            DestinationConflictExitCode => ShortcutFileOperationFailure.DestinationConflict,
            _ => ShortcutFileOperationFailure.ElevationFailed,
        };
        return new ShortcutFileOperationException(failure);
    }

    private static ElevatedShortcutOperationRequest? DecodeRequest(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaximumPayloadLength)
        {
            return null;
        }

        try
        {
            string padded = payload.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            byte[] bytes = Convert.FromBase64String(padded);
            return JsonSerializer.Deserialize<ElevatedShortcutOperationRequest>(bytes, RequestJsonOptions);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsValidRequest(ElevatedShortcutOperationRequest request)
    {
        bool validName = DesktopCandidatePolicy.IsValidWindowsFileName(request.Name)
            && string.Equals(Path.GetFileName(request.Name), request.Name, StringComparison.Ordinal)
            && DesktopEntryClassifier.Classify(request.Name) == DesktopEntryKind.Shortcut;
        if (!validName || request.ShortcutId == Guid.Empty || request.OperationId == Guid.Empty)
        {
            return false;
        }

        return request.Kind switch
        {
            ElevatedShortcutOperationKind.MoveBetweenDesktops =>
                request.RecoveryFolderId is null
                && request.SourceRootKind is DesktopRootKind source
                && request.DestinationRootKind is DesktopRootKind destination
                && Enum.IsDefined(source)
                && Enum.IsDefined(destination)
                && source != destination,
            ElevatedShortcutOperationKind.ParkToRecovery =>
                request.RecoveryFolderId is Guid recoveryId
                && recoveryId != Guid.Empty
                && request.SourceRootKind is DesktopRootKind parkSource
                && Enum.IsDefined(parkSource)
                && request.DestinationRootKind is null,
            ElevatedShortcutOperationKind.RestoreFromRecovery =>
                request.RecoveryFolderId is Guid restoreId
                && restoreId != Guid.Empty
                && request.SourceRootKind is null
                && request.DestinationRootKind is DesktopRootKind restoreDestination
                && Enum.IsDefined(restoreDestination),
            _ => false,
        };
    }

    internal static (string SourcePath, string DestinationPath) ResolvePaths(
        ElevatedShortcutOperationRequest request,
        InteractiveUserFolders userFolders)
    {
        return request.Kind switch
        {
            ElevatedShortcutOperationKind.MoveBetweenDesktops =>
                (BuildDesktopPath(request.SourceRootKind!.Value, request.Name, userFolders),
                    BuildDesktopPath(request.DestinationRootKind!.Value, request.Name, userFolders)),
            ElevatedShortcutOperationKind.ParkToRecovery => ResolveParkPaths(request, userFolders),
            ElevatedShortcutOperationKind.RestoreFromRecovery => ResolveRestorePaths(request, userFolders),
            _ => throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState),
        };
    }

    private static (string SourcePath, string DestinationPath) ResolveParkPaths(
        ElevatedShortcutOperationRequest request,
        InteractiveUserFolders userFolders)
    {
        (string dataDirectory, string recoveryRoot, string recoveryFolder, string recoveryPath) =
            GetRecoveryPaths(request, userFolders.LocalAppDataPath);
        return (
            BuildDesktopPath(request.SourceRootKind!.Value, request.Name, userFolders),
            EnsureRecoveryDestination(
                userFolders.LocalAppDataPath,
                dataDirectory,
                recoveryRoot,
                recoveryFolder,
                recoveryPath));
    }

    private static (string SourcePath, string DestinationPath) ResolveRestorePaths(
        ElevatedShortcutOperationRequest request,
        InteractiveUserFolders userFolders)
    {
        (string dataDirectory, string recoveryRoot, string recoveryFolder, string recoveryPath) =
            GetRecoveryPaths(request, userFolders.LocalAppDataPath);
        return (
            ValidateRecoverySource(
                userFolders.LocalAppDataPath,
                dataDirectory,
                recoveryRoot,
                recoveryFolder,
                recoveryPath),
            BuildDesktopPath(request.DestinationRootKind!.Value, request.Name, userFolders));
    }

    private static (string DataDirectory, string RecoveryRoot, string RecoveryFolder, string RecoveryPath)
        GetRecoveryPaths(ElevatedShortcutOperationRequest request, string localAppDataPath)
    {
        if (request.RecoveryFolderId is not Guid recoveryFolderId || recoveryFolderId == Guid.Empty)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidState);
        }

        string dataDirectory = Path.GetFullPath(Path.Combine(localAppDataPath, "DesktopGuard"));
        string recoveryRoot = Path.GetFullPath(Path.Combine(dataDirectory, "Recovery"));
        string recoveryFolder = Path.GetFullPath(Path.Combine(recoveryRoot, recoveryFolderId.ToString("N")));
        string recoveryPath = Path.GetFullPath(Path.Combine(recoveryFolder, request.Name));
        return (dataDirectory, recoveryRoot, recoveryFolder, recoveryPath);
    }

    private static string BuildDesktopPath(
        DesktopRootKind rootKind,
        string name,
        InteractiveUserFolders userFolders)
    {
        string root = rootKind == DesktopRootKind.User
            ? userFolders.DesktopPath
            : userFolders.PublicDesktopPath;
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        string canonicalRoot = Path.GetFullPath(root);
        string path = Path.GetFullPath(Path.Combine(canonicalRoot, name));
        if (!DesktopCandidatePolicy.IsDirectChildOf(canonicalRoot, path))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        return path;
    }

    private static string EnsureRecoveryDestination(
        string localAppData,
        string dataDirectory,
        string recoveryRoot,
        string recoveryFolder,
        string recoveryPath)
    {
        EnsureDirectory(dataDirectory, localAppData);
        EnsureDirectory(recoveryRoot, dataDirectory);
        EnsureDirectory(recoveryFolder, recoveryRoot);
        if (!DesktopCandidatePolicy.IsDirectChildOf(recoveryRoot, recoveryFolder)
            || !DesktopCandidatePolicy.IsDirectChildOf(recoveryFolder, recoveryPath))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        return recoveryPath;
    }

    private static string ValidateRecoverySource(
        string localAppData,
        string dataDirectory,
        string recoveryRoot,
        string recoveryFolder,
        string recoveryPath)
    {
        ValidateDirectory(dataDirectory, localAppData);
        ValidateDirectory(recoveryRoot, dataDirectory);
        ValidateDirectory(recoveryFolder, recoveryRoot);
        if (!DesktopCandidatePolicy.IsDirectChildOf(recoveryRoot, recoveryFolder)
            || !DesktopCandidatePolicy.IsDirectChildOf(recoveryFolder, recoveryPath))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        return recoveryPath;
    }

    private static void EnsureDirectory(string path, string parent)
    {
        if (!DesktopCandidatePolicy.IsDirectChildOf(parent, path))
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }

        Directory.CreateDirectory(path);
        ValidateDirectory(path, parent);
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
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }
    }

    private static void ValidateDestinationDirectory(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }
    }

    private static void ValidateRegularShortcut(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            || DesktopEntryClassifier.Classify(Path.GetFileName(path)) != DesktopEntryKind.Shortcut)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
        }
    }

    internal sealed record InteractiveUserFolders(
        string DesktopPath,
        string PublicDesktopPath,
        string LocalAppDataPath)
    {
        public static InteractiveUserFolders Resolve()
        {
            IntPtr shellWindow = GetShellWindow();
            if (shellWindow == IntPtr.Zero
                || GetWindowThreadProcessId(shellWindow, out uint processId) == 0
                || processId == 0)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            }

            using Process shellProcess = Process.GetProcessById(checked((int)processId));
            using Process currentProcess = Process.GetCurrentProcess();
            if (shellProcess.SessionId != currentProcess.SessionId)
            {
                throw new ShortcutFileOperationException(ShortcutFileOperationFailure.InvalidPath);
            }

            using SafeProcessHandle processHandle = OpenProcess(
                ProcessQueryLimitedInformation,
                inheritHandle: false,
                processId);
            if (processHandle.IsInvalid
                || !OpenProcessToken(processHandle, TokenQuery, out SafeAccessTokenHandle token))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            using (token)
            {
                return new InteractiveUserFolders(
                    WindowsKnownFolderPath.Get(DesktopFolderId, token.DangerousGetHandle()),
                    WindowsKnownFolderPath.Get(PublicDesktopFolderId, token.DangerousGetHandle()),
                    WindowsKnownFolderPath.Get(LocalAppDataFolderId, token.DangerousGetHandle()));
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        SafeProcessHandle processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);
}
