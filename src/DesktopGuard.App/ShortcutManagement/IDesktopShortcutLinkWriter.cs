namespace DesktopGuard.App.ShortcutManagement;

internal interface IDesktopShortcutLinkWriter
{
    void Create(string linkPath, string targetPath);
}
