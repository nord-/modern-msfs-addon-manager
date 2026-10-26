using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AddonManager.Infrastructure;

internal static class NativeMethods
{
    public const uint JunctionTag = 0xA0000003;   // IO_REPARSE_TAG_MOUNT_POINT
    public const uint SymlinkTag = 0xA000000C;    // IO_REPARSE_TAG_SYMLINK

    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint FsctlGetReparsePoint = 0x000900A8;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareAll = 0x7;        // read | write | delete
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const int ErrorNotAReparsePoint = 4390;
    private const int MaxReparseDataSize = 16 * 1024;

    /// <summary>Creates a directory; fails when anything already holds the name.</summary>
    public static void CreateDirectory(string path)
    {
        if (!CreateDirectoryW(LongPath(path), IntPtr.Zero))
            throw new IOException($"Could not create '{path}'.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    /// <summary>Turns an empty directory into a junction to <paramref name="targetPath"/>.</summary>
    public static bool SetJunction(string linkPath, string targetPath)
    {
        using var handle = Open(linkPath, GenericWrite);
        if (handle.IsInvalid)
            return false;

        var full = Path.GetFullPath(targetPath).TrimEnd('\\');
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + full + @"\");
        var print = Encoding.Unicode.GetBytes(full);

        // REPARSE_DATA_BUFFER with a MountPointReparseBuffer; both names null-terminated.
        var pathBufferLength = substitute.Length + 2 + print.Length + 2;
        var dataLength = 8 + pathBufferLength;
        var buffer = new byte[8 + dataLength];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, JunctionTag);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)dataLength);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], (ushort)(substitute.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], (ushort)print.Length);
        substitute.CopyTo(span[16..]);
        print.CopyTo(span[(16 + substitute.Length + 2)..]);

        return DeviceIoControl(handle, FsctlSetReparsePoint, buffer, buffer.Length, null, 0, out _, IntPtr.Zero);
    }

    /// <summary>The reparse tag of <paramref name="path"/>, or <c>null</c> when it isn't a reparse point.</summary>
    public static uint? GetReparseTag(string path)
    {
        using var handle = Open(path, 0);
        if (handle.IsInvalid)
            return null;

        var buffer = new byte[MaxReparseDataSize];
        if (DeviceIoControl(handle, FsctlGetReparsePoint, null, 0, buffer, buffer.Length, out _, IntPtr.Zero))
            return BinaryPrimitives.ReadUInt32LittleEndian(buffer);

        var error = Marshal.GetLastWin32Error();
        if (error == ErrorNotAReparsePoint)
            return null;
        throw new IOException($"Could not read reparse point of '{path}'.", new Win32Exception(error));
    }

    private static SafeFileHandle Open(string path, uint access)
        => CreateFileW(LongPath(path), access, FileShareAll, IntPtr.Zero, OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);

    private static string LongPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal))
            return full;
        return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, byte[]? inBuffer, int inBufferSize,
        byte[]? outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);
}
