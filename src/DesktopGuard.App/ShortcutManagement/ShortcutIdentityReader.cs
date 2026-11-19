using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using DesktopGuard.Core.Shortcuts;

namespace DesktopGuard.App.ShortcutManagement;

internal sealed class ShortcutIdentityReader
{
    private const int MaximumLinkLength = 1024 * 1024;
    private const int MaximumInternetShortcutLength = 64 * 1024;
    private const int MaximumIdentityFieldLength = 8192;
    private const uint RawShellLinkPath = 0x0004;
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public ShortcutIdentityReadResult? TryRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            int maximumLength = string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase)
                ? MaximumLinkLength
                : MaximumInternetShortcutLength;
            byte[]? bytes = ShortcutFileTransfer.ReadBoundedContent(path, maximumLength, out DateTimeOffset lastWriteUtc);
            if (bytes is null)
            {
                return null;
            }

            byte[] identityHash = string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase)
                ? ReadShellLinkIdentity(bytes)
                : ReadInternetShortcutIdentity(bytes);
            return new ShortcutIdentityReadResult(identityHash, SHA256.HashData(bytes), bytes.Length, lastWriteUtc);
        }
        catch (Exception exception) when (IsExpectedIdentityFailure(exception))
        {
            return null;
        }
    }

    private static byte[] ReadShellLinkIdentity(byte[] bytes)
    {
        Type shellLinkType = Type.GetTypeFromCLSID(ShellLinkClassId, throwOnError: true)
            ?? throw new COMException("The Windows Shell Link parser is unavailable.");
        object shellLink = Activator.CreateInstance(shellLinkType)
            ?? throw new COMException("The Windows Shell Link parser could not be created.");
        IStream? stream = null;
        try
        {
            int createResult = CreateStreamOnHGlobal(IntPtr.Zero, deleteOnRelease: true, out stream);
            Marshal.ThrowExceptionForHR(createResult);
            stream.Write(bytes, bytes.Length, IntPtr.Zero);
            stream.Seek(0, 0, IntPtr.Zero);

            // Load only the shortcut stream and request its stored path. Never call IShellLink::Resolve.
            IPersistStreamNative persist = (IPersistStreamNative)shellLink;
            Marshal.ThrowExceptionForHR(persist.Load(stream));
            IShellLinkW link = (IShellLinkW)shellLink;

            StringBuilder target = new(MaximumIdentityFieldLength + 2);
            int pathResult = link.GetPath(target, target.Capacity, IntPtr.Zero, RawShellLinkPath);
            if (pathResult != 0 || target.Length == 0)
            {
                throw new InvalidDataException("The shortcut has no readable raw target path.");
            }

            StringBuilder arguments = new(MaximumIdentityFieldLength + 2);
            StringBuilder workingDirectory = new(MaximumIdentityFieldLength + 2);
            Marshal.ThrowExceptionForHR(link.GetArguments(arguments, arguments.Capacity));
            Marshal.ThrowExceptionForHR(link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity));
            if (target.Length > MaximumIdentityFieldLength
                || arguments.Length > MaximumIdentityFieldLength
                || workingDirectory.Length > MaximumIdentityFieldLength)
            {
                throw new InvalidDataException("The shortcut launch metadata exceeds the supported length.");
            }

            return ShortcutIdentityFingerprint.Create(
                ".lnk",
                target.ToString(),
                arguments.ToString(),
                workingDirectory.ToString());
        }
        finally
        {
            if (stream is not null && Marshal.IsComObject(stream))
            {
                Marshal.ReleaseComObject(stream);
            }

            if (Marshal.IsComObject(shellLink))
            {
                Marshal.ReleaseComObject(shellLink);
            }
        }
    }

    private static byte[] ReadInternetShortcutIdentity(byte[] bytes)
    {
        string text = DecodeInternetShortcut(bytes);
        bool inInternetShortcutSection = false;
        string? urlValue = null;
        foreach (string rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                inInternetShortcutSection = string.Equals(
                    line[1..^1].Trim(),
                    "InternetShortcut",
                    StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inInternetShortcutSection)
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0
                || !string.Equals(line[..separator].Trim(), "URL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (urlValue is not null)
            {
                throw new InvalidDataException("The Internet shortcut contains more than one URL value.");
            }

            urlValue = line[(separator + 1)..].Trim();
        }

        if (string.IsNullOrWhiteSpace(urlValue)
            || urlValue.Length > MaximumIdentityFieldLength
            || !Uri.TryCreate(urlValue, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidDataException("The Internet shortcut does not contain a valid absolute URL.");
        }

        string canonicalUrl = uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
        return ShortcutIdentityFingerprint.Create(".url", canonicalUrl);
    }

    private static string DecodeInternetShortcut(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            return StrictUtf8.GetString(bytes, Encoding.UTF8.Preamble.Length, bytes.Length - Encoding.UTF8.Preamble.Length);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true)
                .GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true)
                .GetString(bytes, 2, bytes.Length - 2);
        }

        return StrictUtf8.GetString(bytes);
    }

    private static bool IsExpectedIdentityFailure(Exception exception)
    {
        return exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or COMException
            or InvalidCastException
            or DecoderFallbackException
            or SecurityException;
    }

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CreateStreamOnHGlobal(
        IntPtr hGlobal,
        [MarshalAs(UnmanagedType.Bool)] bool deleteOnRelease,
        out IStream stream);

    [ComImport]
    [Guid("00000109-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistStreamNative
    {
        [PreserveSig]
        int GetClassID(out Guid classId);

        [PreserveSig]
        int IsDirty();

        [PreserveSig]
        int Load(IStream stream);

        [PreserveSig]
        int Save(IStream stream, [MarshalAs(UnmanagedType.Bool)] bool clearDirty);

        [PreserveSig]
        int GetSizeMax(out long size);

        [PreserveSig]
        int InitNew();
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig]
        int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int characterCount, IntPtr findData, uint flags);

        [PreserveSig]
        int GetIDList(out IntPtr itemIdList);

        [PreserveSig]
        int SetIDList(IntPtr itemIdList);

        [PreserveSig]
        int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int characterCount);

        [PreserveSig]
        int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);

        [PreserveSig]
        int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int characterCount);

        [PreserveSig]
        int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        [PreserveSig]
        int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int characterCount);

        [PreserveSig]
        int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        [PreserveSig]
        int GetHotkey(out short hotkey);

        [PreserveSig]
        int SetHotkey(short hotkey);

        [PreserveSig]
        int GetShowCmd(out int showCommand);

        [PreserveSig]
        int SetShowCmd(int showCommand);

        [PreserveSig]
        int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int characterCount, out int iconIndex);

        [PreserveSig]
        int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

        [PreserveSig]
        int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

        [PreserveSig]
        int Resolve(IntPtr windowHandle, uint flags);

        [PreserveSig]
        int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
