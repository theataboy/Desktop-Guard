using System.Globalization;
using DesktopGuard.App.Presentation;

namespace DesktopGuard.App.Tests.Presentation;

[TestClass]
public sealed class AboutInformationTests
{
    [TestMethod]
    public void FromAssembly_ReadsVersionAndDisplaysBuildDateAsDdMmYyyy()
    {
        AboutInformation information = AboutInformation.FromAssembly(typeof(AboutInformation).Assembly);

        Assert.IsFalse(string.IsNullOrWhiteSpace(information.Version));
        Assert.IsTrue(
            DateTime.TryParseExact(
                information.BuildDate,
                "dd-MM-yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _),
            $"Expected a dd-MM-yyyy build date, received '{information.BuildDate}'.");
        Assert.AreEqual(
            $"Desktop Guard version {information.Version}; build date {information.BuildDate}",
            information.ToClipboardText());
    }
}
