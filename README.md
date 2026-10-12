# Family Library Tools

A Windows .NET 10 + WPF application for organizing photos and videos, then reviewing duplicate candidates separately.

## Organize the library

1. Open [FamilyLibraryTools.exe](file:///X:/Github/FamilyLibraryTools/bin/Release/net10.0-windows/FamilyLibraryTools.exe).
2. In **Auto-Organizer**, enter input and output folders or use the native Windows folder pickers. Both must exist; neither can contain the other.
3. Select **Scan folders**. Scanning reads supported media recursively in both folders without moving anything.
4. Select **Start verified moves**. Every supported input version is organized, including exact copies and different versions with matching names. The first reference keeps its name; additional versions receive `_DUPLICATE-1`, `_DUPLICATE-2`, etc., before their extension. Existing output files are preserved; numbered destinations are never overwritten.
5. If a capture month is missing or uncertain, inspect the best available date with the file on screen. PP-OCR Small reads eligible photos in the background; the next review image is prepared concurrently. A small animated bar shows pending recognition. You can accept an available date source or enter a date while OCR runs. Once the best suggestion is ready, press **Space** or click **Accept suggested date** to confirm it and continue.
6. To finish the easy work first, click **Skip all non-automatic** during a move run, including while a date is loading or awaiting confirmation. The current undecided file and all remaining files needing date confirmation stay in input. Automatic moves continue, and the completion summary reports the number left for date review. Scan again to review them; the skip setting applies only to that run. File verification failures still stop the run.

Photos go into the output's year and unpadded month. Videos go into a Videos subfolder of that month, created only when needed. For example, an April 2026 photo belongs directly in year 2026, month 4; a video belongs in that month's Videos subfolder.

`_DUPLICATE-#` means a candidate for review, not proof that a file is expendable. Existing output files are not renamed during organization. A later review also discovers existing unmarked candidates.

## Review and remove duplicates

1. Switch to **Duplicate Removal**. Its folder defaults to the organizer's output; you can select a different organized library.
2. Select **Scan duplicates**. An incomplete scan stops the removal workflow. Recovery folders are excluded, regardless of capitalization.
3. Inspect one pair side by side. Each preview shows its resolution when the installed Windows codec can read it. **Shared zoom** enlarges both previews by the same factor relative to fit; drag either zoomed preview to pan both to the same relative region. **Fit / reset** restores both views. Photos retain their original pixels for zooming. The resizable quarantine queue is on the right.
4. Choose **Keep LEFT** or **Keep RIGHT** to quarantine the other file after 60 seconds. **Keep both; rename RIGHT** retains both and renames the right file with an unused `-1`, `-2`, etc., replacing its duplicate suffix. Different-content files then stop colliding by name, including Pixel filenames. Exact copies are still detected on future scans. The rename verifies both identities and contents, preserves all file streams, records the old/new paths, and never overwrites a destination. **Skip for now** changes neither file and applies only to the current scan. A larger similar-looking image is only a suggestion.
5. Each explicit removal choice starts its own **60-second countdown**. You can review other groups while it waits. A group with a pending choice waits before presenting its next candidate, so a retained file cannot simultaneously be queued for removal.
6. **CANCEL DELETION** withdraws that choice and returns the pair for review. A new choice gets a full new 60 seconds. Cancelled rows remain visible as history but are no longer in the active deletion set.
7. **STOP ALL** cancels pending choices and ends the session. Scanning again is required to make new choices. Closing the application also cancels pending choices. No queue is persisted or automatically resumed.

Once 60 seconds have elapsed, both files are verified again. Verification is still cancellable. Once the atomic quarantine move starts, that one move may finish; STOP ALL cancels the remaining choices. The UI distinguishes waiting, verification, cancellation, moving, completion, and failure. A failure stops the entire queue.

## Quarantine and recovery safety

Rejected files now go to **recoverable quarantine instead of the Windows Recycle Bin**. The former Shell recycling handoff could act on a concurrently replaced pathname, so it has been removed. The application has no recycling, permanent deletion, or automatic quarantine cleanup operation.

Before moving, the app checks each file's identity, length and complete-file hash against the scan. Changed, replaced, missing, linked or locked files stop the queue. The chosen keeper remains locked against modification or replacement during the operation.

The rejected file is moved intact through its held handle into a unique quarantine folder on the same volume. Its move handle is never released for another operation to reopen the name. Whole-file relocation preserves alternate filesystem streams as well as the media bytes. Destinations are never overwritten.

Cancellation before the move leaves the file at its original location. A completed move leaves it in quarantine. Planned and completed paths are flushed to the journal. If recording completion fails after the move, the queue stops and reports where the file was retained. Restarting never resumes old choices or cleans up recovery files.

**The app notifies you after each quarantine move and shows a copyable recovery path in the queue.** Expand **Recovery information** for the quarantine root and journal location. To recover a file, copy or move it back to your library with Windows Explorer. The journal records its original location. Quarantine is stored under the chosen library's `.family-library-tools/quarantine`, in a separate unique folder for each choice, and is excluded from scans.

Quarantine retains disk space until you manage those files yourself. Files recycled by older versions remain governed by the Windows Recycle Bin; this version does not modify them.

## Candidate comparison

- Names are compared case-insensitively without the extension, separately for photos and videos. Numbered copy/duplicate suffixes, Pixel motion/RAW variants, and Apple edited/original counters are normalized for grouping.
- File size and whole-file SHA-256 identify exact-content candidates, including copies with different names. No file is discarded based on these automatically.
- Different contents can be related versions or entirely different pictures that happen to share a name. Every removal requires an explicit pairwise choice.
- Differently encoded files with unrelated names are not discovered by a library-wide perceptual search. Size alone never chooses a winner.
- **Keep RIGHT** leaves its existing filename intact. Only **Keep both; rename RIGHT** performs the explicit rename; neither option rewrites media metadata.

## Capture dates

Embedded photo date-taken metadata has priority. Otherwise, recognized filename timestamps are used:

| Convention | Example | Interpretation |
|---|---|---|
| Pixel | PXL_20260412_143015123.MP.COVER.jpg | UTC; requires confirmation near month boundaries because the camera timezone is unknown. |
| Samsung | 20260412_143015.jpg | Calendar date and time encoded in the filename. |
| Older Pixel / Android | IMG_20260412_143015.jpg or VID_20260412_143015.mp4 | Calendar timestamp. |
| Timestamped exports | 2026-04-12 14.30.15.heic | Explicit timestamp. |
| Apple camera counters | IMG_1234.HEIC or IMG_E1234.JPG | No encoded date; uses metadata or asks you to choose. |

Invalid timestamps are rejected. Last-modified time is not used as an automatic capture date. Windows video encoding dates are unconfirmed hints; videos without a usable filename timestamp require month confirmation. Original pattern research is in the [organization plan](./.docs/plan/structural-deduplicator.md).

During date review, available hints are ranked: embedded capture/content dates, recognized filename timestamps, embedded digitization/creation or video encoding dates, embedded modification/import dates, then disk modified and disk created timestamps. The app reads EXIF, XMP, IPTC and PNG date fields supported by installed Windows codecs. Missing, invalid and implausible values are omitted. Last-access time is excluded because reading a file can change it. Disk timestamps are displayed in local time; recorded metadata calendar dates and filename timestamps retain their stated timezone interpretation. The source, uncertainty, exact available time and proposed year/month folder are shown before confirmation.

XMP dates can specify only a month, a calendar day, or a time to the minute. Suggestions display only that recorded precision, and the journal retains the original value with a note identifying any unknown day or time. A year without a month cannot determine a destination and is not offered as an actionable date. PNG creation dates also support RFC1123 text timestamps, retaining their UTC calendar date.

Each fresh **Space** press accepts only the current suggestion after loading finishes; holding Space cannot confirm subsequent files. Space does not override manual date editing or another focused control. After entering a date, use **Use entered date and move**. The confirmed timestamp and its source are saved in the transfer journal; media bytes and embedded metadata are not rewritten. A disk timestamp is always a suggestion requiring your confirmation, never a newly automatic capture date.

Other key presses cannot rearm a held Space shortcut. If Space was released while another window had focus, press and release it once after returning to rearm the shortcut; that first press does not confirm a date.

Button actions also require a fresh Enter press. Holding Enter or Space through an OCR retry, a new photo, or a new duplicate pair cannot confirm the changed content. Pending mouse/Space presses are cancelled when their date or pair changes, and double-clicks cannot make a second choice on a replacement review. Leaving the window cancels pending presses; an Enter or Space key held while returning must be released first. These protections cover date choices, retry/region tools, Keep both, and quarantine choices. Cancel and STOP ALL remain available during review changes.

Invalid manual date text clears the prior selection and displays an error, so leaving the field cannot silently confirm the previous date.

### Dates printed inside images

For photos entering date review without a recognized filename timestamp, the app reads visible text using bundled, local PP-OCRv6 Small. **Read dates in images (OCR)** is enabled by default in Auto-Organizer; turn it off before starting to use metadata, filenames and manual dates without OCR. The setting stays fixed during a run. The app shows cropped image evidence and recognized text for each complete date. OCR dates rank above disk timestamps and below embedded dates and filename timestamps. **Every OCR date requires confirmation.** A date appearing in a screenshot may describe a message or event rather than when the picture was taken.

Supported text includes four-digit year/month/day, numeric month/day/year or day/month/year, and English month names with a full year. Optional time supports 24-hour and AM/PM notation, seconds, fractions, and explicit timezone offsets. Missing years are not invented; missing times remain unknown. Numeric dates with two valid interpretations are shown as separate choices. Multiple distinct dates or low-confidence recognition disable the default Space shortcut; choose explicitly or enter a date. Unsupported formats and unreadable text remain available for manual review. This version reads each text line separately and does not OCR video frames.

The date review includes three compact OCR tools:

- **Try PP-OCR Medium** manually runs the larger model when Small misses or misreads a date. It retains earlier interpretations from the same area, so disagreements require explicit selection. Medium is loaded only on request.
- **Select date region** lets you drag around the date in the photo and click **Read selected region**. Leave a little margin around the text. The selected area uses Small first and replaces the whole-image interpretations; a Medium retry uses that same area. You can select a region while a whole-image read is still running, cancelling that read. Cancelling selection returns to existing choices or restarts Small if its initial read was interrupted. The original image is never cropped or rewritten.
- **Show text** expands a small, read-only box containing each pass's recognized text, including text that could not be parsed as a date. It starts collapsed for each photo.

Explicit date sources and manual entry remain available during recognition and region selection. The best-suggestion button and Space shortcut wait until the result is stable. Entered manual dates are preserved. Cancel and **Skip all non-automatic** work during either operation. Every new photo starts with the full image and its own results.

Two separate OCR workers prepare only the current and next review images, with up to two CPU threads per worker. The current image starts recognition before the next worker is admitted. Making a choice cancels that image's preparation and recognition; cancellation is checked between native inference stages. Admitted work is drained before the chosen file moves, and late results cannot replace another image's choices. Bulk skip, cancellation and close also stop the prefetched image. PP-OCRv6 uses the library's v6 resize preset with a 2,000-pixel maximum side. Very small text in large photos may be missed. Original media bytes and metadata are never rewritten. OCR source, recognized text, and the confirmed interpretation are recorded in the transfer journal.

## Developer Tools: OCR benchmark

The Developer Tools tab is hidden for now. Its benchmark implementation remains for development and regression checks and compares only the two PP-OCR models. It recursively reads photos, skips videos/unsupported files and recovery folders, and reports scan or decoding issues. It does not organize, rename, delete, quarantine, update metadata, or write into the input folder. Organization and duplicate removal sessions cannot run simultaneously with this benchmark.

Both models see the same oriented, decoded image. No capture metadata, filename dates or disk timestamps can fill in a missing OCR result. A **pass** requires exactly one confident, unambiguous year/month/day with an explicitly recognized valid hour and minute. Seconds may remain unknown; date-only results never pass, and midnight passes only when actually recognized. A time range cannot silently pass as one timestamp. PP-OCR uses 90/100 mean character score; confidence is not measured correctness.

The summary shows completed readings, passed readings, date-only readings, errors, total/average elapsed OCR time, and review ratings. Each model's time includes image preparation, recognition, date parsing and any first-use model initialization. Scanning and shared image decoding are excluded from per-model time and included in overall elapsed time. Benchmark models remain loaded for that run with up to four CPU threads. Models execute sequentially with a rotating start order. Stop retains completed results; unrun models are not counted.

Development review can browse photos using **Previous**, **Next**, or the file list. Zoom and drag to inspect text; compare both results and expand **Recognized text**. Ratings remain separate from automatic passes. The preview checks the source hash before enabling ratings. Results and ratings stay in the app session; no report files are created automatically.

## Verified organization transfers

On the same volume, the app locks and verifies the source identity and contents, then moves the whole original through its Windows handle without overwriting. All filesystem streams travel with it.

Between volumes, a new staged output copy is flushed and verified by hash and streamed byte comparison. Its handle stays open and protected through commit and verification. The whole original then moves into a recovery folder on the source volume. That retained original preserves alternate streams and continues occupying source disk space. Duplicate Removal operates on the organized library; it does not remove these retained source originals.

Input/output overlap checks resolve filesystem paths through directory handles and compare directory identities along their ancestry, including extended-path aliases. Directory components are opened relative to their held parents, with links refused. File creation and rename destinations are also relative to held directories; changing a destination to a junction stops the operation rather than redirecting it. Unsupported filesystem behavior stops without a pathname-based fallback.

Only one app session can write to a given library at a time. Journals record planned/completed actions and interruptions. There is no automatic transfer undo or input-folder cleanup. Unsupported files and sidecars such as AAE/XMP/JSON stay in input. Links, junctions and cloud placeholders are not followed. Photos and videos are not transcoded.

## Development and validation

Requires Windows and the .NET 10 SDK. NuGet restores [RapidOcrNet 4.2.0](https://github.com/BobLd/RapidOcrNet) with ONNX Runtime and SkiaSharp. Both PP-OCRv6 model sets and their [license and provenance](./models/NOTICE.md) are included and copied on build/publish. No cloud service or runtime model download is used. If OCR cannot load, the review screen reports the error and still offers other date sources and manual entry.

From the [project folder](file:///X:/Github/FamilyLibraryTools):

```shell
dotnet build FamilyLibraryTools.csproj --configuration Release
dotnet run --project Tests/FamilyLibraryTools.Tests.csproj --configuration Release -- "X:/Github/FamilyLibraryTools"
```

The executable regression suite creates its own fixtures under the project's temporary test folder and never inspects personal media. It includes native quarantine moves, deliberate directory redirection, replacement attempts, and a real 60-second UI countdown, so it takes over a minute. It does not access the Recycle Bin. See the [current safety validation record](./.docs/review/file-safety-fixes-validation.md) for coverage and limitations.
