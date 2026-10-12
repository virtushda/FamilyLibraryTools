using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FamilyLibraryTools.Core;

// All mutations use held file/directory handles, never a second absolute-path lookup.
static class NativeFiles
{
    const uint Read = 0x80000000, Write = 0x40000000, Delete = 0x10000;
    const uint DirectoryAccess = 0x1000A0; // SYNCHRONIZE | READ_ATTRIBUTES | TRAVERSE
    const uint OpenReparsePoint = 0x200000, Synchronous = 0x20;

    public static FileIdentity Identity(FileStream stream) => Identity(stream.SafeFileHandle);

    static FileIdentity Identity(SafeFileHandle handle)
    {
        FileInformation info = Inspect(handle, "opened entry");
        return new(info.VolumeSerial, ((ulong)info.IndexHigh << 32) | info.IndexLow);
    }

    public static FileStream OpenRead(string path) => OpenFile(path, false);
    public static FileStream OpenForMove(string path) => OpenFile(path, true);

    static FileStream OpenFile(string path, bool forMove)
    {
        using var directory = OpenDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return OpenFile(directory, Path.GetFileName(path), FileAccess.Read, FileShare.Read, 1, forMove);
    }

    public static FileStream CreateNewFile(DirectoryHandle directory, string name, FileAccess access, FileShare sharing, bool forMove = false) =>
        OpenFile(directory, name, access, sharing, 2, forMove);

    public static FileStream OpenSessionLock(DirectoryHandle directory) =>
        OpenFile(directory, "session.lock", FileAccess.ReadWrite, FileShare.None, 3, false);

    static FileStream OpenFile(DirectoryHandle directory, string name, FileAccess access, FileShare sharing, uint disposition, bool forMove)
    {
        uint rights = ((access & FileAccess.Read) != 0 ? Read : 0) | ((access & FileAccess.Write) != 0 ? Write : 0);
        SafeFileHandle handle = OpenRelative(directory.Handle, name, rights | 0x80 | (forMove ? Delete : 0),
            (uint)sharing, disposition, 0x40 | Synchronous | OpenReparsePoint); // NON_DIRECTORY_FILE
        try
        {
            Inspect(handle, name);
            return new FileStream(handle, access, 128 * 1024, false);
        }
        catch { handle.Dispose(); throw; }
    }

    public static DirectoryHandle OpenDirectory(string path, bool create = false)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string root = Path.GetPathRoot(fullPath) ?? throw new IOException("A rooted folder path is required.");
        var directory = new DirectoryHandle();
        try
        {
            // TRAVERSE participates in sharing checks without requiring ancestor listing permission.
            SafeFileHandle handle = CreateFileW(Extended(root), DirectoryAccess, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw Error("Cannot open folder", root, Marshal.GetLastWin32Error()); }
            directory.Add(handle);
            Inspect(handle, root);
            foreach (string name in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                handle = OpenRelative(directory.Handle, name, DirectoryAccess, 3, create ? 3u : 1u,
                    1 | Synchronous | OpenReparsePoint); // DIRECTORY_FILE; OPEN_IF or OPEN, never overwrite
                directory.Add(handle);
                Inspect(handle, fullPath);
            }
            return directory;
        }
        catch { directory.Dispose(); throw; }
    }

    public static IDisposable LockDirectories(params string[] directories)
    {
        var locks = new DirectoryLocks();
        try
        {
            foreach (string path in directories) locks.Add(OpenDirectory(path));
            return locks;
        }
        catch { locks.Dispose(); throw; }
    }

    public static bool TryRenameWithinVolume(FileStream source, string destination)
    {
        using var directory = OpenDirectory(Path.GetDirectoryName(destination)!);
        return TryRenameWithinVolume(source, directory, Path.GetFileName(destination));
    }

    // A simple relative name cannot traverse or re-resolve an ancestor. No replacement flag.
    public static bool TryRenameWithinVolume(FileStream source, DirectoryHandle directory, string name)
    {
        ValidateName(name);
        int nameLength = Encoding.Unicode.GetByteCount(name);
        int nameOffset = IntPtr.Size == 8 ? 20 : 12;
        byte[] info = new byte[nameOffset + nameLength + IntPtr.Size];
        if (IntPtr.Size == 8) BinaryPrimitives.WriteInt64LittleEndian(info.AsSpan(8), directory.Handle.DangerousGetHandle().ToInt64());
        else BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(4), directory.Handle.DangerousGetHandle().ToInt32());
        BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(nameOffset - 4), nameLength);
        Encoding.Unicode.GetBytes(name, info.AsSpan(nameOffset, nameLength));
        int status = NtSetInformationFile(source.SafeFileHandle, out _, info, (uint)info.Length, 10); // FileRenameInformation
        GC.KeepAlive(directory);
        if (status >= 0) return true;
        int error = (int)RtlNtStatusToDosError(status);
        if (error == 17) return false; // ERROR_NOT_SAME_DEVICE
        throw Error("Cannot move file; original retained", name, error);
    }

    static SafeFileHandle OpenRelative(SafeFileHandle parent, string name, uint access, uint sharing, uint disposition, uint options)
    {
        ValidateName(name);
        var unicode = new UnicodeString { Length = checked((ushort)Encoding.Unicode.GetByteCount(name)), Buffer = Marshal.StringToHGlobalUni(name) };
        unicode.MaximumLength = unicode.Length;
        IntPtr objectName = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        try
        {
            Marshal.StructureToPtr(unicode, objectName, false);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(), RootDirectory = parent.DangerousGetHandle(), ObjectName = objectName,
                Attributes = 0x1040 // OBJ_DONT_REPARSE | OBJ_CASE_INSENSITIVE
            };
            int status = NtCreateFile(out SafeFileHandle handle, access | 0x100000, ref attributes, out _, IntPtr.Zero,
                0, sharing, disposition, options, IntPtr.Zero, 0);
            GC.KeepAlive(parent);
            if (status >= 0) return handle;
            handle.Dispose();
            throw Error("Cannot safely open or create entry", name, (int)RtlNtStatusToDosError(status));
        }
        finally { Marshal.FreeHGlobal(objectName); Marshal.FreeHGlobal(unicode.Buffer); }
    }

    static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException($"Ambiguous or non-relative filename refused: {name}");
    }

    static FileInformation Inspect(SafeFileHandle handle, string path)
    {
        if (!GetFileInformationByHandle(handle, out FileInformation info)) throw Error("Cannot inspect entry", path, Marshal.GetLastWin32Error());
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0) throw new IOException($"Links, junctions, and cloud placeholders are not followed: {path}");
        return info;
    }

    static string Extended(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;

    static IOException Error(string action, string path, int code)
    {
        var error = new Win32Exception(code);
        return new IOException($"{action}: {path}. {error.Message}", error);
    }

    internal sealed class DirectoryHandle : IDisposable
    {
        readonly List<SafeFileHandle> handles = [];
        internal SafeFileHandle Handle => handles[^1];
        internal void Add(SafeFileHandle handle) => handles.Add(handle);

        public bool IsWithin(DirectoryHandle root)
        {
            FileIdentity rootIdentity = Identity(root.Handle);
            foreach (SafeFileHandle handle in handles)
                if (Identity(handle) == rootIdentity) return true;
            return false;
        }

        // NT volume names also collapse drive-letter aliases; DOS names are used in the UI.
        public string FinalPath(bool physical = false)
        {
            var buffer = new StringBuilder(512);
            uint length = GetFinalPathNameByHandleW(Handle, buffer, (uint)buffer.Capacity, physical ? 2u : 0u);
            if (length >= buffer.Capacity)
            {
                buffer.EnsureCapacity(checked((int)length + 1));
                length = GetFinalPathNameByHandleW(Handle, buffer, (uint)buffer.Capacity, physical ? 2u : 0u);
            }
            if (length == 0 || length >= buffer.Capacity) throw Error("Cannot resolve folder identity", "", Marshal.GetLastWin32Error());
            string path = buffer.ToString();
            if (!physical && path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path[8..];
            else if (!physical && path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length > 6 && path[5] == ':') path = path[4..];
            return Path.TrimEndingDirectorySeparator(path);
        }

        public void Dispose() { for (int index = handles.Count - 1; index >= 0; index--) handles[index].Dispose(); }
    }

    sealed class DirectoryLocks : IDisposable
    {
        readonly List<DirectoryHandle> directories = [];
        public void Add(DirectoryHandle directory) => directories.Add(directory);
        public void Dispose() { for (int index = directories.Count - 1; index >= 0; index--) directories[index].Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    struct ObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory, ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor, SecurityQualityOfService;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct IoStatus { public IntPtr Status; public UIntPtr Information; }
    [StructLayout(LayoutKind.Sequential)]
    struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
    [DllImport("ntdll.dll")]
    static extern int NtCreateFile(out SafeFileHandle handle, uint access, ref ObjectAttributes attributes, out IoStatus result,
        IntPtr allocationSize, uint fileAttributes, uint sharing, uint disposition, uint options, IntPtr eaBuffer, uint eaLength);
    [DllImport("ntdll.dll")]
    static extern int NtSetInformationFile(SafeFileHandle file, out IoStatus result, byte[] information, uint size, int informationClass);
    [DllImport("ntdll.dll")]
    static extern uint RtlNtStatusToDosError(int status);
}
