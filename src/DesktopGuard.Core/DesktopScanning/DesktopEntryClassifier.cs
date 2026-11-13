using System.IO;

namespace DesktopGuard.Core.DesktopScanning;

public static class DesktopEntryClassifier
{
    public static DesktopEntryKind Classify(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string extension = Path.GetExtension(fileName);
        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
            ? DesktopEntryKind.Shortcut
            : DesktopEntryKind.OtherFile;
    }
}
