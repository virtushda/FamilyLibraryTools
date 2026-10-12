namespace FamilyLibraryTools.Core;

public sealed class Organizer
{
    public async Task<RunResult> RunAsync(ScanResult scan,
        Func<MediaFile, CancellationToken, Task<MediaDate?>> chooseDate,
        IProgress<string> progress, CancellationToken token)
    {
        PathSafety.ValidateRoots(scan.InputRoot, scan.OutputRoot);
        foreach (var issue in scan.Issues)
            if (PathSafety.IsWithin(issue.Path, scan.OutputRoot))
                throw new IOException($"Output could not be fully scanned. Resolve this first: {issue.Path}: {issue.Reason}");
        using var journal = new TransferJournal(scan.OutputRoot);
        var mover = new VerifiedMover(scan.InputRoot, scan.OutputRoot, journal);
        int moved = 0, duplicates = 0, skipped = 0, retained = 0;
        journal.Record("Started", scan.InputRoot, scan.OutputRoot);
        progress.Report($"Journal: {journal.Path}");
        foreach (var issue in scan.Issues) journal.Record("ScanIssue", issue.Path, detail: issue.Reason);
        try
        {
            foreach (var group in CollisionGroups.Build(scan.Files))
            {
                bool hasReference = false;
                foreach (MediaFile file in group)
                {
                    token.ThrowIfCancellationRequested();
                    if (file.InOutput) { hasReference = true; continue; }
                    MediaDate? date = file.Date.NeedsReview || !file.Date.Value.HasValue
                        ? await chooseDate(file, token) : file.Date;
                    if (date?.Value is not DateTime selected)
                    {
                        skipped++;
                        journal.Record("DateReviewSkipped", file.Path, hash: file.Hash);
                        continue;
                    }
                    progress.Report($"Verifying move: {file.Path}");
                    MoveResult result = await mover.MoveAsync(file with { Date = date }, selected, hasReference, token);
                    moved++;
                    if (hasReference) duplicates++;
                    hasReference = true;
                    if (result.RetainedOriginal != null)
                    {
                        retained++;
                        progress.Report($"Original kept for recovery: {result.RetainedOriginal}");
                    }
                    progress.Report($"Organized: {result.Destination}");
                }
            }
            journal.Record("Completed", detail: $"Organized: {moved}; marked duplicates: {duplicates}; recovery originals: {retained}; skipped dates: {skipped}");
            return new(moved, duplicates, skipped, journal.Path, retained);
        }
        catch (Exception error)
        {
            journal.Record(error is OperationCanceledException ? "Cancelled" : "Stopped", detail: error.Message);
            throw;
        }
    }
}
