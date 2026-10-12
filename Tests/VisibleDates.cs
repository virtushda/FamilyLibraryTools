using System.Globalization;
using System.IO;
using System.Threading;
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
    static bool DateReady(MainWindow window) =>
        ((StackPanel)window.FindName("DateActions")).IsEnabled &&
        ((Grid)window.FindName("DatePanel")).Visibility == Visibility.Visible &&
        ((ProgressBar)window.FindName("ImageDateProgress")).Visibility == Visibility.Collapsed &&
        !((TextBlock)window.FindName("SuggestedDateText")).Text.StartsWith("Still evaluating");

    static void VisibleDateParsing()
    {
        (string Text, DateTime Value, DatePrecision Precision)[] cases =
        [
            ("Taken 2020/12/25 14:32", new(2020, 12, 25, 14, 32, 0), DatePrecision.Minute),
            ("2020-12-25T14:32:15.123+14:00", new(2020, 12, 25, 14, 32, 15, 123), DatePrecision.Timestamp),
            ("2020-12-25 14:32 GMT+14:00", new(2020, 12, 25, 14, 32, 0), DatePrecision.Minute),
            ("2020-12-25 14:32 UTC-11:30", new(2020, 12, 25, 14, 32, 0), DatePrecision.Minute),
            ("2020-12-25 14:32 UTC -11:30", new(2020, 12, 25, 14, 32, 0), DatePrecision.Minute),
            ("2020-12-25 14:32 GMT -11:30", new(2020, 12, 25, 14, 32, 0), DatePrecision.Minute),
            ("December 25, 2020 at 2:32 PM", new(2020, 12, 25, 14, 32, 0), DatePrecision.Minute),
            ("December 25, 2020 2:32 P M", new(2020, 12, 25, 14, 32, 0), DatePrecision.Minute),
            ("December 25, 2020 2:32 p. m.", new(2020, 12, 25, 14, 32, 0), DatePrecision.Minute),
            ("December 25, 2020 09:00 - 17:00", new(2020, 12, 25), DatePrecision.Day),
            ("25 December 2020 12:01 AM", new(2020, 12, 25, 0, 1, 0), DatePrecision.Minute),
            ("Dec. 25th, 2020, 12:01 p.m.", new(2020, 12, 25, 12, 1, 0), DatePrecision.Minute),
            ("25/12/2020", new(2020, 12, 25), DatePrecision.Day),
            ("12.25.2020", new(2020, 12, 25), DatePrecision.Day),
            ("2024-02-29", new(2024, 2, 29), DatePrecision.Day),
            ("Date:2024-02-29", new(2024, 2, 29), DatePrecision.Day),
            ("11/11/2011", new(2011, 11, 11), DatePrecision.Day),
            ("Sept 1 2020 00:30Z", new(2020, 9, 1, 0, 30, 0), DatePrecision.Minute)
        ];
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            foreach (var item in cases)
            {
                var dates = VisibleDates.Parse(item.Text);
                Check(dates.Count == 1 && dates[0].Value == item.Value && dates[0].Precision == item.Precision && dates[0].NeedsReview,
                    $"Visible date parsed incorrectly: {item.Text}; got {string.Join("; ", dates)}");
                Check(dates[0].Note.Contains(item.Text) && dates[0].Value!.Value.Kind == DateTimeKind.Unspecified,
                    "OCR evidence was lost or timezone guessed.");
            }
            var ambiguous = VisibleDates.Parse("04/05/2020 2:30 PM");
            Check(ambiguous.Count == 2 && ambiguous[0].Value == new DateTime(2020, 4, 5, 14, 30, 0) &&
                ambiguous[1].Value == new DateTime(2020, 5, 4, 14, 30, 0), "Numeric date order was silently guessed from locale.");
            Check(VisibleDates.Parse("2020-12-25 and 2019-11-24").Count == 2, "A second visible date was discarded.");
            foreach (string range in new[] { "2020-12-25-2021-01-02", "12/25/2020-01/02/2021", "2020-12-25 - 2021-01-02",
                "2020-12-25 14:32-2021-01-02 09:15", "2020-12-25 14:32 - 2021-01-02 09:15", "2020-12-25 14:32-01/02/2021 09:15",
                "2020/12/25 14:32-2021/01/02 09:15", "2020-12-25 14:32-2 January 2021 09:15", "2020-12-25 14:32+14:00-2021-01-02 09:15-11:30" })
            {
                var endpoints = VisibleDates.Parse(range);
                Check(endpoints.Any(date => date.Value!.Value.Date == new DateTime(2020, 12, 25)) &&
                    endpoints.Any(date => date.Value!.Value.Date == new DateTime(2021, 1, 2)), "Date range lost an endpoint: " + range);
            }
            Check(VisibleDates.Parse("2020-12-25 and December 25, 2020").Count == 1, "Equivalent dates became a false conflict.");
            // Exercise supported range formats with real offsets on either side, including compact offsets.
            foreach (string left in new[] { "2020-12-25", "2020/12/25", "12/25/2020", "25/12/2020", "December 25, 2020", "25 December 2020" })
            foreach (string right in new[] { "2021-01-02", "2021/01/02", "01/02/2021", "2 January 2021", "January 2, 2021" })
            foreach (string suffix in new[] { "", " 14:32", " 14:32+1400", " 14:32-1100", " 14:32+14:00", " 14:32 UTC-11:30" })
            foreach (string separator in new[] { "-", " - ", " to " })
            {
                string text = left + suffix + separator + right + suffix;
                var parsed = VisibleDates.Parse(text);
                Check(parsed.Any(date => date.Value!.Value.Date == new DateTime(2020, 12, 25)) &&
                    parsed.Any(date => date.Value!.Value.Date == new DateTime(2021, 1, 2)), "Supported range lost an endpoint: " + text);
            }
            foreach (string invalid in new[] { "2023-02-29", "31/04/2020", "2020/12/25 25:00", "2020/12/25 12:99",
                "2020/12/25 00:30 AM", "2020/12/25 12:30+14:30", "2020-12-25 14:32:999", "2020-12-25 14:3", "2020-12-25 14:32:9",
                "2020-12-25 14:32:01.12345678", "2020-12-25 14:32 GMT+14:30", "2020-12-25 14:32+14:0", "2020-12-25 14:32-14:0",
                "2020-12-25 14:32+20:21", "2020-12-25 14:32-20:21", "2020-12-25 14:32 GMT+20:21", "2020-12-25 14:32 : O9", "2020-12-25 14:32 + 15:00",
                "Dec 25", "12/25/20", "2020", "14:32", "2020/12", "180/12/25", "ID2020-12-25X", "٢٠٢٠/١٢/٢٥" })
                Check(VisibleDates.Parse(invalid).Count == 0, "Invalid or incomplete date was accepted: " + invalid);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    static string WriteScreenshot(string root, string name, params string[] lines)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1200, 600));
            for (int index = 0; index < lines.Length; index++)
            {
                var text = new FormattedText(lines[index], CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 44, Brushes.Black, 1);
                drawing.DrawText(text, new Point(50, 60 + index * 95));
            }
        }
        var image = new RenderTargetBitmap(1200, 600, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        string path = Path.Combine(root, name);
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    static void RealVisibleDateOcr()
    {
        using var session = new OcrSession();
        var folders = Folders();
        string path = WriteScreenshot(folders.Input, "screenshot.png", "Family memories", "December 25, 2020 2:32 PM");
        byte[] before = File.ReadAllBytes(path);
        var bitmap = PhotoPreview.Load(path);
        var result = session.Read(bitmap, OcrMethod.PpOcrV6Small, default);
        Check(result.Candidates.Count == 1 && !result.RequiresChoice && result.Candidates[0].Date.Value == new DateTime(2020, 12, 25, 14, 32, 0),
            "Real OCR did not recognize a clear screenshot date: " + result.Status);
        Check(result.Candidates[0].Evidence.IsFrozen && result.Candidates[0].Evidence.PixelHeight < bitmap.PixelHeight / 2,
            "Evidence was not cropped to the recognized region.");
        var dates = DateSuggestions.Read(DateFile(path), result.Candidates);
        Check(dates[0].Source.Contains("PP-OCR") && dates[0].NeedsReview && dates[1].Source.StartsWith("Modified on disk"),
            "OCR did not rank above disk dates or ceased requiring review.");
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(before), "OCR modified its input.");
        path = WriteScreenshot(folders.Input, "ambiguous.png", "Family memories", "04/05/2020 14:32");
        result = session.Read(PhotoPreview.Load(path), OcrMethod.PpOcrV6Small, default);
        Check(result.RequiresChoice && result.Candidates.Count == 2, "Real OCR hid a numeric date ambiguity.");
        path = WriteScreenshot(folders.Input, "multiple.png", "2020-12-25 14:32", "2019-11-24 09:15");
        result = session.Read(PhotoPreview.Load(path), OcrMethod.PpOcrV6Small, default);
        Check(result.RequiresChoice && result.Candidates.Count == 2, "Real OCR hid a second date.");
        path = WriteScreenshot(folders.Input, "range.png", "2020-12-25-2021-01-02");
        result = session.Read(PhotoPreview.Load(path), OcrMethod.PpOcrV6Small, default);
        Check(result.RequiresChoice && result.Candidates.Count == 2, "Real OCR hid a compact range endpoint.");
        path = WriteScreenshot(folders.Input, "timed-range.png", "2020-12-25 14:32-2021-01-02 09:15");
        result = session.Read(PhotoPreview.Load(path), OcrMethod.PpOcrV6Small, default);
        Check(result.RequiresChoice && result.Candidates.Count == 2, "Real OCR treated a timed range as a timezone.");
        path = WriteScreenshot(folders.Input, "empty.png", "Family memories");
        result = session.Read(PhotoPreview.Load(path), OcrMethod.PpOcrV6Small, default);
        Check(result.Candidates.Count == 0 && !result.RequiresChoice, "No-date image manufactured an OCR date.");
        Check(result.RecognizedText.Contains("Family memories"), "Unparseable recognized text was lost.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { session.Read(bitmap, OcrMethod.PpOcrV6Small, cancelled.Token); throw new InvalidOperationException("Cancelled OCR ran."); }
        catch (OperationCanceledException) { }
    }

    static void OcrRegionMapping()
    {
        // Deliberately non-square DPI: the display and selection must follow oriented pixels.
        var image = BitmapSource.Create(1200, 600, 144, 72, PixelFormats.Gray8, null, new byte[720000], 1200);
        image.Freeze();
        var viewport = new Size(600, 500); // Image occupies y=100..400.
        Check(DateRegionPicker.MapSelection(image, viewport, new(50, 125), new(550, 350)) == new Int32Rect(100, 50, 1000, 450),
            "Region mapping ignored letterboxing or image DPI.");
        Check(DateRegionPicker.MapSelection(image, viewport, new(550, 350), new(50, 125)) == new Int32Rect(100, 50, 1000, 450),
            "Reverse drag changed the selected pixels.");
        Check(DateRegionPicker.MapSelection(image, viewport, new(50, 50), new(550, 350)).IsEmpty,
            "A drag starting in the letterbox selected an image region.");
        Check(DateRegionPicker.MapSelection(image, viewport, new(50, 125), new(700, 550)) == new Int32Rect(100, 50, 1100, 550),
            "Region escaping the image was not clamped.");
        Check(DateRegionPicker.MapSelection(image, viewport, new(50, 125), new(51, 126)).IsEmpty, "A click became a crop.");
        Check(DateRegionPicker.MapSelection(image, new Size(), new(), new()).IsEmpty, "An unlaid-out image produced a crop.");
        var portrait = new TransformedBitmap(image, new RotateTransform(90));
        portrait.Freeze();
        Check(DateRegionPicker.MapSelection(portrait, new Size(600, 600), new(150, 0), new(450, 600)) == new Int32Rect(0, 0, 600, 1200),
            "Oriented portrait crop did not cover its actual pixels.");
    }

    static void MediumVisibleDateOcr()
    {
        using var session = new OcrSession();
        var folders = Folders();
        string path = WriteScreenshot(folders.Input, "medium.png", "December 25, 2020", "January 2, 2021");
        byte[] before = File.ReadAllBytes(path);
        var bitmap = PhotoPreview.Load(path);
        var result = session.Read(bitmap, OcrMethod.PpOcrV6Medium, default);
        Check(result.Candidates.Count >= 2 && result.RequiresChoice && result.RecognizedText.Contains("PP-OCRv6 Medium"),
            "Medium OCR discarded conflicting dates.");
        var region = new CroppedBitmap(bitmap, new Int32Rect(20, 40, 850, 85));
        region.Freeze();
        var cropped = session.Read(region, OcrMethod.PpOcrV6Medium, default);
        Check(cropped.Candidates.Count == 1 && cropped.Candidates[0].Date.Value == new DateTime(2020, 12, 25),
            "Selected region read text outside the crop or lost the selected date. " + cropped.RecognizedText);
        var merged = ImageDates.Merge(result, cropped);
        Check(merged.Candidates.Count >= 2 && merged.RequiresChoice, "A retry discarded an earlier conflicting interpretation.");
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(before), "Medium OCR or cropping modified its original.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { session.Read(region, OcrMethod.PpOcrV6Medium, cancellation.Token); throw new InvalidOperationException("Cancelled retry ran."); }
        catch (OperationCanceledException) { }
    }

    static void UiOcrTools()
    {
        var folders = Folders();
        string path = WriteScreenshot(folders.Input, "a.png", "December 25, 2020", "January 2, 2021");
        string next = WriteScreenshot(folders.Input, "b.png", "2020-12-25 14:32-2021-01-02 09:15");
        byte[] before = File.ReadAllBytes(path);
        var window = new MainWindow();
        using var source = new HwndSource(new HwndSourceParameters("Hidden OCR tools input verification")
        { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        var scan = (Button)window.FindName("ScanButton");
        var move = (Button)window.FindName("MoveButton");
        var actions = (StackPanel)window.FindName("DateActions");
        var retry = (Button)window.FindName("RetryOcr");
        var select = (Button)window.FindName("SelectDateRegion");
        var picker = (DateRegionPicker)window.FindName("DateRegion");
        var manual = (DatePicker)window.FindName("CaptureDatePicker");
        var text = (TextBox)window.FindName("RecognizedText");
        void Space(bool up = false) => InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space)
        { RoutedEvent = up ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent, Source = window });
        try
        {
            ((TextBox)window.FindName("InputPath")).Text = folders.Input;
            ((TextBox)window.FindName("OutputPath")).Text = folders.Output;
            Click(scan); PumpUntil(() => move.IsEnabled); Click(move); PumpUntil(() => DateReady(window), 60);
            Check(retry.IsEnabled && select.IsEnabled && text.Visibility == Visibility.Collapsed, "OCR tools missing or debug expanded by default.");
            Click((Button)window.FindName("ShowRecognizedText"));
            Check(text.Visibility == Visibility.Visible && text.Text.Contains("December"), "Recognized text unavailable.");
            manual.SelectedDate = new DateTime(2018, 8, 3);
            Click(retry);
            Check(actions.IsEnabled && ((ProgressBar)window.FindName("ImageDateProgress")).Visibility == Visibility.Visible &&
                !((Button)window.FindName("AcceptSuggestedDate")).IsEnabled && File.Exists(path), "Retry blocked manual choices or left its suggestion active.");
            Space();
            PumpUntil(() => DateReady(window), 60);
            Space();
            Check(File.Exists(path) && manual.SelectedDate == new DateTime(2018, 8, 3) && text.Text.Contains("PP-OCRv6 Medium"),
                "Retry accepted held Space, erased manual input, or failed to refresh debug text.");
            Space(up: true);
            Click((Button)window.FindName("ShowRecognizedText"));
            Click(select);
            Check(actions.IsEnabled && picker.Visibility == Visibility.Visible && !((Button)window.FindName("AcceptSuggestedDate")).IsEnabled,
                "Crop selection blocked manual choices or left its suggestion active.");
            Render(window, "ocr-region-picker.png");
            Click((Button)picker.FindName("CancelRegion"));
            Check(actions.IsEnabled && picker.Visibility == Visibility.Collapsed && manual.SelectedDate == new DateTime(2018, 8, 3),
                "Cancelling a region lost pending choices or manual input.");
            Click(select);
            Render(window, "ocr-region-selection.png");
            var surface = (Canvas)picker.FindName("Surface");
            double scale = Math.Min(surface.ActualWidth / 1200, surface.ActualHeight / 600);
            double left = (surface.ActualWidth - 1200 * scale) / 2, top = (surface.ActualHeight - 600 * scale) / 2;
            // Exercise the same coordinate-to-selection path as a mouse drag; no physical pointer is moved.
            typeof(DateRegionPicker).GetMethod("Select", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(picker, [new Point(left + 20 * scale, top + 40 * scale), new Point(left + 870 * scale, top + 125 * scale)]);
            Render(window, "ocr-region-selected.png");
            Click((Button)picker.FindName("ReadRegion"));
            PumpUntil(() => DateReady(window), 60);
            var choices = (ItemsControl)window.FindName("ImageDateChoices");
            Check(choices.Items.Count == 1 && ((ImageDateCandidate)choices.Items[0]).Date.Value == new DateTime(2020, 12, 25) &&
                ((TextBlock)window.FindName("ImageDateStatus")).Text.StartsWith("Selected region."), "Crop did not replace whole-image interpretations.");
            Check(text.Text.Contains("PP-OCRv6 Small"), "The selected region did not use Small first.");
            Click(retry); PumpUntil(() => DateReady(window), 60);
            Check(choices.Items.Count == 1 && text.Text.Contains("PP-OCRv6 Medium"), "Medium retried the full image instead of the selected region.");
            Space(); Space(up: true);
            Check(File.Exists(path), "Retry rearmed Space despite an entered manual date.");
            Render(window, "ocr-tools-ready.png");
            Render(window, "ocr-tools-compact.png", 1000, 800);
            Click((Button)window.FindName("AcceptSuggestedDate"));
            PumpUntil(() => DateReady(window), 60);
            Check(File.ReadAllBytes(Path.Combine(folders.Output, "2020", "12", "a.png")).AsSpan().SequenceEqual(before),
                "Accepting a cropped OCR date changed or misrouted the original image.");
            Check(text.Visibility == Visibility.Collapsed && !text.Text.Contains("[PP-OCR retry]") && picker.Visibility == Visibility.Collapsed,
                "OCR tools leaked state into the next photo.");
            Check(!((Button)window.FindName("AcceptSuggestedDate")).IsEnabled, "Compact range enabled default Space.");
            Click(retry);
            Click((Button)window.FindName("CancelButton")); PumpUntil(() => scan.IsEnabled);
            Check(File.Exists(next) && !actions.IsEnabled && text.Text.Length == 0, "Cancel during retry allowed a late decision.");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static void UiVisibleDates()
    {
        var folders = Folders();
        string clear = WriteScreenshot(folders.Input, "a.png", "December 25, 2020 2:32 PM");
        string ambiguous = WriteScreenshot(folders.Input, "b.png", "04/05/2020 14:32");
        string cancel = WriteScreenshot(folders.Input, "c.png", "November 24, 2019 09:15");
        byte[] firstBytes = File.ReadAllBytes(clear), secondBytes = File.ReadAllBytes(ambiguous);
        var window = new MainWindow();
        using var source = new HwndSource(new HwndSourceParameters("Hidden OCR date input verification")
        { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        var scan = (Button)window.FindName("ScanButton");
        var move = (Button)window.FindName("MoveButton");
        var actions = (StackPanel)window.FindName("DateActions");
        var accept = (Button)window.FindName("AcceptSuggestedDate");
        var choices = (ItemsControl)window.FindName("ImageDateChoices");
        KeyEventArgs Space(bool up = false)
        {
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space)
            { RoutedEvent = up ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent, Source = window };
            InputManager.Current.ProcessInput(key);
            return key;
        }
        try
        {
            ((TextBox)window.FindName("InputPath")).Text = folders.Input;
            ((TextBox)window.FindName("OutputPath")).Text = folders.Output;
            Click(scan); PumpUntil(() => move.IsEnabled); Click(move);
            Space(); // Loading must not consume the pending file or become a delayed confirmation.
            PumpUntil(() => DateReady(window), 60);
            Check(File.Exists(clear) && accept.IsEnabled && choices.Items.Count == 1, "OCR review was bypassed or failed.");
            Check(((TextBlock)window.FindName("SuggestedDateText")).Text.Contains("2020/12"), "OCR recommendation has the wrong folder.");
            Space();
            Check(actions.IsEnabled, "Held Space accepted a just-loaded OCR result.");
            Space(up: true);
            var fresh = Space();
            Check(!actions.IsEnabled, $"Fresh Space did not accept OCR: repeat={fresh.IsRepeat}, handled={fresh.Handled}, modifiers={Keyboard.Modifiers}, focus={Keyboard.FocusedElement}");
            PumpUntil(() => DateReady(window), 60);
            Space(up: true);
            Check(File.ReadAllBytes(Path.Combine(folders.Output, "2020\\12\\a.png")).AsSpan().SequenceEqual(firstBytes),
                "Confirmed OCR date did not route the intact image.");
            Check(!accept.IsEnabled && choices.Items.Count == 2 && File.Exists(ambiguous), "Ambiguous OCR date became the default.");
            Space(); Space(up: true);
            Check(actions.IsEnabled && File.Exists(ambiguous), "Space chose between ambiguous OCR dates.");
            Render(window, "ocr-date-ambiguity.png");
            Render(window, "ocr-date-ambiguity-compact.png", 940, 690);
            var buttons = new List<Button>();
            void FindChoices(DependencyObject parent)
            {
                for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                {
                    var child = VisualTreeHelper.GetChild(parent, index);
                    if (child is Button { DataContext: ImageDateCandidate } button) buttons.Add(button);
                    FindChoices(child);
                }
            }
            FindChoices(choices);
            Check(buttons.Count == 2, "OCR choice buttons were not displayed.");
            Button choice = buttons.Find(button => ((ImageDateCandidate)button.DataContext).Date.Value!.Value.Month == 5)!;
            Click(choice);
            PumpUntil(() => DateReady(window), 60);
            Check(File.ReadAllBytes(Path.Combine(folders.Output, "2020\\5\\b.png")).AsSpan().SequenceEqual(secondBytes),
                "Explicit day/month/year choice was ignored or changed the image.");
            Click((Button)window.FindName("CancelButton")); PumpUntil(() => scan.IsEnabled);
            Check(File.Exists(cancel) && !actions.IsEnabled && choices.ItemsSource == null, "Cancelled review left OCR choices live.");
            string journal = File.ReadAllText(Directory.GetFiles(folders.Output, "*.jsonl", SearchOption.AllDirectories).Single());
            Check(journal.Contains("User-confirmed: Date read from image (PP-OCRv6 Small)") && journal.Contains("day/month/year") && journal.Contains("04/05/2020"),
                "OCR provenance or explicit interpretation was not journaled.");
            Click(scan); PumpUntil(() => move.IsEnabled); Click(move);
            Click((Button)window.FindName("CancelButton")); PumpUntil(() => scan.IsEnabled);
            Check(File.Exists(cancel) && !actions.IsEnabled, "Cancel during OCR loading allowed a late confirmation.");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static void UiSkipNonAutomatic()
    {
        // Exercise skipping before review, during loading, and after the choices are displayed.
        for (int phase = 0; phase < 5; phase++)
        {
            var folders = Folders();
            string first = WriteScreenshot(folders.Input, "a.png", "December 25, 2020 2:32 PM");
            string second = MakePhoto(folders.Input, "b.png", 37);
            string automatic = MakePhoto(folders.Input, "z\\20200102_030405.png", 160);
            byte[] firstBytes = File.ReadAllBytes(first), secondBytes = File.ReadAllBytes(second), automaticBytes = File.ReadAllBytes(automatic);
            var window = new MainWindow();
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            var scan = (Button)window.FindName("ScanButton");
            var move = (Button)window.FindName("MoveButton");
            var skip = (Button)window.FindName("SkipReviewButton");
            var actions = (StackPanel)window.FindName("DateActions");
            var panel = (Grid)window.FindName("DatePanel");
            try
            {
                Check(!skip.IsEnabled, "Bulk skip enabled outside a move run.");
                ((TextBox)window.FindName("InputPath")).Text = folders.Input;
                ((TextBox)window.FindName("OutputPath")).Text = folders.Output;
                Click(scan); PumpUntil(() => move.IsEnabled);
                Check(!skip.IsEnabled, "Bulk skip enabled during scanning.");
                Click(move);
                if (phase == 1) PumpUntil(() => panel.Visibility == Visibility.Visible);
                if (phase >= 2) PumpUntil(() => DateReady(window), 60);
                if (phase == 3) Click((Button)window.FindName("RetryOcr"));
                if (phase == 4) Click((Button)window.FindName("SelectDateRegion"));
                Check(skip.IsEnabled, "Bulk skip unavailable while organizing.");
                Click(skip);
                PumpUntil(() => scan.IsEnabled);
                Check(File.ReadAllBytes(first).AsSpan().SequenceEqual(firstBytes) && File.ReadAllBytes(second).AsSpan().SequenceEqual(secondBytes),
                    "Bulk skip moved or changed a file needing review.");
                Check(!File.Exists(automatic) && File.ReadAllBytes(Path.Combine(folders.Output, "2020\\1\\20200102_030405.png")).AsSpan().SequenceEqual(automaticBytes),
                    "Bulk skip failed to finish an automatic move.");
                Check(((TextBlock)window.FindName("Summary")).Text.Contains("2 files remain in input for date review") && !skip.IsEnabled && !actions.IsEnabled,
                    "Outstanding count or terminal bulk-skip state is incorrect.");
                string journal = File.ReadAllText(Directory.GetFiles(folders.Output, "*.jsonl", SearchOption.AllDirectories).Single());
                Check(journal.Split("DateReviewSkipped").Length - 1 == 2, "Bulk skip did not journal both outstanding files.");
                Render(window, $"bulk-skip-complete-{phase}.png", 1000, 800);
                // A new run must not inherit the release valve and silently skip everything again.
                Click(scan); PumpUntil(() => move.IsEnabled); Click(move);
                PumpUntil(() => actions.IsEnabled);
                Check(panel.Visibility == Visibility.Visible && File.Exists(first) && File.Exists(second) && skip.IsEnabled,
                    "Bulk skip persisted into a later run or an abandoned OCR result accepted a file.");
                Click((Button)window.FindName("CancelButton")); PumpUntil(() => scan.IsEnabled);
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }
}
