using System.Diagnostics;
using DesktopGuard.App.Presentation;

namespace DesktopGuard.App.Tests.Presentation;

[TestClass]
public sealed class AboutActionLauncherTests
{
    private string _temporaryDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _temporaryDirectory = Directory.CreateTempSubdirectory("DesktopGuard.AboutTests.").FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void OpenReleases_OpensOnlyTheFixedReleaseUrlThroughTheShell()
    {
        ProcessStartInfo? started = null;
        AboutActionLauncher launcher = new(_temporaryDirectory, startInfo =>
        {
            started = startInfo;
            return null;
        });

        launcher.OpenReleases();

        Assert.IsNotNull(started);
        Assert.AreEqual(AboutActionLauncher.ReleasesUrl, started.FileName);
        Assert.IsTrue(started.UseShellExecute);
        Assert.IsEmpty(started.ArgumentList);
    }

    [TestMethod]
    public void OpenLicense_StartsNotepadWithThePackageLicenseAsOneArgument()
    {
        string licensePath = Path.Combine(_temporaryDirectory, "LICENSE");
        File.WriteAllText(licensePath, "test license");
        ProcessStartInfo? started = null;
        AboutActionLauncher launcher = new(_temporaryDirectory, startInfo =>
        {
            started = startInfo;
            return new Process();
        });

        launcher.OpenLicense();

        Assert.IsNotNull(started);
        Assert.AreEqual(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"),
            started.FileName);
        Assert.IsFalse(started.UseShellExecute);
        CollectionAssert.AreEqual(new[] { licensePath }, started.ArgumentList.ToArray());
    }

    [TestMethod]
    public void OpenReadme_OpensThePackageHtmlWithTheDefaultHandler()
    {
        string readmePath = Path.Combine(_temporaryDirectory, "README.html");
        File.WriteAllText(readmePath, "<!doctype html>");
        ProcessStartInfo? started = null;
        AboutActionLauncher launcher = new(_temporaryDirectory, startInfo =>
        {
            started = startInfo;
            return null;
        });

        launcher.OpenReadme();

        Assert.IsNotNull(started);
        Assert.AreEqual(readmePath, started.FileName);
        Assert.IsTrue(started.UseShellExecute);
        Assert.IsEmpty(started.ArgumentList);
    }

    [TestMethod]
    public void OpenPackageDocument_ReportsWhenTheFileIsMissing()
    {
        AboutActionLauncher launcher = new(_temporaryDirectory, _ =>
            throw new AssertFailedException("No process should be started."));

        Assert.Throws<AboutActionLauncher.PackageFileMissingException>(launcher.OpenReadme);
    }

    [TestMethod]
    public void OpenLicense_ReportsWhenTheFileIsMissing()
    {
        AboutActionLauncher launcher = new(_temporaryDirectory, _ =>
            throw new AssertFailedException("No process should be started."));

        Assert.Throws<AboutActionLauncher.PackageFileMissingException>(launcher.OpenLicense);
    }

    [TestMethod]
    public void OpenLicense_ReportsAProcessStartFailure()
    {
        File.WriteAllText(Path.Combine(_temporaryDirectory, "LICENSE"), "test license");
        AboutActionLauncher launcher = new(_temporaryDirectory, _ => null);

        Assert.Throws<InvalidOperationException>(launcher.OpenLicense);
    }

    [TestMethod]
    public void OpenPackageDocument_RejectsDirectoryTargets()
    {
        string readmePath = Path.Combine(_temporaryDirectory, "README.html");
        Directory.CreateDirectory(readmePath);
        AboutActionLauncher directoryLauncher = new(_temporaryDirectory, _ =>
            throw new AssertFailedException("No process should be started."));
        Assert.Throws<IOException>(directoryLauncher.OpenReadme);
    }
}
