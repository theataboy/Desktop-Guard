using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;

namespace DesktopGuard.App.ShortcutManagement;

internal sealed class WindowsShortcutActionExecutor : IShortcutActionExecutor
{
    private readonly IShortcutElevationBroker _elevationBroker;

    public WindowsShortcutActionExecutor(IShortcutElevationBroker elevationBroker)
    {
        ArgumentNullException.ThrowIfNull(elevationBroker);
        _elevationBroker = elevationBroker;
    }

    public Task RenameAsync(
        string sourcePath,
        string destinationPath,
        ElevatedShortcutOperationRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteWithElevationAsync(
            () => File.Move(sourcePath, destinationPath),
            request,
            cancellationToken,
            () => ShortcutFileTransfer.ValidateCompletedMove(sourcePath, destinationPath));
    }

    public Task DeleteToRecycleBinAsync(
        string path,
        ElevatedShortcutOperationRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteWithElevationAsync(
            () => FileSystem.DeleteFile(
                path,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException),
            request,
            cancellationToken,
            () => EnsureFileWasRemoved(path));
    }

    public Task DeletePermanentlyAsync(
        string path,
        ElevatedShortcutOperationRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteWithElevationAsync(
            () => File.Delete(path),
            request,
            cancellationToken,
            () => EnsureFileWasRemoved(path));
    }

    public Task MakeVisibleAsync(
        string path,
        ElevatedShortcutOperationRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteWithElevationAsync(
            () => ShortcutFileTransfer.SetHiddenAttribute(path, isHidden: false),
            request,
            cancellationToken,
            () =>
            {
                if ((File.GetAttributes(path) & FileAttributes.Hidden) != 0)
                {
                    throw new ShortcutFileOperationException(ShortcutFileOperationFailure.IoFailure);
                }
            });
    }

    public void OpenProperties(string path)
    {
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Verb = "properties",
        });
    }

    private async Task ExecuteWithElevationAsync(
        Action action,
        ElevatedShortcutOperationRequest request,
        CancellationToken cancellationToken,
        Action verifyCompletion)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            action();
        }
        catch (Exception exception) when (ShortcutFileTransfer.IsAccessDenied(exception))
        {
            await _elevationBroker.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }

        verifyCompletion();
    }

    private static void EnsureFileWasRemoved(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.AmbiguousRecovery);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
