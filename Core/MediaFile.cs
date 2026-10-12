namespace FamilyLibraryTools.Core;

public enum MediaKind { Photo, Video }

public enum DatePrecision { Timestamp, Minute, Day, Month }

public sealed record MediaDate(DateTime? Value, string Source, bool NeedsReview = false, string Note = "",
    DatePrecision Precision = DatePrecision.Timestamp)
{
    public string DisplayValue => Value?.ToString(Precision switch
    {
        DatePrecision.Month => "yyyy-MM",
        DatePrecision.Day => "yyyy-MM-dd",
        DatePrecision.Minute => "yyyy-MM-dd HH:mm",
        _ => "yyyy-MM-dd HH:mm:ss.fff"
    }, System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown";
}

public sealed record MediaFile(string Path, MediaKind Kind, long Length, string Hash,
    DateTime LastWriteUtc, MediaDate Date, bool InOutput, FileIdentity? Identity = null)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string ContentKey => $"{Kind}:{Length}:{Hash}";
    public string NameKey => $"{Kind}:{FilenameDates.CollisionStem(Name)}";
}

public sealed record ScanIssue(string Path, string Reason);

public sealed record ScanResult(string InputRoot, string OutputRoot, IReadOnlyList<MediaFile> Files,
    IReadOnlyList<ScanIssue> Issues, int IgnoredFiles);

public readonly record struct FileIdentity(uint Volume, ulong Index);

public sealed record LibraryScan(IReadOnlyList<MediaFile> Files, IReadOnlyList<ScanIssue> Issues, int IgnoredFiles);

public sealed record MoveResult(string Destination, string? RetainedOriginal);

public sealed record RunResult(int Moved, int DuplicatesMoved, int Unselected, string JournalPath, int RetainedOriginals);

public static class MediaFormats
{
    static readonly HashSet<string> photos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".heic", ".heif", ".avif", ".webp", ".gif",
        ".bmp", ".tif", ".tiff", ".dng", ".cr2", ".cr3", ".nef", ".nrw", ".arw",
        ".orf", ".rw2", ".raf", ".pef", ".srw"
    };
    static readonly HashSet<string> videos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".mts", ".m2ts", ".3gp", ".3g2",
        ".wmv", ".webm", ".mpg", ".mpeg", ".vob", ".ts", ".m2v"
    };

    public static MediaKind? Classify(string path)
    {
        string extension = System.IO.Path.GetExtension(path);
        if (photos.Contains(extension)) return MediaKind.Photo;
        return videos.Contains(extension) ? MediaKind.Video : null;
    }
}
