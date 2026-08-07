# P7.1 Review Record

## Review scope

The final review covered the six fixed Data8 read operations, the Lenovo-only
sanitized evidence handoff, the live-evidence test, the P7.1 task documents,
and the P7.1 portion of the backend routing specification. It excluded P6.2,
Official Worker, feature-flag activation, consumer traffic, CE writes, P7.2,
P8, deployment, commits, and pushes.

## Findings and disposition

- Fixed: the handoff now snapshots every process environment variable before a
  repository or fixture early exit, so `finally` restores rather than clears a
  caller-owned value.
- Fixed: temporary TRX deletion is isolated by a non-throwing `try/catch` in
  `finally`; it cannot prevent subsequent credential and environment cleanup.
- Fixed: fee and stor-lesson projections each enforce their registry
  `MaximumPageBytes` before the cumulative response budget. New offline tests
  prove that a single oversized page fails closed while the lease-owned client
  is still disposed exactly once.
- Not adopted: broadening `EncoderFallbackException` to `ArgumentException`
  lacks a reproduced runtime path. The strict UTF-8 encoder is configured to
  throw the already-caught derived exception, so this is not a demonstrated
  contract gap.
- Deferred as non-blocking test-depth work: additional handoff failure-reason
  branches can gain direct process-level tests later. The fixed regression
  tests cover the discovered lifecycle defects without running a child CE test.

## Result

The final re-review found no Critical or Warning. Its sole Info claim that the
PowerShell handoff test inherits a nonzero child exit code was independently
reproduced and rejected: the test returned its passed JSON with process exit
code `0`. No finding remains that blocks P7.1 commit and archive.
