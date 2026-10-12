using System.IO;
using FamilyLibraryTools.Core;

static partial class Program
{
    // Exercise the real cross-volume transfer body on our one permitted test volume.
    // No fake filesystem or configurable production behavior is needed.
    static async Task<MoveResult> CopyAndRetain(ScanResult scan, CancellationToken token = default)
    {
        MediaFile file = scan.Files[0];
        string folder = Path.Combine(scan.OutputRoot, "2026", "4");
        if (file.Kind == MediaKind.Video) folder = Path.Combine(folder, "Videos");
        Directory.CreateDirectory(folder);
        using var journal = new TransferJournal(scan.OutputRoot);
        var mover = new VerifiedMover(scan.InputRoot, scan.OutputRoot, journal);
        using var folders = NativeFiles.LockDirectories(Path.GetDirectoryName(file.Path)!, folder);
        using var source = NativeFiles.OpenForMove(file.Path);
        await FileContents.ValidateAsync(source, file, token);
        return await mover.CopyAndRetainAsync(file, source, Path.Combine(folder, file.Name), token);
    }

    static async Task LateStreamRename()
    {
        var folders = Folders();
        string path = Write(folders.Input, "photo-\u00e9-\u7167\u7247.jpg", "original image bytes");
        ScanResult scan = await Scan(folders);
        string destination = Path.Combine(folders.Output, Path.GetFileName(path));
        using (var source = NativeFiles.OpenForMove(path))
        {
            await FileContents.ValidateAsync(source, scan.Files[0], default);
            using (var extra = new FileStream(path + ":late", FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                extra.Write("late metadata"u8);
                extra.Flush(true);
            }
            Check(NativeFiles.TryRenameWithinVolume(source, destination), "Same-volume rename failed.");
            await FileContents.ValidateAsync(source, scan.Files[0], default);
            bool writeBlocked = false;
            try { using var edit = File.Open(destination, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }
            catch (IOException) { writeBlocked = true; }
            Check(writeBlocked, "Renamed file lost its write protection before commit completed.");
        }
        Check(!File.Exists(path) && File.ReadAllText(destination) == "original image bytes" &&
            File.ReadAllText(destination + ":late") == "late metadata", "Late stream did not travel with the original.");
    }

    static async Task NativeRenameCollision()
    {
        var folders = Folders();
        string sourcePath = Write(folders.Input, "image.jpg", "original");
        string destination = Write(folders.Output, "image.jpg", "existing output");
        using (var source = NativeFiles.OpenForMove(sourcePath))
            await Throws<IOException>(() => { NativeFiles.TryRenameWithinVolume(source, destination); return Task.CompletedTask; });
        Check(File.ReadAllText(sourcePath) == "original" && File.ReadAllText(destination) == "existing output", "Native rename overwrote a file.");
    }

    static async Task LateStreamRecovery()
    {
        var folders = Folders();
        var data = await LargeSource(folders);
        File.WriteAllText(data.Source + ":before", "existing metadata");
        var injected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(folders.Output, "*.partial") { IncludeSubdirectories = true };
        watcher.Created += (_, _) =>
        {
            try
            {
                using var stream = new FileStream(data.Source + ":late", FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Write("metadata added during copy"u8);
                stream.Flush(true);
                injected.TrySetResult(true);
            }
            catch (Exception error) { injected.TrySetException(error); }
        };
        watcher.EnableRaisingEvents = true;
        MoveResult result = await CopyAndRetain(data.Scan);
        Check(await injected.Task.WaitAsync(TimeSpan.FromSeconds(5)), "Stream injection did not run.");
        string retained = result.RetainedOriginal!;
        Check(!File.Exists(data.Source) && File.Exists(retained), "Original was not retained for recovery.");
        Check(File.ReadAllText(retained + ":before") == "existing metadata" &&
            File.ReadAllText(retained + ":late") == "metadata added during copy", "Recovery lost filesystem streams.");
        using (var output = FileContents.OpenRead(result.Destination))
        using (var original = FileContents.OpenRead(retained))
            Check(await FileContents.EqualStreamsAsync(original, output, default), "Verified output differs from the retained original.");
        ScanResult rescanned = await Scan(folders);
        Check(rescanned.Files.Count == 1 && rescanned.Files[0].InOutput, "Rescan included recovery originals.");
        string log = Directory.GetFiles(Path.Combine(folders.Output, ".family-library-tools"), "*.jsonl")[0];
        string journal = File.ReadAllText(log);
        Check(journal.Contains("RecoveryPlanned") && journal.Contains("OriginalRetained"), "Recovery paths were not journaled.");
        Check((await Organize(rescanned)).Moved == 0 && File.ReadAllText(retained + ":late") == "metadata added during copy",
            "Restart changed a retained original.");
    }

    static async Task RecoveryCollision()
    {
        var folders = Folders();
        var data = await LargeSource(folders);
        string? blocked = null;
        var injected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(folders.Output, "*.partial") { IncludeSubdirectories = true };
        watcher.Created += (_, _) =>
        {
            try
            {
                string run = Directory.GetDirectories(Path.Combine(folders.Input, ".family-library-tools", "recovery"))[0];
                blocked = Write(run, "large.mp4", "existing recovery file");
                injected.TrySetResult(true);
            }
            catch (Exception error) { injected.TrySetException(error); }
        };
        watcher.EnableRaisingEvents = true;
        await Throws<IOException>(() => CopyAndRetain(data.Scan));
        await injected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        string destination = Path.Combine(folders.Output, "2026", "4", "Videos", "large.mp4");
        Check(File.Exists(data.Source) && File.Exists(destination) && File.ReadAllText(blocked!) == "existing recovery file", "Recovery failure lost or replaced a file.");
        using (var output = FileContents.OpenRead(destination)) await FileContents.ValidateAsync(output, data.Scan.Files[0], default);
        Check((await Organize(await Scan(folders))).Moved == 1 && !File.Exists(data.Source) &&
            File.Exists(Path.Combine(folders.Output, "2026", "4", "Videos", "large_DUPLICATE-1.mp4")), "Restart did not organize the interrupted original as an additional copy.");
    }

    static async Task CancelledAfterCopy()
    {
        var folders = Folders();
        var data = await LargeSource(folders);
        using var cancellation = new CancellationTokenSource();
        using var watcher = new FileSystemWatcher(folders.Output) { IncludeSubdirectories = true };
        watcher.Renamed += (_, change) => { if (Path.GetFileName(change.FullPath) == "large.mp4") cancellation.Cancel(); };
        watcher.EnableRaisingEvents = true;
        await Throws<OperationCanceledException>(() => CopyAndRetain(data.Scan, cancellation.Token));
        Check(File.Exists(data.Source) && File.Exists(Path.Combine(folders.Output, "2026", "4", "Videos", "large.mp4")),
            "Cancellation after commit did not retain both originals.");
    }

    static async Task RecoveryJunction()
    {
        var folders = Folders();
        string source = Write(folders.Input, "image.jpg", "original");
        ScanResult scan = await Scan(folders);
        string other = Path.Combine(Path.GetDirectoryName(folders.Input)!, "elsewhere");
        Directory.CreateDirectory(other);
        MakeJunction(Path.Combine(folders.Input, ".family-library-tools"), other);
        await Throws<IOException>(() => CopyAndRetain(scan));
        Check(File.ReadAllText(source) == "original" && Directory.GetFileSystemEntries(other).Length == 0, "Recovery followed a junction.");
    }
}
