using System.IO;
using System.Text.Json;
using FamilyLibraryTools.Core;

static partial class Program
{
    sealed class ManualClock : TimeProvider
    {
        long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance(double seconds) => ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }

    static async Task<(string Root, IReadOnlyList<IReadOnlyList<MediaFile>> Groups)> ReviewFiles(int count = 1)
    {
        var folders = Folders();
        for (int index = 0; index < count; index++)
        {
            Write(folders.Output, $"2026\\4\\photo{index}.jpg", $"kept photo {index}");
            Write(folders.Output, $"2026\\4\\photo{index}_DUPLICATE-1.jpg", $"rejected photo {index}");
        }
        LibraryScan scan = await new LibraryScanner((_, _) => knownDate).ScanLibraryAsync(folders.Output, quiet, default);
        return (folders.Output, CollisionGroups.Build(scan.Files, true));
    }

    static Task<string> MoveToTestQuarantine(PendingDeletion choice)
    {
        choice.BeginQuarantine();
        File.Move(choice.Reject.Path, choice.Reject.Path + ".test-bin", false);
        return Task.FromResult(choice.Reject.Path + ".test-bin");
    }

    static string ReadJournal(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static async Task RecoveryCasing()
    {
        var folders = Folders();
        string retained = Write(folders.Input, ".FAMILY-LIBRARY-TOOLS\\recovery\\photo.jpg", "retained original");
        Write(folders.Output, ".FAMILY-LIBRARY-TOOLS\\quarantine\\pending.jpg", "pending recovery");
        Write(folders.Input, "photo.jpg", "new input");
        ScanResult scan = await Scan(folders);
        Check(scan.Files.Count == 1 && scan.Files[0].Path.EndsWith("input\\photo.jpg"), "Recovery files entered a scan.");
        await Organize(scan);
        Check(File.ReadAllText(retained) == "retained original", "Recovery source was reorganized.");
        LibraryScan library = await new LibraryScanner((_, _) => knownDate).ScanLibraryAsync(folders.Output, quiet, default);
        Check(library.Files.Count == 1, "Removal scan included recovery files.");
        await Throws<IOException>(() => new LibraryScanner((_, _) => knownDate).ScanLibraryAsync(Path.GetDirectoryName(retained)!, quiet, default));
    }

    static async Task QueueCountdown()
    {
        var data = await ReviewFiles();
        var pair = data.Groups[0];
        var clock = new ManualClock();
        using var queue = new DeletionQueue(data.Groups, MoveToTestQuarantine, clock);
        PendingDeletion first = queue.Enqueue(pair[0], pair[1]);
        clock.Advance(59.999);
        await queue.ProcessDueAsync();
        Check(File.Exists(pair[1].Path) && first.State == DeletionState.Waiting, "Quarantined before the full 60 seconds.");
        Check(first.Cancel(), "Waiting choice could not be cancelled.");
        clock.Advance(100);
        await queue.ProcessDueAsync();
        Check(File.Exists(pair[1].Path), "Cancelled choice quarantined later.");
        PendingDeletion second = queue.Enqueue(pair[0], pair[1]);
        Check(queue.Remaining(second) == TimeSpan.FromSeconds(60), "Fresh choice reused an old deadline.");
        clock.Advance(59.999);
        await queue.ProcessDueAsync();
        Check(File.Exists(pair[1].Path), "Fresh choice quarantined early.");
        clock.Advance(.001);
        await queue.ProcessDueAsync();
        Check(second.State == DeletionState.Quarantined && File.Exists(pair[0].Path) && !File.Exists(pair[1].Path) && File.ReadAllText(pair[1].Path + ".test-bin") == "rejected photo 0", "Queue executed the wrong file or deadline.");
    }

    static async Task QueueStopAll()
    {
        var data = await ReviewFiles(3);
        var clock = new ManualClock();
        using var queue = new DeletionQueue(data.Groups, MoveToTestQuarantine, clock);
        foreach (var pair in data.Groups) queue.Enqueue(pair[0], pair[1]);
        clock.Advance(60);
        queue.StopAll();
        await queue.ProcessDueAsync();
        foreach (var pair in data.Groups) Check(File.Exists(pair[0].Path) && File.Exists(pair[1].Path), "STOP ALL left a live deletion.");
        foreach (var choice in queue.Entries) Check(choice.State == DeletionState.Cancelled, "Pending choice not cancelled.");
        await Throws<InvalidOperationException>(() => { queue.Enqueue(data.Groups[0][0], data.Groups[0][1]); return Task.CompletedTask; });
        using var next = new DeletionQueue(data.Groups, MoveToTestQuarantine, clock);
        clock.Advance(120);
        await next.ProcessDueAsync();
        Check(next.Entries.Count == 0 && File.Exists(data.Groups[0][1].Path), "A new session resumed old deletion choices.");
    }

    static async Task QueueConflict()
    {
        var data = await ReviewFiles(2);
        var pair = data.Groups[0];
        using var queue = new DeletionQueue(data.Groups, MoveToTestQuarantine);
        PendingDeletion pending = queue.Enqueue(pair[0], pair[1]);
        await Throws<InvalidOperationException>(() => { queue.Enqueue(pair[1], pair[0]); return Task.CompletedTask; });
        await Throws<InvalidOperationException>(() => { queue.Enqueue(pair[0], data.Groups[1][1]); return Task.CompletedTask; });
        await Throws<InvalidOperationException>(() => { queue.Enqueue(pair[0], pair[0]); return Task.CompletedTask; });
        Check(pending.Cancel(), "Cannot withdraw conflicting choice.");
        Check(queue.Enqueue(pair[1], pair[0]).State == DeletionState.Waiting, "Cannot reverse an explicitly cancelled choice.");
        var alias = pair[1] with { Identity = pair[0].Identity };
        IReadOnlyList<IReadOnlyList<MediaFile>> aliases = new List<IReadOnlyList<MediaFile>> { new[] { pair[0], alias } };
        using var aliasQueue = new DeletionQueue(aliases, MoveToTestQuarantine);
        await Throws<InvalidOperationException>(() => { aliasQueue.Enqueue(pair[0], alias); return Task.CompletedTask; });
    }

    static async Task QueueCancelVerifying()
    {
        var data = await ReviewFiles();
        var pair = data.Groups[0];
        var clock = new ManualClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var queue = new DeletionQueue(data.Groups, async entry =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, entry.Token);
            return await MoveToTestQuarantine(entry);
        }, clock);
        PendingDeletion entry = queue.Enqueue(pair[0], pair[1]);
        clock.Advance(60);
        Task processing = queue.ProcessDueAsync();
        await entered.Task;
        Check(entry.Cancel() && entry.State == DeletionState.Cancelling, "Verification was not cancellable.");
        await processing;
        Check(entry.State == DeletionState.Cancelled && File.Exists(pair[0].Path) && File.Exists(pair[1].Path), "Cancellation crossed the recycle boundary.");
    }

    static async Task QueueStopAtHandoff()
    {
        var data = await ReviewFiles(2);
        var clock = new ManualClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var queue = new DeletionQueue(data.Groups, async entry =>
        {
            entry.BeginQuarantine();
            entered.SetResult();
            await release.Task;
            File.Move(entry.Reject.Path, entry.Reject.Path + ".test-bin", false);
            return entry.Reject.Path + ".test-bin";
        }, clock);
        PendingDeletion first = queue.Enqueue(data.Groups[0][0], data.Groups[0][1]);
        PendingDeletion second = queue.Enqueue(data.Groups[1][0], data.Groups[1][1]);
        clock.Advance(60);
        Task processing = queue.ProcessDueAsync();
        await entered.Task;
        Check(!first.Cancel(), "Cancellation falsely claimed to undo a native handoff.");
        queue.StopAll();
        release.SetResult();
        await processing;
        Check(first.State == DeletionState.Quarantined && second.State == DeletionState.Cancelled && File.Exists(second.Reject.Path), "STOP ALL started another transfer.");
    }

    static async Task QuarantineChangedFiles()
    {
        for (int scenario = 0; scenario < 3; scenario++)
        {
            var data = await ReviewFiles();
            var pair = data.Groups[0];
            var clock = new ManualClock();
            using var journal = new TransferJournal(data.Root);
            using var queue = new DeletionQueue(data.Groups, new VerifiedQuarantine(data.Root, journal).QuarantineAsync, clock);
            PendingDeletion entry = queue.Enqueue(pair[0], pair[1]);
            if (scenario == 0) File.WriteAllText(pair[0].Path, "changed kept file");
            else if (scenario == 1) File.WriteAllText(pair[1].Path, "changed rejected file");
            else
            {
                File.Move(pair[1].Path, pair[1].Path + ".original");
                File.WriteAllText(pair[1].Path, "rejected photo 0"); // Same bytes; different identity.
            }
            clock.Advance(60);
            await queue.ProcessDueAsync();
            Check(entry.State == DeletionState.Failed && queue.Stopped && File.Exists(pair[0].Path) && File.Exists(pair[1].Path), "Changed file was quarantined.");
            Check(!ReadJournal(journal.Path).Contains("QuarantinePlanned"), "Changed file reached quarantine.");
        }
    }

    static async Task QuarantineLocksAndLinks()
    {
        for (int scenario = 0; scenario < 3; scenario++)
        {
            var data = await ReviewFiles();
            var pair = data.Groups[0];
            var clock = new ManualClock();
            using var journal = new TransferJournal(data.Root);
            using var queue = new DeletionQueue(data.Groups, new VerifiedQuarantine(data.Root, journal).QuarantineAsync, clock);
            PendingDeletion entry = queue.Enqueue(pair[0], pair[1]);
            using var locked = scenario < 2 ? new FileStream(pair[scenario].Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read) : null;
            if (scenario == 2)
            {
                string outside = Path.Combine(Path.GetDirectoryName(data.Root)!, "elsewhere");
                Directory.CreateDirectory(outside);
                MakeJunction(Path.Combine(data.Root, ".family-library-tools", "quarantine"), outside);
            }
            clock.Advance(60);
            await queue.ProcessDueAsync();
            Check(entry.State == DeletionState.Failed && File.Exists(pair[0].Path) && File.Exists(pair[1].Path), "Lock or linked staging allowed quarantine.");
        }
    }

    static async Task QuarantineJournalFailure()
    {
        var data = await ReviewFiles(2);
        var clock = new ManualClock();
        using var journal = new TransferJournal(data.Root);
        using var queue = new DeletionQueue(data.Groups, new VerifiedQuarantine(data.Root, journal).QuarantineAsync, clock);
        PendingDeletion first = queue.Enqueue(data.Groups[0][0], data.Groups[0][1]);
        PendingDeletion second = queue.Enqueue(data.Groups[1][0], data.Groups[1][1]);
        journal.Dispose(); // A failed durable plan must stop before either file can move.
        clock.Advance(60);
        await queue.ProcessDueAsync();
        Check(first.State == DeletionState.Failed && second.State == DeletionState.Cancelled && queue.Stopped, "Journal failure did not stop the queue.");
        foreach (var pair in data.Groups)
            Check(File.Exists(pair[0].Path) && File.Exists(pair[1].Path), "Journal failure moved a reviewed file.");
    }

    static async Task RealQuarantine()
    {
        var data = await ReviewFiles();
        var pair = data.Groups[0];
        File.WriteAllText(pair[1].Path + ":metadata", "extra original metadata");
        var clock = new ManualClock();
        using var journal = new TransferJournal(data.Root);
        using var queue = new DeletionQueue(data.Groups, new VerifiedQuarantine(data.Root, journal).QuarantineAsync, clock);
        PendingDeletion entry = queue.Enqueue(pair[0], pair[1]);
        clock.Advance(60);
        await queue.ProcessDueAsync();
        Check(entry.State == DeletionState.Quarantined, "Quarantine failed: " + entry.Error + "\n" + ReadJournal(journal.Path));
        Check(!File.Exists(pair[1].Path), "Rejected file did not move.");
        File.WriteAllText(pair[1].Path, "replacement at the old path");
        await queue.ProcessDueAsync();
        Check(File.ReadAllText(pair[0].Path) == "kept photo 0" && File.ReadAllText(pair[1].Path) == "replacement at the old path", "Quarantine changed an unselected file.");
        string? quarantined = null;
        foreach (string line in ReadJournal(journal.Path).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var record = JsonDocument.Parse(line);
            if (record.RootElement.GetProperty("Action").GetString() == "Quarantined") quarantined = record.RootElement.GetProperty("Destination").GetString();
        }
        Check(quarantined == entry.Destination && PathSafety.IsWithin(quarantined!, Path.Combine(data.Root, ".family-library-tools", "quarantine")), "Missing confirmed quarantine location.");
        Check(File.ReadAllText(quarantined!) == "rejected photo 0" && File.ReadAllText(quarantined + ":metadata") == "extra original metadata", "Quarantine lost original bytes or streams.");
        using (var retained = FileContents.OpenRead(quarantined!)) Check(NativeFiles.Identity(retained) == pair[1].Identity, "Quarantine moved a different file identity.");
        LibraryScan rescanned = await new LibraryScanner((_, _) => knownDate).ScanLibraryAsync(data.Root, quiet, default);
        Check(rescanned.Files.Count == 2, "Quarantine was included in the next review scan.");
    }
}
