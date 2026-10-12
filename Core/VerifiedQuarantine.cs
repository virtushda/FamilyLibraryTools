namespace FamilyLibraryTools.Core;

/// <summary>Rechecks the reviewed pair and retains the rejected file in recoverable quarantine.</summary>
public sealed class VerifiedQuarantine
{
    readonly string root;
    readonly TransferJournal journal;

    public VerifiedQuarantine(string root, TransferJournal journal)
    {
        this.root = PathSafety.CanonicalDirectory(root);
        PathSafety.RejectBookkeepingRoot(this.root);
        this.journal = journal;
    }

    public Task<string> QuarantineAsync(PendingDeletion choice) => Task.Run(async () =>
    {
        MediaFile keep = choice.Keep, reject = choice.Reject;
        ValidatePath(keep);
        ValidatePath(reject);
        using var folders = NativeFiles.LockDirectories(Path.GetDirectoryName(keep.Path)!, Path.GetDirectoryName(reject.Path)!);
        using var kept = FileContents.OpenRead(keep.Path);
        using var source = NativeFiles.OpenForMove(reject.Path);
        ValidateIdentity(kept, keep);
        ValidateIdentity(source, reject);
        if (NativeFiles.Identity(kept) == NativeFiles.Identity(source)) throw new IOException("Both paths refer to the same file. Nothing was moved.");
        await FileContents.ValidateAsync(kept, keep, choice.Token);
        await FileContents.ValidateAsync(source, reject, choice.Token);

        string folder = Path.Combine(root, ".family-library-tools", "quarantine", Guid.NewGuid().ToString("N"));
        using var quarantine = NativeFiles.OpenDirectory(folder, true);
        string destination = Path.Combine(folder, reject.Name);
        journal.Record("QuarantinePlanned", reject.Path, destination, reject.Hash, $"Explicitly kept: {keep.Path}");
        // This exclusive move handle is never released to a path-based recycler.
        choice.BeginQuarantine();
        if (!NativeFiles.TryRenameWithinVolume(source, quarantine, reject.Name))
            throw new IOException("Quarantine must be on the same volume. Original retained.");
        try { journal.Record("Quarantined", reject.Path, destination, reject.Hash, $"Kept: {keep.Path}"); }
        catch (Exception error)
        {
            throw new IOException($"File is safely retained at {destination}, but recording completion failed: {error.Message}", error);
        }
        return destination;
    }, choice.Token);

    void ValidatePath(MediaFile file)
    {
        string path = Path.GetFullPath(file.Path);
        if (!file.InOutput || !PathSafety.IsWithin(path, root) || MediaFormats.Classify(path) != file.Kind ||
            !path.Equals(file.Path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Only scanned media inside the chosen library can be quarantined.");
        PathSafety.RejectBookkeepingRoot(path);
    }

    static void ValidateIdentity(FileStream stream, MediaFile file)
    {
        if (file.Identity == null || NativeFiles.Identity(stream) != file.Identity)
            throw new IOException($"A reviewed file was replaced. Scan again; nothing was moved: {file.Path}");
    }
}
