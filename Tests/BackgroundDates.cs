using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using FamilyLibraryTools;
using FamilyLibraryTools.Core;
using FamilyLibraryTools.Media;

static partial class Program
{
    static async Task PreparationCancellation()
    {
        var folders = Folders();
        string path = WriteScreenshot(folders.Input, "waiting.png", "December 25, 2020 2:32 PM");
        byte[] before = File.ReadAllBytes(path);
        var scan = await Scan(folders, new(null, "Unknown", true));
        var admission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var engine = new OcrSession();
        await using var prepared = new DatePreparation(scan.Files[0], engine, true, default, admission.Task);
        Check(!prepared.Hints.IsCompleted && !prepared.Photo.IsCompleted && !prepared.Small.IsCompleted && !prepared.Started.IsCompleted,
            "A waiting preparation read its source before admission.");
        prepared.Cancel();
        await prepared.DisposeAsync();
        Check(prepared.Hints.IsCanceled && prepared.Photo.IsCanceled && prepared.Small.IsCanceled && prepared.Started.IsCompleted,
            "Cancellation left a waiting preparation or admission barrier live.");
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        using var bytes = new MemoryStream();
        exclusive.CopyTo(bytes);
        Check(bytes.ToArray().AsSpan().SequenceEqual(before), "Cancelling waiting preparation changed its source or leaked a read handle.");
    }

    static async Task ParallelDatePreparation()
    {
        var folders = Folders();
        Directory.CreateDirectory(Path.Combine(folders.Input, "z"));
        Directory.CreateDirectory(Path.Combine(folders.Input, "a"));
        string firstPath = WriteScreenshot(folders.Input, "z\\same.png", "December 25, 2020 2:32 PM");
        string secondPath = WriteScreenshot(folders.Input, "a\\same.png", "January 2, 2021 9:15 AM");
        string thirdPath = WriteScreenshot(folders.Input, "next.png", "November 24, 2019 10:16 AM");
        var scan = await Scan(folders, new(null, "Unknown", true));
        scan = scan with { Files = scan.Files.OrderBy(file => file.Path == firstPath ? 0 : file.Path == secondPath ? 1 : 2).ToArray() };
        var ordered = CollisionGroups.Build(scan.Files).SelectMany(group => group).Where(file => !file.InOutput).ToArray();
        Check(ordered[0].Path == secondPath && ordered[1].Path == firstPath, "Fixture did not exercise collision-group order.");
        await using var reviews = new DateReviewSession(scan, true, default);
        var engines = (OcrSession[])typeof(DateReviewSession).GetField("engines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reviews)!;
        var gate = (SemaphoreSlim)typeof(OcrSession).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engines[0])!;
        Check(engines.Length == 2 && engines[0] != engines[1], "Review workers share a native session.");
        gate.Wait(); // Hold the current worker: the next worker must still make progress independently.
        DatePreparation first;
        DatePreparation next;
        try
        {
            first = reviews.Begin(ordered[0]);
            next = (DatePreparation)typeof(DateReviewSession).GetField("next", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reviews)!;
            var result = await next.Small.WaitAsync(TimeSpan.FromSeconds(30));
            Check(first.Started.IsCompleted && !first.Small.IsCompleted && result?.Candidates[0].Date.Value == new DateTime(2020, 12, 25, 14, 32, 0),
                "The next image did not run while the admitted current worker was occupied.");
            first.Cancel();
            await first.DisposeAsync();
            Check(first.Small.IsCanceled && !next.Token.IsCancellationRequested, "Choosing the current file cancelled its ready successor.");
        }
        finally { gate.Release(); }
        var promoted = reviews.Begin(ordered[1]);
        Check(ReferenceEquals(next, promoted) && promoted.Small.IsCompletedSuccessfully && promoted.Photo.IsCompletedSuccessfully,
            "Promoting the prepared image discarded its cached read-only result.");
        promoted.Cancel();
        await promoted.DisposeAsync();
        var third = reviews.Begin(ordered[2]);
        var last = await third.Small.WaitAsync(TimeSpan.FromSeconds(30));
        Check(third.File.Path == thirdPath && last?.Candidates[0].Date.Value == new DateTime(2019, 11, 24, 10, 16, 0),
            "Reusing a worker mixed results from different images.");
        foreach (var file in scan.Files)
        {
            using var stream = FileContents.OpenRead(file.Path);
            await FileContents.ValidateAsync(stream, file, default);
        }
        Check(Directory.GetFiles(folders.Output, "*", SearchOption.AllDirectories).Length == 0, "Read-only preparation wrote to output.");
    }

    static async Task ChangedPreparedSource()
    {
        var folders = Folders();
        string path = WriteScreenshot(folders.Input, "cached.png", "December 25, 2020 2:32 PM");
        var scan = await Scan(folders, new(null, "Unknown", true));
        await using var reviews = new DateReviewSession(scan, true, default);
        var prepared = reviews.Begin(scan.Files[0]);
        await prepared.Small;
        prepared.Cancel();
        await prepared.DisposeAsync();
        File.WriteAllText(path, "Changed after read-only preparation");
        await Throws<IOException>(() => Organize(scan, (_, _) => Task.FromResult<MediaDate?>(knownDate)));
        Check(File.ReadAllText(path) == "Changed after read-only preparation" && Directory.GetFiles(folders.Output, "*.png", SearchOption.AllDirectories).Length == 0,
            "Cached pixels bypassed source verification during the later move.");
    }

    static void UiBackgroundDates()
    {
        for (int phase = 0; phase < 3; phase++)
        {
            var folders = Folders();
            string first = WriteScreenshot(folders.Input, "a.png", "December 25, 2020 2:32 PM");
            string second = WriteScreenshot(folders.Input, "b.png", "January 2, 2021 9:15 AM");
            byte[] original = File.ReadAllBytes(first);
            var window = new MainWindow();
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            using var source = new HwndSource(new HwndSourceParameters("Hidden background date input verification")
            { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = (System.Windows.Media.Visual)window.Content;
            var scan = (Button)window.FindName("ScanButton");
            var move = (Button)window.FindName("MoveButton");
            var enabled = (CheckBox)window.FindName("OcrEnabled");
            var actions = (StackPanel)window.FindName("DateActions");
            var progress = (ProgressBar)window.FindName("ImageDateProgress");
            var confirm = (Button)window.FindName("ConfirmDate");
            try
            {
                Check(enabled.IsChecked == true, "OCR is not enabled by default.");
                if (phase == 1) enabled.IsChecked = false;
                ((TextBox)window.FindName("InputPath")).Text = folders.Input;
                ((TextBox)window.FindName("OutputPath")).Text = folders.Output;
                Click(scan); PumpUntil(() => move.IsEnabled); Click(move);
                PumpUntil(() => actions.IsEnabled);
                Check(!enabled.IsEnabled, "OCR setting can change during a run.");
                if (phase != 1)
                {
                    Check(progress.Visibility == Visibility.Visible && progress.IsIndeterminate && !((Button)window.FindName("AcceptSuggestedDate")).IsEnabled,
                        "Processing was not shown or an unstable suggestion became actionable.");
                    ButtonKey(source, (Button)window.FindName("AcceptSuggestedDate"), Key.Space);
                    ButtonKey(source, (Button)window.FindName("AcceptSuggestedDate"), Key.Space, up: true);
                    Check(File.Exists(first), "Space accepted an image while OCR was pending.");
                    Render(window, "ocr-processing.png", 1000, 800);
                    if (phase == 2)
                    {
                        bool closed = false;
                        window.Closed += (_, _) => closed = true;
                        window.Close();
                        PumpUntil(() => closed, 60);
                        using var exclusive = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.None);
                        Check(File.Exists(second) && exclusive.Length == original.Length && Directory.GetFiles(folders.Output, "*.png", SearchOption.AllDirectories).Length == 0,
                            "Close while current/next images were processing moved a file or left a read handle open.");
                        continue;
                    }
                }
                else
                {
                    PumpUntil(() => DateReady(window));
                    Check(progress.Visibility == Visibility.Collapsed && ((StackPanel)window.FindName("ImageDatesPanel")).Visibility == Visibility.Collapsed &&
                        ((TextBlock)window.FindName("SuggestedDateText")).Text.Contains("Modified on disk"), "Disabled OCR still ran or removed date hints.");
                    var reviews = (DateReviewSession)typeof(MainWindow).GetField("dateReviews", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var engines = (OcrSession[])typeof(DateReviewSession).GetField("engines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reviews)!;
                    foreach (var engine in engines)
                        Check(typeof(OcrSession).GetField("small", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine) == null &&
                            typeof(OcrSession).GetField("medium", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine) == null,
                            "Disabled OCR initialized a native model.");
                }
                ((DatePicker)window.FindName("CaptureDatePicker")).SelectedDate = new DateTime(2018, 8, 3);
                ButtonKey(source, confirm, Key.Enter);
                PumpUntil(() => DateReady(window), 60);
                Check(File.ReadAllBytes(Path.Combine(folders.Output, "2018", "8", "a.png")).AsSpan().SequenceEqual(original) && File.Exists(second),
                    "An in-flight OCR result overrode the explicit manual choice.");
                Check(ButtonKey(source, (Button)window.FindName("AcceptSuggestedDate"), Key.Enter).Handled && File.Exists(second),
                    "Held Enter accepted a replacement image after an early manual choice.");
                ButtonKey(source, confirm, Key.Enter, up: true);
                if (phase == 0)
                    Check(((TextBlock)window.FindName("SuggestedDateText")).Text.Contains("2021/1") && !((TextBox)window.FindName("RecognizedText")).Text.Contains("December"),
                        "The next image used an abandoned current-image result.");
                Click((Button)window.FindName("CancelButton")); PumpUntil(() => scan.IsEnabled, 60);
                Check(enabled.IsEnabled && !actions.IsEnabled && progress.Visibility == Visibility.Collapsed && File.Exists(second),
                    "Cancellation left processing or confirmation active.");
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }
}
