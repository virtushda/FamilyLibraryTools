namespace FamilyLibraryTools.Core;

public sealed class LibraryScanner
{
    readonly Func<string, MediaKind, MediaDate> readMetadata;

    public LibraryScanner(Func<string, MediaKind, MediaDate> readMetadata) => this.readMetadata = readMetadata;

    public async Task<ScanResult> ScanAsync(string input, string output, IProgress<string> progress, CancellationToken token)
    {
        var roots = PathSafety.ValidateRoots(input, output);
        LibraryScan source = await ScanRootAsync(roots.Input, false, progress, token);
        LibraryScan destination = await ScanRootAsync(roots.Output, true, progress, token);
        var files = new List<MediaFile>(source.Files);
        files.AddRange(destination.Files);
        var issues = new List<ScanIssue>(source.Issues);
        issues.AddRange(destination.Issues);
        return new(roots.Input, roots.Output, files, issues, source.IgnoredFiles + destination.IgnoredFiles);
    }

    public Task<LibraryScan> ScanLibraryAsync(string root, IProgress<string> progress, CancellationToken token) =>
        ScanRootAsync(PathSafety.CanonicalDirectory(root), true, progress, token);

    async Task<LibraryScan> ScanRootAsync(string root, bool inOutput, IProgress<string> progress, CancellationToken token)
    {
        PathSafety.RejectLinks(root);
        PathSafety.RejectBookkeepingRoot(root);
        var files = new List<MediaFile>();
        var issues = new List<ScanIssue>();
        int ignored = 0;
        var reportTimer = System.Diagnostics.Stopwatch.StartNew();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string folder = pending.Pop();
            string[] entries;
            try
            {
                PathSafety.RejectLinks(folder);
                entries = Directory.GetFileSystemEntries(folder);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                issues.Add(new(folder, error.Message));
                continue;
            }
            foreach (string path in entries)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        issues.Add(new(path, "Link / junction / cloud placeholder left untouched."));
                        continue;
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!Path.GetFileName(path).Equals(".family-library-tools", StringComparison.OrdinalIgnoreCase)) pending.Push(path);
                        continue;
                    }
                    MediaKind? kind = MediaFormats.Classify(path);
                    if (kind == null) { ignored++; continue; }
                    if (files.Count == 0 || reportTimer.ElapsedMilliseconds >= 100)
                    {
                        progress.Report($"Reading {files.Count + 1}: {path}");
                        reportTimer.Restart();
                    }
                    using var stream = FileContents.OpenRead(path);
                    long length = stream.Length;
                    if (length == 0) { issues.Add(new(path, "Empty media file left untouched.")); continue; }
                    string hash = await FileContents.HashAsync(stream, token);
                    MediaDate date = inOutput ? new(null, "Already in output") : FilenameDates.Resolve(Path.GetFileName(path), readMetadata(path, kind.Value));
                    files.Add(new(path, kind.Value, length, hash, File.GetLastWriteTimeUtc(path), date, inOutput, NativeFiles.Identity(stream)));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    issues.Add(new(path, error.Message));
                }
            }
        }
        files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
        return new(files, issues, ignored);
    }
}
