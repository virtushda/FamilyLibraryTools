# File safety fixes

## Verdict

Proceed with the three requested fixes and the approved recoverable quarantine. Preserve the 60-second countdown, explicit selection, CANCEL DELETION, and STOP ALL.

## Critical issues

- Shell recycling reopens a pathname after the selected handle is released. Even creating its Shell item beforehand recycled a replacement in the generated-fixture reproduction. Remove that handoff entirely.
- Attribute-only directory handles neither bind mutations to the directory nor prevent reparse redirection. Stronger sharing alone is insufficient: a directory can still be converted to a junction using attribute access.
- Lexical root comparisons do not establish filesystem identity; extended paths and other aliases can bypass overlap checks.

## Revised plan

1. Open directory components relative to held parent handles, reject reparse points, and deny directory deletion/renaming while operations are active. Create files and rename selected file handles relative to these directories. Never fall back to an absolute-path mutation.
2. Resolve selected roots through directory handles. Compare normalized volume-relative filesystem paths for equality and containment; return consistent canonical paths to scanning and transfer code.
3. Keep the rejected file's exclusive move handle throughout verification and a single no-overwrite rename into a unique quarantine folder within the library. Keep the chosen file locked until completion. No Shell recycling or permanent deletion is performed.
4. Retain the cancellation boundary immediately before the atomic rename. Record recovery paths durably before mutation and display the completed quarantine location. No automatic retry, cleanup, or restart replay.
5. Bind transfer staging, recovery, and journal creation to directory handles too. Keep the staged output handle open through verification and commit; retain the complete source when copying across volumes.

## Validation

- Build the application and run the existing transfer, queue, metadata, and WPF workflow tests with quarantine semantics.
- Add regression checks for identical and nested aliases, attempted directory replacement, a junction introduced after opening a destination, and file creation beneath a redirected directory.
- Verify no-overwrite behavior, selected-file identity, alternate streams, recovery on failure, cancellation during verification, STOP ALL, close, restart, and the actual 60-second UI countdown.
- Use only generated files beneath the project test directory. Preserve fixtures; do not empty or modify existing Recycle Bin contents.

## Residual risks

- Cancellation cannot undo an atomic rename that has already started. The file remains recoverable in quarantine, and the UI must describe this precisely.
- Unsupported filesystem/native behavior fails closed. The cross-volume body is testable here, but a second physical volume is not available within the permitted test area.
- External changes after the application releases its handles are outside its control. Journal failures after a completed rename must identify the retained file's location rather than attempt destructive cleanup.

## Native API evidence

[NtCreateFile](https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile) supports relative opens through an object root directory. [FILE_RENAME_INFORMATION](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information) supports a relative filename and root directory. The generated-fixture probe verified normal NT relative renames and safe failure after reparse conversion on this host. The Win32 wrapper rejected the same relative rename, so the implementation must use the tested NT operation without a path fallback.
