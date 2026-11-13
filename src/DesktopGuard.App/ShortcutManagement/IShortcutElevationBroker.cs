namespace DesktopGuard.App.ShortcutManagement;

internal interface IShortcutElevationBroker
{
    Task ExecuteAsync(ElevatedShortcutOperationRequest request, CancellationToken cancellationToken);
}
