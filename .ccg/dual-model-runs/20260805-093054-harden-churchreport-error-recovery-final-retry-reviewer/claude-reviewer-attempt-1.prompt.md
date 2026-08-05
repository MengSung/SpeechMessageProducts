ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: harden-churchreport-error-recovery-final-retry

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Final review: ChurchReport error recovery and CRM lifecycle hardening

Review the current uncommitted diff plus commit `d47bb43f`.

## Context

An MVC controller used to dispose a provider-owned shared `ToolUtility` instance.
That could leave a later request holding a disposed CRM client. The error handler
could then throw `NullReferenceException` while writing `TempData`, masking the
original lifecycle failure. It also exposed raw exception messages in AJAX JSON,
routes, and redirect URLs.

## Required contracts

- A controller must never dispose a provider/factory-owned singleton.
- CRM connection leases must remain request/operation-owned and return through
  their documented `finally` paths.
- `TempData` failures must not mask the original exception.
- Browser responses, AJAX JSON, redirects, routes, and view data must not expose
  raw exception, CRM endpoint, credential, session, organization, or profile data.
- Friendly error text is permitted only via a closed whitelist of server-defined
  `errorCode` values. Unknown codes must fail closed to a generic message.
- The added regression tests must be meaningful and non-flaky.

## Files in scope

- `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineBinding.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationContactCreationService.cs`
- `ChurchReport.MemberInfo.Tests/Controllers/BaseChurchControllerErrorRecoveryTests.cs`

Report Critical, Warning, and Info findings with exact file and line references.
Focus on security, resource ownership, session/cross-tenant isolation, MVC route
compatibility, error disclosure, and test validity. Do not propose unrelated
architecture changes.


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