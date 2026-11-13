using System.Runtime.InteropServices;

namespace DesktopGuard.App.DesktopScanning;

internal static class WindowsKnownFolderPath
{
    public static string Get(Guid folderId, IntPtr token = default)
    {
        int result = SHGetKnownFolderPath(ref folderId, 0, token, out IntPtr pathPointer);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        try
        {
            string? path = Marshal.PtrToStringUni(pathPointer);
            return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
                ? Path.GetFullPath(path)
                : throw new IOException("Windows returned an invalid known-folder path.");
        }
        finally
        {
            if (pathPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pathPointer);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        ref Guid folderId,
        uint flags,
        IntPtr token,
        out IntPtr path);
}
