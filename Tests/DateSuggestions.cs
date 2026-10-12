using System.IO;
using System.Security.Cryptography;
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

static partial class Program
{
    static void RankedDates()
    {
        var folders = Folders();
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/exif/{ushort=36867}", "2014:02:03 04:05:06");
        metadata.SetQuery("/xmp/exif:DateTimeOriginal", "2015-03-01T00:30:45.123+14:00");
        metadata.SetQuery("/app1/ifd/exif/{ushort=36868}", "2017:04:05 06:07:08");
        metadata.SetQuery("/xmp/xmp:CreateDate", "2018-05-06T07:08:09Z");
        metadata.SetQuery("/xmp/xmp:ModifyDate", "2019-06-07T08:09:10Z");
        metadata.SetQuery("/xmp/MicrosoftPhoto:DateAcquired", "2020-07-08T09:10:11Z");
        string path = WriteDatedPhoto(folders.Input, "IMG_20160304_050607.jpg", metadata);
        File.SetLastWriteTime(path, new DateTime(2021, 8, 9, 10, 11, 12));
        File.SetCreationTime(path, new DateTime(2022, 9, 10, 11, 12, 13));
        byte[] bytes = File.ReadAllBytes(path);
        IReadOnlyList<MediaDate> dates = DateSuggestions.Read(DateFile(path));
        Check(dates.Count == 9, "Expected 9 independent date sources, got: " + string.Join(", ", dates.Select(date => date.Source)));
        for (int index = 0; index < dates.Count; index++)
            Check(dates[index].Value!.Value.Year == 2014 + index && dates[index].NeedsReview, "Date source priority or required confirmation changed.");
        Check(dates[1].Value == new DateTime(2015, 3, 1, 0, 30, 45, 123) && dates[1].Note.Contains("+14:00"),
            "XMP offset or subsecond precision was lost or moved into a different month.");
        Check(dates[7].Source.StartsWith("Modified on disk") && dates[8].Source.StartsWith("Created on disk"), "Disk timestamp priorities reversed.");
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "Reading date hints rewrote photo bytes.");

        var digital = new BitmapMetadata("jpg");
        digital.SetQuery("/xmp/xmp:CreateDate", "2018-05-06T07:08:09Z");
        path = WriteDatedPhoto(folders.Input, "IMG_20160304_050608.jpg", digital);
        dates = DateSuggestions.Read(DateFile(path));
        Check(dates[0].Source.Contains("filename") && dates[1].Source == "XMP creation date", "WIC DateTaken alias outranked the real filename source.");

        digital.SetQuery("/xmp/xmp:CreateDate", "2018-05-01T00:30:45+14:00");
        path = WriteDatedPhoto(folders.Input, "IMG_20160304_050609.jpg", digital);
        dates = DateSuggestions.Read(DateFile(path));
        Check(dates[0].Source.Contains("filename") && dates[1].Source == "XMP creation date" && dates[1].Value == new DateTime(2018, 5, 1, 0, 30, 45),
            "Timezone-adjusted WIC alias changed the best source or original calendar month.");

        var invalid = new BitmapMetadata("jpg");
        invalid.SetQuery("/app1/ifd/exif/{ushort=36867}", "0000:00:00 00:00:00");
        invalid.SetQuery("/xmp/xmp:CreateDate", "1601-01-01T00:00:00Z");
        path = WriteDatedPhoto(folders.Input, "IMG_1234.jpg", invalid);
        dates = DateSuggestions.Read(DateFile(path));
        Check(dates.Count == 2 && dates[0].Source.StartsWith("Modified on disk"), "Invalid metadata hid valid disk hints or became the recommendation.");

        path = Write(folders.Input, "unknown.mp4", "Synthetic unsupported video");
        dates = DateSuggestions.Read(DateFile(path, MediaKind.Video));
        Check(dates.Count == 2 && dates[0].Source.StartsWith("Modified on disk"), "Unreadable video metadata hid disk dates.");
    }

    static MediaFile DateFile(string path, MediaKind kind = MediaKind.Photo) => new(path, kind, new FileInfo(path).Length,
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), File.GetLastWriteTimeUtc(path), new(null, "Missing", true), false);

    static async Task MetadataDateFormats()
    {
        var folders = Folders();
        (string Format, string Raw, DatePrecision Precision, string Display, DateTime Value)[] cases =
        [
            ("jpg", "2018-05", DatePrecision.Month, "2018-05", new(2018, 5, 1)),
            ("jpg", "2018-05-01T00:30+14:00", DatePrecision.Minute, "2018-05-01 00:30", new(2018, 5, 1, 0, 30, 0)),
            ("jpg", "2018-01-01T00:30Z", DatePrecision.Minute, "2018-01-01 00:30", new(2018, 1, 1, 0, 30, 0)),
            ("jpg", "2018-05-12T14:30", DatePrecision.Minute, "2018-05-12 14:30", new(2018, 5, 12, 14, 30, 0)),
            ("jpg", "2018-05-12", DatePrecision.Day, "2018-05-12", new(2018, 5, 12)),
            ("png", "Fri, 12 Apr 2013 14:15:16 GMT", DatePrecision.Timestamp, "2013-04-12 14:15:16.000", new(2013, 4, 12, 14, 15, 16, DateTimeKind.Utc)),
            ("png", "Sun, 01 Apr 2018 00:30:00 GMT", DatePrecision.Timestamp, "2018-04-01 00:30:00.000", new(2018, 4, 1, 0, 30, 0, DateTimeKind.Utc))
        ];
        var chosen = new Dictionary<string, MediaDate>();
        var originals = new Dictionary<string, byte[]>();
        for (int index = 0; index < cases.Length; index++)
        {
            var item = cases[index];
            var metadata = new BitmapMetadata(item.Format);
            metadata.SetQuery(item.Format == "png" ? "/tEXt/{str=Creation Time}" : "/xmp/photoshop:DateCreated", item.Raw);
            string path = WriteDatedPhoto(folders.Input, $"IMG_{index:0000}.{item.Format}", metadata);
            File.SetLastWriteTime(path, new DateTime(2020, 9, 10));
            originals.Add(path, File.ReadAllBytes(path));
            MediaDate best = DateSuggestions.Read(DateFile(path))[0];
            Check(best.Value == item.Value && best.Value.Value.Kind == item.Value.Kind && best.Precision == item.Precision &&
                best.DisplayValue == item.Display && best.Note.Contains(item.Raw) && best.NeedsReview,
                $"Metadata date or its recorded precision was lost: {item.Raw}; got {best}");
            chosen.Add(path, best);
        }
        ScanResult scan = await new LibraryScanner(CaptureMetadata.Read).ScanAsync(folders.Input, folders.Output, quiet, default);
        int reviews = 0;
        RunResult result = await Organize(scan, (file, _) =>
        {
            reviews++;
            return Task.FromResult<MediaDate?>(chosen[file.Path] with { Source = "User-confirmed: " + chosen[file.Path].Source, NeedsReview = false });
        });
        Check(reviews == cases.Length && result.Moved == cases.Length, "Supported metadata hints bypassed explicit review or failed to organize.");
        foreach (var item in chosen)
        {
            DateTime date = item.Value.Value!.Value;
            string output = Path.Combine(folders.Output, date.Year.ToString(), date.Month.ToString(), Path.GetFileName(item.Key));
            Check(File.ReadAllBytes(output).AsSpan().SequenceEqual(originals[item.Key]), "Confirmed metadata date moved into the wrong month or rewrote bytes.");
        }
        // JSON escapes some timezone characters, so compare the decoded journal detail.
        string details = "";
        foreach (string line in File.ReadLines(result.JournalPath))
        {
            using var entry = System.Text.Json.JsonDocument.Parse(line);
            details += entry.RootElement.GetProperty("Detail").GetString();
        }
        foreach (var item in cases) Check(details.Contains(item.Raw), "Journal lost the original timestamp representation.");
        Check(details.Contains("day and time are unknown") && details.Contains("seconds are unknown"), "Journal implied invented day/time precision.");

        foreach (string invalid in new[] { "2018", "2018-13", "2018-02-30", "2018-05-12T25:30+02:00", "05/12/2018" })
        {
            var metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/xmp/photoshop:DateCreated", invalid);
            string path = WriteDatedPhoto(folders.Input, Guid.NewGuid() + ".jpg", metadata);
            Check(DateSuggestions.Read(DateFile(path))[0].Source.StartsWith("Modified on disk"), "Invalid or month-ambiguous metadata became a suggestion: " + invalid);
        }
    }

    static void UiDatePrecision()
    {
        var folders = Folders();
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/xmp/photoshop:DateCreated", "2018-05");
        metadata.SetQuery("/xmp/xmp:ModifyDate", "2019-07-08T09:10+02:00");
        string path = WriteDatedPhoto(folders.Input, "IMG_1234.jpg", metadata);
        byte[] bytes = File.ReadAllBytes(path);
        var window = new MainWindow();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            ((TextBox)window.FindName("InputPath")).Text = folders.Input;
            ((TextBox)window.FindName("OutputPath")).Text = folders.Output;
            Click((Button)window.FindName("ScanButton"));
            PumpUntil(() => ((Button)window.FindName("MoveButton")).IsEnabled);
            Click((Button)window.FindName("MoveButton"));
            PumpUntil(() => DateReady(window), 60);
            string shown = ((TextBlock)window.FindName("SuggestedDateText")).Text;
            Check(shown.StartsWith("2018-05\n") && shown.Contains("day and time are unknown") && shown.Contains("Output folder: 2018/5"),
                "Month-only suggestion displays an invented day or time.");
            ((Expander)window.FindName("OtherDates")).IsExpanded = true;
            Render(window, "date-precision-review.png");
            var alternatives = (ItemsControl)window.FindName("DateAlternatives");
            var other = FindDateButton(alternatives)!;
            var labels = (StackPanel)other.Content;
            Check(((TextBlock)labels.Children[1]).Text == "2019-07-08 09:10", "Alternative button displays invented seconds.");
            Click((Button)window.FindName("AcceptSuggestedDate"));
            PumpUntil(() => ((Button)window.FindName("ScanButton")).IsEnabled);
            Check(File.ReadAllBytes(Path.Combine(folders.Output, "2018\\5\\IMG_1234.jpg")).AsSpan().SequenceEqual(bytes), "Month-only UI choice was routed incorrectly or changed the photo.");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static string WriteDatedPhoto(string root, string name, BitmapMetadata metadata)
    {
        string path = Path.Combine(root, name);
        var source = BitmapSource.Create(80, 40, 96, 96, PixelFormats.Bgr24, null, new byte[80 * 40 * 3], 80 * 3);
        BitmapEncoder encoder = metadata.Format == "png" ? new PngBitmapEncoder() : new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    static void UiDateSuggestions()
    {
        var folders = Folders();
        var paths = new List<string>();
        for (int index = 0; index < 5; index++)
        {
            string path = MakePhoto(folders.Input, $"unknown-{index}.png", (byte)(index * 30));
            File.SetLastWriteTime(path, new DateTime(2020, 4, 12, 14, 15, 16));
            File.SetCreationTime(path, new DateTime(2021, 8, 9, 10, 11, 12));
            paths.Add(path);
        }
        var window = new MainWindow();
        using var source = new HwndSource(new HwndSourceParameters("Hidden date input verification")
        { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        var accept = (Button)window.FindName("AcceptSuggestedDate");
        var scan = (Button)window.FindName("ScanButton");
        var move = (Button)window.FindName("MoveButton");
        var picker = (DatePicker)window.FindName("CaptureDatePicker");
        var actions = (StackPanel)window.FindName("DateActions");
        var confirm = (Button)window.FindName("ConfirmDate");
        bool Ready() => DateReady(window);
        KeyEventArgs SendKey(Key value, bool up = false, UIElement? target = null)
        {
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, value)
            { RoutedEvent = up ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent, Source = target ?? window };
            InputManager.Current.ProcessInput(key);
            return key;
        }
        void Space(bool up = false)
        {
            // WPF reads real keyboard modifiers even for synthetic events. Do not test Shift+Space accidentally.
            if (!up) PumpUntil(() => Keyboard.Modifiers == ModifierKeys.None);
            SendKey(Key.Space, up);
        }
        try
        {
            ((TextBox)window.FindName("InputPath")).Text = folders.Input;
            ((TextBox)window.FindName("OutputPath")).Text = folders.Output;
            Click(scan);
            PumpUntil(() => move.IsEnabled);
            Click(move);
            PumpUntil(Ready);
            Check(paths.All(File.Exists), "Date suggestions automatically moved a source.");
            string suggested = ((TextBlock)window.FindName("SuggestedDateText")).Text;
            Check(suggested.Contains("2020-04-12 14:15:16") && suggested.Contains("Modified on disk") && suggested.Contains("2020/4"), "Recommended date or destination is wrong.");
            Render(window, "date-suggestions.png");
            Render(window, "date-suggestions-compact.png", 940, 690);
            var alternatives = (ItemsControl)window.FindName("DateAlternatives");
            Check(alternatives.Items.Count == 1 && ((MediaDate)alternatives.Items[0]).Source.StartsWith("Created on disk"), "Missing one-click alternative date.");
            Space();
            Check(!actions.IsEnabled, "Space left date actions active after acceptance.");
            PumpUntil(Ready);
            Check(!File.Exists(paths[0]) && File.Exists(paths[1]), "Space did not confirm exactly one file.");
            for (int repeat = 0; repeat < 6; repeat++) Space();
            Check(actions.IsEnabled && File.Exists(paths[1]), "Held Space confirmed a second file.");
            SendKey(Key.LeftShift); SendKey(Key.LeftShift, up: true);
            Check(!SendKey(Key.Space).IsRepeat && actions.IsEnabled && File.Exists(paths[1]),
                "An intervening Shift press/release allowed held Space to confirm the next file.");
            // A release delivered outside this window resets WPF's repeat flag, but must not rearm the app.
            SendKey(Key.Space, up: true, target: new Button());
            Check(!SendKey(Key.Space).IsRepeat && actions.IsEnabled && File.Exists(paths[1]),
                "A missed Space release allowed an unintended date confirmation after returning to the window.");
            Space(up: true);
            ((Expander)window.FindName("OtherDates")).IsExpanded = true;
            Render(window, "date-suggestions-expanded.png");
            // Exercise the actual templated button, preserving the full alternative timestamp.
            var other = FindDateButton(alternatives);
            Click(other!);
            PumpUntil(Ready);
            Check(File.Exists(Path.Combine(folders.Output, "2021\\8\\unknown-1.png")), "Alternative date button used the recommendation instead.");

            picker.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, null, picker)
            { RoutedEvent = Keyboard.PreviewGotKeyboardFocusEvent });
            Space(); Space(up: true);
            Check(actions.IsEnabled && File.Exists(paths[2]), "Space overrode an in-progress manual edit.");
            picker.SelectedDate = new DateTime(2012, 9, 3);
            Space(); Space(up: true);
            Check(actions.IsEnabled && File.Exists(paths[2]), "Space overrode the entered date.");
            Click(confirm);
            PumpUntil(Ready);
            Check(File.Exists(Path.Combine(folders.Output, "2012\\9\\unknown-2.png")), "Manual confirmation ignored the entered month.");
            Space(); Space(up: true);
            PumpUntil(Ready);
            Check(File.Exists(Path.Combine(folders.Output, "2020\\4\\unknown-3.png")), "Fresh Space did not work after a manual choice.");
            Click((Button)window.FindName("CancelButton"));
            PumpUntil(() => scan.IsEnabled);
            Space(); Space(up: true); Click(accept);
            Check(File.Exists(paths[4]), "Cancelled review retained an active suggestion.");
            string journal = File.ReadAllText(Directory.GetFiles(folders.Output, "*.jsonl", SearchOption.AllDirectories).Single());
            Check(journal.Contains("User-confirmed: Modified on disk") && journal.Contains("User-confirmed: Created on disk") &&
                journal.Contains("2021-08-09T10:11:12") && journal.Contains("User-confirmed manual date"), "UI lost the confirmed date provenance.");

            // Cancellation immediately after starting must not enable a delayed preview/date continuation.
            Click(scan); PumpUntil(() => move.IsEnabled); Click(move);
            Click((Button)window.FindName("CancelButton"));
            PumpUntil(() => scan.IsEnabled);
            Check(File.Exists(paths[4]) && !actions.IsEnabled, "Cancellation during loading enabled or accepted a suggestion.");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static Button? FindDateButton(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button { DataContext: MediaDate } button) return button;
            Button? found = FindDateButton(child);
            if (found != null) return found;
        }
        return null;
    }
}
