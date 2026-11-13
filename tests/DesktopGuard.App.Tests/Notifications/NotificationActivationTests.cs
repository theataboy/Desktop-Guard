using DesktopGuard.App.Notifications;

namespace DesktopGuard.App.Tests.Notifications;

[TestClass]
public sealed class NotificationActivationTests
{
    [TestMethod]
    public void ParseShortcutId_AcceptsOneNonEmptyOpaqueIdentifier()
    {
        Guid expected = Guid.NewGuid();
        Dictionary<string, string> arguments = new(StringComparer.Ordinal)
        {
            ["shortcutId"] = expected.ToString("N"),
        };

        Assert.AreEqual(expected, NotificationActivation.ParseShortcutId(arguments));
    }

    [TestMethod]
    public void ParseShortcutId_RejectsMalformedEmptyOrAdditionalArguments()
    {
        Assert.IsNull(NotificationActivation.ParseShortcutId(new Dictionary<string, string>()));
        Assert.IsNull(NotificationActivation.ParseShortcutId(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["shortcutId"] = "not-a-guid",
        }));
        Assert.IsNull(NotificationActivation.ParseShortcutId(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["shortcutId"] = Guid.Empty.ToString("N"),
        }));
        Assert.IsNull(NotificationActivation.ParseShortcutId(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["shortcutId"] = Guid.NewGuid().ToString("N"),
            ["path"] = @"C:\Users\Public\Desktop\shortcut.lnk",
        }));
        Assert.IsNull(NotificationActivation.ParseShortcutId(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ShortcutId"] = Guid.NewGuid().ToString("N"),
        }));
    }

    [TestMethod]
    public void ResolveCurrentShortcutId_RejectsUnknownIdentifiers()
    {
        Guid unknown = Guid.NewGuid();
        Guid? parsed = NotificationActivation.ParseShortcutId(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["shortcutId"] = unknown.ToString("N"),
        });

        Assert.IsNotNull(parsed);
        Assert.IsNull(NotificationActivation.ResolveCurrentShortcutId(parsed, _ => false));
        Assert.IsNull(NotificationActivation.ResolveCurrentShortcutId(Guid.Empty, _ => true));
        Assert.IsNull(NotificationActivation.ResolveCurrentShortcutId(null, _ => true));
    }

    [TestMethod]
    public void ResolveCurrentShortcutId_OnlyReturnsAnIdentifierPresentInCurrentState()
    {
        Guid known = Guid.NewGuid();
        HashSet<Guid> currentShortcutIds = [known];

        Assert.AreEqual(
            known,
            NotificationActivation.ResolveCurrentShortcutId(known, currentShortcutIds.Contains));
    }
}
