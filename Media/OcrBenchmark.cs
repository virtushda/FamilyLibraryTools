using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;
using FamilyLibraryTools.Core;

namespace FamilyLibraryTools.Media;

public sealed record BenchmarkDate(MediaDate Date, double? Confidence)
{
    public string Display => $"{Date.DisplayValue}  |  score: {Confidence?.ToString("F1") ?? "unknown"}/100" +
        (Date.Precision == DatePrecision.Minute ? "  |  seconds unknown" : Date.Precision == DatePrecision.Day ? "  |  time unknown" : "");
}

public enum BenchmarkVerdict { Unreviewed, Correct, Incorrect, Uncertain }

/// <summary>Stores text results only, so benchmarking a large folder does not retain decoded images.</summary>
public sealed class BenchmarkReading(OcrMethod method, double milliseconds, ImageDateResult result) : INotifyPropertyChanged
{
    BenchmarkVerdict verdict;
    public event PropertyChangedEventHandler? PropertyChanged;
    public OcrMethod Method { get; } = method;
    public string Name => OcrSession.Name(Method);
    public double Milliseconds { get; } = milliseconds;
    public string Timing => $"{Milliseconds:N0} ms";
    public IReadOnlyList<BenchmarkDate> Dates { get; } = result.Candidates.Select(candidate => new BenchmarkDate(candidate.Date, candidate.Confidence)).ToArray();
    public bool Failed { get; } = result.Failed;
    public bool RequiresChoice { get; } = result.RequiresChoice;
    public string Detail { get; } = result.Status;
    public string RecognizedText { get; } = result.RecognizedText;
    public BenchmarkVerdict Verdict
    {
        get => verdict;
        set { verdict = value; PropertyChanged?.Invoke(this, new(nameof(Verdict))); }
    }
    public bool ClearDate => !Failed && !RequiresChoice && Dates.Count == 1 && Dates[0].Date.Value.HasValue &&
        Dates[0].Date.Precision != DatePrecision.Month && Dates[0].Confidence >= 90;
    // Midnight counts only when explicitly read. Date-only parsing uses Day, never a guessed time.
    public bool Passed => ClearDate && Dates[0].Date.Precision is DatePrecision.Minute or DatePrecision.Timestamp;
    public string Outcome => Failed ? "Error" : Passed ? "PASS: date + valid time" : ClearDate ? "Date only: time missing" :
        Dates.Count == 0 ? "No complete date/time" : "Uncertain / multiple readings";
    public string DateText => Dates.Count == 0 ? "No date picked" : string.Join("\n\n", Dates.Select(date => date.Display));
    public string EvidenceText => string.Join("\n\n", Dates.Select(date => date.Date.Note));
}

public sealed class BenchmarkPhoto(MediaFile file)
{
    public MediaFile File { get; } = file;
    public string Label => File.Path;
    public List<BenchmarkReading> Readings { get; } = [];
    public string? Error { get; set; }
}

public sealed record BenchmarkSummary(string Method, int Completed, int Passed, int DateOnly, int Errors,
    string TotalTime, string AverageTime, int Correct, int Incorrect, int Uncertain, int Reviewed);

public sealed class BenchmarkRun
{
    public List<BenchmarkPhoto> Photos { get; } = [];
    public List<ScanIssue> Issues { get; } = [];
    public int PhotoCount { get; set; }
    public int SkippedFiles { get; set; }
    public bool Cancelled { get; set; }
    public TimeSpan Elapsed { get; set; }

    public IReadOnlyList<BenchmarkSummary> Summaries()
    {
        var summaries = new List<BenchmarkSummary>();
        foreach (var method in Enum.GetValues<OcrMethod>())
        {
            int completed = 0, passed = 0, dateOnly = 0, errors = 0, correct = 0, incorrect = 0, uncertain = 0, reviewed = 0;
            double total = 0;
            foreach (var photo in Photos)
            foreach (var reading in photo.Readings)
            {
                if (reading.Method != method) continue;
                completed++; total += reading.Milliseconds;
                if (reading.Failed) errors++;
                if (reading.Passed) passed++;
                else if (reading.ClearDate) dateOnly++;
                if (reading.Verdict == BenchmarkVerdict.Correct) correct++;
                if (reading.Verdict == BenchmarkVerdict.Incorrect) incorrect++;
                if (reading.Verdict == BenchmarkVerdict.Uncertain) uncertain++;
                if (reading.Verdict != BenchmarkVerdict.Unreviewed) reviewed++;
            }
            summaries.Add(new(OcrSession.Name(method), completed, passed, dateOnly, errors, $"{total / 1000:N2} s",
                completed == 0 ? "-" : $"{total / completed:N0} ms", correct, incorrect, uncertain, reviewed));
        }
        return summaries;
    }
}

/// <summary>Read-only OCR comparisons. Every method receives the same oriented pixels; no metadata date fallback.</summary>
public static class OcrBenchmark
{
    public static async Task<BenchmarkRun> RunAsync(string root, IProgress<string> progress, CancellationToken token)
    {
        var run = new BenchmarkRun();
        var elapsed = Stopwatch.StartNew();
        using var engines = new OcrSession();
        try
        {
            var scan = await new LibraryScanner((_, _) => new(null, "Benchmark")).ScanLibraryAsync(root, progress, token);
            run.Issues.AddRange(scan.Issues);
            run.PhotoCount = scan.Files.Count(file => file.Kind == MediaKind.Photo);
            run.SkippedFiles = scan.IgnoredFiles + scan.Files.Count - run.PhotoCount;
            var methods = Enum.GetValues<OcrMethod>();
            foreach (var file in scan.Files)
            {
                token.ThrowIfCancellationRequested();
                if (file.Kind != MediaKind.Photo) continue;
                var photo = new BenchmarkPhoto(file);
                run.Photos.Add(photo);
                BitmapSource bitmap;
                try { bitmap = await LoadPhotoAsync(file, token); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or
                    ArgumentException or System.Runtime.InteropServices.COMException)
                {
                    photo.Error = error.Message;
                    run.Issues.Add(new(file.Path, error.Message));
                    continue;
                }
                // Rotate the first method to distribute ordering/cache effects; never run competing OCR jobs.
                for (int index = 0; index < methods.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var method = methods[(index + run.Photos.Count - 1) % methods.Length];
                    progress.Report($"Photo {run.Photos.Count}/{run.PhotoCount}: {file.Name} - {OcrSession.Name(method)}");
                    var timer = Stopwatch.StartNew();
                    var result = engines.Read(bitmap, method, token);
                    timer.Stop();
                    photo.Readings.Add(new(method, timer.Elapsed.TotalMilliseconds, result));
                }
                photo.Readings.Sort((left, right) => left.Method.CompareTo(right.Method));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { run.Cancelled = true; }
        run.Elapsed = elapsed.Elapsed;
        return run;
    }

    public static async Task<BitmapSource> LoadPhotoAsync(MediaFile file, CancellationToken token)
    {
        using var stream = FileContents.OpenRead(file.Path);
        await FileContents.ValidateAsync(stream, file, token);
        token.ThrowIfCancellationRequested();
        return PhotoPreview.Load(stream);
    }
}
