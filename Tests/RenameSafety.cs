using System.IO;
using FamilyLibraryTools.Core;

static partial class Program
{
    static async Task KeepBothRename()
    {
        foreach (string name in new[] { "photo.jpg", "PXL_20260412_143015123.MP.jpg", "IMG_E1234.heic", "20260412_143015.mp4" })
        {
            var folders = Folders();
            string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
            string keep = Write(folders.Output, "2026\\4\\" + name, "kept original");
            string duplicate = Write(folders.Output, $"2026\\4\\{stem}_DUPLICATE-1{extension}", "different selected version");
            string occupied = Write(folders.Output, $"2025\\3\\{stem}-1{extension}", "existing unrelated variant");
            Directory.CreateDirectory(Path.Combine(folders.Output, $"2026\\4\\{stem}-2{extension}"));
            File.WriteAllText(duplicate + ":metadata", "retained metadata stream");
            LibraryScan scan = await new LibraryScanner((_, _) => knownDate).ScanLibraryAsync(folders.Output, quiet, default);
            MediaFile original = FindMedia(scan.Files, keep), right = FindMedia(scan.Files, duplicate);
            using var journal = new TransferJournal(folders.Output);
            MediaFile renamed = await new VerifiedRenamer(folders.Output, scan.Files, journal).KeepBothAsync(original, right, default);
            Check(renamed.Name == $"{stem}-3{extension}" && !File.Exists(duplicate), "Keep both did not choose an unused -number name.");
            Check(File.ReadAllText(keep) == "kept original" && File.ReadAllText(occupied) == "existing unrelated variant" &&
                File.ReadAllText(renamed.Path) == "different selected version" && File.ReadAllText(renamed.Path + ":metadata") == "retained metadata stream",
                "Keep both changed bytes or lost a stream.");
            using (var moved = FileContents.OpenRead(renamed.Path)) Check(NativeFiles.Identity(moved) == right.Identity, "Keep both moved a different identity.");
            LibraryScan after = await new LibraryScanner((_, _) => knownDate).ScanLibraryAsync(folders.Output, quiet, default);
            foreach (var group in CollisionGroups.Build(after.Files, true)) Check(group.Count == 1, "Retained variants still collide by name after rescanning.");
            Check(ReadJournal(journal.Path).Contains("KeptBoth"), "Keep-both completion was not journaled.");
        }
    }

    static async Task KeepBothExactCopies()
    {
        var folders = Folders();
        string original = Write(folders.Output, "photo.jpg", "same photo");
        string duplicate = Write(folders.Output, "photo_DUPLICATE-1.jpg", "same photo");
        LibraryScan scan = await new LibraryScanner((_, _) => knownDate).ScanLibraryAsync(folders.Output, quiet, default);
        using var journal = new TransferJournal(folders.Output);
        await new VerifiedRenamer(folders.Output, scan.Files, journal).KeepBothAsync(FindMedia(scan.Files, original), FindMedia(scan.Files, duplicate), default);
        LibraryScan after = await new LibraryScanner((_, _) => knownDate).ScanLibraryAsync(folders.Output, quiet, default);
        var groups = CollisionGroups.Build(after.Files, true);
        Check(groups.Count == 1 && groups[0].Count == 2, "Renaming hid an exact-content copy.");
    }

    static async Task KeepBothFailures()
    {
        for (int scenario = 0; scenario < 6; scenario++)
        {
            var data = await ReviewFiles();
            var pair = data.Groups[0];
            using var journal = new TransferJournal(data.Root);
            var rename = new VerifiedRenamer(data.Root, pair, journal);
            using var cancellation = new CancellationTokenSource();
            if (scenario == 0) File.WriteAllText(pair[0].Path, "changed original");
            if (scenario == 1) File.WriteAllText(pair[1].Path, "changed duplicate");
            if (scenario == 2)
            {
                File.Move(pair[1].Path, pair[1].Path + ".original");
                File.WriteAllText(pair[1].Path, "rejected photo 0");
            }
            using var locked = scenario == 3 ? new FileStream(pair[1].Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read) : null;
            if (scenario == 4) cancellation.Cancel();
            if (scenario == 5) journal.Dispose();
            bool failed = false;
            try { await rename.KeepBothAsync(pair[0], pair[1], cancellation.Token); }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { failed = true; }
            Check(failed && File.Exists(pair[0].Path) && File.Exists(pair[1].Path), "An unsafe keep-both request moved a reviewed file.");
            Check(!File.Exists(Path.Combine(Path.GetDirectoryName(pair[1].Path)!, FilenameDates.OriginalStem(pair[1].Name) + "-1.jpg")),
                "A failed keep-both request created a renamed file.");
        }
    }

    static MediaFile FindMedia(IReadOnlyList<MediaFile> files, string path)
    {
        foreach (MediaFile file in files) if (file.Path == path) return file;
        throw new InvalidOperationException("Fixture file was not scanned: " + path);
    }
}
