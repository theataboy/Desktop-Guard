using System.Runtime.InteropServices;

namespace DesktopGuard.App.ShortcutManagement;

internal static class RecycleBinFileOperation
{
    private const uint FileOperationDelete = 3;
    private const ushort Silent = 0x0004;
    private const ushort NoConfirmation = 0x0010;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoConfirmDirectoryCreate = 0x0200;
    private const ushort NoErrorUi = 0x0400;
    private const int AccessDenied = 5;
    private const int AccessDeniedSource = 0x78;
    private const int OperationAborted = 995;

    public static void Delete(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A fully qualified file path is required.", nameof(path));
        }

        IntPtr sourcePath = Marshal.StringToHGlobalUni(path + "\0\0");
        try
        {
            ShellFileOperation operation = new()
            {
                Function = FileOperationDelete,
                SourcePath = sourcePath,
                Flags = Silent | NoConfirmation | AllowUndo | NoConfirmDirectoryCreate | NoErrorUi,
            };
            int result = SHFileOperationW(ref operation);
            if (result != 0)
            {
                throw CreateFailure(result);
            }

            if (operation.WasAborted != 0)
            {
                throw CreateFailure(OperationAborted);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(sourcePath);
        }
    }

    internal static IOException CreateFailure(int errorCode)
    {
        int win32Error = errorCode == AccessDeniedSource ? AccessDenied : errorCode;
        return new IOException(
            "The shortcut could not be moved to the Recycle Bin.",
            unchecked((int)(0x80070000u | (uint)win32Error)));
    }

    [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShellFileOperation operation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ShellFileOperation
    {
        public IntPtr WindowHandle;
        public uint Function;
        public IntPtr SourcePath;
        public IntPtr DestinationPath;
        public ushort Flags;
        public int WasAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }
}
