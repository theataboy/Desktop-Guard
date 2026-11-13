using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.App.DesktopScanning;

internal sealed class WindowsDesktopRootProvider : IDesktopRootProvider
{
    private static readonly Guid DesktopFolderId = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    private static readonly Guid PublicDesktopFolderId = new("C4AA340D-F20F-4863-AFEF-F87EF2E6BA25");

    public IReadOnlyList<DesktopRoot> GetRoots()
    {
        return
        [
            new DesktopRoot(DesktopRootKind.User, WindowsKnownFolderPath.Get(DesktopFolderId)),
            new DesktopRoot(DesktopRootKind.Public, WindowsKnownFolderPath.Get(PublicDesktopFolderId)),
        ];
    }
}
