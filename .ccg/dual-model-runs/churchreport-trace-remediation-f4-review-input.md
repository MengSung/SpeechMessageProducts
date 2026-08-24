# F4 background trace boundary review

Review the current uncommitted changes for F4 only. Inspect `git diff` and the relevant source/tests. Do not modify files.

Requirements:

- `DataverseTrace.BeginBackgroundOperation(string operationName)` creates a child trace `{parentTraceId}#bg{seq}`, a new statistics object, keeps the parent's pseudonymous user, clears any inherited lease, emits `bg.begin`/`bg.end`, and restores only the background flow's prior context on Dispose.
- Parent `request.end` metrics must not include background CRM work; nested and parallel backgrounds must be isolated.
- `bg.end` contains all request aggregate fields plus `parentTraceId` and `op`; no user-controlled or secret data enters the operation name.
- ToolUtility stays host-neutral. No pool/gateway lifecycle changes.
- SaveIntegrate opens the scope before its background DI scope.
- Tests must genuinely protect the contracts and C# documentation must satisfy the project's Traditional Chinese lifecycle/isolation requirements.

Report only verified Critical/Warning/Info findings with file/line evidence.
