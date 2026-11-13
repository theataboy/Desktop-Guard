using System.IO;

namespace DesktopGuard.Core.DesktopScanning;

public static class DesktopCandidatePolicy
{
    private const string InvalidWindowsFileNameCharacters = "<>:\"/\\|?*";
    private static readonly HashSet<string> ReservedDeviceNames = CreateReservedDeviceNames();

    public static bool IsValidWindowsFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Length > 255
            || fileName is "." or ".."
            || fileName.Any(character => char.IsControl(character)
                || InvalidWindowsFileNameCharacters.Contains(character))
            || fileName[^1] is ' ' or '.')
        {
            return false;
        }

        int firstPeriod = fileName.IndexOf('.');
        string deviceName = (firstPeriod < 0 ? fileName : fileName[..firstPeriod]).TrimEnd(' ', '.');
        return !ReservedDeviceNames.Contains(deviceName);
    }

    public static bool IsDirectChildOf(string rootPath, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)
            || string.IsNullOrWhiteSpace(candidatePath)
            || !Path.IsPathFullyQualified(rootPath)
            || !Path.IsPathFullyQualified(candidatePath))
        {
            return false;
        }

        try
        {
            string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            string canonicalCandidate = Path.GetFullPath(candidatePath);
            string? candidateParent = Path.GetDirectoryName(canonicalCandidate);

            return candidateParent is not null
                && string.Equals(
                    Path.TrimEndingDirectorySeparator(candidateParent),
                    canonicalRoot,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static bool IsSafeDirectFile(string rootPath, string candidatePath, FileAttributes attributes)
    {
        const FileAttributes unsafeAttributes = FileAttributes.Directory | FileAttributes.ReparsePoint;

        return (attributes & unsafeAttributes) == 0
            && IsValidWindowsFileName(Path.GetFileName(candidatePath))
            && IsDirectChildOf(rootPath, candidatePath);
    }

    private static HashSet<string> CreateReservedDeviceNames()
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "CONIN$",
            "CONOUT$",
        };

        for (int number = 1; number <= 9; number++)
        {
            names.Add($"COM{number}");
            names.Add($"LPT{number}");
        }

        foreach (char superscriptDigit in new[] { '¹', '²', '³' })
        {
            names.Add($"COM{superscriptDigit}");
            names.Add($"LPT{superscriptDigit}");
        }

        return names;
    }
}
