using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using FamilyLibraryTools;
using FamilyLibraryTools.Core;
using FamilyLibraryTools.Views;

static partial class Program
{
    // Route both stages explicitly in the hidden test tree, including WPF Button's real key handling.
    static KeyEventArgs ButtonKey(HwndSource source, UIElement target, Key value, bool up = false)
    {
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, value)
        { RoutedEvent = up ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(key);
        if (!key.Handled)
        {
            key.RoutedEvent = up ? Keyboard.KeyUpEvent : Keyboard.KeyDownEvent;
            target.RaiseEvent(key);
        }
        return key;
    }

    static MouseButtonEventArgs ButtonMouse(UIElement target, bool up = false, int clickCount = 1)
    {
        var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = up ? Mouse.PreviewMouseUpEvent : Mouse.PreviewMouseDownEvent };
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(mouse, clickCount);
        target.RaiseEvent(mouse);
        return mouse;
    }

    static void HeldButtonGestures()
    {
        using var source = new HwndSource(new HwndSourceParameters("Hidden guarded button verification")
        { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
        var guard = new ReviewInputGuard();
        var root = new StackPanel();
        var scope = new StackPanel();
        var button = new Button { Content = "Review choice" };
        var stop = new Button { Content = "Stop" };
        var text = new TextBox();
        scope.Children.Add(button); scope.Children.Add(text);
        root.Children.Add(scope); root.Children.Add(stop);
        root.PreviewKeyDown += (_, e) => guard.KeyDown(e);
        root.PreviewKeyUp += (_, e) => guard.KeyUp(e);
        root.PreviewMouseDown += (_, e) => guard.MouseDown(e);
        root.PreviewMouseUp += (_, e) => guard.MouseUp(e);
        int clicks = 0, stops = 0;
        button.Click += (_, _) => clicks++;
        stop.Click += (_, _) => stops++;
        ButtonKey(source, button, Key.Enter);
        Check(clicks == 1, "Fresh Enter failed.");
        guard.Invalidate(scope);
        ButtonKey(source, button, Key.LeftShift); ButtonKey(source, button, Key.LeftShift, up: true);
        Check(ButtonKey(source, button, Key.Enter).Handled && clicks == 1, "Another key rearmed held Enter.");
        ButtonKey(source, new Button(), Key.Enter, up: true); // Key-up outside this window is deliberately not observed.
        Check(ButtonKey(source, button, Key.Enter).Handled && clicks == 1, "Missed Enter release rearmed a choice.");
        ButtonKey(source, button, Key.Enter, up: true);
        ButtonKey(source, button, Key.Enter); ButtonKey(source, button, Key.Enter, up: true);
        Check(clicks == 2, "A fresh Enter did not rearm after release.");

        ButtonKey(source, button, Key.Space);
        guard.Invalidate(scope);
        Check(ButtonKey(source, button, Key.Space, up: true).Handled && clicks == 2,
            "Releasing Space acted on a changed review.");
        ButtonKey(source, text, Key.Enter);
        Check(ButtonKey(source, button, Key.Enter).Handled && clicks == 2, "Enter held in a text field activated a later button.");
        ButtonKey(source, button, Key.Enter, up: true);

        Check(!ButtonMouse(button).Handled, "Fresh mouse press was blocked.");
        guard.Invalidate(scope);
        Check(ButtonMouse(button, up: true).Handled, "Mouse release acted on a changed review.");
        Check(!ButtonMouse(button).Handled && !ButtonMouse(button, up: true).Handled, "Fresh mouse gesture did not rearm.");
        Check(ButtonMouse(button, clickCount: 2).Handled && ButtonMouse(button, up: true, clickCount: 2).Handled,
            "Double-click could act on a replacement review.");

        ButtonKey(source, stop, Key.Space);
        guard.Invalidate(scope);
        ButtonKey(source, stop, Key.Space, up: true);
        Check(stops == 1, "Review refresh blocked a pending emergency stop.");
        ButtonMouse(stop); guard.Invalidate(scope);
        Check(!ButtonMouse(stop, up: true).Handled, "Review refresh blocked a mouse stop gesture.");
        ButtonKey(source, button, Key.Space);
        ButtonKey(source, stop, Key.Enter); ButtonKey(source, stop, Key.Enter, up: true);
        guard.Invalidate(scope);
        ButtonKey(source, button, Key.Space, up: true);
        Check(stops == 2 && clicks == 2, "A different held key blocked a fresh emergency-stop press.");
    }

    static void UiHeldDateInputs()
    {
        var folders = Folders();
        string first = WriteScreenshot(folders.Input, "a.png", "December 25, 2020");
        string second = WriteScreenshot(folders.Input, "b.png", "November 24, 2019");
        string third = WriteScreenshot(folders.Input, "c.png", "January 2, 2021");
        var window = new MainWindow();
        using var source = new HwndSource(new HwndSourceParameters("Hidden held date input verification")
        { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        var scan = (Button)window.FindName("ScanButton");
        var move = (Button)window.FindName("MoveButton");
        var actions = (StackPanel)window.FindName("DateActions");
        var accept = (Button)window.FindName("AcceptSuggestedDate");
        var retry = (Button)window.FindName("RetryOcr");
        var picker = (DatePicker)window.FindName("CaptureDatePicker");
        var confirm = (Button)window.FindName("ConfirmDate");
        try
        {
            ((TextBox)window.FindName("InputPath")).Text = folders.Input;
            ((TextBox)window.FindName("OutputPath")).Text = folders.Output;
            Click(scan); PumpUntil(() => move.IsEnabled); Click(move); PumpUntil(() => DateReady(window), 60);
            ButtonKey(source, retry, Key.Enter);
            Check(((ProgressBar)window.FindName("ImageDateProgress")).Visibility == Visibility.Visible, "Enter did not start the real retry.");
            PumpUntil(() => DateReady(window), 60);
            Check(ButtonKey(source, accept, Key.Enter).Handled && actions.IsEnabled && File.Exists(first),
                "Held Enter confirmed a retried result.");
            ButtonKey(source, accept, Key.Enter, up: true);
            ButtonKey(source, accept, Key.Enter); PumpUntil(() => DateReady(window), 60);
            Check(!File.Exists(first) && File.Exists(second), "Fresh Enter did not confirm exactly one file.");
            Check(ButtonKey(source, accept, Key.Enter).Handled && actions.IsEnabled && File.Exists(second),
                "Held Enter accepted the next photo.");
            ButtonKey(source, accept, Key.Enter, up: true);

            Render(window, "held-input-manual-date.png");
            picker.SelectedDate = new DateTime(2018, 8, 3);
            var dateText = (DatePickerTextBox)picker.Template.FindName("PART_TextBox", picker);
            ButtonMouse(confirm);
            dateText.Text = "not-a-date";
            dateText.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Check(ButtonMouse(confirm, up: true).Handled && picker.SelectedDate == null && !confirm.IsEnabled &&
                ((TextBlock)window.FindName("DateDestination")).Text.Contains("Invalid date"),
                "Invalid date text silently restored the previous selection or left confirmation active.");
            Click(confirm);
            Check(actions.IsEnabled && File.Exists(second), "Invalid manual text confirmed a stale date.");

            // The existing button is reused; changing its manual date must invalidate a pending mouse press.
            picker.SelectedDate = new DateTime(2018, 8, 3);
            ButtonMouse(confirm);
            picker.SelectedDate = new DateTime(2017, 7, 4);
            Check(ButtonMouse(confirm, up: true).Handled && File.Exists(second), "Mouse-up accepted a date changed during the press.");
            ButtonMouse(accept);
            Click(retry); PumpUntil(() => DateReady(window), 60);
            Check(ButtonMouse(accept, up: true).Handled && File.Exists(second), "Mouse-up accepted replacement OCR results.");
            Click(confirm); PumpUntil(() => DateReady(window), 60);
            Check(File.Exists(Path.Combine(folders.Output, "2017", "7", "b.png")), "Manual date was lost after an invalidated press.");

            Click((Button)window.FindName("SelectDateRegion"));
            var region = (DateRegionPicker)window.FindName("DateRegion");
            Render(window, "held-input-region.png");
            var surface = (Canvas)region.FindName("Surface");
            double scale = Math.Min(surface.ActualWidth / 1200, surface.ActualHeight / 600);
            double left = (surface.ActualWidth - 1200 * scale) / 2, top = (surface.ActualHeight - 600 * scale) / 2;
            typeof(DateRegionPicker).GetMethod("Select", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(region, [new Point(left + 20 * scale, top + 40 * scale), new Point(left + 870 * scale, top + 125 * scale)]);
            ButtonKey(source, (Button)region.FindName("ReadRegion"), Key.Enter);
            PumpUntil(() => DateReady(window), 60);
            Check(ButtonKey(source, accept, Key.Enter).Handled && File.Exists(third) && actions.IsEnabled,
                "Held Enter from region reading accepted its result.");
            ButtonKey(source, accept, Key.Enter, up: true);
            Click((Button)window.FindName("CancelButton")); PumpUntil(() => scan.IsEnabled);
            Check(File.Exists(third), "Cancelling the input test moved its last photo.");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static void UiHeldDuplicateInputs()
    {
        var folders = Folders();
        for (int index = 0; index < 4; index++)
        {
            MakePhoto(folders.Output, $"group{index}.png", (byte)(20 + index * 20));
            MakePhoto(folders.Output, $"group{index}_DUPLICATE-1.png", (byte)(130 + index * 20));
        }
        var window = new MainWindow();
        using var source = new HwndSource(new HwndSourceParameters("Hidden held duplicate input verification")
        { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        var review = (DuplicateReview)window.FindName("RemovalReview");
        var keep = (Button)review.FindName("KeepOriginal");
        var both = (Button)review.FindName("KeepBoth");
        var queue = (ListBox)review.FindName("QueueList");
        try
        {
            ((TabControl)window.FindName("Workflows")).SelectedIndex = 1;
            ((TextBox)review.FindName("LibraryPath")).Text = folders.Output;
            Click((Button)review.FindName("ScanButton")); PumpUntil(() => keep.IsEnabled);
            ButtonKey(source, keep, Key.Enter); PumpUntil(() => keep.IsEnabled);
            Check(queue.Items.Count == 1, "Fresh Enter did not queue one choice.");
            Check(ButtonKey(source, keep, Key.Enter).Handled && queue.Items.Count == 1, "Held Enter queued a second pair.");
            ButtonKey(source, keep, Key.Enter, up: true);
            ButtonKey(source, both, Key.Enter); PumpUntil(() => keep.IsEnabled);
            int renamed = Directory.GetFiles(folders.Output, "*-1.png").Count(path => !path.Contains("DUPLICATE"));
            Check(renamed == 1, "Fresh Enter failed to keep both once.");
            Check(ButtonKey(source, both, Key.Enter).Handled && Directory.GetFiles(folders.Output, "*-1.png").Count(path => !path.Contains("DUPLICATE")) == 1,
                "Held Enter renamed another pair.");
            ButtonKey(source, both, Key.Enter, up: true);

            Render(window, "held-input-pairs.png");
            ButtonMouse(keep);
            Click(FindButton(queue, "CANCEL DELETION")!); PumpUntil(() => keep.IsEnabled);
            Check(ButtonMouse(keep, up: true).Handled && queue.Items.Count == 1 && ((DeletionRow)queue.Items[0]).Entry.State == DeletionState.Cancelled,
                "Mouse-up acted on a pair replaced by queue cancellation.");
            ButtonKey(source, keep, Key.Space);
            Click((Button)review.FindName("SkipPair")); PumpUntil(() => keep.IsEnabled);
            Check(ButtonKey(source, keep, Key.Space, up: true).Handled && queue.Items.Count == 1,
                "Space release acted on a changed pair.");
            ButtonKey(source, (Button)review.FindName("StopButton"), Key.Enter);
            PumpUntil(() => !review.HasSession);
            Check(Directory.GetFiles(folders.Output, "*.png", SearchOption.AllDirectories).Length == 8,
                "Held-input checks lost a media file.");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
