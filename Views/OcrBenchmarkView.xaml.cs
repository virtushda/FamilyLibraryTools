using System.IO;
using System.Windows;
using System.Windows.Controls;
using FamilyLibraryTools.Media;
using Microsoft.Win32;

namespace FamilyLibraryTools.Views;

public partial class OcrBenchmarkView : UserControl
{
    CancellationTokenSource? operation, previewLoading;
    Task work = Task.CompletedTask;
    BenchmarkRun? run;
    int previewGeneration;
    public bool HasSession => operation != null;
    public event EventHandler? SessionChanged;
    public event Action<DependencyObject>? ReviewChanging;

    public OcrBenchmarkView() => InitializeComponent();

    void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose photos for the read-only OCR benchmark", Multiselect = false, AddToRecent = false };
        if (Directory.Exists(InputPath.Text)) dialog.InitialDirectory = InputPath.Text;
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) InputPath.Text = dialog.FolderName;
    }

    async void OnRun(object sender, RoutedEventArgs e)
    {
        if (HasSession) return;
        work = RunAsync(InputPath.Text);
        await work;
    }

    async Task RunAsync(string root)
    {
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        ClearPreview();
        run = null;
        PhotoList.ItemsSource = null;
        Summaries.ItemsSource = null;
        FolderControls.IsEnabled = RunButton.IsEnabled = Navigation.IsEnabled = false;
        StopButton.IsEnabled = true;
        Busy.Visibility = Visibility.Visible;
        SessionChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var progress = new Progress<string>(message => { if (operation == cancellation) Status.Text = message; });
            run = await Task.Run(() => OcrBenchmark.RunAsync(root, progress, cancellation.Token));
            Summaries.ItemsSource = run.Summaries();
            PhotoList.ItemsSource = run.Photos;
            Status.Text = $"{(run.Cancelled ? "Stopped; partial results" : "Finished")}: {run.Photos.Count}/{run.PhotoCount} photos; " +
                $"{run.SkippedFiles} non-photo files skipped; {run.Issues.Count} scan/load issues. Elapsed: {run.Elapsed.TotalSeconds:N1} s. All input files were left in place.";
            Status.ToolTip = string.Join("\n", run.Issues.Select(issue => $"{issue.Path}: {issue.Reason}"));
            Navigation.IsEnabled = run.Photos.Count > 0;
            if (run.Photos.Count > 0) PhotoList.SelectedIndex = 0;
        }
        catch (Exception error)
        {
            Status.Text = "Benchmark stopped: " + error.Message;
        }
        finally
        {
            operation = null;
            FolderControls.IsEnabled = RunButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            Busy.Visibility = Visibility.Collapsed;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    void OnStop(object sender, RoutedEventArgs e) { operation?.Cancel(); StopButton.IsEnabled = false; Status.Text = "Stopping recognition..."; }
    public async Task StopAsync() { operation?.Cancel(); await work; ClearPreview(); }

    public void ClearPreview()
    {
        ReviewChanging?.Invoke(ResultsPanel);
        previewGeneration++;
        previewLoading?.Cancel();
        ResultsPanel.IsEnabled = false;
        ResultsPanel.ItemsSource = null;
        Preview.Clear();
    }

    async void OnPhotoChanged(object sender, SelectionChangedEventArgs e)
    {
        ClearPreview();
        Previous.IsEnabled = PhotoList.SelectedIndex > 0;
        Next.IsEnabled = PhotoList.SelectedIndex >= 0 && PhotoList.SelectedIndex + 1 < PhotoList.Items.Count;
        if (PhotoList.SelectedItem is not BenchmarkPhoto photo) return;
        int generation = previewGeneration;
        using var cancellation = new CancellationTokenSource();
        previewLoading = cancellation;
        ResultsPanel.ItemsSource = photo.Readings;
        PreviewStatus.Text = "Verifying the photo still matches this benchmark...";
        try
        {
            var bitmap = await Task.Run(() => OcrBenchmark.LoadPhotoAsync(photo.File, cancellation.Token));
            if (generation != previewGeneration) return;
            ReviewChanging?.Invoke(ResultsPanel);
            Preview.ShowVerifiedPhoto(photo.File, bitmap);
            Zoom.Value = 1;
            PreviewStatus.Text = photo.Error ?? $"Photo {PhotoList.SelectedIndex + 1}/{PhotoList.Items.Count}. Review the date and all time components shown by each method.";
            ResultsPanel.IsEnabled = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (generation == previewGeneration) PreviewStatus.Text = "Review unavailable: " + error.Message;
        }
        finally { if (previewLoading == cancellation) previewLoading = null; }
    }

    void OnPrevious(object sender, RoutedEventArgs e) { if (PhotoList.SelectedIndex > 0) PhotoList.SelectedIndex--; }
    void OnNext(object sender, RoutedEventArgs e) { if (PhotoList.SelectedIndex + 1 < PhotoList.Items.Count) PhotoList.SelectedIndex++; }
    void OnZoom(object sender, RoutedPropertyChangedEventArgs<double> e) { if (Preview != null) Preview.SetView(e.NewValue, Preview.Pan); }

    void OnVerdict(object sender, RoutedEventArgs e)
    {
        if (!ResultsPanel.IsEnabled || run == null || PhotoList.SelectedItem is not BenchmarkPhoto photo ||
            sender is not Button { DataContext: BenchmarkReading reading, Tag: BenchmarkVerdict verdict } || !photo.Readings.Contains(reading)) return;
        reading.Verdict = verdict;
        Summaries.ItemsSource = run.Summaries();
    }
}
