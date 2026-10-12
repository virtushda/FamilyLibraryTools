namespace FamilyLibraryTools.Core;

/// <summary>Retains a reviewed pair and gives the right-hand file a distinct name.</summary>
public sealed class VerifiedRenamer
{
    readonly string root;
    readonly TransferJournal journal;
    readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

    public VerifiedRenamer(string root, IReadOnlyList<MediaFile> files, TransferJournal journal)
    {
        this.root = PathSafety.CanonicalDirectory(root);
        PathSafety.RejectBookkeepingRoot(this.root);
        this.journal = journal;
        foreach (MediaFile file in files) names.Add(file.NameKey);
    }

    public async Task<MediaFile> KeepBothAsync(MediaFile original, MediaFile duplicate, CancellationToken token)
    {
        ValidatePath(original);
        ValidatePath(duplicate);
        token.ThrowIfCancellationRequested();
        using var originalFolder = NativeFiles.OpenDirectory(Path.GetDirectoryName(original.Path)!);
        using var folder = NativeFiles.OpenDirectory(Path.GetDirectoryName(duplicate.Path)!);
        using var kept = NativeFiles.OpenRead(original.Path);
        using var source = NativeFiles.OpenForMove(duplicate.Path);
        if (original.Identity == null || duplicate.Identity == null || original.Identity == duplicate.Identity ||
            NativeFiles.Identity(kept) != original.Identity || NativeFiles.Identity(source) != duplicate.Identity)
            throw new IOException("A reviewed file was replaced or both paths identify the same file. Scan again; nothing was renamed.");
        await FileContents.ValidateAsync(kept, original, token);
        await FileContents.ValidateAsync(source, duplicate, token);

        string stem = FilenameDates.OriginalStem(duplicate.Name);
        string extension = Path.GetExtension(duplicate.Name);
        int suffix = 1;
        MediaFile renamed;
        do
        {
            string destination = Path.Combine(Path.GetDirectoryName(duplicate.Path)!, $"{stem}-{suffix++}{extension}");
            renamed = duplicate with { Path = destination };
        }
        while (names.Contains(renamed.NameKey) || File.Exists(renamed.Path) || Directory.Exists(renamed.Path));

        journal.Record("KeepBothPlanned", duplicate.Path, renamed.Path, duplicate.Hash, $"Also kept: {original.Path}");
        token.ThrowIfCancellationRequested();
        if (!NativeFiles.TryRenameWithinVolume(source, folder, renamed.Name))
            throw new IOException("The renamed file must stay in its original folder. Original retained.");
        names.Add(renamed.NameKey);
        try { journal.Record("KeptBoth", duplicate.Path, renamed.Path, duplicate.Hash, $"Also kept: {original.Path}"); }
        catch (Exception error)
        {
            throw new IOException($"Both files are retained; the renamed file is at {renamed.Path}, but recording completion failed: {error.Message}", error);
        }
        return renamed;
    }

    void ValidatePath(MediaFile file)
    {
        string path = Path.GetFullPath(file.Path);
        if (!file.InOutput || !PathSafety.IsWithin(path, root) || MediaFormats.Classify(path) != file.Kind ||
            !path.Equals(file.Path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Only scanned media inside the chosen library can be renamed.");
        PathSafety.RejectBookkeepingRoot(path);
    }
}
