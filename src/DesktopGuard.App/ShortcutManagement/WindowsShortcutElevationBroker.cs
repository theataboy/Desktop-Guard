using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace DesktopGuard.App.ShortcutManagement;

internal sealed class WindowsShortcutElevationBroker : IShortcutElevationBroker
{
    private const string ElevatedOperationArgument = "--elevated-file-operation";

    public async Task ExecuteAsync(
        ElevatedShortcutOperationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string executablePath = Environment.ProcessPath
            ?? throw new ShortcutFileOperationException(ShortcutFileOperationFailure.ElevationFailed);
        string payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(request))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        ProcessStartInfo startInfo = new(executablePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"{ElevatedOperationArgument} {payload}",
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
        };

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new ShortcutFileOperationException(ShortcutFileOperationFailure.ElevationFailed);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            if (process.ExitCode != ElevatedShortcutOperationRunner.SuccessExitCode)
            {
                throw ElevatedShortcutOperationRunner.CreateFailure(process.ExitCode);
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.ElevationCancelled, exception);
        }
        catch (Win32Exception exception)
        {
            throw new ShortcutFileOperationException(ShortcutFileOperationFailure.ElevationFailed, exception);
        }
    }
}
