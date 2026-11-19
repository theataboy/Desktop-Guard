namespace DesktopGuard.App.ShortcutManagement;

internal interface IShortcutMoveExecutor
{
    Task MoveAsync(
        string sourcePath,
        string destinationPath,
        Guid operationId,
        ElevatedShortcutOperationRequest elevatedRequest,
        CancellationToken cancellationToken,
        bool allowElevation = true,
        byte[]? expectedSourceContentHash = null);

    Task RetryAsync(
        string sourcePath,
        string destinationPath,
        Guid operationId,
        ElevatedShortcutOperationRequest elevatedRequest,
        CancellationToken cancellationToken);
}
