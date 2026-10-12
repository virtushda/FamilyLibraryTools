# Duplicate organization and delayed recycling

Historical plan: the user subsequently approved recoverable quarantine instead of recycling. The [file safety fix plan](./file-safety-fixes.md) and [current validation](../review/file-safety-fixes-validation.md) supersede the recycling portions below.

## Verdict

Revise: separate preservation from removal, and make cancellation and the native handoff explicit.

## Critical issues

- Generic shell deletion can have permanent-delete behavior. Use the dedicated recycle operation, with no deletion fallback.
- A path can be replaced after review. Revalidate file identity and contents, retain the keeper under a read lock, and isolate the exact rejected file by handle before recycling.
- Pairwise choices can create contradictory dependencies. Permit only one pending comparison per group and reject overlapping queued pairs.
- Cancellation cannot undo a native operation after handoff. Serialize cancellation and handoff, expose their boundary, and never report an already-started transfer as cancelled.
- Staging changes the Windows Restore destination. Show the journal location and explain recovery clearly.
- Recovery directories must be excluded case-insensitively, including when directly selected as a scan root.

## Revised plan

1. Organize every version with existing verification and capture-date behavior. Mark additional candidates before the extension with `_DUPLICATE-#`; never overwrite.
2. Add a separate library scan and pairwise review tab. Require an explicit keeper selection; support keeping both.
3. Queue session-only choices against a monotonic 60-second clock. Cancel withdraws one choice; a new selection starts a new full interval. STOP ALL and window closure discard pending choices.
4. Recheck the keeper and rejected file under locks, then move the rejected whole file into unique same-volume staging. Recycle only that isolated item through Windows' dedicated API.
5. Restore staging without overwriting if recycling fails. Otherwise retain and report the recovery path. Flush journal records before and after filesystem transitions.
6. Test the real filesystem and WPF controls, including native recycling, cancellation races, replacement, locks, recovery failures and a real elapsed-minute countdown.

## Validation

See the [validation record](../review/duplicate-removal-validation.md). Relevant implementation: [queue](file:///X:/Github/FamilyLibraryTools/Core/DeletionQueue.cs), [verified recycling](file:///X:/Github/FamilyLibraryTools/Core/VerifiedRecycler.cs), and [Windows adapter](file:///X:/Github/FamilyLibraryTools/Core/WindowsRecycleBin.cs).

## Residual risks

- An OS recycle handoff can complete after STOP ALL. Pending work stops; previously recycled files require Recycle Bin recovery.
- Process/power interruption after staging can leave a recoverable file in staging. No automatic cleanup occurs; the flushed journal records its location.
- Windows Restore targets the staging folder. The journal supplies the former organized path for manual relocation.
- Network/removable-volume recycling, power-loss durability and the full range of installed codecs require environment-specific validation. Unsupported recycling stops without a permanent-delete fallback.
