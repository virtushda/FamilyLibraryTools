# Duplicate removal validation

Historical validation of the former recycling implementation. The subsequent end-to-end investigation found races not covered here. Recycling has now been removed in favor of user-approved quarantine; see the [current safety validation](./file-safety-fixes-validation.md).

Validated 2026-10-06 on the current Windows host. No personal media was inspected or processed.

## Results

- Release application and test builds completed without warnings or errors.
- All 40 executable regression checks passed.
- The real WPF countdown ran for at least 60 seconds, then recycled only the explicitly rejected generated PNG. The keeper and an unrelated PNG remained byte-identical.
- The real Windows recycle adapter returned a confirmed Recycle Bin path. Reading that test item confirmed preservation of media bytes and an alternate filesystem stream.
- The WPF pair view and queue were rendered and inspected. Keep-left, keep-right, CANCEL DELETION, STOP ALL, rescan and window-close behavior passed.

Final fixtures and rendered UI evidence: [test run](file:///X:/Github/FamilyLibraryTools/.codexTemp/tests/3968cbe6b97649fa92275349119d8371). Generated recycled fixtures were left in the Recycle Bin. The application does not empty it.

## Safety coverage

- All input versions reach the organization output, including renamed exact copies, equal-size different contents, different-size versions, and collisions with existing output.
- Additional names receive `_DUPLICATE-#` suffixes without overwriting. Unknown dates still require a choice.
- The full 60-second interval is required. Cancelling a choice prevents later execution; choosing again starts a fresh interval.
- STOP ALL cancels multiple queued choices, blocks new choices until a fresh session, and prevents a second transfer after an existing native handoff.
- Opposite/overlapping choices, self-pairs, same-file identities and unrelated groups are refused.
- Cancellation while verification is pending leaves both files in place.
- Changed keeper bytes, changed rejected bytes, and same-byte replacement with a different file identity all stop the queue before staging.
- Kept/rejected file locks and a junction at the staging location stop recycling.
- A simulated unavailable native recycle operation restores the rejected file. If an external file occupies the original path, both that replacement and the staged original survive; nothing is overwritten.
- Replacing the rejected file's former path after staging does not redirect the actual Windows recycle operation.
- Uppercase recovery/bookkeeping folder names are excluded from both scanners. Selecting a recovery subtree directly is rejected.
- Existing organization tests still cover native no-overwrite moves, source locks, late-added streams, staged-copy interruption, verified cross-volume transfer body, recovery collisions, junctions and session exclusion.
- Source inspection found no permanent file/folder deletion API in the application. The Windows adapter exposes only the COM method prefix through the dedicated recycle operation.

## Environment distinction

An initial full recycling test hit Windows access denial inside the tool sandbox. Originals were retained and the failure stopped the queue. The same generated-file check, followed by the complete suite, passed with normal host Recycle Bin access. This was not handled by weakening application checks or adding a permanent-delete fallback.

## Remaining limits

- A native recycle operation that has already been handed to Windows can finish after STOP ALL. The UI reports this boundary; cancellation does not claim to undo a committed operation.
- Windows Restore returns the item to its recorded staging folder. The journal records its former organized location for manual recovery.
- Actual transfers between two physical drives, remote/removable-volume recycling, arbitrary Recycle Bin policies, sudden power loss and every photo/video codec were not validated. The cross-volume organization body is tested using local generated fixtures.
- The tests exercise expected concurrency and file replacement; they are not a guarantee against malicious same-user processes, hardware failure or later Windows/user Recycle Bin cleanup.

Implementation: [queue](file:///X:/Github/FamilyLibraryTools/Core/DeletionQueue.cs), [verified recycler](file:///X:/Github/FamilyLibraryTools/Core/VerifiedRecycler.cs), [Windows adapter](file:///X:/Github/FamilyLibraryTools/Core/WindowsRecycleBin.cs), [UI workflow](file:///X:/Github/FamilyLibraryTools/Views/DuplicateReview.xaml.cs), [safety tests](file:///X:/Github/FamilyLibraryTools/Tests/DeletionSafety.cs).

Primary API reference: [Microsoft ITransferSource.RecycleItem](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itransfersource-recycleitem).
