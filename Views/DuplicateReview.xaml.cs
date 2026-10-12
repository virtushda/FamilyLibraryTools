using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FamilyLibraryTools.Core;
using FamilyLibraryTools.Media;
using Microsoft.Win32;

namespace FamilyLibraryTools.Views;

public partial class DuplicateReview : UserControl
{
    readonly ObservableCollection<DeletionRow> rows = [];
    readonly List<List<MediaFile>> groups = [];
    readonly HashSet<List<MediaFile>> waitingGroups = [];
    readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    List<MediaFile>? current;
    TransferJournal? journal;
    DeletionQueue? queue;
    VerifiedRenamer? renamer;
    CancellationTokenSource? scanCancellation;
    Task scanWork = Task.CompletedTask, quarantineWork = Task.CompletedTask;
    Task renameWork = Task.CompletedTask;
    Task? stopWork;
    int previewGeneration;
    public bool HasSession { get; private set; }
    public event EventHandler? SessionChanged;
    public event Action<DependencyObject>? PairChanging;

    public DuplicateReview()
    {
        InitializeComponent();
        QueueList.ItemsSource = rows;
        timer.Tick += OnTick;
        OriginalPreview.PanChanged += OnPanChanged;
        DuplicatePreview.PanChanged += OnPanChanged;
    }

    public void SuggestLibrary(string path) { if (!HasSession) LibraryPath.Text = path; }

    void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the organized library to review", Multiselect = false, AddToRecent = false };
        if (Directory.Exists(LibraryPath.Text)) dialog.InitialDirectory = LibraryPath.Text;
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) LibraryPath.Text = dialog.FolderName;
    }

    async void OnScan(object sender, RoutedEventArgs e)
    {
        if (HasSession || stopWork is { IsCompleted: false }) return;
        stopWork = null;
        scanWork = ScanAsync();
        await scanWork;
    }

    async Task ScanAsync()
    {
        HasSession = true;
        FolderInputs.IsEnabled = false;
        StopButton.IsEnabled = true;
        SessionChanged?.Invoke(this, EventArgs.Empty);
        rows.Clear(); groups.Clear(); waitingGroups.Clear();
        scanCancellation = new();
        CancellationToken scanToken = scanCancellation.Token;
        try
        {
            string root = PathSafety.CanonicalDirectory(LibraryPath.Text);
            PathSafety.RejectBookkeepingRoot(root);
            journal = new TransferJournal(root);
            JournalPath.Text = "Journal: " + journal.Path;
            RecoveryPath.Text = "Recover files from: " + Path.Combine(root, ".family-library-tools", "quarantine");
            var progress = new Progress<string>(message => { if (HasSession && !scanToken.IsCancellationRequested) ReviewStatus.Text = message; });
            LibraryScan scan = await Task.Run(() => new LibraryScanner(CaptureMetadata.Read).ScanLibraryAsync(root, progress, scanToken));
            scanToken.ThrowIfCancellationRequested();
            if (scan.Issues.Count > 0) throw new IOException($"Scan incomplete; resolve this before quarantine: {scan.Issues[0].Path}: {scan.Issues[0].Reason}");
            var candidates = CollisionGroups.Build(scan.Files, true);
            foreach (var group in candidates) if (group.Count > 1) groups.Add(new(group));
            queue = new(candidates, new VerifiedQuarantine(root, journal).QuarantineAsync);
            renamer = new(root, scan.Files, journal);
            journal.Record("DuplicateReviewStarted", root);
            ReviewStatus.Text = $"{groups.Count} candidate groups. Your explicit choices move rejected files to recoverable quarantine after 60 seconds.";
            timer.Start();
            await ShowNextPairAsync();
        }
        catch (OperationCanceledException) { ReviewStatus.Text = "Stopped. No choices will resume automatically."; }
        catch (Exception error) { ReviewStatus.Text = "Stopped: " + error.Message; }
        finally { if (queue == null) ReleaseSession(); }
    }

    async Task ShowNextPairAsync(List<MediaFile>? preferred = null)
    {
        ClearPair();
        if (queue == null || queue.Stopped) return;
        if (preferred is { Count: > 1 } && !waitingGroups.Contains(preferred)) current = preferred;
        else foreach (var group in groups) if (group.Count > 1 && !waitingGroups.Contains(group)) { current = group; break; }
        if (current == null)
        {
            ComparisonHint.Text = waitingGroups.Count > 0 ? "Other groups are awaiting your countdowns. Cancel a choice in the queue to review it again." : "No more pairs in this scan. STOP ALL ends the session; scanning again includes skipped pairs and exact copies.";
            return;
        }
        int generation = previewGeneration;
        MediaFile left = current[0], right = current[1];
        ComparisonHint.Text = left.ContentKey == right.ContentKey
            ? "Matching size and whole-file hash. Review both files and explicitly choose what to keep."
            : "Names suggest related versions; their contents differ. Compare both before making a choice.";
        await Task.WhenAll(OriginalPreview.ShowAsync(left), DuplicatePreview.ShowAsync(right));
        if (generation != previewGeneration || queue is not { Stopped: false }) return;
        if (left.Length != right.Length && OriginalPreview.LoadedPhoto is { } first && DuplicatePreview.LoadedPhoto is { } second && PhotoPreview.LooksSimilar(first, second))
            ComparisonHint.Text += $" The larger {(left.Length > right.Length ? "LEFT" : "RIGHT")} preview is suggested, but size does not prove quality.";
        KeepOriginal.IsEnabled = KeepDuplicate.IsEnabled = KeepBoth.IsEnabled = SkipPair.IsEnabled = Zoom.IsEnabled = true;
    }

    void ClearPair()
    {
        PairChanging?.Invoke(PairPanel);
        previewGeneration++;
        current = null;
        KeepOriginal.IsEnabled = KeepDuplicate.IsEnabled = KeepBoth.IsEnabled = SkipPair.IsEnabled = Zoom.IsEnabled = false;
        Zoom.Value = 1;
        OriginalPreview.Clear(); DuplicatePreview.Clear();
    }

    async void OnKeepOriginal(object sender, RoutedEventArgs e) => await ChooseAsync(true);
    async void OnKeepDuplicate(object sender, RoutedEventArgs e) => await ChooseAsync(false);
    async Task ChooseAsync(bool keepLeft)
    {
        if (current == null || queue == null || queue.Stopped || !KeepBoth.IsEnabled) return;
        var group = current;
        MediaFile keep = group[keepLeft ? 0 : 1], reject = group[keepLeft ? 1 : 0];
        try
        {
            journal!.Record("QuarantineChoice", reject.Path, keep.Path, reject.Hash, "Explicit selection; 60 second grace period.");
            PendingDeletion entry = queue.Enqueue(keep, reject);
            rows.Add(new(entry, group));
            waitingGroups.Add(group);
            await ShowNextPairAsync();
        }
        catch (Exception error) { ReviewStatus.Text = error.Message; }
    }

    async void OnKeepBoth(object sender, RoutedEventArgs e)
    {
        if (current == null || queue is not { Stopped: false } || !KeepBoth.IsEnabled || !renameWork.IsCompleted) return;
        var group = current;
        ClearPair(); // Release video previews before requesting the exclusive move handle.
        renameWork = KeepBothAsync(group);
        await renameWork;
        if (queue is { Stopped: true }) await StopAsync();
        else if (queue != null) await ShowNextPairAsync(group);
    }

    async Task KeepBothAsync(List<MediaFile> group)
    {
        try
        {
            var rename = renamer!;
            CancellationToken token = scanCancellation!.Token;
            MediaFile renamed = await Task.Run(() => rename.KeepBothAsync(group[0], group[1], token));
            group.RemoveAt(1);
            if (queue is { Stopped: false }) ReviewStatus.Text = $"Kept both. Renamed RIGHT to {renamed.Name}. Exact copies will still be detected on future scans.";
        }
        catch (OperationCanceledException) { ReviewStatus.Text = "Rename cancelled. Both files remain in the library."; }
        catch (Exception error)
        {
            ReviewStatus.Text = "Rename stopped: " + error.Message;
            queue?.StopAll();
        }
    }

    async void OnSkipPair(object sender, RoutedEventArgs e)
    {
        if (current == null || !SkipPair.IsEnabled) return;
        current.RemoveAt(1); // Defer this pair without changing either file.
        await ShowNextPairAsync();
    }

    void OnZoom(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        OnPanChanged(e.NewValue == 1 ? new Point() : OriginalPreview.Pan);
    }

    void OnPanChanged(Point pan)
    {
        OriginalPreview.SetView(Zoom.Value, pan);
        DuplicatePreview.SetView(Zoom.Value, pan);
    }

    void OnResetView(object sender, RoutedEventArgs e) { Zoom.Value = 1; OnPanChanged(new Point()); }

    async void OnCancelDeletion(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeletionRow row } || !row.Entry.Cancel()) return;
        await SettleChoicesAsync(row.Group);
    }

    async void OnTick(object? sender, EventArgs e)
    {
        if (queue == null) return;
        foreach (DeletionRow row in rows) row.Refresh(queue.Remaining(row.Entry));
        if (!quarantineWork.IsCompleted) return;
        quarantineWork = queue.ProcessDueAsync();
        await quarantineWork;
        await SettleChoicesAsync();
        if (queue is { Stopped: true })
        {
            foreach (DeletionRow row in rows) if (row.Entry.State == DeletionState.Failed) ReviewStatus.Text = "Queue stopped: " + row.Entry.Error;
            await StopAsync();
        }
    }

    async Task SettleChoicesAsync(List<MediaFile>? preferred = null)
    {
        bool changed = false;
        foreach (DeletionRow row in rows)
        {
            row.Refresh(queue?.Remaining(row.Entry) ?? TimeSpan.Zero);
            if (row.Handled || row.Entry.IsActive) continue;
            row.Handled = true;
            waitingGroups.Remove(row.Group);
            if (row.Entry.State == DeletionState.Quarantined)
            {
                row.Group.Remove(row.Entry.Reject);
                ReviewStatus.Text = $"Quarantined {row.Entry.Reject.Name}. It remains recoverable; the queue on the right shows its location.";
            }
            changed = true;
        }
        if (changed && renameWork.IsCompleted && (preferred != null || current == null)) await ShowNextPairAsync(preferred);
    }

    async void OnStopAll(object sender, RoutedEventArgs e)
    {
        ReviewStatus.Text = "STOP ALL: pending choices cancelled. A file move already started may finish; quarantined files remain recoverable.";
        await StopAsync();
    }

    public Task StopAsync() => stopWork ??= StopCoreAsync();

    async Task StopCoreAsync()
    {
        timer.Stop();
        queue?.StopAll();
        scanCancellation?.Cancel();
        ClearPair();
        await scanWork;
        await quarantineWork;
        await renameWork;
        foreach (DeletionRow row in rows) row.Refresh(TimeSpan.Zero);
        ReleaseSession();
    }

    void ReleaseSession()
    {
        timer.Stop();
        queue?.Dispose(); queue = null;
        renamer = null;
        journal?.Dispose(); journal = null;
        scanCancellation?.Dispose(); scanCancellation = null;
        HasSession = false;
        FolderInputs.IsEnabled = true;
        StopButton.IsEnabled = false;
        ClearPair();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}
