using System.IO;
using System.Windows.Media.Imaging;
using FamilyLibraryTools.Core;

namespace FamilyLibraryTools.Media;

/// <summary>Prepares only the current and next date review, with separate, bounded native OCR workers.</summary>
public sealed class DateReviewSession : IAsyncDisposable
{
    readonly List<MediaFile> files = [];
    readonly OcrSession[] engines;
    readonly bool enabled;
    readonly CancellationToken token;
    DatePreparation? current, next;
    int index = -1;

    public DateReviewSession(ScanResult scan, bool enabled, CancellationToken token)
    {
        this.enabled = enabled;
        this.token = token;
        int threads = Math.Max(1, Math.Min(2, Environment.ProcessorCount / 2));
        engines = [new(threads), new(threads)];
        // Use the organizer's actual collision-group order, skipping automatically dated and output files.
        foreach (var group in CollisionGroups.Build(scan.Files))
            foreach (var file in group)
                if (!file.InOutput && (file.Date.NeedsReview || !file.Date.Value.HasValue)) files.Add(file);
    }

    public DatePreparation Begin(MediaFile file)
    {
        token.ThrowIfCancellationRequested();
        index++;
        if (index >= files.Count || files[index] != file) throw new InvalidOperationException("Date review order changed.");
        current = enabled && index > 0 ? next! : new(file, engines[index % 2], enabled, token, Task.CompletedTask);
        next = enabled && index + 1 < files.Count
            ? new(files[index + 1], engines[(index + 1) % 2], enabled, token, current.Started) : null;
        return current;
    }

    public void Cancel() { current?.Cancel(); next?.Cancel(); }

    public async ValueTask DisposeAsync()
    {
        Cancel();
        if (current != null) await current.DisposeAsync();
        if (next != null) await next.DisposeAsync();
        await Task.Run(() => { foreach (var engine in engines) engine.Dispose(); });
    }
}

/// <summary>Owns read-only preparation and cancellable OCR for one immutable scan entry.</summary>
public sealed class DatePreparation : IAsyncDisposable
{
    readonly OcrSession engine;
    readonly CancellationTokenSource lifetime, automatic;
    readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly List<(Task Work, CancellationTokenSource Cancellation)> retries = [];
    Task? cleanup;
    bool cancelled;
    public MediaFile File { get; }
    public bool UsesOcr { get; }
    public Task Started => started.Task;
    public Task<IReadOnlyList<MediaDate>> Hints { get; }
    public Task<BitmapSource?> Photo { get; }
    public Task<ImageDateResult?> Small { get; }
    public CancellationToken Token => lifetime.Token;

    public DatePreparation(MediaFile file, OcrSession engine, bool enabled, CancellationToken token, Task after)
    {
        File = file;
        this.engine = engine;
        UsesOcr = enabled && file.Kind == MediaKind.Photo && FilenameDates.Parse(file.Name) == null;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        automatic = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        Hints = ReadHintsAsync(after);
        Photo = ReadPhotoAsync(after);
        Small = ReadSmallAsync(after);
    }

    async Task<IReadOnlyList<MediaDate>> ReadHintsAsync(Task after)
    {
        await after.WaitAsync(Token).ConfigureAwait(false);
        return await Task.Run(() => DateSuggestions.Read(File), Token).ConfigureAwait(false);
    }

    async Task<BitmapSource?> ReadPhotoAsync(Task after)
    {
        await after.WaitAsync(Token).ConfigureAwait(false);
        if (File.Kind != MediaKind.Photo) return null;
        try { return await Task.Run(() => OcrBenchmark.LoadPhotoAsync(File, Token), Token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or
            ArgumentException or System.Runtime.InteropServices.COMException) { return null; }
    }

    async Task<ImageDateResult?> ReadSmallAsync(Task after)
    {
        try
        {
            await after.WaitAsync(automatic.Token).ConfigureAwait(false);
            if (!UsesOcr) return null;
            var photo = await Photo.WaitAsync(automatic.Token).ConfigureAwait(false);
            if (photo == null) return new([], true, "Image preview unavailable; choose another date or enter one.", Failed: true);
            return await Task.Run(() =>
            {
                started.TrySetResult();
                return engine.Read(photo, OcrMethod.PpOcrV6Small, automatic.Token);
            }, automatic.Token).ConfigureAwait(false);
        }
        finally { started.TrySetResult(); }
    }

    public async Task<ImageDateResult?> ReadAsync(BitmapSource photo, OcrMethod method)
    {
        CancelOcr();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var work = Task.Run(() => engine.Read(photo, method, cancellation.Token), cancellation.Token);
        retries.Add((work, cancellation));
        return await work.ConfigureAwait(false);
    }

    public void CancelOcr()
    {
        automatic.Cancel();
        foreach (var retry in retries) retry.Cancellation.Cancel();
    }

    public void Cancel()
    {
        if (cancelled) return;
        cancelled = true;
        lifetime.Cancel();
        started.TrySetResult();
    }
    public ValueTask DisposeAsync() => new(cleanup ??= StopAsync());

    async Task StopAsync()
    {
        Cancel();
        try
        {
            var work = new List<Task> { Hints, Photo, Small };
            foreach (var retry in retries) work.Add(retry.Work);
            await Task.WhenAll(work).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException or
            NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // After an explicit choice, unused preparation is discarded; the mover independently verifies the file.
        }
        finally
        {
            foreach (var retry in retries) retry.Cancellation.Dispose();
            automatic.Dispose();
            lifetime.Dispose();
        }
    }
}
