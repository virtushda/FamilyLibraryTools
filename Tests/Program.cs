using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FamilyLibraryTools;
using FamilyLibraryTools.Core;
using FamilyLibraryTools.Media;
using FamilyLibraryTools.Views;

static partial class Program
{
    static readonly MediaDate knownDate = new(new DateTime(2026, 4, 12, 14, 15, 16), "Test capture metadata");
    static readonly IProgress<string> quiet = new ImmediateProgress(_ => { });
    static string scratch = "";
    static int passed;

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 1 || !File.Exists(Path.Combine(args[0], "FamilyLibraryTools.csproj")))
            throw new ArgumentException("Pass the project directory so all synthetic fixtures stay inside the workspace.");
        scratch = Path.GetFullPath(Path.Combine(args[0], ".codexTemp", "tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(scratch);
        try
        {
            Test("Phone filenames, metadata precedence, and ambiguous dates", DatePatterns);
            Test("WPF workflow tabs and no automatic deletion choices", WindowInitialization);
            Test("Photo metadata, orientation, and perceptual hints", PhotoChecks);
            Test("OCR benchmark passes require explicit valid time, confidence and no ambiguity", BenchmarkPassRules);
            Test("Both real PP-OCR models read dates locally and preserve source bytes", TwoOcrModels);
            Run("Read-only recursive benchmarks report all methods, failures and partial cancellation", BenchmarkFolder);
            Test("Hidden Developer Tools review, ratings, stale inputs, cancellation and Medium retry", UiBenchmark);
            Run("Cancelled waiting preparations never read sources or leave handles open", PreparationCancellation);
            Run("Current-first preparation runs the next review in parallel and promotes its cached result", ParallelDatePreparation);
            Run("Prepared pixels cannot bypass later source identity and content verification", ChangedPreparedSource);
            Test("OCR defaults on, can be disabled, and pending reads permit explicit manual choices without stale inputs", UiBackgroundDates);
            Test("Ranked capture, filename, embedded and disk date suggestions", RankedDates);
            Test("Visible date parsing preserves ambiguity, precision, and evidence", VisibleDateParsing);
            Test("Real local OCR reads screenshot dates without modifying images", RealVisibleDateOcr);
            Test("Date-region mapping handles DPI, letterboxing, reverse drags, edges and orientation", OcrRegionMapping);
            Test("Medium OCR and selected regions retain evidence, ambiguity, and original bytes", MediumVisibleDateOcr);
            Test("WPF OCR retry, crop, debug text, manual edits, cancellation and stale-result isolation", UiOcrTools);
            Test("Button gestures require release, survive missed key-up, and invalidate stale mouse/Space presses", HeldButtonGestures);
            Test("Held inputs cannot accept OCR refreshes, changed manual dates, or subsequent photos", UiHeldDateInputs);
            Test("Held inputs cannot queue or rename subsequent pairs, and stale releases cannot choose a replaced pair", UiHeldDuplicateInputs);
            Test("WPF OCR suggestions require confirmation, disambiguation, and honor cancellation", UiVisibleDates);
            Test("Bulk skip leaves review files intact, finishes automatic moves, and resets next run", UiSkipNonAutomatic);
            Run("XMP partial dates and PNG RFC1123 dates preserve precision and require confirmation", MetadataDateFormats);
            Run("Recursive routing and exact duplicates with different names", ExactDuplicates);
            Run("Equal-size different contents are both organized", SameSizeConflict);
            Run("Different-size files are both organized", DifferentSizeConflict);
            Run("More than two candidates can all be retained without overwrites", MultipleCandidates);
            Run("Existing output exact copy moves input as a marked duplicate", ExistingExact);
            Run("Existing output is preserved when a new variant is selected", ExistingDifferent);
            Run("Missing and uncertain dates invoke explicit review", DateReview);
            Run("Skipping a date retains its source", SkipDate);
            Run("Changed source detected before relocation", ChangedSource);
            Run("Locked source retained on failure", LockedSource);
            Run("Destination appearing after scanning is never overwritten", LateCollision);
            Run("Cancelled review leaves every variant untouched", CancelledReview);
            Run("Cancellation during staged copy retains original", CancelledCopy);
            Run("Staged-copy commit failure retains source and existing destination", CommitFailure);
            Run("Source cannot be modified or renamed during relocation", SourceLock);
            Run("Whole-file moves preserve existing filesystem streams", AlternateStreams);
            Run("Streams added after locking survive the native move", LateStreamRename);
            Run("Native rename never overwrites a late destination", NativeRenameCollision);
            Run("Copy and recovery retain streams added during copying", LateStreamRecovery);
            Run("Recovery collision preserves source and verified output", RecoveryCollision);
            Run("Cancellation after copying retains the original in place", CancelledAfterCopy);
            Run("Linked recovery folders cannot redirect originals", RecoveryJunction);
            Test("Overlapping roots are refused", Overlap);
            Run("Filesystem root aliases cannot bypass overlap checks", RootAliases);
            Run("Traversable ancestors need no directory-list permission", TraversableAncestor);
            Run("Held directories and ancestors cannot be renamed", DirectoryRenameBlocked);
            Run("Reparse conversion cannot redirect held-directory operations", RedirectedDirectoryHandles);
            Run("Quarantine move handles never allow a replacement handoff", RetainedHandleCannotBeReplaced);
            Run("Verified mover stops when its destination becomes a junction", RedirectedMoverDestination);
            Run("Directory junctions cannot enter scans or redirect transfers", Junctions);
            Test("Only one output session can write at a time", SessionLock);
            Run("Bookkeeping folders are excluded regardless of case", RecoveryCasing);
            Run("Countdown, cancellation, and fresh-choice timer", QueueCountdown);
            Run("STOP ALL cancels multiple pending choices and requires a new session", QueueStopAll);
            Run("Conflicting choices cannot queue both files for removal", QueueConflict);
            Run("Cancellation during verification stops quarantine", QueueCancelVerifying);
            Run("STOP ALL respects the native handoff boundary", QueueStopAtHandoff);
            Run("Changed or replaced reviewed files are retained", QuarantineChangedFiles);
            Run("Locked files and linked quarantine folders stop removal", QuarantineLocksAndLinks);
            Run("Journal failure retains both files and stops the queue", QuarantineJournalFailure);
            Run("Quarantine preserves identity, bytes, and alternate streams", RealQuarantine);
            Run("Keep both renames without losing bytes, streams, or name separation", KeepBothRename);
            Run("Keep both still detects exact copies on a future scan", KeepBothExactCopies);
            Run("Keep both refuses changed, replaced, locked, cancelled, or unjournaled files", KeepBothFailures);
            Test("WPF organization, duplicate review, cancel, stop, and close", UiWorkflow);
            Test("WPF date suggestions, single-press Space, manual override and cancellation", UiDateSuggestions);
            Test("WPF month and minute precision display and verified month routing", UiDatePrecision);
            Test("WPF keep-both rename, source resolution, shared zoom/pan, and right-side queue", UiReviewTools);
            Test("WPF real 60-second countdown quarantines only the selected image", UiTimedQuarantine);
            Console.WriteLine($"PASS: {passed} tests. Synthetic fixtures: {scratch}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine($"Fixtures retained at {scratch}");
            return 1;
        }
    }

    static void Test(string name, Action test) { test(); passed++; Console.WriteLine($"PASS {name}"); }
    static void Run(string name, Func<Task> test) => Test(name, () => test().GetAwaiter().GetResult());
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    static void DatePatterns()
    {
        var pixel = FilenameDates.Parse("PXL_20260412_143015123.MP.COVER.jpg")!;
        Check(pixel.Value == new DateTime(2026, 4, 12, 14, 30, 15, 123) && !pixel.NeedsReview, "Pixel timestamp failed.");
        Check(FilenameDates.Parse("PXL_20260401_003015123.jpg")!.NeedsReview, "Pixel month boundary needs review.");
        Check(FilenameDates.Parse("PXL_20261231_233015123.mp4")!.NeedsReview, "Pixel year boundary needs review.");
        Check(FilenameDates.Parse("20260412_143015.jpg")!.Value == new DateTime(2026, 4, 12, 14, 30, 15), "Samsung timestamp failed.");
        Check(FilenameDates.Parse("VID_20260412_143015.mp4")!.Value!.Value.Month == 4, "Android video timestamp failed.");
        Check(FilenameDates.Parse("IMG_20260412_143015.jpg") != null, "Older Pixel/Android name failed.");
        Check(FilenameDates.Parse("2026-04-12 14.30.15.heic") != null, "Timestamped export failed.");
        Check(FilenameDates.Parse("IMG_1234.HEIC") == null && FilenameDates.Parse("IMG_E1234.JPG") == null, "Apple counters are not dates.");
        Check(FilenameDates.Parse("20260230_143015.jpg") == null, "Invalid date accepted.");
        Check(FilenameDates.Parse("PXL_20260412_253015123.jpg") == null, "Invalid time accepted.");
        Check(FilenameDates.Resolve("20250412_143015.jpg", knownDate) == knownDate, "Metadata must win over filename.");
        Check(FilenameDates.Resolve("20250412_143015.jpg", new(new DateTime(2020, 1, 1), "Encoded", true)).Value!.Value.Year == 2025, "Unreliable encoding date must not beat filename.");
        Check(FilenameDates.CollisionStem("PXL_20260412_143015123.RAW-01.COVER.jpg") == FilenameDates.CollisionStem("PXL_20260412_143015123.RAW-02.ORIGINAL.dng"), "Pixel variants not grouped.");
        Check(FilenameDates.CollisionStem("IMG_E1234.jpg") == FilenameDates.CollisionStem("IMG_1234.HEIC"), "Apple edits not grouped.");
    }

    static void WindowInitialization()
    {
        var window = new MainWindow();
        var tabs = (TabControl)window.FindName("Workflows");
        Check(tabs.SelectedIndex == 0 && ((TabItem)tabs.Items[0]).Header.ToString() == "Auto-Organizer", "Wrong default tab.");
        Check(!((Button)window.FindName("MoveButton")).IsEnabled, "Moves must require scanning.");
        Check(tabs.Items.Count == 3 && ((TabItem)tabs.Items[1]).Header.ToString() == "Duplicate Removal" &&
            ((TabItem)tabs.Items[2]).Header.ToString() == "Developer Tools", "A workflow tab is missing.");
        var review = (DuplicateReview)window.FindName("RemovalReview");
        Check(!((Button)review.FindName("KeepOriginal")).IsEnabled && !((Button)review.FindName("KeepDuplicate")).IsEnabled, "Removal enabled before scan.");
        window.Close();
    }

    static void PhotoChecks()
    {
        string folder = Path.Combine(scratch, "photos");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "IMG_1234.jpg");
        byte[] pixels = new byte[80 * 40 * 3];
        for (int index = 0; index < pixels.Length; index += 3) { pixels[index] = 30; pixels[index + 1] = 70; pixels[index + 2] = 160; }
        BitmapSource bitmap = BitmapSource.Create(80, 40, 96, 96, PixelFormats.Bgr24, null, pixels, 80 * 3);
        var metadata = new BitmapMetadata("jpg") { DateTaken = "2026-04-12T14:15:16" };
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using (var file = File.Create(path)) encoder.Save(file);
        MediaDate capture = CaptureMetadata.Read(path, MediaKind.Photo);
        Check(capture.Value?.Month == 4 && !capture.NeedsReview, "Date taken metadata not read.");
        BitmapSource preview = PhotoPreview.Load(path);
        Check(preview.PixelWidth == 40 && preview.PixelHeight == 80, "EXIF rotation not applied.");
        Check(PhotoPreview.LooksSimilar(preview, preview), "Identical preview should appear similar.");
        Array.Fill(pixels, (byte)245);
        BitmapSource other = BitmapSource.Create(40, 80, 96, 96, PixelFormats.Bgr24, null, pixels, 40 * 3);
        Check(!PhotoPreview.LooksSimilar(preview, other), "Very different images labeled similar.");
    }

    static (string Input, string Output) Folders()
    {
        string folder = Path.Combine(scratch, Guid.NewGuid().ToString("N"));
        string input = Path.Combine(folder, "input"), output = Path.Combine(folder, "output");
        Directory.CreateDirectory(input); Directory.CreateDirectory(output);
        return (input, output);
    }
    static string Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }
    static Task<ScanResult> Scan((string Input, string Output) folders, MediaDate? date = null) =>
        new LibraryScanner((_, _) => date ?? knownDate).ScanAsync(folders.Input, folders.Output, quiet, CancellationToken.None);
    static Task<MediaDate?> NoDate(MediaFile file, CancellationToken token) => throw new InvalidOperationException("Unexpected date review.");
    static Task<RunResult> Organize(ScanResult scan, Func<MediaFile, CancellationToken, Task<MediaDate?>>? date = null, CancellationToken token = default) =>
        new Organizer().RunAsync(scan, date ?? NoDate, quiet, token);

    static async Task ExactDuplicates()
    {
        var folders = Folders();
        string first = Write(folders.Input, "a\\same.jpg", "exact-photo");
        string duplicate = Write(folders.Input, "b\\renamed.jpeg", "exact-photo");
        string video = Write(folders.Input, "nested\\clip.mov", "video-content");
        string sidecar = Write(folders.Input, "a\\same.aae", "sidecar");
        RunResult result = await Organize(await Scan(folders));
        Check(result.Moved == 3 && result.DuplicatesMoved == 1, "Wrong move or duplicate count.");
        Check(!File.Exists(first) && !File.Exists(duplicate) && File.Exists(sidecar), "Duplicate move/sidecar retention failed.");
        Check(File.ReadAllText(Path.Combine(folders.Output, "2026\\4\\renamed_DUPLICATE-1.jpeg")) == "exact-photo", "Renamed duplicate was not retained.");
        Check(File.ReadAllText(Path.Combine(folders.Output, "2026\\4\\same.jpg")) == "exact-photo", "Photo routing failed.");
        Check(File.ReadAllText(Path.Combine(folders.Output, "2026\\4\\Videos\\clip.mov")) == "video-content" && !File.Exists(video), "Video routing failed.");
        Check(File.ReadAllText(result.JournalPath).Contains("MovePlanned"), "Missing verified-move audit.");
    }
    static async Task SameSizeConflict()
    {
        var folders = Folders();
        string first = Write(folders.Input, "a\\same.jpg", "AAAA"), second = Write(folders.Input, "b\\SAME.JPG", "BBBB");
        RunResult result = await Organize(await Scan(folders));
        Check(result.Moved == 2 && result.DuplicatesMoved == 1 && !File.Exists(first) && !File.Exists(second), "Both versions must be organized.");
        Check(File.ReadAllText(Path.Combine(folders.Output, "2026\\4\\SAME_DUPLICATE-1.JPG")) == "BBBB", "Different content was lost.");
    }
    static async Task DifferentSizeConflict()
    {
        var folders = Folders();
        string small = Write(folders.Input, "a\\same.jpg", "SMALL"), large = Write(folders.Input, "b\\same.jpg", "LARGER-BUT-DIFFERENT");
        RunResult result = await Organize(await Scan(folders));
        Check(result.Moved == 2 && !File.Exists(small) && !File.Exists(large), "Both sizes must be retained in output.");
        Check(File.ReadAllText(Path.Combine(folders.Output, "2026\\4\\same.jpg")) == "SMALL" && File.ReadAllText(Path.Combine(folders.Output, "2026\\4\\same_DUPLICATE-1.jpg")) == "LARGER-BUT-DIFFERENT", "Size selected a winner.");
    }
    static async Task MultipleCandidates()
    {
        var folders = Folders();
        Write(folders.Input, "a\\same.jpg", "one"); Write(folders.Input, "b\\same.jpg", "two"); Write(folders.Input, "c\\same.jpg", "three");
        RunResult result = await Organize(await Scan(folders));
        Check(result.Moved == 3, "Not all versions were organized.");
        string folder = Path.Combine(folders.Output, "2026\\4");
        Check(File.ReadAllText(Path.Combine(folder, "same.jpg")) == "one" && File.ReadAllText(Path.Combine(folder, "same_DUPLICATE-1.jpg")) == "two" && File.ReadAllText(Path.Combine(folder, "same_DUPLICATE-2.jpg")) == "three", "A retained version was overwritten.");
    }
    static async Task ExistingExact()
    {
        var folders = Folders();
        string input = Write(folders.Input, "name.jpg", "same");
        string output = Write(folders.Output, "2026\\4\\other.jpg", "same");
        RunResult result = await Organize(await Scan(folders));
        Check(result.Moved == 1 && result.DuplicatesMoved == 1 && !File.Exists(input) && File.Exists(output) && File.ReadAllText(Path.Combine(folders.Output, "2026\\4\\name_DUPLICATE-1.jpg")) == "same", "Existing exact copy did not preserve both versions.");
    }
    static async Task ExistingDifferent()
    {
        var folders = Folders();
        Write(folders.Input, "name.jpg", "new contents");
        string output = Write(folders.Output, "2026\\4\\name.jpg", "old contents");
        RunResult result = await Organize(await Scan(folders));
        Check(result.Moved == 1 && File.ReadAllText(output) == "old contents" && File.ReadAllText(Path.Combine(folders.Output, "2026\\4\\name_DUPLICATE-1.jpg")) == "new contents", "Existing output changed.");
    }
    static async Task DateReview()
    {
        var folders = Folders();
        Write(folders.Input, "IMG_1234.jpg", "no date");
        int reviews = 0;
        RunResult result = await Organize(await Scan(folders, new(null, "Missing", true)), date: (_, _) =>
        {
            reviews++;
            return Task.FromResult<MediaDate?>(new(new DateTime(2021, 9, 3, 14, 15, 16), "User-confirmed: Modified on disk", Note: "Test date provenance"));
        });
        Check(reviews == 1 && File.Exists(Path.Combine(folders.Output, "2021\\9\\IMG_1234.jpg")), "Manual month ignored.");
        string journal = File.ReadAllText(result.JournalPath);
        Check(journal.Contains("2021-09-03T14:15:16") && journal.Contains("User-confirmed: Modified on disk") && journal.Contains("Test date provenance"),
            "Confirmed time and date source were not retained in the journal.");
    }
    static async Task SkipDate()
    {
        var folders = Folders();
        string path = Write(folders.Input, "IMG_1234.jpg", "no date");
        RunResult result = await Organize(await Scan(folders, new(null, "Missing", true)), date: (_, _) => Task.FromResult<MediaDate?>(null));
        Check(result.Moved == 0 && result.Unselected == 1 && File.Exists(path), "Skipped date moved its source.");
    }
    static async Task ChangedSource()
    {
        var folders = Folders();
        string path = Write(folders.Input, "image.jpg", "original");
        ScanResult scan = await Scan(folders);
        File.WriteAllText(path, "modified");
        File.SetLastWriteTimeUtc(path, scan.Files[0].LastWriteUtc);
        await Throws<IOException>(() => Organize(scan));
        Check(File.ReadAllText(path) == "modified", "Changed source was removed.");
    }
    static async Task LockedSource()
    {
        var folders = Folders();
        string path = Write(folders.Input, "image.jpg", "original");
        ScanResult scan = await Scan(folders);
        using var locked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        await Throws<IOException>(() => Organize(scan));
        Check(File.Exists(path), "Locked source removed.");
    }
    static async Task LateCollision()
    {
        var folders = Folders();
        string path = Write(folders.Input, "image.jpg", "input");
        ScanResult scan = await Scan(folders);
        string other = Write(folders.Output, "2026\\4\\image.jpg", "appeared later");
        await Throws<IOException>(() => Organize(scan));
        Check(File.Exists(path) && File.ReadAllText(other) == "appeared later", "Late output collision overwritten.");
    }
    static async Task CancelledReview()
    {
        var folders = Folders();
        string first = Write(folders.Input, "a\\same.jpg", "first");
        string second = Write(folders.Input, "b\\same.jpg", "second");
        using var cancellation = new CancellationTokenSource();
        ScanResult scan = await Scan(folders, new(null, "Missing", true));
        await Throws<OperationCanceledException>(() => Organize(scan, async (file, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return null;
        }, token: cancellation.Token));
        Check(File.Exists(first) && File.Exists(second), "Cancelled review moved a variant.");
    }

    static async Task<(ScanResult Scan, string Source)> LargeSource((string Input, string Output) folders)
    {
        string path = Path.Combine(folders.Input, "large.mp4");
        using (var stream = File.Create(path)) stream.SetLength(64 * 1024 * 1024);
        return (await Scan(folders), path);
    }
    static async Task CancelledCopy()
    {
        var folders = Folders();
        var data = await LargeSource(folders);
        using var cancellation = new CancellationTokenSource();
        using var watcher = new FileSystemWatcher(folders.Output, "*.partial") { IncludeSubdirectories = true, EnableRaisingEvents = true };
        watcher.Created += (_, _) => cancellation.Cancel();
        await Throws<OperationCanceledException>(() => CopyAndRetain(data.Scan, cancellation.Token));
        Check(File.Exists(data.Source) && new FileInfo(data.Source).Length == 64 * 1024 * 1024, "Cancelled staged copy removed source.");
    }
    static async Task CommitFailure()
    {
        var folders = Folders();
        var data = await LargeSource(folders);
        string destination = Path.Combine(folders.Output, "2026\\4\\Videos\\large.mp4");
        using var watcher = new FileSystemWatcher(folders.Output, "*.partial") { IncludeSubdirectories = true, EnableRaisingEvents = true };
        watcher.Created += (_, _) => File.WriteAllText(destination, "external file");
        await Throws<IOException>(() => CopyAndRetain(data.Scan));
        Check(File.Exists(data.Source) && File.ReadAllText(destination) == "external file", "Commit failure lost an original.");
        Check(Directory.GetFiles(folders.Output, "*.partial", SearchOption.AllDirectories).Length == 1, "Failed staging file should be retained.");
    }
    static async Task SourceLock()
    {
        var folders = Folders();
        var data = await LargeSource(folders);
        var checkedLocks = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(folders.Output, "*.partial") { IncludeSubdirectories = true, EnableRaisingEvents = true };
        watcher.Created += (_, _) =>
        {
            bool writeBlocked = false, renameBlocked = false;
            try { using var edit = File.Open(data.Source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }
            catch (IOException) { writeBlocked = true; }
            try { File.Move(data.Source, data.Source + ".renamed"); }
            catch (IOException) { renameBlocked = true; }
            checkedLocks.TrySetResult(writeBlocked && renameBlocked);
        };
        MoveResult result = await CopyAndRetain(data.Scan);
        Check(await checkedLocks.Task.WaitAsync(TimeSpan.FromSeconds(5)) && File.Exists(result.RetainedOriginal), "Source was not locked throughout verification.");
    }
    static async Task AlternateStreams()
    {
        var folders = Folders();
        string path = Write(folders.Input, "image.jpg", "main contents");
        File.WriteAllText(path + ":extra", "must not lose this");
        RunResult result = await Organize(await Scan(folders));
        string destination = Path.Combine(folders.Output, "2026\\4\\image.jpg");
        Check(result.Moved == 1 && result.RetainedOriginals == 0 && !File.Exists(path), "Whole-file move did not finish.");
        Check(File.ReadAllText(destination + ":extra") == "must not lose this", "Alternate stream was lost.");
    }
    static void Overlap()
    {
        var folders = Folders();
        string nested = Path.Combine(folders.Input, "nested"); Directory.CreateDirectory(nested);
        foreach (var pair in new[] { (folders.Input, folders.Input), (folders.Input, nested), (nested, folders.Input) })
        {
            bool rejected = false;
            try { PathSafety.ValidateRoots(pair.Item1, pair.Item2); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Overlapping roots accepted.");
        }
    }
    static void SessionLock()
    {
        var folders = Folders();
        using var journal = new TransferJournal(folders.Output);
        bool rejected = false;
        try { using var other = new TransferJournal(folders.Output); } catch (IOException) { rejected = true; }
        Check(rejected, "Concurrent output sessions accepted.");
    }
    sealed class ImmediateProgress(Action<string> action) : IProgress<string>
    {
        public void Report(string value) => action(value);
    }
}
