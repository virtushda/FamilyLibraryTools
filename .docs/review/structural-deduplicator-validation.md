# Structural Deduplicator validation

Validated on Windows with the installed .NET 10 SDK on 2026-09-22. Tests used only generated files under the project.

## Build

Release build: successful, zero warnings, zero errors. The [project](file:///X:/Github/FamilyLibraryTools/FamilyLibraryTools.csproj) references a separate filesystem workflow library and has no third-party runtime packages.

## Automated checks

The [executable regression suite](file:///X:/Github/FamilyLibraryTools/Tests/Program.cs) passed all 29 tests:

- Pixel, Samsung, older Android, and timestamped export filename parsing; metadata precedence; invalid timestamps; Apple counter rejection; Pixel month/year boundary review.
- WPF default workflow selection and no preselected conflict inclusion.
- Real generated JPEG date metadata, EXIF orientation, and conservative preview comparison.
- Recursive scanning; photo/month routing; video-only Videos folders; sidecars retained.
- Exact duplicates under different names; existing output duplicates leave input copies untouched.
- Equal-size and different-size content conflicts; manual rejection; three simultaneous versions retained without overwrite.
- Existing output preservation when a different source version is selected.
- Manual date selection and skipped date retention.
- Changed source detected even when its byte count and modified timestamp are restored.
- Locked source retention; output collisions created after scanning.
- Cancellation during review and during actual staged copy.
- Commit failure injected after staging; source, partial file, and conflicting output all retained.
- Attempted writes and renames during an actual move are denied by the source lock.
- Same-volume whole-file moves preserve existing data streams and streams added after the source is locked; renamed files remain write-protected until the transfer releases its handle.
- A native rename refuses an existing destination without altering either file.
- The copy-and-retain path preserves existing and late-added streams in the recovery original, verifies output media bytes, and excludes recovery originals from subsequent scans.
- Recovery collisions preserve the source, verified output, and conflicting recovery file. A subsequent run leaves those originals intact.
- Cancellation after committing a copy retains both the source and output; recovery junctions cannot redirect originals.
- Overlapping input/output roots refused.
- Input directory junctions skipped; linked roots rejected; output junctions cannot redirect a transfer.
- Concurrent writer session excluded.
- Actual WPF button events exercise scan, two previews, explicit selection, missing-date review, and completed verified relocation.

The UI test renders the real WPF controls offscreen. The [ready layout](./structural-deduplicator-ready.png), [conflict layout](./structural-deduplicator-conflict.png), and [date-review layout](./structural-deduplicator-date.png) were inspected. Long-path details are bounded so action buttons remain available.

## Remaining validation

- Interactive native folder-picker behavior was not automated; the UI uses WPF's native OpenFolderDialog.
- Native video playback, seeking, HEIC/RAW decoding, and real phone metadata need representative files and the machine's codecs. No personal library was used.
- Same-volume moves and the real copy-and-retain transfer body ran on one physical volume. The latter is invoked directly by the tests, without a fake filesystem or production test switches. Dispatch between two physical drives, network storage, and power-loss recovery were not exercised.
- Exact-byte decisions and name-collision review are implemented. Global perceptual matching of unrelated filenames and automatic undo are outside this version.
