using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FamilyLibraryTools;
using FamilyLibraryTools.Views;

static partial class Program
{
    static void UiWorkflow()
    {
        var folders = Folders();
        string first = MakePhoto(folders.Input, "a\\IMG_1234.png", 20);
        string second = MakePhoto(folders.Input, "b\\IMG_1234.png", 200);
        var window = new MainWindow();
        var input = (TextBox)window.FindName("InputPath");
        var output = (TextBox)window.FindName("OutputPath");
        var scanButton = (Button)window.FindName("ScanButton");
        var moveButton = (Button)window.FindName("MoveButton");
        var datePanel = (Grid)window.FindName("DatePanel");
        var picker = (DatePicker)window.FindName("CaptureDatePicker");
        var review = (DuplicateReview)window.FindName("RemovalReview");
        var keepLeft = (Button)review.FindName("KeepOriginal");
        var keepRight = (Button)review.FindName("KeepDuplicate");
        var queueList = (ListBox)review.FindName("QueueList");
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            input.Text = folders.Input;
            output.Text = folders.Output;
            Render(window, "window-ready.png");
            Click(scanButton);
            PumpUntil(() => moveButton.IsEnabled);
            Check(File.Exists(first) && File.Exists(second), "Scanning changed files.");
            Click(moveButton);
            for (int index = 0; index < 2; index++)
            {
                PumpUntil(() => datePanel.Visibility == Visibility.Visible && picker.SelectedDate == null && ((StackPanel)window.FindName("DateActions")).IsEnabled);
                picker.SelectedDate = new DateTime(2026, 4, 12);
                Render(window, "window-date-review.png");
                Click((Button)window.FindName("ConfirmDate"));
            }
            PumpUntil(() => scanButton.IsEnabled);
            string original = Path.Combine(folders.Output, "2026\\4\\IMG_1234.png");
            string duplicate = Path.Combine(folders.Output, "2026\\4\\IMG_1234_DUPLICATE-1.png");
            Check(File.Exists(original) && File.Exists(duplicate) && !File.Exists(first) && !File.Exists(second), "UI did not organize both versions.");
            Check(!moveButton.IsEnabled, "Completed scan was reusable.");
            ((TabControl)window.FindName("Workflows")).SelectedIndex = 1;
            Click((Button)review.FindName("ScanButton"));
            PumpUntil(() => keepLeft.IsEnabled && keepRight.IsEnabled);
            Check(queueList.Items.Count == 0, "Scanning auto-selected a deletion.");
            Render(window, "window-duplicate-review.png");
            Click(keepLeft);
            Check(queueList.Items.Count == 1 && File.Exists(original) && File.Exists(duplicate), "Choice moved a file before its countdown.");
            var row = (DeletionRow)queueList.Items[0];
            Check(row.Entry.Keep.Path == original && row.Entry.Reject.Path == duplicate, "Keep LEFT selected the wrong file.");
            Render(window, "window-deletion-queue.png");
            Click(FindButton(queueList, "CANCEL DELETION")!);
            PumpUntil(() => keepRight.IsEnabled);
            Check(row.Entry.State == FamilyLibraryTools.Core.DeletionState.Cancelled && File.Exists(duplicate), "Cancel did not withdraw the deletion.");
            Click(keepRight);
            var reversed = (DeletionRow)queueList.Items[1];
            Check(reversed.Entry.Keep.Path == duplicate && reversed.Entry.Reject.Path == original, "Keep RIGHT selected the wrong file.");
            Click((Button)review.FindName("StopButton"));
            PumpUntil(() => !review.HasSession);
            Check(reversed.Entry.State == FamilyLibraryTools.Core.DeletionState.Cancelled && File.Exists(original) && File.Exists(duplicate), "STOP ALL did not clear the pending deletion.");
            Click((Button)review.FindName("ScanButton"));
            PumpUntil(() => keepLeft.IsEnabled);
            Check(queueList.Items.Count == 0, "Rescan resumed old choices.");
            Click(keepLeft);
            var closing = (DeletionRow)queueList.Items[0];
            window.Close();
            PumpUntil(() => !review.HasSession);
            Check(closing.Entry.State == FamilyLibraryTools.Core.DeletionState.Cancelled && File.Exists(duplicate), "Closing kept a deletion alive.");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static void UiTimedQuarantine()
    {
        var folders = Folders();
        string keep = MakePhoto(folders.Output, "2026\\4\\timed.png", 25);
        string reject = MakePhoto(folders.Output, "2026\\4\\timed_DUPLICATE-1.png", 150);
        string unrelated = MakePhoto(folders.Output, "2026\\4\\unrelated.png", 240);
        byte[] keptBytes = File.ReadAllBytes(keep), rejectedBytes = File.ReadAllBytes(reject), otherBytes = File.ReadAllBytes(unrelated);
        var window = new MainWindow();
        var review = (DuplicateReview)window.FindName("RemovalReview");
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            ((TabControl)window.FindName("Workflows")).SelectedIndex = 1;
            ((TextBox)review.FindName("LibraryPath")).Text = folders.Output;
            Click((Button)review.FindName("ScanButton"));
            var keepLeft = (Button)review.FindName("KeepOriginal");
            PumpUntil(() => keepLeft.IsEnabled);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            Click(keepLeft);
            var row = (DeletionRow)((ListBox)review.FindName("QueueList")).Items[0];
            Check(row.Entry.Keep.Path == keep && row.Entry.Reject.Path == reject, "Timed UI choice reversed.");
            PumpUntil(() => !row.Entry.IsActive, 75);
            Check(elapsed.Elapsed >= TimeSpan.FromSeconds(60), "UI quarantined before 60 seconds elapsed.");
            Check(row.Entry.State == FamilyLibraryTools.Core.DeletionState.Quarantined && !File.Exists(reject), "Timed quarantine did not complete: " + row.Entry.Error);
            Check(File.ReadAllBytes(row.Entry.Destination).AsSpan().SequenceEqual(rejectedBytes), "Quarantine did not retain the selected image.");
            Check(File.ReadAllBytes(keep).AsSpan().SequenceEqual(keptBytes) && File.ReadAllBytes(unrelated).AsSpan().SequenceEqual(otherBytes), "Timed quarantine changed an unselected image.");
            PumpUntil(() => ((TextBlock)review.FindName("ReviewStatus")).Text.StartsWith("Quarantined ") && row.Status.Contains(row.Entry.Destination));
            Check(((TextBlock)review.FindName("ReviewStatus")).Text.Contains("recoverable") && row.Status.Contains(row.Entry.Destination), "UI did not notify the user or show a recovery location.");
            Render(window, "window-quarantined.png");
            Click((Button)review.FindName("StopButton"));
            PumpUntil(() => !review.HasSession);
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static void UiReviewTools()
    {
        var folders = Folders();
        string original = MakePhoto(folders.Output, "2026\\4\\scene.png", 30, 2400, 1600);
        string duplicate = MakePhoto(folders.Output, "2026\\4\\scene_DUPLICATE-1.png", 180, 1200, 800);
        byte[] leftBytes = File.ReadAllBytes(original), rightBytes = File.ReadAllBytes(duplicate);
        var window = new MainWindow();
        var review = (DuplicateReview)window.FindName("RemovalReview");
        var left = (MediaPreview)review.FindName("OriginalPreview");
        var right = (MediaPreview)review.FindName("DuplicatePreview");
        var zoom = (Slider)review.FindName("Zoom");
        var both = (Button)review.FindName("KeepBoth");
        var queue = (ListBox)review.FindName("QueueList");
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            ((TabControl)window.FindName("Workflows")).SelectedIndex = 1;
            ((TextBox)review.FindName("LibraryPath")).Text = folders.Output;
            Render(window, "review-tools-ready.png");
            Click((Button)review.FindName("ScanButton"));
            PumpUntil(() => both.IsEnabled);
            Check(((TextBlock)left.FindName("ResolutionText")).Text == "Resolution: 2400 × 1600 px" &&
                ((TextBlock)right.FindName("ResolutionText")).Text == "Resolution: 1200 × 800 px", "Resolution showed thumbnail dimensions.");
            Check(left.LoadedPhoto!.PixelWidth == 2400, "Zoom preview discarded source pixels.");
            Render(window, "review-tools-fit.png");
            var content = (UIElement)window.Content;
            Check(queue.TranslatePoint(new Point(), content).X > right.TranslatePoint(new Point(), content).X + right.ActualWidth &&
                queue.ActualHeight > left.ActualHeight * .8, "Queue is not alongside the image area.");
            Render(window, "review-tools-compact.png", 960, 750);
            Check(((Grid)left.FindName("Viewport")).ActualHeight >= 100 && both.TranslatePoint(new Point(), content).Y + both.ActualHeight < 750,
                "The compact layout clipped the preview or keep controls.");
            Render(window, "review-tools-fit.png");
            zoom.Value = 3;
            Check(((ScaleTransform)left.FindName("ViewScale")).ScaleX == 3 && ((ScaleTransform)right.FindName("ViewScale")).ScaleX == 3,
                "Zoom was not applied equally.");
            left.PanBy(new Vector(-70, 35));
            Check(left.Pan == right.Pan && left.Pan != new Point() && ((TranslateTransform)right.FindName("ViewOffset")).X < 0,
                "Panning the left preview did not move both.");
            right.PanBy(new Vector(100000, -100000));
            Check(left.Pan == right.Pan && right.Pan == new Point(1, -1), "Panning the right preview did not synchronize or clamp at the edges.");
            Render(window, "review-tools-zoomed.png");
            Click(FindButton(review, "Fit / reset")!);
            Check(zoom.Value == 1 && left.Pan == new Point() && right.Pan == new Point(), "Fit/reset did not reset both views.");
            Click(both);
            string renamed = Path.Combine(folders.Output, "2026\\4\\scene-1.png");
            PumpUntil(() => ((TextBlock)review.FindName("ReviewStatus")).Text.StartsWith("Kept both."));
            Check(!File.Exists(duplicate) && File.ReadAllBytes(original).AsSpan().SequenceEqual(leftBytes) &&
                File.ReadAllBytes(renamed).AsSpan().SequenceEqual(rightBytes) && queue.Items.Count == 0, "Keep both changed data or queued quarantine.");
            Click((Button)review.FindName("StopButton"));
            PumpUntil(() => !review.HasSession);
            Click((Button)review.FindName("ScanButton"));
            PumpUntil(() => ((TextBlock)review.FindName("ComparisonHint")).Text.StartsWith("No more pairs"));
            Check(!both.IsEnabled && queue.Items.Count == 0, "Kept distinct photos returned as duplicates.");
            Click((Button)review.FindName("StopButton"));
            PumpUntil(() => !review.HasSession);
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static Button? FindButton(DependencyObject parent, string text)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button button && button.Content?.ToString() == text) return button;
            Button? found = FindButton(child, text);
            if (found != null) return found;
        }
        return null;
    }
    static string MakePhoto(string root, string relative, byte color, int width = 480, int height = 320)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] pixels = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 3;
                pixels[index] = color;
                pixels[index + 1] = (byte)(x / 2);
                pixels[index + 2] = (byte)(y / 2);
            }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    static void PumpUntil(Func<bool> predicate, int timeoutSeconds = 15)
    {
        if (predicate()) return;
        var frame = new DispatcherFrame();
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (predicate() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Check(predicate(), "Timed out waiting for the WPF workflow.");
    }

    static void Render(MainWindow window, string name, int width = 1340, int height = 850)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (DrawingContext drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(scratch, name));
        encoder.Save(stream);
    }
}

