[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: phase4c-compatibility-harness-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Phase 4C compatibility-harness design review

We are implementing the first safe, operator-only Phase 4C compatibility harness in the repository.

## Required outcome

Create a new PowerShell script and its local contract tests:

- `docs/scripts/Invoke-DynamicsOfficialWorkerCompatibility.ps1`
- `docs/scripts/Invoke-DynamicsOfficialWorkerCompatibility.Tests.ps1`

It is deliberately a *read-only identity sub-gate*, not a claim that the whole Phase 4C matrix has passed.  It must call only the existing Gateway route:

```text
GET  {GatewayBaseUri}/ready        (before and after)
POST {GatewayBaseUri}/v1/organizations/{escaped ProfileAlias}/operations/runtime.health.whoami
```

The POST has exactly these headers and body:

```http
Content-Type: application/json; charset=utf-8
Accept: application/json
```

```json
{"parameters":{}}
```

The script accepts mandatory `GatewayBaseUri` (absolute HTTPS URI with no query/fragment), `ProfileAlias`, `DeploymentManifestPath`, and `GatewayOverlayPath`, optional bounded `TimeoutSeconds` (1..60), and `Json`.

It must never accept a password, token, cookie, credential, Authorization header, arbitrary operation/path/body, certificate bypass, redirect opt-in, or proxy option.  Its HTTP handler must use the current Windows identity/default credentials, disable redirects and proxies, keep normal certificate validation, use a bounded cancellation timeout, and dispose handler/client/request/content/response/CTS deterministically on every path.

## Existing contracts that must remain true

- The sole public operation route is `POST /v1/organizations/{alias}/operations/{capabilityOperationId}` (`SpeechMessage.Dynamics.Gateway/Program.cs`).
- Gateway derives workload identity from server-side Windows authentication and `DynamicsGateway:WorkloadBindingSets`; body and headers must not control it.
- `runtime.health.whoami` is the smallest registered, read-only operation.  Its successful JSON has the equivalent of:
  `succeeded=true`, `data.operationId=runtime.health.whoami`, `data.responseKind=WhoAmI`, worker CE version, and `data.whoAmI.userId`, `businessUnitId`, `organizationId` GUIDs.
- The official workers are exactly `OfficialCrm82Worker` and `OfficialCrm91Worker`.  They have independent package locks and expected CE versions 8.2 and 9.1.
- `New-DynamicsOfficialWorkerDeployment.ps1` validates the published manifest and produces a fixed adjacent Gateway overlay, but it also writes/refuses existing deployment output.  Do **not** invoke it from this harness.  It must validate the supplied manifest/overlay read-only instead.
- An overlay has `DynamicsProfiles.Profiles.<alias>` containing `WorkerProfileGenerationId`, `WorkerKind`, `WorkerExecutablePath`, `WorkerExecutableSha256`, `PackageLockId`, `OrganizationBaseUri`, and `Admission.ExpectedOrganizationId`.
- The manifest pins both worker kind, package-lock ID/hash, executable relative path, executable SHA-256/size, and inventory.
- Gateway workload bindings are in the base `appsettings.json`, not the overlay.  The harness can derive the adjacent base `appsettings.json` from `GatewayOverlayPath`; it must prove the current Windows identity has an exact active binding that allows exactly the selected alias and `runtime.health.whoami` before making a network call.
- Product/Gateway responses are private/no-store.  Never use direct Web API, Data8, a generic proxy, a website automation endpoint, Deployment PowerShell, IFD wizard, or CRM diagnostics.

## Evidence and lifecycle requirements

The script may emit a sanitized, bounded result such as:

```json
{
  "schemaVersion": 1,
  "outcome": "passed|failed",
  "profileAlias": "crm91",
  "workerKind": "OfficialCrm91Worker",
  "packageLockId": "...",
  "operationId": "runtime.health.whoami",
  "httpStatus": 200,
  "elapsedMilliseconds": 123,
  "readyBefore": true,
  "readyAfter": true,
  "identityShape": {
    "responseKind": "WhoAmI",
    "ceVersionMatches": true,
    "hasUserId": true,
    "hasBusinessUnitId": true,
    "organizationIdMatchesExpected": true
  },
  "cleanupCompleted": true
}
```

Never emit or persist raw Gateway URI, paths, organization/user/business-unit IDs, credentials, token/cookie/header values, CRM body, connection strings, raw diagnostics, stack traces, or exception messages.  Non-JSON output must also be sanitized.  The script should return a nonzero exit code on failure, while still producing sanitized JSON when `-Json` is selected.

This script alone cannot prove website -> Gateway because the existing product integration is startup preflight, not an externally callable compatibility endpoint.  Do not obscure that limitation or call it full Phase 4C completion.

## Ask

Review this design for correctness, security/isolation, artifact-validation completeness, PowerShell/.NET Framework compatibility, resource cleanup, error/redaction behavior, and missing contract tests.  Identify only concrete issues and propose exact corrections.  Do not modify files.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.
  PID: 43940
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-43940.log
