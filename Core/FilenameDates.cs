using System.Globalization;
using System.Text.RegularExpressions;

namespace FamilyLibraryTools.Core;

public static partial class FilenameDates
{
    [GeneratedRegex(@"^PXL_(?<date>\d{8})_(?<time>\d{6})(?<fraction>\d{3})?(?:[._ -].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PixelPattern();

    [GeneratedRegex(@"^(?:(?:IMG|VID|PHOTO|VIDEO|Screenshot)[_-])?(?<date>\d{8})[_-](?<time>\d{6})(?:[._ -].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AndroidPattern();

    [GeneratedRegex(@"^(?:(?:IMG|VID|PHOTO|VIDEO)[_-])?(?<date>\d{4}-\d{2}-\d{2})[ _T](?<time>\d{2}[.:-]\d{2}[.:-]\d{2})(?:[._ -].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExportPattern();

    [GeneratedRegex(@"(?: ?\(\d+\)|_DUPLICATE-\d+)+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CopySuffix();

    public static string OriginalStem(string filename) => CopySuffix().Replace(Path.GetFileNameWithoutExtension(filename), "");

    [GeneratedRegex(@"-\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex RetainedSuffix();

    [GeneratedRegex(@"^IMG_[EO](\d{4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AppleEdit();

    public static MediaDate Resolve(string filename, MediaDate metadata)
    {
        if (metadata.Value.HasValue && !metadata.NeedsReview) return metadata;
        MediaDate? inferred = Parse(filename);
        return inferred ?? metadata;
    }

    public static MediaDate? Parse(string filename)
    {
        string stem = Path.GetFileNameWithoutExtension(filename);
        Match match = PixelPattern().Match(stem);
        if (match.Success)
        {
            if (!TryDate(match, "yyyyMMddHHmmss", out DateTime value)) return null;
            if (match.Groups["fraction"].Success)
                value = value.AddMilliseconds(int.Parse(match.Groups["fraction"].Value, CultureInfo.InvariantCulture));
            // Camera timezone is unavailable. Only ask when an actual month could differ.
            bool boundary = (value.Day == 1 && value.TimeOfDay < TimeSpan.FromHours(12)) ||
                (value.Day == DateTime.DaysInMonth(value.Year, value.Month) && value.TimeOfDay >= TimeSpan.FromHours(10));
            return new(value, "Pixel filename (UTC)", boundary,
                boundary ? "The camera timezone could change the month. Confirm the capture month." : "UTC timestamp; every normal timezone gives the same year/month.");
        }

        match = AndroidPattern().Match(stem);
        if (match.Success && TryDate(match, "yyyyMMddHHmmss", out DateTime androidDate))
            return new(androidDate, "Samsung / Android filename", false, "Timestamp inferred from the filename, not embedded metadata.");

        match = ExportPattern().Match(stem);
        if (match.Success)
        {
            string text = match.Groups["date"].Value + match.Groups["time"].Value.Replace('.', ':').Replace('-', ':');
            if (DateTime.TryParseExact(text, "yyyy-MM-ddHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value))
                return new(value, "Timestamped export filename", false, "Explicit timestamp in an exported filename.");
        }
        return null;
    }

    public static string CollisionStem(string filename)
    {
        string stem = OriginalStem(filename);
        // A retained -number variant has its own name, including Pixel camera filenames.
        if (RetainedSuffix().IsMatch(stem)) return stem;
        Match pixel = PixelPattern().Match(stem);
        if (pixel.Success)
            return "PXL_" + pixel.Groups["date"].Value + "_" + pixel.Groups["time"].Value + pixel.Groups["fraction"].Value;
        Match apple = AppleEdit().Match(stem);
        return apple.Success ? "IMG_" + apple.Groups[1].Value : stem;
    }

    static bool TryDate(Match match, string format, out DateTime value) => DateTime.TryParseExact(
        match.Groups["date"].Value + match.Groups["time"].Value, format,
        CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
