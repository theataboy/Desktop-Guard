namespace DesktopGuard.App.DesktopScanning;

internal interface IDesktopRootProvider
{
    IReadOnlyList<DesktopRoot> GetRoots();
}
