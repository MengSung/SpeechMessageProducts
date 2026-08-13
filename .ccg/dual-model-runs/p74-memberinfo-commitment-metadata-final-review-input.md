# P7.4 MemberInfo commitment metadata final review

Review only the active child `.trellis/tasks/08-13-p74-memberinfo-commitment-metadata-read-boundary/`.

## Scope

This is a disabled-by-default, local-only `ORG-CALL-00040` consumer boundary for
`contact.customertypecode`. It must not enable traffic, modify CE, claim CE evidence,
remove ToolUtility, or advance P7.5/P8.

## Required invariants

1. Both `Package03SpecialResourcesEnabled` and
   `Package03MemberInfoCommitmentMetadataReadEnabled` must be true before typed
   composition; checked-in settings must remain false.
2. Gate=true must use one request-local Package03 typed metadata snapshot with fixed
   deployment profile/workload/target and request cancellation. It must not accept
   caller routing values, retain request data, retry, or fall back to legacy metadata.
3. `SearchDistrictTree`, `LoadGroupMembers`, and `LoadUngroupedMembers` must use the
   typed snapshot for search, ordering, row labels, and the exact unique `結案` value.
   In the typed branch, the closed-status resolver must not touch legacy
   `GetSharedOptionSetService`.
4. A missing or duplicate typed `結案` option must fail closed. Unknown typed values
   must render blank, not invoke legacy metadata.
5. Cancellation must propagate. Connections, process hosts, pools and clients must
   retain their existing single owner and deterministic cleanup; no user/profile state
   may cross requests.
6. Review only current diff/task-scoped files. Do not propose scope expansion into CE,
   images, P7.5, P8, or generic legacy cleanup.

## Evidence already run

- Focused relevant tests: 42 passed.
- `ChurchReport.MemberInfo.Tests`: 606 passed, 14 controlled live/CE skips.
- Release solution test: 0 failures; `SpeechMessage.Dynamics.Tests` 739 passed / 7 skips;
  `ChurchReport.MemberInfo.Tests` 600 passed / 14 skips.
- Release build: 0 warnings, 0 errors.
- Task-scoped UTF-8 no BOM, CRLF-only, final CRLF check: passed.
- `git diff --check`: passed.

## Output

Return only verified findings, classified Critical / Warning / Info. State explicitly
when a claim is not proven by the code. Do not treat skipped live tests as CE evidence.
