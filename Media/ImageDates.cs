using System.Windows.Media.Imaging;
using FamilyLibraryTools.Core;

namespace FamilyLibraryTools.Media;

public sealed record ImageDateCandidate(MediaDate Date, BitmapSource Evidence, double? Confidence = null);
public sealed record ImageDateResult(IReadOnlyList<ImageDateCandidate> Candidates, bool RequiresChoice, string Status,
    string RecognizedText = "", bool Failed = false);

/// <summary>Preserves uncertainty and evidence when explicitly retrying the same image area.</summary>
public static class ImageDates
{
    public static ImageDateResult Merge(ImageDateResult previous, ImageDateResult retry)
    {
        var candidates = new List<ImageDateCandidate>(previous.Candidates);
        foreach (var candidate in retry.Candidates)
            if (!candidates.Exists(existing => existing.Date.Value == candidate.Date.Value && existing.Date.Precision == candidate.Date.Precision))
                candidates.Add(candidate);
        var combined = Result(candidates, (previous.Candidates.Count > 0 && previous.RequiresChoice) || retry.RequiresChoice,
            previous.RecognizedText + "\n[PP-OCR retry]\n" + retry.RecognizedText);
        return retry.Failed ? combined with { Status = retry.Status, Failed = true, RequiresChoice = true } : combined;
    }

    internal static ImageDateResult Result(List<ImageDateCandidate> candidates, bool uncertain, string recognized)
    {
        bool requiresChoice = uncertain || candidates.Count > 1;
        return new(candidates, requiresChoice, candidates.Count == 0
            ? "No complete date was recognized. Try Medium, select the date region, or choose another date."
            : requiresChoice
                ? "Multiple interpretations or uncertain text. Choose a date explicitly; Space is disabled."
                : "Date found. Confirm it belongs to this picture rather than something shown in it.", recognized);
    }
}
