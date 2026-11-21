using System.Diagnostics;
using System.Security;

namespace DesktopGuard.App.Presentation;

internal sealed class AboutActionLauncher
{
    internal const string ReleasesUrl = "https://github.com/theataboy/Desktop-Guard/releases";

    private readonly string _applicationDirectory;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public AboutActionLauncher(
        string applicationDirectory,
        Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        _applicationDirectory = Path.GetFullPath(applicationDirectory);
        _startProcess = startProcess ?? StartProcess;
    }

    public void OpenReleases()
    {
        Start(new ProcessStartInfo(ReleasesUrl)
        {
            UseShellExecute = true,
        });
    }

    public void OpenLicense()
    {
        string licensePath = ResolvePackageFile("LICENSE");
        string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string notepadPath = Path.Combine(systemDirectory, "notepad.exe");
        ProcessStartInfo startInfo = new(notepadPath)
        {
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(licensePath);
        Start(startInfo);
    }

    public void OpenReadme()
    {
        string readmePath = ResolvePackageFile("README.html");
        Start(new ProcessStartInfo(readmePath)
        {
            UseShellExecute = true,
        });
    }

    private string ResolvePackageFile(string fileName)
    {
        string path = Path.GetFullPath(Path.Combine(_applicationDirectory, fileName));
        string applicationDirectoryPrefix = Path.EndsInDirectorySeparator(_applicationDirectory)
            ? _applicationDirectory
            : _applicationDirectory + Path.DirectorySeparatorChar;
        if (!path.StartsWith(applicationDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException("The requested About document is outside the application folder.");
        }

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException exception)
        {
            throw new PackageFileMissingException(fileName, path, exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new PackageFileMissingException(fileName, path, exception);
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new PackageFileMissingException(fileName, path);
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The About document is a reparse point and cannot be opened.");
        }

        return path;
    }

    private void Start(ProcessStartInfo startInfo)
    {
        Process? process = _startProcess(startInfo);
        if (process is null && !startInfo.UseShellExecute)
        {
            throw new InvalidOperationException("Windows did not start the requested About action.");
        }

        process?.Dispose();
    }

    private static Process? StartProcess(ProcessStartInfo startInfo) => Process.Start(startInfo);

    internal sealed class PackageFileMissingException : IOException
    {
        public PackageFileMissingException(string packageFileName, string packagePath, Exception? innerException = null)
            : base($"The packaged About file '{packageFileName}' is unavailable at '{packagePath}'.", innerException)
        {
            PackageFileName = packageFileName;
            PackagePath = packagePath;
        }

        public string PackageFileName { get; }

        public string PackagePath { get; }
    }
}
