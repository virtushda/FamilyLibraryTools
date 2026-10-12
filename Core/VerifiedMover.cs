using System.Globalization;

namespace FamilyLibraryTools.Core;

public sealed class VerifiedMover
{
    readonly string inputRoot;
    readonly string outputRoot;
    readonly TransferJournal journal;
    internal Action? SourceVerified { get; init; } // Deterministic checkpoint for filesystem race tests.

    public VerifiedMover(string inputRoot, string outputRoot, TransferJournal journal)
    {
        var roots = PathSafety.ValidateRoots(inputRoot, outputRoot);
        this.inputRoot = roots.Input;
        this.outputRoot = roots.Output;
        this.journal = journal;
    }

    public async Task<MoveResult> MoveAsync(MediaFile file, DateTime date, bool duplicate, CancellationToken token)
    {
        if (file.InOutput || !PathSafety.IsWithin(file.Path, inputRoot)) throw new ArgumentException("Only scanned input files can be moved.");
        token.ThrowIfCancellationRequested();
        using var rootLocks = NativeFiles.LockDirectories(Path.GetDirectoryName(file.Path)!, outputRoot);
        string folder = System.IO.Path.Combine(outputRoot, date.Year.ToString("D4", CultureInfo.InvariantCulture), date.Month.ToString(CultureInfo.InvariantCulture));
        if (file.Kind == MediaKind.Video) folder = System.IO.Path.Combine(folder, "Videos");
        using var folderLocks = CreateLockedDirectory(folder);
        string destination = System.IO.Path.Combine(folder, file.Name);
        if (duplicate)
        {
            int suffix = 1;
            string stem = FilenameDates.OriginalStem(file.Name);
            string extension = System.IO.Path.GetExtension(file.Name);
            do { destination = System.IO.Path.Combine(folder, $"{stem}_DUPLICATE-{suffix++}{extension}"); }
            while (File.Exists(destination) || Directory.Exists(destination));
        }
        else if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException($"A destination appeared after scanning. Scan again: {destination}");
        using var source = NativeFiles.OpenForMove(file.Path);
        if (file.Identity == null || NativeFiles.Identity(source) != file.Identity)
            throw new IOException($"Source was replaced after scanning. Scan again: {file.Path}");
        await FileContents.ValidateAsync(source, file, token);
        SourceVerified?.Invoke();
        journal.Record("MovePlanned", file.Path, destination, file.Hash, $"Date: {date:O}; source: {file.Date.Source}; note: {file.Date.Note}");
        token.ThrowIfCancellationRequested();
        // No bytes are recreated on the same volume, so all streams travel with the original.
        if (NativeFiles.TryRenameWithinVolume(source, folderLocks, System.IO.Path.GetFileName(destination)))
        {
            journal.Record("Moved", file.Path, destination, file.Hash);
            return new(destination, null);
        }
        return await CopyAndRetainAsync(file, source, destination, token);
    }

    // The caller holds the source handle and its input/output parent directories locked.
    internal async Task<MoveResult> CopyAndRetainAsync(MediaFile file, FileStream source, string destination, CancellationToken token)
    {
        string recovery = System.IO.Path.Combine(inputRoot, ".family-library-tools", "recovery",
            System.IO.Path.GetFileNameWithoutExtension(journal.Path), System.IO.Path.GetRelativePath(inputRoot, file.Path));
        using var recoveryLocks = CreateLockedDirectory(System.IO.Path.GetDirectoryName(recovery)!);
        using var destinationFolder = NativeFiles.OpenDirectory(System.IO.Path.GetDirectoryName(destination)!);
        string partial = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(destination)!, $".flt-{Guid.NewGuid():N}.partial");
        journal.Record("CopyPlanned", file.Path, destination, file.Hash, $"Staging: {partial}; original recovery path: {recovery}");
        source.Position = 0;
        using var staging = NativeFiles.CreateNewFile(destinationFolder, System.IO.Path.GetFileName(partial), FileAccess.ReadWrite, FileShare.None, true);
        await source.CopyToAsync(staging, token);
        await staging.FlushAsync(token);
        staging.Flush(true);
        await FileContents.ValidateAsync(staging, file, token);
        if (!await FileContents.EqualStreamsAsync(source, staging, token)) throw new IOException("Staged bytes do not match the source. Original retained.");
        File.SetCreationTimeUtc(staging.SafeFileHandle, File.GetCreationTimeUtc(source.SafeFileHandle));
        File.SetLastWriteTimeUtc(staging.SafeFileHandle, file.LastWriteUtc);
        token.ThrowIfCancellationRequested();
        if (!NativeFiles.TryRenameWithinVolume(staging, destinationFolder, System.IO.Path.GetFileName(destination)))
            throw new IOException($"Staged output must remain on its original volume. Original retained; staging: {partial}");
        // The same verified output handle stays locked through commit and source retention.
        await FileContents.ValidateAsync(staging, file, token);
        if (!await FileContents.EqualStreamsAsync(source, staging, token)) throw new IOException("Destination verification failed. Original retained.");
        journal.Record("DestinationVerified", file.Path, destination, file.Hash);
        token.ThrowIfCancellationRequested();
        journal.Record("RecoveryPlanned", file.Path, recovery, file.Hash, $"Verified output: {destination}");
        token.ThrowIfCancellationRequested();
        if (!NativeFiles.TryRenameWithinVolume(source, recoveryLocks, System.IO.Path.GetFileName(recovery)))
            throw new IOException($"Recovery must be on the source volume. Original remains at {file.Path}; verified output: {destination}");
        journal.Record("OriginalRetained", file.Path, recovery, file.Hash, $"Verified output: {destination}");
        return new(destination, recovery);
    }

    internal static NativeFiles.DirectoryHandle CreateLockedDirectory(string folder) => NativeFiles.OpenDirectory(folder, true);
}
