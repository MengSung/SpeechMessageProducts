# Phase 4 local isolation hardening — verification

Date: 2026-07-28

## Scope completed in this increment

- The named ADFS token `HttpClient` now explicitly disables cookies,
  automatic redirects, proxies, automatic decompression, and
  pre-authentication. Its handler pool has a five-minute lifetime and its
  per-request factory wrapper uses the configured bounded timeout and is
  disposed after use.
- The CRM transport now has `PreAuthenticate = false` while retaining its
  request-scoped authorization header behavior.
- Local admission atomically reserves the combined in-flight plus queue
  capacity, serializes each manager's host-slot acquisition/renewal, and
  releases each reservation on cancellation, timeout, exception, permit
  disposal, or shutdown. A shutdown cancels a pending coordinator call rather
  than leaving a background task or waiter retained.
- The in-memory host-slot coordinator serializes its local transitions. It is
  still explicitly non-durable. Expired leases cannot be renewed or revived,
  and expired records are purged during coordinator operations.
- ADFS token endpoint failures no longer read or echo response bodies. Successful
  token documents are streamed into a fixed 32 KiB maximum buffer that is
  cleared before return, preventing unbounded body retention.

No Dynamics consumer was enabled. `DynamicsAccess:Package01FeeReadsEnabled`
remains `false`.

## Test-first evidence

| Regression | Red result before implementation | Green evidence |
| --- | --- | --- |
| Atomic local admission | Initial-free 32-caller burst observed `InFlight + Queued = 7` with a limit of 4. | Concurrent-burst tests: 2/2; manager suite: 8/8. |
| Local host slot atomicity | 64 concurrent requests received 21 leases when the limit was 1. | Concurrent host-slot test received exactly 1 lease; a released slot was reused. |
| Expiry fencing | A lease whose 20 ms TTL had elapsed renewed successfully (`renewed == true`). | `Expired_host_slot_cannot_be_renewed_or_resurrected`: 1/1; a replacement host acquired the expired capacity. |
| ADFS handler isolation | Named handler policy test observed `UseCookies == true`; CRM handler test observed `PreAuthenticate == true`. | Both handler policy tests passed, 2/2. |
| Manager lease single-flight | Parallel `AcquireAsync` calls entered 16 host-slot acquisition operations for one manager. | Concurrent-manager test allows exactly 1 acquisition. |
| Shutdown lifecycle | `DisposeAsync` waited indefinitely for a coordinator call that ignored the caller token. | Disposal cancels the linked manager lifetime token and leaves no pending acquisition. |
| ADFS token retention | Token endpoint errors echoed a 300-character body preview, while successful bodies were read without a size limit. | Error bodies are not read/echoed; a 32 KiB stream limit rejects oversized success responses; factory wrapper is disposed and timeout-bounded. |

## Fresh local verification

```text
dotnet test SpeechMessage.Dynamics.Tests --no-restore
  Passed: 59, Failed: 0, Skipped: 0

dotnet build SpeechMessageProducts.sln --configuration Release --no-restore
  Succeeded: 0 errors
```

The Release build retains 10 unrelated warnings already present in
`ToolUtility`, `PowerPlatform.Dataverse.Client`, and `Line.Messaging`; no new
warning was emitted by the Dynamics hardening files.

`git diff --check` passed. Modified source/test files were checked as UTF-8
without BOM and CRLF-only.

## Live baseline, 2026-07-28

- Fresh WinRM probes confirm that `D365DC01` and `D365APP01` are reachable
  through WSMan 3.0. The local client has the two resolved VM IP addresses in
  `TrustedHosts`, but its non-Kerberos logon session is no longer valid, so
  service/app-pool inspection and restart were not attempted. WinRM was not
  restarted because the health probe already passed.
- The visible in-app browser reaches the configured ADFS login page from the
  organization root. No login, cookie, local-storage, password-store, or
  response-body inspection was performed.
- The supported IFD setting correction still requires a local operator to
  provide a DWS administrative credential to the official
  `Get-CrmSetting`/`Set-CrmSetting` cmdlets. No password was extracted,
  persisted, or supplied to automation; consequently no CRM configuration was
  changed in this increment.

## Remaining release blockers

This is not Phase 4 completion. The following still require implementation and
fresh evidence before any feature enablement:

1. Durable cross-host coordinator with epoch/fencing/quarantine semantics.
2. Profile-generation isolation, replace-and-drain, and deterministic async
   runtime disposal.
3. Bounded response streaming and token/body redaction across all ADFS and CRM
   paths.
4. Gateway workload JWT/mTLS authentication and removal of caller-controlled
   workload subject data.
5. Two-profile/generation, reload/drain, cancellation/fault, socket/timer/heap
   soak, and Gateway-plus-Embedded aggregate-capacity suites.
6. Authenticated CE 8.2 and CE 9.1 live smoke matrix, same-organization parity,
   and two-replica capacity validation.

The required external Gemini/Claude review completed through the project
self-healing entrypoint in
`20260728-143828-dynamics-phase4-isolation-hardening-reviewer`. Both backends
completed with `ok=true`, `degradedFallback=false`, and `quotaBlocked=false`.
Neither found a Critical issue. A concurrent manager lease-init warning and
ADFS response/wrapper lifecycle observations were corrected with new red/green
regressions before the final local verification.
