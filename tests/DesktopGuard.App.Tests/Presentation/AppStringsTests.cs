using DesktopGuard.App.Resources;

namespace DesktopGuard.App.Tests.Presentation;

[TestClass]
public sealed class AppStringsTests
{
    [TestMethod]
    public void SettingsMenuMnemonic_DoesNotChangeSettingsDialogTitle()
    {
        Assert.AreEqual("Settings", AppStrings.Settings);
        Assert.AreEqual("S&ettings", AppStrings.SettingsMenuItem);
    }
}
