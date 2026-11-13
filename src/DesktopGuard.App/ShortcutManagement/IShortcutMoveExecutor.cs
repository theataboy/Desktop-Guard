namespace DesktopGuard.App.ShortcutManagement;

internal interface IShortcutMoveExecutor
{
    Task MoveAsync(
        string sourcePath,
        string destinationPath,
        Guid operationId,
        ElevatedShortcutOperationRequest elevatedRequest,
        CancellationToken cancellationToken);

    Task RetryAsync(
        string sourcePath,
        string destinationPath,
        Guid operationId,
        ElevatedShortcutOperationRequest elevatedRequest,
        CancellationToken cancellationToken);
}
