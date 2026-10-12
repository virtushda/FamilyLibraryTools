using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using FamilyLibraryTools.Core;
using Microsoft.Win32.SafeHandles;

static partial class Program
{
    static async Task TraversableAncestor()
    {
        var folders = Folders();
        string first = Write(folders.Input, "a\\photo.jpg", "keep this image");
        Write(folders.Input, "b\\photo.jpg", "quarantine this image");
        var parent = new DirectoryInfo(Path.GetDirectoryName(folders.Input)!);
        Check(PathSafety.IsWithin(parent.FullName, scratch) && parent.FullName != scratch, "ACL fixture escaped its generated folder.");
        byte[] original = parent.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
        var restricted = new DirectorySecurity();
        restricted.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
        using var identity = WindowsIdentity.GetCurrent();
        restricted.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
        try
        {
            parent.SetAccessControl(restricted);
            await Throws<UnauthorizedAccessException>(() => { parent.GetFileSystemInfos(); return Task.CompletedTask; });
            Check(File.ReadAllText(first) == "keep this image" && Directory.GetFiles(folders.Input, "*.jpg", SearchOption.AllDirectories).Length == 2,
                "Fixture should permit reading and listing the library itself.");
            using (var held = NativeFiles.OpenDirectory(folders.Input))
            {
                string renamed = folders.Input + "-renamed";
                Check(PathSafety.IsWithin(Path.GetFullPath(folders.Input), scratch) && PathSafety.IsWithin(Path.GetFullPath(renamed), scratch), "Rename fixture escaped scratch.");
                await Throws<IOException>(() => { Directory.Move(folders.Input, renamed); return Task.CompletedTask; });
            }

            ScanResult scan = await Scan(folders);
            Check(scan.Files.Count == 2 && scan.Issues.Count == 0, "Traversable ancestor prevented a complete scan.");
            Check((await Organize(scan)).Moved == 2, "Traversable ancestor prevented verified organization.");
            LibraryScan library = await new LibraryScanner((_, _) => knownDate).ScanLibraryAsync(folders.Output, quiet, default);
            var groups = CollisionGroups.Build(library.Files, true);
            Check(library.Issues.Count == 0 && groups.Count == 1 && groups[0].Count == 2, "Traversable ancestor prevented duplicate review.");
            using var journal = new TransferJournal(folders.Output);
            var clock = new ManualClock();
            using var queue = new DeletionQueue(groups, new VerifiedQuarantine(folders.Output, journal).QuarantineAsync, clock);
            PendingDeletion choice = queue.Enqueue(groups[0][0], groups[0][1]);
            clock.Advance(60);
            await queue.ProcessDueAsync();
            Check(choice.State == DeletionState.Quarantined && File.ReadAllText(choice.Keep.Path) == "keep this image" &&
                File.ReadAllText(choice.Destination) == "quarantine this image", "Traversable ancestor prevented safe quarantine: " + choice.Error);
        }
        finally
        {
            // A fresh descriptor marks the original DACL for restoration; a read-only snapshot does not.
            var restore = new DirectorySecurity();
            restore.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            parent.SetAccessControl(restore);
            Check(parent.GetFileSystemInfos().Length > 0, "Fixture listing permission was not restored.");
        }
    }

    static async Task RootAliases()
    {
        var folders = Folders();
        string nested = Path.Combine(folders.Input, "nested");
        Directory.CreateDirectory(nested);
        foreach (var pair in new[]
        {
            (folders.Input, @"\\?\" + folders.Input), (@"\\?\" + folders.Input, folders.Input),
            (folders.Input, @"\\?\" + nested), (@"\\?\" + nested, folders.Input),
            (folders.Input, folders.Input.ToUpperInvariant() + "\\"), (folders.Input, Path.Combine(nested, ".."))
        })
            await Throws<ArgumentException>(() => { PathSafety.ValidateRoots(pair.Item1, pair.Item2); return Task.CompletedTask; });

        var shortName = new StringBuilder(32768);
        uint length = GetShortPathNameW(nested, shortName, (uint)shortName.Capacity);
        Check(length > 0 && length < shortName.Capacity, "Cannot inspect short-path alias support.");
        await Throws<ArgumentException>(() => { PathSafety.ValidateRoots(folders.Input, shortName.ToString()); return Task.CompletedTask; });
        Console.WriteLine(shortName.ToString().Equals(nested, StringComparison.OrdinalIgnoreCase)
            ? "INFO: 8.3 alias not supplied by this filesystem; extended/case/dot aliases exercised."
            : "INFO: 8.3 nested-root alias also rejected.");

        // Disjoint aliases must also yield paths consistent with scanner/mover containment checks.
        string photo = Write(folders.Input, "photo.jpg", "only the selected input");
        ScanResult scan = await new LibraryScanner((_, _) => knownDate).ScanAsync(@"\\?\" + folders.Input, @"\\?\" + folders.Output, quiet, default);
        Check(scan.InputRoot == folders.Input && scan.OutputRoot == folders.Output, "Root aliases were not canonicalized.");
        RunResult result = await Organize(scan);
        Check(result.Moved == 1 && !File.Exists(photo) && File.ReadAllText(Path.Combine(folders.Output, "2026", "4", "photo.jpg")) == "only the selected input", "Canonicalized roots cannot organize safely.");
    }

    static async Task DirectoryRenameBlocked()
    {
        var folders = Folders();
        string child = Path.Combine(folders.Output, "child");
        Directory.CreateDirectory(child);
        using var held = NativeFiles.OpenDirectory(child);
        await Throws<IOException>(() => { Directory.Move(child, child + "-replaced"); return Task.CompletedTask; });
        await Throws<IOException>(() => { Directory.Move(folders.Output, folders.Output + "-replaced"); return Task.CompletedTask; });
        Check(Directory.Exists(child), "Held directory or its ancestor was renamed.");
    }

    static async Task RedirectedDirectoryHandles()
    {
        for (int scenario = 0; scenario < 3; scenario++)
        {
            var folders = Folders();
            string sourcePath = Write(folders.Input, "photo.jpg", "selected photo");
            string outside = Path.Combine(Path.GetDirectoryName(folders.Input)!, "outside");
            Directory.CreateDirectory(outside);
            using var held = NativeFiles.OpenDirectory(folders.Output);
            // Attribute-only access can still change reparse data despite directory sharing locks.
            SetJunctionOnExistingDirectory(folders.Output, outside);
            if (scenario == 0)
            {
                using var source = NativeFiles.OpenForMove(sourcePath);
                await Throws<IOException>(() => { NativeFiles.TryRenameWithinVolume(source, held, "photo.jpg"); return Task.CompletedTask; });
            }
            else if (scenario == 1)
                await Throws<IOException>(() => { using var created = NativeFiles.CreateNewFile(held, "stage.partial", FileAccess.ReadWrite, FileShare.None, true); return Task.CompletedTask; });
            else
                await Throws<IOException>(() => { using var created = NativeFiles.OpenDirectory(Path.Combine(folders.Output, "year", "month"), true); return Task.CompletedTask; });
            Check(File.ReadAllText(sourcePath) == "selected photo" && Directory.GetFileSystemEntries(outside).Length == 0, "Changed directory redirected a mutation outside the selected tree.");
        }
    }

    static async Task RetainedHandleCannotBeReplaced()
    {
        var folders = Folders();
        string sourcePath = Write(folders.Input, "photo.jpg", "selected photo");
        string destination = Path.Combine(folders.Output, "photo.jpg");
        using var directory = NativeFiles.OpenDirectory(folders.Output);
        using (var source = NativeFiles.OpenForMove(sourcePath))
        {
            FileIdentity identity = NativeFiles.Identity(source);
            Check(NativeFiles.TryRenameWithinVolume(source, directory, "photo.jpg"), "Quarantine-style move failed.");
            await Throws<IOException>(() => { File.Move(destination, destination + ".displaced"); return Task.CompletedTask; });
            await Throws<IOException>(() => { File.WriteAllText(destination, "unselected replacement"); return Task.CompletedTask; });
            File.WriteAllText(sourcePath, "replacement at original name");
            Check(NativeFiles.Identity(source) == identity, "Selected identity changed during quarantine.");
        }
        Check(File.ReadAllText(destination) == "selected photo" && File.ReadAllText(sourcePath) == "replacement at original name", "Quarantine handoff operated on a replacement.");
    }

    static async Task RedirectedMoverDestination()
    {
        var folders = Folders();
        string source = Write(folders.Input, "video.mp4", "selected video bytes");
        ScanResult scan = await Scan(folders);
        string month = Path.Combine(folders.Output, "2026", "4", "Videos");
        string outside = Path.Combine(Path.GetDirectoryName(folders.Input)!, "outside");
        Directory.CreateDirectory(outside);
        bool mutated = false;
        using var journal = new TransferJournal(folders.Output);
        var mover = new VerifiedMover(folders.Input, folders.Output, journal)
        {
            SourceVerified = () =>
            {
                SetJunctionOnExistingDirectory(month, outside);
                mutated = true;
            }
        };
        try
        {
            await mover.MoveAsync(scan.Files[0], knownDate.Value!.Value, false, default);
            throw new InvalidOperationException("Move through a redirected destination unexpectedly succeeded.");
        }
        catch (IOException error)
        {
            Check(mutated && error.InnerException is Win32Exception { NativeErrorCode: 1921 },
                "Expected the redirected-directory rename failure (1921), not a sharing violation or another earlier error: " + error.Message);
        }
        Check(mutated && File.ReadAllText(source) == "selected video bytes" && Directory.GetFileSystemEntries(outside).Length == 0,
            "Mover followed a junction introduced after verification.");
    }

    static void SetJunctionOnExistingDirectory(string folder, string target)
    {
        Check(PathSafety.IsWithin(Path.GetFullPath(folder), scratch) && PathSafety.IsWithin(Path.GetFullPath(target), scratch), "Junction fixture escaped scratch root.");
        using var writer = OpenDirectoryForAttributes(folder, 0x100, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        Check(!writer.IsInvalid, "Cannot open generated directory for reparse conversion.");
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + target), print = Encoding.Unicode.GetBytes(target);
        byte[] data = new byte[20 + substitute.Length + print.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0xA0000003);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), (ushort)(data.Length - 8));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), (ushort)(substitute.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), (ushort)print.Length);
        substitute.CopyTo(data, 16);
        print.CopyTo(data, 18 + substitute.Length);
        Check(SetReparseData(writer, 0x900A4, data, data.Length, IntPtr.Zero, 0, out _, IntPtr.Zero), "Cannot reproduce directory redirection: " + Marshal.GetLastWin32Error());
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle OpenDirectoryForAttributes(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetReparseData(SafeFileHandle handle, uint control, byte[] input, int inputSize, IntPtr output, int outputSize, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetShortPathNameW(string path, StringBuilder result, uint size);
}
