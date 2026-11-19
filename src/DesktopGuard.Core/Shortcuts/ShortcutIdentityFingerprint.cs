using System.Security.Cryptography;
using System.Text;

namespace DesktopGuard.Core.Shortcuts;

public static class ShortcutIdentityFingerprint
{
    private const int CurrentVersion = 1;

    public static byte[] Create(
        string kind,
        string target,
        string arguments = "",
        string workingDirectory = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        if (!string.Equals(kind, ".lnk", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(kind, ".url", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        using MemoryStream buffer = new();
        using (BinaryWriter writer = new(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(CurrentVersion);
            writer.Write(kind.ToLowerInvariant());
            writer.Write(string.Equals(kind, ".url", StringComparison.OrdinalIgnoreCase)
                ? target
                : NormalizeWindowsPath(target));
            writer.Write(arguments);
            writer.Write(NormalizeWindowsPath(workingDirectory));
        }

        return SHA256.HashData(buffer.ToArray());
    }

    private static string NormalizeWindowsPath(string value)
    {
        return value.ToUpperInvariant();
    }
}
