using System.IO;
using System.Security;
using System.Text.Json;
using DesktopGuard.Core.Shortcuts;
using DesktopGuard.Core.State;

namespace DesktopGuard.App.Persistence;

internal sealed class DesktopGuardStateStore : IDesktopGuardStateStore
{
    private const long MaximumStateFileLength = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = 16,
        WriteIndented = true,
    };

    private readonly string _dataDirectory;
    private readonly string _stateFilePath;

    public DesktopGuardStateStore()
        : this(GetDefaultDataDirectory())
    {
    }

    internal DesktopGuardStateStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Path.IsPathFullyQualified(dataDirectory))
        {
            throw new ArgumentException("The state directory must be an absolute path.", nameof(dataDirectory));
        }

        _dataDirectory = Path.GetFullPath(dataDirectory);
        _stateFilePath = Path.Combine(_dataDirectory, "state.json");
    }

    public string RecoveryDirectoryPath => Path.Combine(_dataDirectory, "Recovery");

    public DesktopGuardState Load()
    {
        EnsureDataDirectory();

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(_stateFilePath);
        }
        catch (FileNotFoundException)
        {
            return new DesktopGuardState();
        }
        catch (DirectoryNotFoundException)
        {
            return new DesktopGuardState();
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw new StateStoreException(StateStoreFailureKind.Unavailable, exception);
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0
            || (attributes & FileAttributes.Directory) != 0)
        {
            throw new StateStoreException(StateStoreFailureKind.UnsafePath);
        }

        try
        {
            using FileStream stream = new(_stateFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length <= 0 || stream.Length > MaximumStateFileLength)
            {
                throw new StateStoreException(StateStoreFailureKind.TooLarge);
            }

            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("SchemaVersion", out JsonElement schemaElement)
                || !schemaElement.TryGetInt32(out int schemaVersion))
            {
                throw new StateStoreException(StateStoreFailureKind.Corrupt);
            }

            DesktopGuardState? state = JsonSerializer.Deserialize<DesktopGuardState>(
                document.RootElement.GetRawText(),
                JsonOptions);
            if (state is null)
            {
                throw new StateStoreException(StateStoreFailureKind.Corrupt);
            }

            if (schemaVersion == 1 && DesktopStateValidator.IsValidVersionOne(state))
            {
                // Preserve the old Public checkpoint. Reconciliation uses it to distinguish
                // the existing baseline from shortcuts added after the upgrade.
                return state with
                {
                    SchemaVersion = DesktopGuardState.CurrentSchemaVersion,
                    StartMinimized = true,
                    Shortcuts = state.Shortcuts
                        .Select(shortcut => shortcut.Operation == DesktopGuard.Core.Shortcuts.ShortcutOperation.None
                            ? shortcut
                            : shortcut with { OperationId = shortcut.RecoveryFolderId })
                        .ToList(),
                };
            }

            if (schemaVersion == 2 && DesktopStateValidator.IsValidVersionTwo(state))
            {
                return state with
                {
                    SchemaVersion = DesktopGuardState.CurrentSchemaVersion,
                    StartMinimized = true,
                };
            }

            if (schemaVersion == 3 && DesktopStateValidator.IsValidVersionThree(state))
            {
                return MigrateCreatingShortcuts(state with { StartMinimized = true });
            }

            if (schemaVersion == 4 && DesktopStateValidator.IsValidVersionFour(state))
            {
                return MigrateCreatingShortcuts(state);
            }

            if (schemaVersion == 5 && DesktopStateValidator.IsValidVersionFive(state))
            {
                return MigrateCreatingShortcuts(state);
            }

            if (schemaVersion != DesktopGuardState.CurrentSchemaVersion || !DesktopStateValidator.IsValid(state))
            {
                throw new StateStoreException(StateStoreFailureKind.Corrupt);
            }

            return state;
        }
        catch (StateStoreException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException
                or NotSupportedException
                or IOException
                or UnauthorizedAccessException
                or SecurityException)
        {
            StateStoreFailureKind kind = exception is JsonException or NotSupportedException
                ? StateStoreFailureKind.Corrupt
                : StateStoreFailureKind.Unavailable;
            throw new StateStoreException(kind, exception);
        }
    }

    public void Save(DesktopGuardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!DesktopStateValidator.IsValid(state))
        {
            throw new StateStoreException(StateStoreFailureKind.Corrupt);
        }

        EnsureDataDirectory();
        EnsureStateFileIsNotReparsePoint();

        string temporaryPath = Path.Combine(_dataDirectory, $"state.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, state, JsonOptions);
                if (stream.Length > MaximumStateFileLength)
                {
                    throw new StateStoreException(StateStoreFailureKind.TooLarge);
                }

                stream.Flush(flushToDisk: true);
            }

            EnsureStateFileIsNotReparsePoint();
            File.Move(temporaryPath, _stateFilePath, overwrite: true);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception) || exception is JsonException)
        {
            throw new StateStoreException(StateStoreFailureKind.Unavailable, exception);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                // The temporary file contains only a redundant state serialization.
            }
        }
    }

    public void EnsureRecoveryDirectory()
    {
        EnsureDataDirectory();
        try
        {
            Directory.CreateDirectory(RecoveryDirectoryPath);
            FileAttributes attributes = File.GetAttributes(RecoveryDirectoryPath);
            if ((attributes & FileAttributes.Directory) == 0
                || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new StateStoreException(StateStoreFailureKind.UnsafePath);
            }
        }
        catch (StateStoreException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw new StateStoreException(StateStoreFailureKind.Unavailable, exception);
        }
    }

    private void EnsureDataDirectory()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            FileAttributes attributes = File.GetAttributes(_dataDirectory);
            if ((attributes & FileAttributes.Directory) == 0
                || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new StateStoreException(StateStoreFailureKind.UnsafePath);
            }
        }
        catch (StateStoreException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw new StateStoreException(StateStoreFailureKind.Unavailable, exception);
        }
    }

    private void EnsureStateFileIsNotReparsePoint()
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(_stateFilePath);
            if ((attributes & FileAttributes.ReparsePoint) != 0
                || (attributes & FileAttributes.Directory) != 0)
            {
                throw new StateStoreException(StateStoreFailureKind.UnsafePath);
            }
        }
        catch (FileNotFoundException)
        {
            // The initial state file does not exist yet.
        }
        catch (DirectoryNotFoundException)
        {
            // The initial state file does not exist yet.
        }
        catch (StateStoreException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            throw new StateStoreException(StateStoreFailureKind.Unavailable, exception);
        }
    }

    private static string GetDefaultDataDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new StateStoreException(StateStoreFailureKind.Unavailable);
        }

        return Path.Combine(localAppData, "DesktopGuard");
    }

    private static DesktopGuardState MigrateCreatingShortcuts(DesktopGuardState state)
    {
        return state with
        {
            SchemaVersion = DesktopGuardState.CurrentSchemaVersion,
            Shortcuts = state.Shortcuts
                .Select(shortcut => shortcut.Operation == ShortcutOperation.Creating
                    ? shortcut with { IsDesktopGuardShortcut = true }
                    : shortcut)
                .ToList(),
        };
    }

    private static bool IsExpectedFileSystemException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or SecurityException;
    }
}
