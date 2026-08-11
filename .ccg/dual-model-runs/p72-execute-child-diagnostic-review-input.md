# P7.2 Slice C ExecuteFixture diagnostic review

Review the current uncommitted changes only in:

- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1`

Context: A controlled CE Slice C ExecuteFixture child can write strict, bounded
`no-go` evidence and then exit nonzero because its final xUnit assertion requires
`go`. The parent previously returned only `child-process-failed`, losing the
safe failure category needed to diagnose a future independent cycle.

Required behavior:

1. A nonzero child exit always remains `no-go / child-process-failed`; it must
   not become success, cleanup authority, CE evidence, or retry authority.
2. Only a parent-owned non-reparse temporary root, exact evidence filename,
   strict full evidence schema, and allowlisted `no-go` reason may add the one
   deidentified `diagnosticCategory` field.
3. `go`, malformed, unexpected-path, reparse, and any non-allowlisted evidence
   must expose no diagnostic category.
4. No credentials, endpoint, CRM IDs, raw child output, exceptions, feature
   flags, traffic, CE 8.2, Official Worker, or remote mutation may be added.
5. Preserve deterministic process stream and temporary-directory cleanup,
   current-user isolation, Windows PowerShell 5.1 compatibility, UTF-8 no BOM,
   CRLF, and task-scoped behavior.

Return a concise Critical / Warning / Info report with exact source evidence.
