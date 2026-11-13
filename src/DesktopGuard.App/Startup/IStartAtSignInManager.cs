namespace DesktopGuard.App.Startup;

internal interface IStartAtSignInManager
{
    void SetEnabled(bool enabled, bool startMinimized);
}
