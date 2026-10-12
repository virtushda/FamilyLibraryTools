# Investigation Result: end-to-end file safety

**Fix status, 2026-10-07:** This document preserves the original investigation evidence. The three findings were subsequently addressed with recoverable quarantine, directory-relative operations, and filesystem root normalization. See the [fix plan](../../plan/file-safety-fixes.md) and [current validation](../../review/file-safety-fixes-validation.md). References below to the removed recycling sources describe the pre-fix implementation.

## Summary

Three confirmed safety gaps remain: recycling can target a replacement at its staging pathname, root aliases bypass overlap rejection, and directory handles do not prevent destination redirection. All 40 existing synthetic regressions passed, including real Windows recycling and a real 60-second WPF countdown. The new failures were reproduced with generated files only. Application and existing regression sources were not changed.

## Confirmed Problems

### H1 / P1: an unselected staging replacement can be recycled

**Root cause:** [VerifiedRecycler.cs:59](file:///X:/Github/FamilyLibraryTools/Core/VerifiedRecycler.cs#L59) verifies the staged file through a handle that permits deletion/renaming, then releases the stronger source handle and passes a pathname to [WindowsRecycleBin.cs:14](file:///X:/Github/FamilyLibraryTools/Core/WindowsRecycleBin.cs#L14). That adapter resolves the name again. The protected handle does not bind the pathname to the selected file.

**Trigger and evidence:** Another process renames the staged item and puts a different file at that name during the final handoff. A deterministic probe performed those operations through the existing test seam, then called the unchanged Windows adapter. Windows recycled the unselected replacement. The queue reported Recycled, and the journal associated that result with the originally selected hash. The selected file remained at its displaced location; the keeper stayed intact. [Reproducer](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/Program.cs#L64), [native test fixtures](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/fixtures/b1a75fe101c04d9b95a79c5674a0c3f7).

**Impact:** An unselected file reaches the Recycle Bin and the audit claims the selected file was recycled. This requires concurrent staging manipulation; ordinary replacement at the former organized pathname is already protected. The experiment does not establish a natural occurrence rate.

**Recommended fix:** Bind the final operation to the verified file identity for its entire handoff. If that guarantee cannot be provided by the chosen Shell operation, stop safely with the original retained in quarantine instead of automatically calling it. Verify the returned recycled identity before recording success. Another pathname check alone would leave a race; checking afterward detects the error but does not prevent it. Add the staging-replacement regression alongside the existing old-path-replacement test. Confidence: high.

### M1 / P2: aliases of the same root are accepted as separate folders

**Root cause:** [PathSafety.cs:9](file:///X:/Github/FamilyLibraryTools/Core/PathSafety.cs#L9) normalizes path text, then uses lexical containment. It does not resolve filesystem aliases before deciding whether roots overlap.

**Trigger and evidence:** Use a normal absolute path for input and the same path with the Windows extended-path prefix for output. Validation accepted both; scanning produced two records with the same identity. Organization then moved the existing output original into a numbered duplicate filename. A nested output expressed through the same alias was also accepted. [Reproducer](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/Program.cs#L25), [fixtures](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/fixtures/1fdcadf45ade43f89c834a75ca7829aa).

**Impact:** Existing output is processed as input, contrary to the separate-tree and output-preservation rules. The reproduced file remained intact but its original location/name changed without a separate output tree.

**Recommended fix:** Resolve both roots to one consistent filesystem representation, preserving link rejection, before scanning or comparing containment. Compare directory identities/ancestry where needed and reject unsupported ambiguous roots. Microsoft provides [final-path resolution from directory handles](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew). Cover identical and nested alias pairs; exercise short-name/drive aliases on hosts that provide them. Confidence: high.

### M2 / P2: destination directory handles do not prevent redirection

**Root cause:** [NativeFiles.cs:49](file:///X:/Github/FamilyLibraryTools/Core/NativeFiles.cs#L49) opens directories with read-attributes access and checks reparse attributes once. These handles do not freeze the directory namespace. [The move](file:///X:/Github/FamilyLibraryTools/Core/NativeFiles.cs#L66) subsequently resolves an absolute destination pathname.

**Trigger and evidence:** Another actor converts the empty destination month folder to a junction after the mover has locked the source. The unchanged organizer completed successfully, removed the generated source from its input location and placed it in a sibling directory outside the selected output. A separate test also successfully renamed an empty directory while production directory handles were held. [Integrated reproducer](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/Program.cs#L151), [fixtures](file:///X:/Github/FamilyLibraryTools/.codexTemp/EndToEndSafetyProbe/fixtures/cd688874de664209872f9ff5d820be08).

**Impact:** The application can follow a newly introduced junction and relocate media outside the selected physical tree while reporting a normal move. The tested file remained intact; no overwrite or deletion was needed to demonstrate this failure.

**Recommended fix:** Bind destination creation and rename operations to verified directory handles, with explicit failure/recovery if their location changes. Microsoft documents [directory-relative rename destinations](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info). Apply the approach consistently to organization, recovery and recycle staging. Removing write sharing alone is insufficient: a write-attributes handle still changed a junction under a stricter read handle in the probe. Add concurrent directory rename/reparse tests. Confidence: high.

## Ruled Out

- No permanent-delete API, automatic recovery cleanup or persisted deletion queue was found. Windows' dedicated [RecycleItem operation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itransfersource-recycleitem) is used without a permanent-delete fallback.
- CANCEL DELETION gives a fresh interval on reselection; STOP ALL/closing cancel pending work; cancellation and native handoff are serialized. A handoff already made can finish, as the UI states.
- Existing tests passed for changed/replaced files, keeper protection, late overwrite collisions, native recycle failure recovery, extra filesystem streams and case-insensitive recovery-folder exclusion. The historical recovery-casing bug is fixed.
- Source originals are retained during failed cross-volume work. Whole originals remain available in recovery after successful cross-volume copies. Windows Restore returning to staging is disclosed in the UI and README.

## Validation and Remaining Limits

Release build: zero warnings/errors. All 40 existing checks passed in the [current regression run](file:///X:/Github/FamilyLibraryTools/.codexTemp/tests/5812002c6f334743a628c7c6e4b06577). New targeted probes independently confirmed H1, M1 and M2; they are diagnostic fixtures, not yet permanent regressions. Generated recycled files remain in the Recycle Bin.

Actual physical-drive dispatch, remote/removable storage, unusual/full-bin policies, abrupt power loss, and representative HEIC/RAW/video codecs remain unverified. H1 and M2 require concurrent external changes; the ordinary regression runs did not produce either failure. No claim is made that these deliberate probes measure how likely such interference is in normal use.

## Recommended Fix Order

1. H1: prevent an unselected item from crossing the recycle handoff.
2. M2: make destination operations stable against directory replacement/redirection.
3. M1: reject filesystem aliases and overlapping roots before scanning.

## Files Investigated

Reviewed all application/core/media/WPF sources, all executable regression sources, project configuration, and current/historical plans and validation records. The [completed investigation inventory](./end-to-end-file-safety-investigation.md#completed-area-assessment) links each area and its evidence. The [journal](./end-to-end-file-safety-investigation.md) contains detailed test results and ruled-out leads.
