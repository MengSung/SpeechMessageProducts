# P7.2 seed control-plane correction review

Review the current worktree changes for the P7.2 Slice C seed bootstrap and fresh-fixture control-plane split.

Scope:
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1`
- `docs/scripts/Invoke-Package02Data8ListManagementFreshFixture.Tests.ps1`
- task records and the seed-bootstrap design artifacts

Required checks:
1. The permanent seed is current-user bound, strict UTF-8-no-BOM/CRLF, atomic, and contains only static list IDs, baseline leader, UTC Sunday, and fixed deployment metadata.
2. Legacy `targetOwnerId`, source contact IDs, and expected relationship IDs cannot become authority or reach the child environment.
3. Bootstrap performs only WhoAmI/Retrieve/RetrieveMultiple through the existing read-only preflight child, and publishes no seed until sanitized `go` evidence and read-back succeed.
4. Fresh preflight/provision/cleanup use the retained seed; cleanup removes only fresh descriptors, ledger, and fresh CRM entities. Zero-active weekly reports remain unlinked; exactly-one-active is exact lookup/read-back; duplicate/unavailable is fail-closed.
5. Timeout, ambiguous, no-go, read-back mismatch, publication failure, and cleanup uncertainty do not retry or touch later slices.
6. Check process/environment/resource cleanup and cross-user isolation. Identify only verified Critical/Warning/Info findings; do not infer from filenames or user-provided owner values.

Use local source and test evidence. Do not execute any CE mutation, enable any feature flag, switch traffic, or alter shared/official data. If an external backend is unavailable, classify that explicitly as incomplete/degraded rather than successful dual-model review.
