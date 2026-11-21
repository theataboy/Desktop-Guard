using System.Diagnostics;
using System.Security;
using System.Text;

namespace DesktopGuard.App.Presentation;

internal sealed class AboutActionLauncher
{
    internal const string ReleasesUrl = "https://github.com/theataboy/Desktop-Guard/releases";
    private const int MaximumLicenseBytes = 64 * 1024;

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

    public string ReadLicenseText()
    {
        string licensePath = ResolvePackageFile("LICENSE");
        using FileStream stream = new(licensePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] buffer = new byte[MaximumLicenseBytes + 1];
        int totalBytesRead = 0;
        while (totalBytesRead < buffer.Length)
        {
            int bytesRead = stream.Read(buffer, totalBytesRead, buffer.Length - totalBytesRead);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytesRead += bytesRead;
        }

        if (totalBytesRead > MaximumLicenseBytes)
        {
            throw new IOException("The packaged license exceeds its size limit.");
        }

        string licenseText = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(buffer, 0, totalBytesRead);
        return licenseText.TrimStart('\uFEFF');
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
