# File safety fixes: validation

Validated 2026-10-07 on this Windows host using generated files under the project. No personal media was processed. The final regression suite does not access the Windows Recycle Bin.

## Results

- Release application build: zero warnings, zero errors.
- All 46 executable regression checks passed on the final source, including the ancestor-permission regression.
- The WPF test waited at least 60 actual seconds, quarantined only the explicitly rejected PNG, confirmed its retained bytes, and verified the keeper and unrelated PNG were unchanged.
- The UI test confirmed the recovery notification and destination path. The rendered quarantine queue was visually inspected; recovery paths wrap and can be copied.

Final generated fixtures and rendered evidence: [test run](file:///X:/Github/FamilyLibraryTools/.codexTemp/tests/cb3815590bc440b3b9947a29a57f8200). The preceding complete runs and inspected UI renders are also retained: [initial fixes](file:///X:/Github/FamilyLibraryTools/.codexTemp/tests/8054fdb7f02b46999bbd3ba89d254e77), [earlier run](file:///X:/Github/FamilyLibraryTools/.codexTemp/tests/878b9241d4624a72a5d81bcb8cfd4099).

## Follow-up review fixes

- Ancestors now require traversal and attribute access without requesting permission to list their contents. Directory rename protection and reparse rejection remain enabled. A generated ACL fixture denies listing its parent while allowing access to the library; scanning, organization, duplicate review, journaling, and quarantine all succeed. Directory renaming remains blocked. The fixture's original permissions are restored and checked in a finally block. [Regression](file:///X:/Github/FamilyLibraryTools/Tests/PathSafetyRegressions.cs#L13).
- The junction regression no longer competes with the mover using a polling writer. A narrowly scoped internal checkpoint introduces the junction after source verification while the production source/destination handles remain held. The test requires the intended Windows rename error, not an arbitrary earlier IOException, and confirms unchanged source bytes and an empty outside target. [Regression](file:///X:/Github/FamilyLibraryTools/Tests/PathSafetyRegressions.cs#L146).
- The deterministic junction test passed 64 consecutive focused repetitions in addition to the full suite. [Repeat fixtures](file:///X:/Github/FamilyLibraryTools/.codexTemp/PermissionFixRepeat/fixtures/383784904073424da0dcf74e835c21d6).

## Findings addressed

### H1: replacement during the recycle handoff

The user approved recoverable quarantine. The Shell recycler and its adapter have been removed. [VerifiedQuarantine](file:///X:/Github/FamilyLibraryTools/Core/VerifiedQuarantine.cs#L16) verifies both reviewed identities and contents, keeps the keeper protected, and retains the rejected file's exclusive move handle through a single no-overwrite rename. There is no release/reopen handoff and no automatic deletion or cleanup.

The regression attempts to rename or replace the quarantined pathname while its move handle remains held; both attempts fail. An actual quarantine operation also verifies the retained file's identity, media bytes, and alternate stream. Replacing its former library pathname cannot cause another transfer. Quarantine is excluded from subsequent review scans.

### M2: directory replacement and junction redirection

[NativeFiles](file:///X:/Github/FamilyLibraryTools/Core/NativeFiles.cs#L51) walks directory components through held parent handles with reparse traversal refused. Directory handles request traversal access and deny delete sharing. File creation and renames use a held destination directory plus a validated single filename, with no absolute-path fallback. This covers output, copy staging, original recovery, quarantine, and journal creation. Copied staging files remain held through verification and commit.

Regression evidence:

- Renaming a held directory or its ancestor fails.
- An attribute-only handle deliberately converts an already-held directory into a junction. Relative rename, file creation, and child-directory creation all fail; the selected source remains intact and the outside target remains empty.
- The verified-mover test deterministically changes the destination to a junction after the source is locked and verified. The move stops with the expected native error, the source remains intact, and nothing reaches the outside target.
- Existing no-overwrite, locked-source, late-stream, recovery-collision, and staged-copy interruption checks still pass.

### M1: root aliases bypassing overlap checks

[PathSafety](file:///X:/Github/FamilyLibraryTools/Core/PathSafety.cs#L5) compares normalized filesystem paths and directory identities along opened ancestry before accepting separate roots. Scanning and subsequent transfers receive consistent canonical paths.

Identical and nested normal/extended aliases are rejected in both directions. Case, trailing separator, and dot-component aliases are rejected. Disjoint extended roots normalize and organize correctly. The host did not supply distinct 8.3 names for the generated fixture, so a separate short-name alias was not available to exercise. Directory-identity comparison supplements pathname normalization.

## Queue and recovery behavior

- The complete 60-second grace period, cancellation, fresh countdown on reselection, STOP ALL, window close, and no restart replay pass.
- STOP ALL during verification prevents the move. A rename whose commit boundary was already crossed may finish; its result remains recoverable and the UI explains that boundary.
- Changed/replaced/locked files stop removal. A journal failure before the move retains both files and cancels the remaining queue.
- Planned quarantine paths are flushed before mutation. If completion logging fails after the rename, the error identifies the retained location. This post-commit logging-error branch was source-reviewed, not fault-injected.
- No production permanent-delete, Shell recycle, automatic cleanup, or path-based move call remains. Historical diagnostic probes are excluded from application compilation.

## Remaining limits

- Actual dispatch between separate physical drives, remote/removable storage behavior, sudden power loss, hardware faults, and all photo/video codecs were not tested. The cross-volume transfer body is exercised with local generated files, including cancellation, collision, byte verification, and retention of complete originals and streams.
- The concurrent junction/replacement regressions prove the reproduced failures are closed on this host; they do not establish behavior on every filesystem or against privileged external interference.
- Quarantine consumes disk space and requires manual recovery/management. There is no automatic purge. Files previously recycled by older versions are unchanged.

## Reproduction

From the [project root](file:///X:/Github/FamilyLibraryTools):

```shell
dotnet build FamilyLibraryTools.csproj --configuration Release --no-restore
dotnet run --project Tests/FamilyLibraryTools.Tests.csproj --configuration Release -- X:/Github/FamilyLibraryTools
```

Tests: [path and identity regressions](file:///X:/Github/FamilyLibraryTools/Tests/PathSafetyRegressions.cs), [queue and quarantine](file:///X:/Github/FamilyLibraryTools/Tests/DeletionSafety.cs), [transfer safety](file:///X:/Github/FamilyLibraryTools/Tests/TransferSafety.cs), [WPF workflows](file:///X:/Github/FamilyLibraryTools/Tests/UiWorkflow.cs).
