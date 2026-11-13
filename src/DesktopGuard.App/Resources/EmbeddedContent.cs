using System.Reflection;
using System.Resources;
using System.Text;

namespace DesktopGuard.App.Resources;

internal static class EmbeddedContent
{
    private const string PayloadName = "DesktopGuard.App.Data.8d2f";

    public static string Read()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadName)
            ?? throw new MissingManifestResourceException("The embedded content could not be read.");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        if (bytes.Length < 3 || bytes[0] != 0 || bytes[1] != 0xA5 || bytes[2] != 0)
        {
            throw new MissingManifestResourceException("The embedded content could not be read.");
        }

        for (int index = 3; index < bytes.Length; index++)
        {
            bytes[index] ^= unchecked((byte)(0x41 + (index - 3) * 23));
        }

        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }
}
