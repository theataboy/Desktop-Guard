namespace DesktopGuard.App.ShortcutManagement;

internal interface IShortcutActionExecutor
{
    Task RenameAsync(
        string sourcePath,
        string destinationPath,
        ElevatedShortcutOperationRequest request,
        CancellationToken cancellationToken);

    Task DeleteToRecycleBinAsync(
        string path,
        ElevatedShortcutOperationRequest request,
        CancellationToken cancellationToken);

    Task DeletePermanentlyAsync(
        string path,
        ElevatedShortcutOperationRequest request,
        CancellationToken cancellationToken);

    Task MakeVisibleAsync(
        string path,
        ElevatedShortcutOperationRequest request,
        CancellationToken cancellationToken);

    void OpenProperties(string path);
}
