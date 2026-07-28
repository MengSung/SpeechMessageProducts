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
  token documents are parsed directly from a fixed 32 KiB maximum rented buffer
  that is cleared before return, preventing unbounded body retention and an
  extra managed `byte[]` copy.
- Runtime host-slot release has one deterministic cleanup owner. `await using`
  remains the normal path; the synchronous compatibility path waits for release,
  propagates release errors, and executes off a caller-owned synchronization
  context so it cannot leave an unobserved background task or deadlock a UI/
  legacy context.

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
| Synchronous lease release | `RuntimeHostSlotLease.Dispose()` returned before a blocking coordinator release completed. | `Dispose()` waits for release completion and the release work is never fire-and-forget. |
| Synchronization-context safety | A yielding coordinator posted its release continuation to the caller's synchronization context. | The compatibility release runs on the ThreadPool and is synchronously observed; no caller-context post occurs. |
| Release-failure ownership | A fire-and-forget coordinator release could fault after the lease owner had returned. | Synchronous disposal surfaces the injected coordinator `InvalidOperationException` to its caller. |

## Fresh local verification

```text
dotnet test SpeechMessage.Dynamics.Tests --no-restore
  Passed: 62, Failed: 0, Skipped: 0

dotnet build SpeechMessageProducts.sln --configuration Release --no-restore
  Succeeded: 0 errors
```

The Release build retains 10 unrelated warnings already present in
`ToolUtility`, `PowerPlatform.Dataverse.Client`, and `Line.Messaging`; no new
warning was emitted by the Dynamics hardening files.

`git diff --check` passed. Modified source/test/spec files were checked as UTF-8
without BOM and CRLF-only.

The full solution command was also run:

```text
dotnet test SpeechMessageProducts.sln --configuration Release --no-restore
  Passed: 304, Failed: 22, Skipped: 0
```

The 22 failures are pre-existing, unrelated payment/LINE boundary tests. Their
repository-root helper looks for `ChurchReport.sln`, while this worktree
contains `SpeechMessageProducts.sln`; the focused failure is reproducible as
`DirectoryNotFoundException because ChurchReport.sln was not found.` No Phase 4 source or
test file overlaps those projects, so this increment does not change that
unrelated test infrastructure.

## Live baseline, 2026-07-28

- Fresh WinRM probes confirm that `D365DC01` (`192.168.50.10`) and `D365APP01`
  (`192.168.50.20`) are reachable through WSMan Stack 3.0. No VM restart was
  needed: both health probes succeeded. No credentials were retrieved or used
  for remote application/service inspection.
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

Dual-model reviews ran through the project self-healing entrypoint. The latest
lease-lifecycle review, `20260728-153906-dynamics-phase4-final-lease-lifecycle-reviewer`,
completed Gemini and Claude with `ok=true`, `degradedFallback=false`, and
`quotaBlocked=false`. It found no Critical issue. Its synchronous-disposal
warning was fixed with the three red/green regressions above; successful token
documents are now parsed in-place from the cleared rented buffer.

The final completion review,
`20260728-155852-dynamics-phase4-final-completion-reviewer`, then completed
both Gemini and Claude with `ok=true`, `degradedFallback=false`, and
`quotaBlocked=false`. Both reported PASS with no Critical or Warning finding in
this local hardening scope.
