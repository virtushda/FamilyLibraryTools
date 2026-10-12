using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FamilyLibraryTools.Core;
using FamilyLibraryTools.Media;
using FamilyLibraryTools.Views;
using Microsoft.Win32;

namespace FamilyLibraryTools;

public partial class MainWindow : Window
{
    readonly ObservableCollection<string> activity = [];
    readonly ReviewInputGuard reviewInput = new();
    ScanResult? scan;
    CancellationTokenSource? operation;
    CancellationTokenSource? dateLoading;
    DateReviewSession? dateReviews;
    DatePreparation? datePreparation;
    TaskCompletionSource<MediaDate?>? dateDecision;
    MediaFile? dateFile;
    MediaDate? suggestedDate;
    IReadOnlyList<MediaDate> dateHints = [];
    ImageDateResult? imageDates;
    Int32Rect? dateRegion;
    int ocrGeneration;
    bool hintsReady, ocrPending, selectingRegion;
    bool dateReviewReady, manualDateEditing;
    bool ready, closeRequested;
    bool skipDateReviews;

    public MainWindow()
    {
        InitializeComponent();
        DateRegion.Completed += OnDateRegionCompleted;
        RemovalReview.PairChanging += reviewInput.Invalidate;
        BenchmarkView.ReviewChanging += reviewInput.Invalidate;
        Activity.ItemsSource = activity;
        ready = true;
    }

    void OnBrowseInput(object sender, RoutedEventArgs e) => Browse(InputPath, "Choose the input photo and video folder");
    void OnBrowseOutput(object sender, RoutedEventArgs e) => Browse(OutputPath, "Choose a separate output folder");
    void Browse(TextBox field, string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false, AddToRecent = false };
        if (Directory.Exists(field.Text)) dialog.InitialDirectory = field.Text;
        if (dialog.ShowDialog(this) == true) field.Text = dialog.FolderName;
    }

    void OnPathsChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready) return;
        scan = null;
        MoveButton.IsEnabled = false;
        RemovalReview.SuggestLibrary(OutputPath.Text);
        Summary.Text = "Folder selection changed. Scan both folders before moving files.";
    }

    void OnRemovalSessionChanged(object? sender, EventArgs e)
    {
        if (!ready) return;
        OrganizeTab.IsEnabled = !RemovalReview.HasSession;
        DeveloperTab.IsEnabled = !RemovalReview.HasSession;
        scan = null;
        MoveButton.IsEnabled = false;
    }

    void OnBenchmarkSessionChanged(object? sender, EventArgs e)
    {
        if (!ready) return;
        OrganizeTab.IsEnabled = RemovalTab.IsEnabled = !BenchmarkView.HasSession;
    }

    async void OnScan(object sender, RoutedEventArgs e)
    {
        string input = InputPath.Text, output = OutputPath.Text;
        scan = null;
        activity.Clear();
        await ExecuteAsync(async token =>
        {
            var progress = new Progress<string>(message => Status.Text = message);
            scan = await Task.Run(() => new LibraryScanner(CaptureMetadata.Read).ScanAsync(input, output, progress, token));
            int inputs = 0, existing = 0, dates = 0;
            foreach (MediaFile file in scan.Files)
            {
                if (file.InOutput) existing++;
                else { inputs++; if (file.Date.NeedsReview || !file.Date.Value.HasValue) dates++; }
            }
            Summary.Text = $"Scanned {inputs:N0} input files and {existing:N0} output files. {dates:N0} input dates need review. {scan.Issues.Count:N0} scan issues; {scan.IgnoredFiles:N0} unrecognized files left untouched.";
            foreach (ScanIssue issue in scan.Issues) AddActivity($"Left untouched: {issue.Path}: {issue.Reason}");
            Status.Text = inputs > 0 ? "Scan complete. Start verified moves to organize every version, marking duplicates for later review." : "No supported input media found.";
            if (inputs > 0 && !CanStart()) Status.Text = "The output scan has issues. Resolve them in Activity, then scan again.";
        });
    }

    async void OnStart(object sender, RoutedEventArgs e)
    {
        if (scan == null) return;
        ScanResult snapshot = scan;
        scan = null;
        skipDateReviews = false;
        await ExecuteAsync(async token =>
        {
            dateReviews = new DateReviewSession(snapshot, OcrEnabled.IsChecked == true, token);
            SkipReviewButton.IsEnabled = true;
            var progress = new Progress<string>(message => { Status.Text = message; AddActivity(message); });
            RunResult result = await Task.Run(() => new Organizer().RunAsync(snapshot,
                (file, cancellation) => Dispatcher.InvokeAsync(() => ReviewDateAsync(file, cancellation)).Task.Unwrap(), progress, token));
            Summary.Text = $"Finished: {result.Moved:N0} organized, including {result.DuplicatesMoved:N0} marked duplicates; {result.RetainedOriginals:N0} originals kept for recovery. {result.Unselected:N0} files remain in input for date review. Scan again to review them.";
            Status.Text = $"Use Duplicate Removal to review copies. Transfer journal: {result.JournalPath}";
            AddActivity(Status.Text);
        });
    }

    async Task ExecuteAsync(Func<CancellationToken, Task> work)
    {
        if (operation != null || RemovalReview.HasSession || BenchmarkView.HasSession) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        FolderInputs.IsEnabled = RemovalTab.IsEnabled = DeveloperTab.IsEnabled = false;
        ScanButton.IsEnabled = MoveButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        BusyProgress.Visibility = Visibility.Visible;
        try { await work(cancellation.Token); }
        catch (OperationCanceledException)
        {
            Summary.Text = "Cancelled. Completed moves remain in output; unfinished files are retained. Scan again to continue.";
            Status.Text = "No automatic cleanup was performed. Check the activity and transfer journal.";
            scan = null;
        }
        catch (Exception error)
        {
            scan = null;
            Summary.Text = "Stopped. Resolve the issue and scan again before continuing.";
            Status.Text = error.Message;
            AddActivity(error.ToString());
            MessageBox.Show(this, error.Message, "Operation stopped", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ClearReview();
            if (dateReviews != null) await dateReviews.DisposeAsync();
            dateReviews = null;
            operation = null;
            FolderInputs.IsEnabled = ScanButton.IsEnabled = RemovalTab.IsEnabled = DeveloperTab.IsEnabled = true;
            CancelButton.IsEnabled = false;
            SkipReviewButton.IsEnabled = false;
            BusyProgress.Visibility = Visibility.Collapsed;
            MoveButton.IsEnabled = CanStart();
            if (closeRequested) Close();
        }
    }

    bool CanStart()
    {
        if (scan == null) return false;
        foreach (ScanIssue issue in scan.Issues) if (PathSafety.IsWithin(issue.Path, scan.OutputRoot)) return false;
        foreach (MediaFile file in scan.Files) if (!file.InOutput) return true;
        return false;
    }

    async Task<MediaDate?> ReviewDateAsync(MediaFile file, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (skipDateReviews) return null;
        using var loading = CancellationTokenSource.CreateLinkedTokenSource(token);
        dateLoading = loading;
        var prepared = datePreparation = dateReviews!.Begin(file);
        var decision = dateDecision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        dateFile = file;
        dateHints = [];
        imageDates = null;
        dateRegion = null;
        hintsReady = selectingRegion = false;
        ocrPending = prepared.UsesOcr;
        manualDateEditing = false;
        dateReviewReady = DateActions.IsEnabled = false;
        CaptureDatePicker.SelectedDate = null;
        CaptureDatePicker.Text = "";
        manualDateEditing = false;
        RecognizedText.Text = "";
        RecognizedText.Visibility = Visibility.Collapsed;
        ShowRecognizedText.Content = "Show text";
        OtherDates.IsExpanded = false;
        DateInformation.Text = $"{file.Name}\n{file.Date.Note}";
        WelcomePanel.Visibility = Visibility.Collapsed;
        DatePanel.Visibility = Visibility.Visible;
        BusyProgress.Visibility = Visibility.Collapsed;
        Status.Text = prepared.UsesOcr
            ? "Confirm a date when ready. You can choose another source or enter a date while OCR is running."
            : "Confirm the best available date, choose another source, or enter a date.";
        dateReviewReady = DateActions.IsEnabled = true;
        UpdateDateDestination();
        UpdateDateReview();
        int generation = ++ocrGeneration;
        Task population = PopulateDateReviewAsync(prepared, generation, loading.Token);
        try
        {
            await Task.WhenAny(decision.Task, population).WaitAsync(loading.Token);
            if (!decision.Task.IsCompleted) await population;
            return await decision.Task.WaitAsync(loading.Token);
        }
        catch (OperationCanceledException) when (skipDateReviews && !token.IsCancellationRequested) { return null; }
        finally
        {
            loading.Cancel();
            prepared.Cancel();
            datePreparation = null;
            dateLoading = null;
            dateDecision = null;
            dateFile = null;
            ClearReview();
            try { await population; }
            catch (OperationCanceledException) { }
            catch (Exception error) when (decision.Task.IsCompleted && error is IOException or UnauthorizedAccessException or
                NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
            await prepared.DisposeAsync();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
    }

    bool IsCurrent(DatePreparation prepared) => datePreparation == prepared &&
        dateDecision?.Task.IsCompleted == false && dateLoading?.IsCancellationRequested == false;

    async Task PopulateDateReviewAsync(DatePreparation prepared, int generation, CancellationToken token)
    {
        await Task.WhenAll(ReadHintsAsync(), ShowPhotoAsync(),
            prepared.UsesOcr ? DisplayOcrAsync(prepared, prepared.Small, generation, null, token) : Task.CompletedTask);

        async Task ReadHintsAsync()
        {
            var hints = await prepared.Hints.WaitAsync(token);
            if (!IsCurrent(prepared)) return;
            if (imageDates?.Candidates.Count is not > 0) dateHints = hints;
            hintsReady = true;
            UpdateDateReview();
        }

        async Task ShowPhotoAsync()
        {
            var photo = await prepared.Photo.WaitAsync(token);
            if (!IsCurrent(prepared)) return;
            if (photo != null) DatePreview.ShowVerifiedPhoto(prepared.File, photo);
            else if (prepared.File.Kind == MediaKind.Video) await DatePreview.ShowAsync(prepared.File);
            UpdateDateReview();
        }
    }

    async Task DisplayOcrAsync(DatePreparation prepared, Task<ImageDateResult?> work, int generation,
        ImageDateResult? previous, CancellationToken token)
    {
        try
        {
            var result = await work.WaitAsync(token);
            if (!IsCurrent(prepared) || generation != ocrGeneration) return;
            if (result != null && previous != null) result = ImageDates.Merge(previous, result);
            if (result != null && dateRegion.HasValue) result = result with { Status = "Selected region. " + result.Status };
            var hints = await prepared.Hints.WaitAsync(token);
            if (result?.Candidates.Count > 0) hints = DateSuggestions.WithImageDates(hints, result.Candidates);
            if (!IsCurrent(prepared) || generation != ocrGeneration) return;
            imageDates = result;
            dateHints = hints;
            hintsReady = true;
            ocrPending = false;
            UpdateDateReview();
        }
        catch (OperationCanceledException) { } // A choice, retry, crop or skip discarded this result.
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or
            ArgumentException or System.Runtime.InteropServices.COMException)
        {
            if (!IsCurrent(prepared) || generation != ocrGeneration) return;
            imageDates = new([], true, "Date reading unavailable: " + error.Message, Failed: true);
            ocrPending = false;
            UpdateDateReview();
        }
    }

    void UpdateDateReview()
    {
        reviewInput.Invalidate(DateActions);
        bool stable = hintsReady && !ocrPending && !selectingRegion;
        bool needsChoice = imageDates?.RequiresChoice == true;
        suggestedDate = stable && !needsChoice && dateHints.Count > 0 ? dateHints[0] : null;
        ImageDatesPanel.Visibility = datePreparation?.UsesOcr == true ? Visibility.Visible : Visibility.Collapsed;
        ImageDateProgress.Visibility = ocrPending ? Visibility.Visible : Visibility.Collapsed;
        ImageDateStatus.Text = selectingRegion ? "Select the date region in the image."
            : ocrPending ? "Reading the image date... You can choose another date or enter one now." : imageDates?.Status ?? "";
        ImageDateChoices.ItemsSource = imageDates?.Candidates;
        RecognizedText.Text = imageDates?.RecognizedText is { Length: > 0 } text ? text : "No text recognized.";
        RetryOcr.IsEnabled = !ocrPending && !selectingRegion && DatePreview.LoadedPhoto != null;
        SelectDateRegion.IsEnabled = !selectingRegion && DatePreview.LoadedPhoto != null;
        SuggestedDateText.Text = suggestedDate?.Value is DateTime date
            ? $"{suggestedDate.DisplayValue}\n{suggestedDate.Source}\n{suggestedDate.Note}\nOutput folder: {DateFolder(date)}"
            : !stable ? "Still evaluating the best date. Choose an available source below or enter a date to continue immediately."
                : needsChoice ? "Choose an image date above, another available date, or enter a date. Space cannot choose between uncertain dates."
                    : "No usable date found. Enter a date below.";
        var alternatives = new List<MediaDate>();
        for (int index = suggestedDate == null ? 0 : 1; index < dateHints.Count; index++)
            if (imageDates == null || !imageDates.Candidates.Any(candidate => candidate.Date.Value == dateHints[index].Value && candidate.Date.Source == dateHints[index].Source))
                alternatives.Add(dateHints[index]);
        DateAlternatives.ItemsSource = alternatives;
        OtherDates.Visibility = alternatives.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OtherDates.IsExpanded = needsChoice || !stable;
        AcceptSuggestedDate.IsEnabled = suggestedDate != null;
        if (!manualDateEditing && suggestedDate != null && !DateActions.IsKeyboardFocusWithin) AcceptSuggestedDate.Focus();
    }

    async void OnRetryOcr(object sender, RoutedEventArgs e)
    {
        if (!RetryOcr.IsEnabled || datePreparation == null) return;
        await ReadReviewRegionAsync(OcrMethod.PpOcrV6Medium, imageDates);
    }

    async Task ReadReviewRegionAsync(OcrMethod method, ImageDateResult? previous)
    {
        var prepared = datePreparation!;
        BitmapSource photo = DatePreview.LoadedPhoto!;
        if (dateRegion is Int32Rect region) { photo = new CroppedBitmap(photo, region); photo.Freeze(); }
        int generation = ++ocrGeneration;
        ocrPending = true;
        UpdateDateReview();
        await DisplayOcrAsync(prepared, prepared.ReadAsync(photo, method), generation, previous, dateLoading!.Token);
    }

    void OnSelectDateRegion(object sender, RoutedEventArgs e)
    {
        if (!SelectDateRegion.IsEnabled || datePreparation == null) return;
        reviewInput.Invalidate(DatePanel);
        datePreparation.CancelOcr();
        ocrGeneration++;
        ocrPending = false;
        selectingRegion = true;
        DateRegion.Show(DatePreview.LoadedPhoto!);
        UpdateDateReview();
    }

    async void OnDateRegionCompleted(Int32Rect? region)
    {
        if (datePreparation == null || !IsCurrent(datePreparation)) return;
        reviewInput.Invalidate(DatePanel);
        DateRegion.Clear();
        selectingRegion = false;
        if (region.HasValue)
        {
            dateRegion = region;
            imageDates = null;
            await ReadReviewRegionAsync(OcrMethod.PpOcrV6Small, null);
        }
        else
        {
            if (imageDates == null) await ReadReviewRegionAsync(OcrMethod.PpOcrV6Small, null);
            else { UpdateDateReview(); SelectDateRegion.Focus(); }
        }
    }


    void OnShowRecognizedText(object sender, RoutedEventArgs e)
    {
        bool show = RecognizedText.Visibility != Visibility.Visible;
        RecognizedText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ShowRecognizedText.Content = show ? "Hide text" : "Show text";
    }

    void OnDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        if (dateReviewReady)
        {
            manualDateEditing = true;
            reviewInput.Invalidate(DateActions);
        }
        UpdateDateDestination();
    }
    void OnManualDateFocus(object sender, KeyboardFocusChangedEventArgs e) => manualDateEditing = true;
    void OnDateTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!dateReviewReady) return;
        manualDateEditing = true;
        reviewInput.Invalidate(DateActions);
    }
    void OnDateValidationError(object? sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        // WPF otherwise restores the previous selection after invalid text loses focus.
        CaptureDatePicker.SelectedDate = null;
        DateDestination.Text = "Invalid date. Enter or choose a valid date before moving.";
    }
    string DateFolder(DateTime date) => $"{date.Year}/{date.Month}" + (dateFile?.Kind == MediaKind.Video ? "/Videos" : "");
    void UpdateDateDestination()
    {
        ConfirmDate.IsEnabled = CaptureDatePicker.SelectedDate.HasValue;
        DateDestination.Text = CaptureDatePicker.SelectedDate is DateTime date
            ? $"Output folder: {DateFolder(date)}" : "Choose a capture month to continue.";
    }
    void OnConfirmDate(object sender, RoutedEventArgs e)
    {
        if (CaptureDatePicker.SelectedDate is DateTime date) CompleteDate(new(date, "User-confirmed manual date"));
    }
    void OnAcceptSuggestedDate(object sender, RoutedEventArgs e) { if (suggestedDate != null) AcceptDate(suggestedDate); }
    void OnAcceptAlternativeDate(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MediaDate date }) AcceptDate(date);
    }
    void OnAcceptImageDate(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ImageDateCandidate candidate }) AcceptDate(candidate.Date);
    }
    void AcceptDate(MediaDate date) => CompleteDate(date with { Source = "User-confirmed: " + date.Source, NeedsReview = false });
    void CompleteDate(MediaDate? date)
    {
        if (!dateReviewReady || dateDecision?.TrySetResult(date) != true) return;
        dateReviewReady = DateActions.IsEnabled = false;
        datePreparation?.Cancel();
    }
    void OnSkipDate(object sender, RoutedEventArgs e) => CompleteDate(null);

    void OnSkipAllReviews(object sender, RoutedEventArgs e)
    {
        if (!SkipReviewButton.IsEnabled) return;
        skipDateReviews = true;
        SkipReviewButton.IsEnabled = false;
        dateReviewReady = DateActions.IsEnabled = false;
        dateDecision?.TrySetResult(null);
        dateReviews?.Cancel();
        dateLoading?.Cancel();
        Status.Text = "Leaving files that need confirmation in input; finishing automatic moves.";
    }

    void OnReviewKeyDown(object sender, KeyEventArgs e)
    {
        bool shortcut = e.Key == Key.Space && dateDecision != null && Keyboard.Modifiers == ModifierKeys.None &&
            (e.OriginalSource == AcceptSuggestedDate || (e.OriginalSource == this && Keyboard.FocusedElement == null));
        reviewInput.KeyDown(e, shortcut ? AcceptSuggestedDate : null);
        if (e.Handled || !shortcut) return;
        e.Handled = true;
        if (!dateReviewReady || manualDateEditing || suggestedDate == null) return;
        AcceptDate(suggestedDate);
    }
    void OnReviewKeyUp(object sender, KeyEventArgs e) => reviewInput.KeyUp(e);
    void OnReviewMouseDown(object sender, MouseButtonEventArgs e) => reviewInput.MouseDown(e);
    void OnReviewMouseUp(object sender, MouseButtonEventArgs e) => reviewInput.MouseUp(e);
    void OnActivated(object? sender, EventArgs e) => reviewInput.Activated();
    void OnDeactivated(object? sender, EventArgs e) => reviewInput.Invalidate(this);
    void OnCancel(object sender, RoutedEventArgs e) => operation?.Cancel();
    void ClearReview()
    {
        reviewInput.Invalidate(DatePanel);
        dateReviewReady = DateActions.IsEnabled = false;
        suggestedDate = null;
        ocrGeneration++;
        ocrPending = false;
        ImageDateProgress.Visibility = Visibility.Collapsed;
        ImageDateChoices.ItemsSource = null;
        ImageDatesPanel.Visibility = Visibility.Collapsed;
        RecognizedText.Text = "";
        RecognizedText.Visibility = Visibility.Collapsed;
        DateRegion.Clear();
        DatePreview.Clear();
        DatePanel.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = Visibility.Visible;
        BusyProgress.Visibility = operation == null ? Visibility.Collapsed : Visibility.Visible;
    }
    void AddActivity(string message)
    {
        activity.Add(message);
        if (activity.Count > 200) activity.RemoveAt(0);
        Activity.ScrollIntoView(message);
    }
    async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (BenchmarkView.HasSession)
        {
            e.Cancel = true;
            if (closeRequested) return;
            closeRequested = true;
            await BenchmarkView.StopAsync();
            await Dispatcher.InvokeAsync(Close);
            return;
        }
        BenchmarkView.ClearPreview();
        if (RemovalReview.HasSession)
        {
            e.Cancel = true;
            if (closeRequested) return;
            closeRequested = true;
            await RemovalReview.StopAsync();
            await Dispatcher.InvokeAsync(Close);
            return;
        }
        if (operation == null) { ClearReview(); return; }
        e.Cancel = true;
        closeRequested = true;
        operation.Cancel();
        Status.Text = "Finishing the current verification boundary before closing...";
    }
}
