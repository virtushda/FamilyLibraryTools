using System.Globalization;
using System.Text.RegularExpressions;

namespace FamilyLibraryTools.Core;

/// <summary>Parses visible dates without inventing a year, time, timezone, or numeric date order.</summary>
public static class VisibleDates
{
    const string Months = "January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec";
    const string RangeDateStart = @"(?:[0-9]{4}[-/.][0-9]{1,2}[-/.][0-9]{1,2}|[0-9]{1,2}[-/.][0-9]{1,2}[-/.][0-9]{4}|[0-9]{1,2}(?:st|nd|rd|th)?\s+(?:" + Months + @")\b)";
    static readonly Regex timeRange = new(@"\G\s*[-\u2013\u2014]\s*[0-9]{1,2}\s*:", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    static readonly Regex pattern = new(
        @"(?<![\p{L}\d/.+])(?:" +
        @"(?<year>[0-9]{4})(?<sep>[-/.])(?<month>[0-9]{1,2})\k<sep>(?<day>[0-9]{1,2})|" +
        @"(?<first>[0-9]{1,2})(?<sep>[-/.])(?<second>[0-9]{1,2})\k<sep>(?<year>[0-9]{4})|" +
        @"(?<monthName>" + Months + @")\.?\s+(?<day>[0-9]{1,2})(?:st|nd|rd|th)?,?\s+(?<year>[0-9]{4})|" +
        @"(?<day>[0-9]{1,2})(?:st|nd|rd|th)?\s+(?<monthName>" + Months + @")\.?,?\s+(?<year>[0-9]{4}))" +
        // Do not backtrack from a malformed timestamp to a plausible date-only prefix.
        @"(?>(?:(?:[T\s]+|,\s*|\s+at\s+)(?<hour>[0-9]{1,2})\s*:\s*(?<minute>[0-9]{2})" +
        @"(?:\s*:\s*(?<seconds>[0-9]{2})(?:\.(?<fraction>[0-9]{1,7}))?)?" +
        // Offset hours cannot exceed 14. A supported year (1800 onward) cannot be consumed as an offset.
        @"(?:\s*(?<ampm>[AP]\s*\.?\s*M\.?))?(?:\s*(?<zone>(?:(?:UTC|GMT)\s*)?(?<offset>[+-](?:0[0-9]|1[0-4]):?[0-9]{2})|Z|UTC|GMT))?)?)" +
        @"(?!(?:[T\s]+|,\s*|\s+at\s+)[0-9]{1,2}\s*:)" +
        @"(?(hour)(?!\s*(?::|[AP]\b|\+\s*[0-9]|-(?!" + RangeDateStart + @")[0-9])))" +
        @"(?![\p{L}\d/:]|\.\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<MediaDate> Parse(string text)
    {
        var dates = new List<MediaDate>();
        foreach (Match match in pattern.Matches(text))
        {
            int Number(string name) => int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture);
            int year = Number("year");
            if (year < 1800 || year > DateTime.Now.Year + 1) continue;
            int hour = 0, minute = 0, second = 0;
            DatePrecision precision = DatePrecision.Day;
            if (match.Groups["hour"].Success)
            {
                hour = Number("hour"); minute = Number("minute");
                precision = DatePrecision.Minute;
                if (match.Groups["seconds"].Success) { second = Number("seconds"); precision = DatePrecision.Timestamp; }
                if (match.Groups["ampm"].Success)
                {
                    if (hour is < 1 or > 12) continue;
                    hour = hour % 12 + (char.ToUpperInvariant(match.Groups["ampm"].Value[0]) == 'P' ? 12 : 0);
                }
                if (hour > 23 || minute > 59 || second > 59) continue;
            }
            string zone = match.Groups["zone"].Value;
            if (match.Groups["offset"].Success)
            {
                string digits = match.Groups["offset"].Value[1..].Replace(":", "");
                int hours = int.Parse(digits[..2], CultureInfo.InvariantCulture);
                int minutes = int.Parse(digits[2..], CultureInfo.InvariantCulture);
                if (hours > 14 || minutes > 59 || (hours == 14 && minutes != 0)) continue;
                // A separated negative offset without a named zone may be a time range.
                int offsetIndex = match.Groups["offset"].Index;
                if (match.Groups["offset"].Value[0] == '-' && offsetIndex > 0 &&
                    char.IsWhiteSpace(text[offsetIndex - 1]) && !zone.StartsWith("UTC", StringComparison.OrdinalIgnoreCase) &&
                    !zone.StartsWith("GMT", StringComparison.OrdinalIgnoreCase))
                {
                    precision = DatePrecision.Day;
                    hour = minute = second = 0;
                    zone = "";
                }
            }
            if (match.Groups["hour"].Success && timeRange.IsMatch(text, match.Index + match.Length))
            {
                precision = DatePrecision.Day;
                hour = minute = second = 0;
                zone = "";
            }
            void Add(int month, int day, string interpretation)
            {
                if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return;
                var value = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
                if (precision == DatePrecision.Timestamp && match.Groups["fraction"].Success)
                    value = value.AddTicks(long.Parse(match.Groups["fraction"].Value.PadRight(7, '0'), CultureInfo.InvariantCulture));
                foreach (MediaDate existing in dates)
                    if (existing.Value == value && existing.Precision == precision) return;
                string missing = precision switch
                {
                    DatePrecision.Day => " Time is unknown.",
                    DatePrecision.Minute => " Seconds are unknown.",
                    _ => ""
                };
                string timezone = zone.Length == 0 ? " Timezone is unknown." : $" Visible timezone: {zone}; using the displayed calendar date without conversion.";
                dates.Add(new(value, "Date read from image (OCR)", true,
                    $"Read: {text.Trim()}\nInterpretation: {interpretation}.{missing}{timezone} Confirm this date belongs to the picture.", precision));
            }
            if (match.Groups["first"].Success)
            {
                int first = Number("first"), secondPart = Number("second");
                Add(first, secondPart, "month/day/year");
                Add(secondPart, first, "day/month/year");
            }
            else if (match.Groups["monthName"].Success)
            {
                string monthName = match.Groups["monthName"].Value[..3];
                for (int month = 1; month <= 12; month++)
                    if (string.Equals(CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month), monthName, StringComparison.OrdinalIgnoreCase))
                        Add(month, Number("day"), "written month name");
            }
            else Add(Number("month"), Number("day"), "year/month/day");
        }
        return dates;
    }
}
