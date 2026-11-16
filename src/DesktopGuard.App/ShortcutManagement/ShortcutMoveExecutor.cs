namespace DesktopGuard.App.ShortcutManagement;

internal sealed class ShortcutMoveExecutor : IShortcutMoveExecutor
{
    private readonly IShortcutElevationBroker _elevationBroker;
    private readonly Action<string, string, Guid, bool, bool> _move;

    public ShortcutMoveExecutor(IShortcutElevationBroker elevationBroker)
        : this(elevationBroker, (source, destination, operationId, allowExisting, preserveHidden) =>
            ShortcutFileTransfer.Move(source, destination, operationId, allowExisting, preserveHidden))
    {
    }

    internal ShortcutMoveExecutor(
        IShortcutElevationBroker elevationBroker,
        Action<string, string, Guid, bool, bool> move)
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
            _move(
                sourcePath,
                destinationPath,
                operationId,
                allowExistingDestination,
                elevatedRequest.Kind == ElevatedShortcutOperationKind.ParkToRecovery);
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
