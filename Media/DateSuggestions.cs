using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using FamilyLibraryTools.Core;

namespace FamilyLibraryTools.Media;

/// <summary>Read-only hints for explicit date review. These never authorize an automatic move.</summary>
public static class DateSuggestions
{
    public static IReadOnlyList<MediaDate> Read(MediaFile file, IReadOnlyList<ImageDateCandidate>? imageDates = null)
    {
        var dates = new List<(int Rank, MediaDate Date)>();
        using var stream = FileContents.OpenRead(file.Path);
        void Add(int rank, DateTime? value, string source, string note, DatePrecision precision = DatePrecision.Timestamp)
        {
            if (value is not DateTime date || date.Year < 1800 || date.Year > DateTime.Now.Year + 1) return;
            foreach (var existing in dates)
                if (existing.Date.Value == date && existing.Date.Source == source) return;
            note += precision switch
            {
                DatePrecision.Month => " Only the year and month are recorded; day and time are unknown.",
                DatePrecision.Day => " Only the date is recorded; time is unknown.",
                DatePrecision.Minute => " Time is recorded to the minute; seconds are unknown.",
                _ => ""
            };
            dates.Add((rank, new(date, source, true, note, precision)));
        }

        if (file.Kind == MediaKind.Photo)
        {
            try
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames[0].Metadata is BitmapMetadata metadata)
                {
                    void Query(int rank, string path, string source, string note)
                    {
                        string? text = ReadText(metadata, path);
                        if (TryParse(text, out DateTime date, out DatePrecision precision))
                            Add(rank, date, source, $"{note} Recorded value: {text}", precision);
                    }
                    foreach (string prefix in new[] { "/app1/ifd", "/ifd" })
                    {
                        Query(0, prefix + "/exif/{ushort=36867}", "EXIF date taken", "Camera capture time; timezone may be unspecified.");
                        Query(30, prefix + "/exif/{ushort=36868}", "EXIF digitized date", "Time digitized; may differ from when the picture was taken.");
                        Query(40, prefix + "/{ushort=306}", "Embedded modification date", "May describe an edit rather than capture.");
                    }
                    foreach (string prefix in new[] { "/xmp", "/ifd/xmp" })
                    {
                        Query(1, prefix + "/exif:DateTimeOriginal", "XMP date taken", "Recorded calendar time; any recorded timezone is shown below.");
                        Query(2, prefix + "/photoshop:DateCreated", "XMP content creation date", "Intended content creation time; confirm it matches the picture.");
                        Query(30, prefix + "/exif:DateTimeDigitized", "XMP digitized date", "May describe digitization rather than capture.");
                        Query(31, prefix + "/xmp:CreateDate", "XMP creation date", "May describe an export or digital file creation.");
                        Query(40, prefix + "/xmp:ModifyDate", "XMP modification date", "May describe an edit rather than capture.");
                        Query(41, prefix + "/xmp:MetadataDate", "XMP metadata update date", "When metadata was updated, not necessarily when the picture was taken.");
                        Query(50, prefix + "/MicrosoftPhoto:DateAcquired", "Embedded acquired date", "May describe an import or transfer.");
                    }
                    foreach (string prefix in new[] { "/app13/irb/8bimiptc/iptc", "/ifd/iptc", "/ifd/irb/8bimiptc/iptc" })
                    {
                        string? dateText = ReadText(metadata, prefix + "/date created");
                        string? timeText = ReadText(metadata, prefix + "/time created");
                        if (!TryParse(dateText, out DateTime date, out DatePrecision precision)) continue;
                        if (timeText?.Length >= 6 && DateTime.TryParseExact(timeText[..6], "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
                        {
                            date = date.Date + time.TimeOfDay;
                            precision = DatePrecision.Timestamp;
                        }
                        Add(3, date, "IPTC content creation date", $"Recorded value: {dateText} {timeText}. Confirm it matches the picture.", precision);
                    }
                    Query(31, "/[*]tEXt/Creation Time", "PNG creation date", "May describe file creation rather than capture.");
                    // WIC's general DateTaken property can alias lower-ranked creation/digitization fields.
                    if (DateTime.TryParse(metadata.DateTaken, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime taken) &&
                        !dates.Exists(item => item.Date.Value == taken))
                        Add(10, taken, "Embedded date taken", "Date reported by the installed Windows image codec.");
                }
            }
            catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or COMException)
            {
                // Unsupported image metadata must not hide filename or filesystem hints.
            }
        }
        else
        {
            MediaDate video = CaptureMetadata.Read(file.Path, file.Kind);
            Add(31, video.Value, video.Source, video.Note);
        }

        MediaDate? filename = FilenameDates.Parse(file.Name);
        if (filename != null) Add(20, filename.Value, filename.Source, filename.Note);
        Add(60, File.GetLastWriteTime(stream.SafeFileHandle), "Modified on disk (local time)", "May be an edit or transfer date. Confirm it matches the picture or video.");
        Add(70, File.GetCreationTime(stream.SafeFileHandle), "Created on disk (local time)", "Often the date copied to this disk, not the original capture date.");
        dates.Sort((left, right) => left.Rank.CompareTo(right.Rank));
        var result = new List<MediaDate>(dates.Count);
        foreach (var item in dates) result.Add(item.Date);
        return imageDates == null ? result : WithImageDates(result, imageDates);
    }

    public static IReadOnlyList<MediaDate> WithImageDates(IReadOnlyList<MediaDate> hints, IReadOnlyList<ImageDateCandidate> imageDates)
    {
        var result = new List<MediaDate>(hints);
        int index = result.FindIndex(date => date.Source is "Modified on disk (local time)" or "Created on disk (local time)");
        if (index < 0) index = result.Count;
        foreach (var candidate in imageDates) result.Insert(index++, candidate.Date);
        return result;
    }

    static string? ReadText(BitmapMetadata metadata, string query)
    {
        try { return metadata.GetQuery(query) as string; }
        catch (Exception error) when (error is NotSupportedException or ArgumentException or COMException) { return null; }
    }

    static bool TryParse(string? text, out DateTime date, out DatePrecision precision)
    {
        text = text?.Trim().TrimEnd('\0');
        // A year alone cannot choose an output month. Partial dates retain their precision for review and audit.
        precision = DatePrecision.Month;
        if (DateTime.TryParseExact(text, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        precision = DatePrecision.Day;
        if (DateTime.TryParseExact(text, ["yyyyMMdd", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        precision = DatePrecision.Minute;
        if (DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mmK", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset minute))
        {
            date = minute.DateTime;
            return true;
        }
        precision = DatePrecision.Timestamp;
        if (DateTime.TryParseExact(text, ["yyyy:MM:dd HH:mm:ss", "yyyy:MM:dd HH:mm:ss.FFFFFFF"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        if (DateTime.TryParseExact(text, "r", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date)) return true;
        if (!DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset offset)) return false;
        date = offset.DateTime; // Keep the recorded calendar month, not this computer's timezone conversion.
        return true;
    }
}
