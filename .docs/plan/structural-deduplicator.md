# Structural Deduplicator

### Verdict
Revise: automatic choices must require full byte equality; larger files and similar previews only inform an explicit choice.

### Critical issues
- A filename or equal byte count cannot prove duplicate content. Hash matches must also pass a streamed byte comparison before consolidation.
- A selected source must stay locked against writes and replacement until its verified destination exists. Cancellation or failure must retain the source.
- Existing output files must be scanned and never overwritten. Input/output roots must not overlap or traverse directory links.
- Capture dates take priority over filename dates. Pixel filename timestamps use UTC, which can change the month at timezone boundaries. Apple camera counters do not encode dates.
- Similar-looking media, RAW/JPEG pairs, edits, and unrelated pictures sharing a camera counter need manual review. Larger does not guarantee higher quality.

### Revised plan
1. Replace the starter window with a workflow tab host whose default tab is Structural Deduplicator, native input/output folder pickers, scan/start/cancel controls, and visible progress.
2. Recursively inventory recognized photo/video formats in both roots. Report unreadable files, links, and unhandled files; leave them untouched. Group by case-insensitive media name and also discover byte-identical files with different names using size/SHA-256.
3. Resolve dates from embedded capture metadata, then strictly parsed Pixel/Samsung/Android timestamped names and explicit timestamped exports. Never interpret Apple IMG_1234/IMG_E1234 as dates. Prompt with preview and available file information when unresolved or timezone-ambiguous.
4. Collapse only byte-confirmed duplicates, favoring an existing output copy. Stop on differing variants and show two previews plus the complete candidate list. No inclusion is preselected; allow keeping multiple distinct files or leaving the whole group behind.
5. Move selected files into year/unpadded-month, adding Videos only for video. Use an exclusive session journal and verify the locked source. Within a volume, rename that whole original without overwriting. Between volumes, flush and verify a staged copy, commit and reverify it, then retain the whole original in a recovery folder on its source volume. Record recovery paths and exclude them from scans. Preserve rejected copies and never delete originals, recovery files, or partial files automatically.
6. Keep filesystem workflow code separate from WPF preview/metadata/UI code. Test against synthetic files under the project, never the user's media.

### Validation
- Build the WPF app and run executable regression tests for recursive scanning, name collisions, unequal/equal-size differences, renamed exact duplicates, existing output protection, and month/video routing.
- Test date patterns, invalid dates, metadata precedence, Apple counters, and Pixel month boundaries.
- Test changed/locked sources, cancellation, partial-copy failures, overlapping roots, reparse points, and collisions appearing after scanning.
- Exercise WPF initialization, image decoding/rotation, date review, and multi-candidate choice wiring where feasible. Native video codecs and interactive playback require testing on representative user files.

### Residual risks
- No software can guarantee against hardware failure or external tampering. Interrupted operations retain the source or an already verified destination and record the transfer state.
- Perceptual preview comparison is a review hint only. Differently encoded media with unrelated filenames are not automatically classified as duplicates.
- Previews depend on installed Windows codecs. Unsupported media remains eligible for byte comparisons, with explicit preview errors.
- Sidecars are reported and left in place. Filesystem streams travel with same-volume moves; between volumes, they remain in the retained whole original. Recovery originals continue consuming space on the source volume.

## Filename research
- [Pixel users' original examples and UTC observations](https://support.google.com/pixelphone/thread/73590196/pixel-4xl-photo-names-are-all-offset-by-4-hours?hl=en): PXL_YYYYMMDD_HHMMSSmmm; older IMG_date_time names. These are observed conventions, not a vendor guarantee.
- [Samsung user's original camera attachment](https://r1.community.samsung.com/t5/%EC%9B%A8%EC%96%B4%EB%9F%AC%EB%B8%94-%EC%9B%8C%EC%B9%98-%EB%B2%84%EC%A6%88-%EA%B8%B0%ED%83%80/%EB%B2%84%EC%A6%882%ED%94%84%EB%A1%9C-%EB%B3%B8%EC%B2%B4-%EA%B7%9C%EA%B2%A9/td-p/29672278): date/time prefix such as 20241007_163950, with additional suffixes after the timestamp.
- [Apple's documented original and edited video names](https://support.apple.com/guide/motion/motn5aea30bb/mac): IMG_0001.MOV and IMG_E0001.mov encode a counter, not a capture date.
- [Microsoft's DateEncoded definition](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-media-dateencoded): encoding time is not reliable capture metadata and is only a manual-review hint.
