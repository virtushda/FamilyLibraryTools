using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FamilyLibraryTools;
using FamilyLibraryTools.Core;
using FamilyLibraryTools.Media;
using FamilyLibraryTools.Views;

static partial class Program
{
    static void BenchmarkPassRules()
    {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Gray8, null, new byte[1], 1);
        image.Freeze();
        BenchmarkReading Reading(string text, OcrMethod method, double score = 99, bool uncertain = false, bool failed = false) =>
            new(method, 10, new(VisibleDates.Parse(text).Select(date => new ImageDateCandidate(date, image, score)).ToArray(), uncertain, "Test", text, failed));
        foreach (var method in Enum.GetValues<OcrMethod>())
        {
            foreach (string text in new[] { "2020-12-25 14:32", "2020-12-25 00:00", "December 25, 2020 12:00 AM", "2020-12-25 14:32:01", "December 25, 2020 2:32 P M" })
                Check(Reading(text, method).Passed, "Explicit valid date/time did not pass: " + text);
            foreach (string text in new[] { "2020-12-25", "December 25, 2020", "14:32", "2020-12-25 25:00", "2020-12-25 14:99", "2020-12-25 14:32:99", "04/05/2020 14:32", "2020-12-25 14:32-2021-01-02 09:15",
                "December 25, 2020 09:00 - 17:00", "December 25, 2020 14:32 : O9", "December 25, 2020 14:32 + 15:00" })
                Check(!Reading(text, method).Passed, "Incomplete, invalid or ambiguous time passed: " + text);
            Check(!Reading("2020-12-25 14:32", method, 50).Passed && !Reading("2020-12-25 14:32", method, double.NaN).Passed,
                "Unconfident date/time passed.");
            Check(!Reading("2020-12-25 14:32", method, uncertain: true).Passed && !Reading("2020-12-25 14:32", method, failed: true).Passed,
                "An error or uncertain OCR run passed.");
        }
        var run = new BenchmarkRun();
        var photo = new BenchmarkPhoto(new("test.png", MediaKind.Photo, 0, "", default, new(null, "Test"), false));
        var reading = Reading("2020-12-25 14:32", OcrMethod.PpOcrV6Small);
        photo.Readings.Add(reading); run.Photos.Add(photo);
        Check(run.Summaries()[0].Passed == 1 && run.Summaries()[0].Reviewed == 0, "Automatic pass masqueraded as human correctness.");
        reading.Verdict = BenchmarkVerdict.Incorrect;
        Check(run.Summaries()[0].Passed == 1 && run.Summaries()[0].Incorrect == 1, "Incorrect review changed the measured pass count.");
    }

    static void TwoOcrModels()
    {
        var folders = Folders();
        string path = WriteScreenshot(folders.Input, "date.png", "December 25, 2020 2:32 PM");
        byte[] before = File.ReadAllBytes(path);
        var bitmap = PhotoPreview.Load(path);
        using var session = new OcrSession();
        foreach (var method in Enum.GetValues<OcrMethod>())
        {
            var result = session.Read(bitmap, method, default);
            Check(!result.Failed, OcrSession.Name(method) + ": " + result.Status);
            Check(result.Candidates.Any(candidate => candidate.Date.Value == new DateTime(2020, 12, 25, 14, 32, 0)),
                OcrSession.Name(method) + " lost the real screenshot timestamp: " + result.RecognizedText);
            Check(new BenchmarkReading(method, 0, result).Passed, OcrSession.Name(method) + " did not pass the clear printed timestamp: " + result.Status);
            Check(result.Candidates.All(candidate => candidate.Date.NeedsReview && candidate.Evidence.IsFrozen), "OCR removed explicit confirmation or returned live pixels.");
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            try { session.Read(bitmap, method, cancel.Token); throw new InvalidOperationException("Cancelled OCR ran."); }
            catch (OperationCanceledException) { }
        }
        string dateOnly = WriteScreenshot(folders.Input, "date-only.png", "December 25, 2020");
        foreach (var method in Enum.GetValues<OcrMethod>())
        {
            var result = session.Read(PhotoPreview.Load(dateOnly), method, default);
            Check(!result.Failed && result.Candidates.Count > 0 && !new BenchmarkReading(method, 0, result).Passed,
                "Real date-only recognition invented time: " + OcrSession.Name(method) + " " + result.RecognizedText);
        }
        string multiple = WriteScreenshot(folders.Input, "multiple.png", "December 25, 2020 2:32 PM", "January 2, 2021 9:15 AM");
        var multipleImage = PhotoPreview.Load(multiple);
        var region = new CroppedBitmap(multipleImage, new Int32Rect(20, 40, 1150, 85)); region.Freeze();
        foreach (var method in new[] { OcrMethod.PpOcrV6Small, OcrMethod.PpOcrV6Medium })
        {
            var full = session.Read(multipleImage, method, default);
            Check(!full.Failed && full.Candidates.Count == 2 && full.RequiresChoice && !new BenchmarkReading(method, 0, full).Passed,
                OcrSession.Name(method) + " hid a competing visible date: " + full.RecognizedText);
            var cropped = session.Read(region, method, default);
            Check(new BenchmarkReading(method, 0, cropped).Passed && cropped.Candidates[0].Date.Value == new DateTime(2020, 12, 25, 14, 32, 0),
                OcrSession.Name(method) + " failed the selected region: " + cropped.RecognizedText);
        }
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(before), "OCR changed source bytes.");
    }

    static async Task BenchmarkFolder()
    {
        var folders = Folders();
        string first = WriteScreenshot(folders.Input, "a.png", "December 25, 2020 2:32 PM");
        string nested = Path.Combine(folders.Input, "nested"); Directory.CreateDirectory(nested);
        WriteScreenshot(nested, "date-only.png", "December 25, 2020");
        Write(folders.Input, "broken.png", "broken image");
        Write(folders.Input, "video.mp4", "ignored video");
        var before = Directory.GetFiles(folders.Input, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var run = await Task.Run(() => OcrBenchmark.RunAsync(folders.Input, quiet, default));
        Check(!run.Cancelled && run.PhotoCount == 3 && run.Photos.Count == 3 && run.SkippedFiles == 1, "Recursive benchmark counts are wrong.");
        foreach (var summary in run.Summaries())
            Check(summary.Completed == 2 && summary.Passed == 1 && summary.DateOnly == 1 && summary.Errors == 0, "Bad benchmark totals: " + summary);
        Check(run.Issues.Count == 1 && run.Photos.Count(photo => photo.Error != null) == 1, "Decode error disappeared from results.");
        foreach (var file in before) Check(File.ReadAllBytes(file.Key).AsSpan().SequenceEqual(file.Value), "Benchmark modified " + file.Key);
        Check(Directory.GetFiles(folders.Input, "*", SearchOption.AllDirectories).Length == before.Count, "Benchmark wrote into input.");
        using var cancel = new CancellationTokenSource();
        int started = 0;
        var partial = await Task.Run(() => OcrBenchmark.RunAsync(folders.Input, new ImmediateProgress(message =>
        {
            if (message.StartsWith("Photo ") && ++started == 2) cancel.Cancel();
        }), cancel.Token));
        Check(partial.Cancelled && partial.Photos.Sum(photo => photo.Readings.Count) == 1 && partial.Summaries().Sum(summary => summary.Completed) == 1,
            "Cancellation counted unrun methods or lost completed work.");
        var original = run.Photos.Single(photo => photo.File.Path == first).File;
        File.WriteAllText(first, "changed after benchmark");
        try { await OcrBenchmark.LoadPhotoAsync(original, default); throw new InvalidOperationException("Changed source allowed a misleading review."); }
        catch (IOException) { }
    }

    static void UiBenchmark()
    {
        var folders = Folders();
        WriteScreenshot(folders.Input, "a.png", "December 25, 2020 2:32 PM");
        WriteScreenshot(folders.Input, "b.png", "December 25, 2020");
        var window = new MainWindow();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        using var source = new HwndSource(new HwndSourceParameters("Hidden OCR benchmark input verification")
        { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
        var root = (FrameworkElement)window.Content;
        source.RootVisual = root;
        var developer = (TabItem)window.FindName("DeveloperTab");
        Check(developer.Visibility == Visibility.Collapsed, "Developer Tools is still exposed.");
        developer.Visibility = Visibility.Visible; // The hidden benchmark remains available for regression coverage.
        var tabs = (TabControl)window.FindName("Workflows"); tabs.SelectedIndex = 2;
        var view = (OcrBenchmarkView)window.FindName("BenchmarkView");
        var run = (Button)view.FindName("RunButton");
        var panel = (ItemsControl)view.FindName("ResultsPanel");
        var list = (ComboBox)view.FindName("PhotoList");
        ((TextBox)view.FindName("InputPath")).Text = folders.Input;
        try
        {
            Click(run);
            Check(view.HasSession && !((TabItem)window.FindName("OrganizeTab")).IsEnabled && !((TabItem)window.FindName("RemovalTab")).IsEnabled,
                "Benchmark allowed concurrent organization or quarantine.");
            PumpUntil(() => !view.HasSession && panel.IsEnabled, 90);
            Check(panel.Items.Count == 2 && list.Items.Count == 2, "Two-model review is incomplete.");
            root.Measure(new Size(1380, 920)); root.Arrange(new Rect(0, 0, 1380, 920)); root.UpdateLayout();
            var correct = FindButton(panel, "Correct")!;
            Check(correct != null, "Correctness review button is missing.");
            var first = (BenchmarkReading)correct!.DataContext;
            ButtonKey(source, correct, Key.Enter);
            Check(first.Verdict == BenchmarkVerdict.Correct, "Review rating was not applied.");
            ButtonMouse(correct);
            Click((Button)view.FindName("Next")); PumpUntil(() => panel.IsEnabled);
            root.UpdateLayout();
            var nextCorrect = FindButton(panel, "Correct")!;
            var nextReading = (BenchmarkReading)nextCorrect.DataContext;
            Check(ButtonMouse(nextCorrect, up: true).Handled && ButtonKey(source, nextCorrect, Key.Enter).Handled &&
                nextReading.Verdict == BenchmarkVerdict.Unreviewed, "Held gesture rated a replacement photo.");
            ButtonKey(source, nextCorrect, Key.Enter, up: true);
            Click(nextCorrect);
            Check(nextReading.Verdict == BenchmarkVerdict.Correct && !nextReading.Passed, "Review rating turned a date-only result into a benchmark pass.");
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Render(window, "developer-tools.png", 1380, 920);
            Render(window, "developer-tools-minimum.png", 1000, 800);
            Click(run); Click((Button)view.FindName("StopButton")); PumpUntil(() => !view.HasSession);
            Check(((TextBlock)view.FindName("Status")).Text.StartsWith("Stopped; partial results"), "Cancelled benchmark reported completion.");
            // Exercise a new model in the organizer, including held Enter after its async result replaces the UI.
            tabs.SelectedIndex = 0;
            ((TextBox)window.FindName("InputPath")).Text = folders.Input;
            ((TextBox)window.FindName("OutputPath")).Text = folders.Output;
            var scan = (Button)window.FindName("ScanButton"); var move = (Button)window.FindName("MoveButton");
            var actions = (StackPanel)window.FindName("DateActions");
            Click(scan); PumpUntil(() => move.IsEnabled); Click(move); PumpUntil(() => DateReady(window), 60);
            ButtonKey(source, (Button)window.FindName("RetryOcr"), Key.Enter);
            PumpUntil(() => DateReady(window), 60);
            Check(((TextBox)window.FindName("RecognizedText")).Text.Contains("PP-OCRv6 Medium"), "Organizer did not use the Medium retry.");
            Check(ButtonKey(source, (Button)window.FindName("AcceptSuggestedDate"), Key.Enter).Handled && Directory.GetFiles(folders.Output, "*.png", SearchOption.AllDirectories).Length == 0,
                "Held Enter accepted a new model result.");
            ButtonKey(source, (Button)window.FindName("AcceptSuggestedDate"), Key.Enter, up: true);
            Click((Button)window.FindName("CancelButton")); PumpUntil(() => scan.IsEnabled);
        }
        finally
        {
            if (view.HasSession) { Click((Button)view.FindName("StopButton")); PumpUntil(() => !view.HasSession, 60); }
            var cancel = (Button)window.FindName("CancelButton");
            if (cancel.IsEnabled) { Click(cancel); PumpUntil(() => !cancel.IsEnabled, 60); }
            window.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
}
