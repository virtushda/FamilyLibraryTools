# Investigation: end-to-end file safety

## Scope

Read-only review of current application source and the latest organization/removal changes, with particular attention to deletion, path handling, concurrency, and recovery. Application and existing test sources will not be modified. Any diagnostic harnesses use generated fixtures inside the project's temporary folder.

## Intended Behavior

Organize all supported media by capture month, preserving additional versions with duplicate suffixes. A separate pairwise workflow requires explicit keep/recycle choices, waits at least 60 seconds, supports cancellation and STOP ALL, revalidates the reviewed files, and uses Windows recycling without a permanent-delete fallback. Originals and recovery files must not be accidentally lost, overwritten, or reprocessed.

## Areas To Investigate

- [x] Change boundaries, application entry, build configuration and deployment.
- [x] Scanning, path normalization, containment, root selection and reserved folders.
- [x] Duplicate grouping, naming, date extraction and output organization.
- [x] Verified same-volume moves, cross-volume copies, recovery and interruption.
- [x] Native handles, file identity, hard links, streams and directory locks.
- [x] Journal integrity, session exclusion and concurrency across roots/instances.
- [x] Deletion queue state transitions, deadline, cancellation and native handoff.
- [x] Verified recycling, staging, restore-on-failure and Windows API semantics.
- [x] WPF pair selection, previews, async ownership, errors, STOP ALL and closure.
- [x] Regression coverage, targeted reproducers and documentation accuracy.

## Confirmed Issues

- **H1, confirmed, high confidence: a replacement at the staging pathname can be recycled without selection.** Intended invariant: only the scanned and explicitly rejected file may reach the Recycle Bin. [Verified recycling](file:///X:/Github/FamilyLibraryTools/Core/VerifiedRecycler.cs#L59) permits delete sharing, checks identity, releases the non-delete-sharing handle and passes the pathname to the [Windows adapter](file:///X:/Github/FamilyLibraryTools/Core/WindowsRecycleBin.cs#L14). Renaming that staged file and placing a different generated file at the pathname immediately before native resolution is allowed. A real Windows probe recycled the replacement and the queue reported Recycled. The verified original remained at its displaced path, and the keeper was unchanged. This requires another actor to rename/replace the staging item during the narrow handoff interval; ordinary replacement at the former organized pathname is correctly protected. Prevent mutable-path retargeting through handoff, or retain the quarantined original and stop when that guarantee cannot be made. A further unlocked pathname check alone cannot close the race.
- **M1, confirmed, high confidence: root aliases bypass overlap validation.** Intended invariant: input and output must be separate physical trees and existing output must remain untouched. [Root validation](file:///X:/Github/FamilyLibraryTools/Core/PathSafety.cs#L9) uses lexical normalization and prefix checks. Supplying one generated folder normally and the same folder with the Windows extended-path prefix was accepted. Scanning produced input/output records with equal file identities, and organization moved the pre-existing output original to a marked-duplicate name. An aliased nested output was also accepted. Resolve roots to a consistent filesystem-canonical representation or compare directory identities/ancestry before accepting either scan.
- **M2, confirmed, high confidence: held destination directories can be renamed or converted to junctions.** [Directory handles](file:///X:/Github/FamilyLibraryTools/Core/NativeFiles.cs#L49) request only read-attributes access and check the reparse flag only when opened; those handles do not provide the assumed directory stability. A native reparse-point mutation succeeded with all production directory locks held, and a complete unchanged organizer run followed the new junction into a sibling fixture outside the selected output. The organizer reported success. A separate probe also renamed a production-locked empty directory successfully. This requires another actor to change an empty destination directory during the operation. Bind destination operations to verified directory handles and fail closed on retargeting; merely removing write sharing or adding another unlocked attribute check is insufficient.

## Ruled Out / Corrected

- Current [scanner](file:///X:/Github/FamilyLibraryTools/Core/LibraryScanner.cs#L62) excludes reserved recovery folders case-insensitively, and direct selection is rejected by [root validation](file:///X:/Github/FamilyLibraryTools/Core/PathSafety.cs#L24). The historical casing issue is fixed.
- The [recycle adapter](file:///X:/Github/FamilyLibraryTools/Core/WindowsRecycleBin.cs#L25) calls the dedicated recycle operation. There is no permanent-delete API, fallback, cleanup, or queue replay in application source.
- [Cancellation and handoff](file:///X:/Github/FamilyLibraryTools/Core/DeletionQueue.cs#L107) share a lock; an already handed-off transfer is intentionally not cancellable. The UI explicitly describes this boundary.
- [Recovery information](file:///X:/Github/FamilyLibraryTools/Views/DuplicateReview.xaml#L26) and the README disclose that Windows Restore targets staging. This is a documented usability limitation, not an undisclosed direct restore guarantee.

## Nearby Leads

- Lead: Recovery-folder case exclusion was previously faulty.
  Reason: Retained originals must stay outside both workflows.
  Status: ruled out; current code fixes the historical casing issue.
- Lead: Recovery staging changes Windows Restore's destination.
  Reason: Interrupted and completed operations need a usable recovery path.
  Status: documented limitation, verified in current UI and README.
- Lead: Different Windows spellings can refer to the same folder, bypassing lexical containment.
  Reason: Organizer must not treat existing output as input or accept nested roots.
  Status: confirmed as M1 for extended-path aliases. Ordinary drive-root containment works. This volume did not provide distinct short filenames, so 8.3 aliases were not independently reproduced.
- Lead: Staging allows delete sharing immediately before the native adapter resolves its path.
  Reason: A replacement at the staging pathname could differ from the identity verified through the held handle.
  Status: confirmed as H1 with the real Windows recycle adapter and generated files.
- Lead: Directory handles block renaming/deletion but may not block in-place reparse-point mutation.
  Reason: Destination paths must stay inside the chosen physical tree throughout a move.
  Status: complete; integrated organizer reproduction and directory-access experiments confirmed M2.

## Area Notes

### Initial inventory

Status: complete

The application has two WPF workflows and a shared core. The current source inventory includes scanning/grouping/date parsing, verified moves, the deletion queue, verified recycling, a dedicated Windows recycle adapter, transfer journals, previews, and executable synthetic regression tests. Recent change context comes from the current conversation and project records; memory is not treated as current evidence.

### Source pass

Read all core services, application entry/build configuration, both WPF workflows, media metadata and preview code, regression sources, README and the current duplicate-removal plan/validation record. Traced move/recycle callers, disposal, stop/close ownership, staging and restore paths. Organization preserves whole originals on same-volume moves and retains cross-volume originals; existing tests cover late destination collisions, alternate streams, recovery collisions and cancellation boundaries. Native identity is checked for both files immediately before recycling; the keeper remains protected. Session locking is per selected root, with file/directory locks providing the later mutation boundary.

### Validation completed

The existing synthetic regression suite passed all 40 checks with host Recycle Bin access, including actual recycling and a real elapsed-minute WPF test. Fixtures: [regression run](file:///X:/Github/FamilyLibraryTools/.codexTemp/tests/5812002c6f334743a628c7c6e4b06577). Only generated fixtures were processed. Microsoft documentation confirms the dedicated [recycle operation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itransfersource-recycleitem), [transfer flags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_transfer_source_flags), and [final-path normalization](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew). No generic delete fallback was introduced or exercised.

### Targeted reproduction evidence

Diagnostic harness: [source](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/Program.cs). Application and existing regression source remain unchanged.

- Alias run: [fixtures](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/fixtures/1fdcadf45ade43f89c834a75ca7829aa). Same physical input/output accepted; scan count 2, issues 0, equal identities true. Organizer moved 1, marked 1; existing output original no longer existed at its original pathname. It remained intact under the organized marked name. Nested extended alias also accepted. Drive-root overlap was correctly rejected; bookkeeping selection was correctly rejected.
- Staging boundary simulation: [fixtures](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/fixtures/e3a27de3a1114cf0b06235bd7e48997d). A rename and replacement succeeded despite all production-held locks. The callback moved the replacement to a test-bin filename and the production queue reported success.
- Actual Windows staging boundary: [fixtures](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/fixtures/b1a75fe101c04d9b95a79c5674a0c3f7). Same deterministic intervention through the existing internal adapter seam, followed by the unchanged Windows adapter. Result: queue state Recycled, no error, keeper bytes unchanged, verified rejected bytes retained at a displaced pathname, Recycle Bin item bytes equal to the unselected replacement. This proves retargeting is possible under the permitted sharing modes; it does not estimate how frequently unrelated software would hit the interval naturally.
- Directory mutation: [fixtures](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/fixtures/5214035cbc3b455fbc7481ea802f274f). Set-junction operations succeeded both without and with production directory locks. A generated source then moved through the supposedly locked destination into a sibling fixture outside the designated destination. No overwrite or permanent deletion was used.
- Complete organizer redirection: [fixtures](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/fixtures/cd688874de664209872f9ff5d820be08). After scanning a generated 128 MiB source, an external worker waited until production opened the source for moving, then converted the empty month directory to a junction. Output: Organizer completed, moved 1; conversion succeeded; source no longer at its original location; media existed in the sibling outside-output fixture. The production move/journal/organizer code was unchanged.
- Directory-lock semantics: [fixtures](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/fixtures/b457990ff85e4008a3a7210adb264033). Renaming a production-locked empty directory succeeded. Changing share flags alone while retaining read-attributes access did not stop junction creation. A generic-read directory handle blocked a generic-write opener, but a write-attributes opener still changed the reparse point. This rules out a one-flag sharing change as a complete repair. [Microsoft sharing documentation](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew) explicitly distinguishes attribute access from ordinary read/write/delete sharing; [rename documentation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info) describes directory-handle-relative destinations.

## Completed Area Assessment

All tracked areas are complete as a source/runtime review. Completion here does not claim validation on untested devices or every external actor.

| Area | Assessment and evidence |
|---|---|
| Entry/build/change intent | [App entry](file:///X:/Github/FamilyLibraryTools/App.xaml#L4), [application project](file:///X:/Github/FamilyLibraryTools/FamilyLibraryTools.csproj), [core project](file:///X:/Github/FamilyLibraryTools/Core/FamilyLibraryTools.Core.csproj), and [test project](file:///X:/Github/FamilyLibraryTools/Tests/FamilyLibraryTools.Tests.csproj) reviewed. Two tabs and preservation-first organization are intentional changes. Release build succeeded with zero warnings/errors. No third-party package/install/deployment or application-source changes were made during this review. No Git history was used. |
| Scanning/roots/reserved paths | [Scanner](file:///X:/Github/FamilyLibraryTools/Core/LibraryScanner.cs) and [path validation](file:///X:/Github/FamilyLibraryTools/Core/PathSafety.cs) traced. Existing links are rejected, scan errors are surfaced, deletion requires a complete scan, and recovery exclusion is fixed. M1 covers root-alias overlap. M2 covers mutations after checking/locking. Ordinary drive-root containment passed. Distinct short names were unavailable on this fixture volume. |
| Grouping/naming/dates | [Groups](file:///X:/Github/FamilyLibraryTools/Core/CollisionGroups.cs), [filename handling](file:///X:/Github/FamilyLibraryTools/Core/FilenameDates.cs), [media classification](file:///X:/Github/FamilyLibraryTools/Core/MediaFile.cs), and [metadata](file:///X:/Github/FamilyLibraryTools/Media/CaptureMetadata.cs) reviewed. Name/hash graph grouping produces candidates, never automatic removal. Transitive groups and RAW/JPEG pairs can differ; manual selection is intentional. Duplicate suffixes preserve original grouping; no overwrite flags are used. Invalid/uncertain dates and video encoding dates require review or filename resolution. Phone-pattern tests and generated photo metadata/orientation tests passed. |
| Transfers/recovery | [Organizer](file:///X:/Github/FamilyLibraryTools/Core/Organizer.cs), [mover](file:///X:/Github/FamilyLibraryTools/Core/VerifiedMover.cs), [contents](file:///X:/Github/FamilyLibraryTools/Core/FileContents.cs), and [native operations](file:///X:/Github/FamilyLibraryTools/Core/NativeFiles.cs) traced through success, cancellation, late collision, copy verification and recovery collision. Same-volume operations preserve whole files. Cross-volume originals are retained, including extra streams; copies are hash/byte checked. No partial/source/recovery cleanup exists. Path mutation findings are separate from byte/overwrite protections, which passed generated-file tests. |
| Identity/streams/native ownership | File identities, whole-file handles, reparse flags, rename buffer layout, disposal and stream-sharing interactions reviewed. Self/alias identities are rejected for a keep/reject pair. Keeper and source ordinary byte writes/replacement are protected at the tested validation boundary. Alternate streams travel with whole-file moves/recycling and remain in retained cross-volume originals. H1 and M2 identify the remaining pathname stability gaps. |
| Journals/sessions/concurrency | [Journal](file:///X:/Github/FamilyLibraryTools/Core/TransferJournal.cs) flushes planned/completed records, uses a per-root exclusive lock, and disposes that lock if journal creation fails. No replay exists. Nested separately selected libraries are not globally mutually excluded; later file checks/keeper locks prevent treating a missing/changed keeper as valid. An overlap failure remains covered by M1. Power-loss/device durability is not proven. |
| Queue/cancellation | [Queue](file:///X:/Github/FamilyLibraryTools/Core/DeletionQueue.cs) traced from enqueue through grace period, verification, cancellation, handoff, terminal state and disposal. Explicit choices, monotonic time, fresh countdowns, same-group/distinct identity checks, active-pair exclusion and stop-on-failure are present. STOP ALL and handoff share a lock. Waiting/verification cancellation, reverse choice, stop during handoff and no replay tests passed. |
| Recycle/restore/API | [Verified recycler](file:///X:/Github/FamilyLibraryTools/Core/VerifiedRecycler.cs) and [Windows adapter](file:///X:/Github/FamilyLibraryTools/Core/WindowsRecycleBin.cs) traced. Dedicated recycle operation with no permanent-delete fallback; native failure/cancellation restoration avoids overwriting. Old-path replacement test passed. H1 demonstrates staging-path replacement can recycle another file and falsely confirm the selected identity. Process interruption can leave staging recoverable; Windows Restore targets staging as disclosed. |
| UI/preview/lifecycle | [Main window](file:///X:/Github/FamilyLibraryTools/MainWindow.xaml.cs), [main layout](file:///X:/Github/FamilyLibraryTools/MainWindow.xaml), [duplicate review](file:///X:/Github/FamilyLibraryTools/Views/DuplicateReview.xaml.cs), [review layout](file:///X:/Github/FamilyLibraryTools/Views/DuplicateReview.xaml), [rows](file:///X:/Github/FamilyLibraryTools/Views/DeletionRow.cs), [preview lifecycle](file:///X:/Github/FamilyLibraryTools/Views/MediaPreview.xaml.cs), [preview layout](file:///X:/Github/FamilyLibraryTools/Views/MediaPreview.xaml), and [photo handling](file:///X:/Github/FamilyLibraryTools/Media/PhotoPreview.cs) reviewed. Generation counters reject stale photo-preview completion; one pending pair per group; video opening completes separately through media events; stop/close await active file work before disposal. Existing WPF event tests and actual-minute recycle passed. Unsupported previews remain an explicit user-visible limitation, and representative video/RAW/HEIC runtime coverage is absent. |
| Tests/docs | Read [core regression tests](file:///X:/Github/FamilyLibraryTools/Tests/Program.cs), [transfer tests](file:///X:/Github/FamilyLibraryTools/Tests/TransferSafety.cs), [junction tests](file:///X:/Github/FamilyLibraryTools/Tests/Junctions.cs), [deletion tests](file:///X:/Github/FamilyLibraryTools/Tests/DeletionSafety.cs), [UI tests](file:///X:/Github/FamilyLibraryTools/Tests/UiWorkflow.cs), and current/historical project plans and validation records. All 40 current regressions passed; new isolated probes expose H1/M1/M2. Current [README](file:///X:/Github/FamilyLibraryTools/README.md) documents staging recovery and known scope limits; older plan/validation files describe their historical behavior and are not proof of the revised flow. Current root/link/exact-recycle guarantees need correction through the identified fixes. |

## Residual Validation Boundaries

- Actual dispatch between two physical drives, SMB/removable-volume semantics, unusual Recycle Bin policy/full-bin behavior, sudden power loss and representative installed codecs were not tested.
- H1 and M2 use deliberate concurrent fixture mutations to make the failure deterministic. Neither proves that unrelated software commonly performs those mutations; neither occurred during ordinary single-process regression runs.
- This review added only investigation artifacts and a temporary diagnostic harness. Existing application and regression sources were not edited. Generated recycled files were left in the Recycle Bin; no bin cleanup was performed.
