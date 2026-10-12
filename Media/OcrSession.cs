using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FamilyLibraryTools.Core;
using Microsoft.ML.OnnxRuntime;
using RapidOcrNet;
using SkiaSharp;

namespace FamilyLibraryTools.Media;

public enum OcrMethod { PpOcrV6Small, PpOcrV6Medium }

/// <summary>Owns local models for one organizer or benchmark run. Disposal waits for admitted recognition.</summary>
public sealed class OcrSession(int threads = 4) : IDisposable
{
    readonly SemaphoreSlim gate = new(1, 1);
    RapidOcr? small, medium;
    bool disposed;

    public static string Name(OcrMethod method) => method switch
    {
        OcrMethod.PpOcrV6Small => "PP-OCRv6 Small",
        OcrMethod.PpOcrV6Medium => "PP-OCRv6 Medium",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    public ImageDateResult Read(BitmapSource photo, OcrMethod method, CancellationToken token)
    {
        gate.Wait(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            token.ThrowIfCancellationRequested();
            RapidOcr engine = method switch
            {
                OcrMethod.PpOcrV6Small => small ??= Load(false),
                OcrMethod.PpOcrV6Medium => medium ??= Load(true),
                _ => throw new ArgumentOutOfRangeException(nameof(method))
            };
            token.ThrowIfCancellationRequested();
            return ReadPaddle(engine, photo, method, token);
        }
        catch (Exception error) when (error is OnnxRuntimeException or IOException or NotSupportedException or
            DllNotFoundException or BadImageFormatException or TypeInitializationException)
        {
            token.ThrowIfCancellationRequested();
            return new([], true, $"{Name(method)} unavailable: {error.GetBaseException().Message}", Failed: true);
        }
        finally { gate.Release(); }
    }

    RapidOcr Load(bool medium)
    {
        var preset = medium ? RapidOcrModelSet.PPOCRv6Medium : RapidOcrModelSet.PPOCRv6Small;
        string root = AppContext.BaseDirectory;
        var models = preset with
        {
            DetModelPath = Path.Combine(root, preset.DetModelPath),
            RecModelPath = Path.Combine(root, preset.RecModelPath),
            ClsModelPath = Path.Combine(root, preset.ClsModelPath),
            KeysPath = Path.Combine(root, "models", "v6", medium ? "ppocrv6_medium_dict.txt" : "ppocrv6_small_dict.txt")
        };
        var engine = new RapidOcr();
        try { engine.InitModels(models, numThread: Math.Min(threads, Environment.ProcessorCount)); return engine; }
        catch { engine.Dispose(); throw; }
    }

    static ImageDateResult ReadPaddle(RapidOcr engine, BitmapSource photo, OcrMethod method, CancellationToken token)
    {
        using var pixels = new SKBitmap(photo.PixelWidth, photo.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        var converted = new FormatConvertedBitmap(photo, PixelFormats.Pbgra32, null, 0);
        token.ThrowIfCancellationRequested();
        converted.CopyPixels(Int32Rect.Empty, pixels.GetPixels(), checked(pixels.RowBytes * pixels.Height), pixels.RowBytes);
        token.ThrowIfCancellationRequested();
        // Keep low-confidence lines for review; only the pass decision applies the 90-point threshold.
        var result = engine.Detect(pixels, RapidOcrOptions.PPOCRv6 with { TextScore = 0 }, token);
        var candidates = new List<ImageDateCandidate>();
        var recognized = new StringBuilder().AppendLine($"[{Name(method)}]");
        bool uncertain = false;
        foreach (var block in result.TextBlocks)
        {
            token.ThrowIfCancellationRequested();
            double confidence = 0;
            if (block.CharScores is { Length: > 0 } scores)
            {
                foreach (float score in scores) confidence += score;
                confidence = confidence * 100 / scores.Length;
            }
            recognized.AppendLine($"[{confidence:F1}/100] {block.Text}");
            var dates = VisibleDates.Parse(block.Text);
            if (dates.Count == 0) continue;
            uncertain |= !double.IsFinite(confidence) || confidence < 90;
            int left = photo.PixelWidth, top = photo.PixelHeight, right = 0, bottom = 0;
            foreach (var point in block.BoxPoints)
            {
                left = Math.Min(left, point.X); top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X); bottom = Math.Max(bottom, point.Y);
            }
            left = Math.Clamp(left - 8, 0, photo.PixelWidth - 1);
            top = Math.Clamp(top - 8, 0, photo.PixelHeight - 1);
            right = Math.Clamp(right + 8, left + 1, photo.PixelWidth);
            bottom = Math.Clamp(bottom + 8, top + 1, photo.PixelHeight);
            var evidence = new CroppedBitmap(photo, new Int32Rect(left, top, right - left, bottom - top));
            evidence.Freeze();
            foreach (var date in dates)
            {
                var identified = date with { Source = $"Date read from image ({Name(method)})" };
                int index = candidates.FindIndex(candidate => candidate.Date.Value == date.Value && candidate.Date.Precision == date.Precision);
                if (index < 0) candidates.Add(new(identified, evidence, confidence));
                else if (confidence < candidates[index].Confidence) candidates[index] = new(identified, evidence, confidence);
            }
        }
        token.ThrowIfCancellationRequested();
        return ImageDates.Result(candidates, uncertain, recognized.ToString());
    }

    public void Dispose()
    {
        gate.Wait();
        try
        {
            if (disposed) return;
            disposed = true;
            small?.Dispose(); medium?.Dispose();
            small = medium = null;
        }
        finally { gate.Release(); }
    }
}
