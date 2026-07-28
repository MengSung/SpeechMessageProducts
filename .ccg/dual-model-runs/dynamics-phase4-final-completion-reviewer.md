# CCG reviewer Task: dynamics-phase4-final-completion

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Phase 4 local hardening — final completion review

Review every current uncommitted change in this repository. This is a high-risk
Dynamics integration boundary. Do not expose, request, print, or retain any
secret, password, token, cookie, browser storage, session data, or user ID.

## Required outcomes

1. Runtime host-slot release must have deterministic ownership. `await using` /
   `DisposeAsync()` is the normal asynchronous path. The legacy synchronous
   `Dispose()` path must not fire-and-forget, must wait for completion, must
   propagate a release failure, and must not capture a caller-owned UI/legacy
   synchronization context.
2. ADFS token responses are limited to 32 KiB; successful documents parse
   directly from a rented buffer that is zeroed before return. Errors must not
   read or expose body content. Confirm that the implementation does not retain
   a response buffer or introduce a parser issue.
3. ADFS and CRM handler policy remains `cookies=false`, `redirects=false`,
   `proxy=false`, `automatic decompression=false`, and `pre-auth=false`.
4. Local admission/host-slot behavior must remain capacity-bounded and must not
   introduce a session, token, queue, socket, handler, timer, or memory leak.
5. `DynamicsAccess:Package01FeeReadsEnabled` remains `false`; do not suggest
   enabling consumer CRM traffic.

## Evidence already available

- `dotnet test SpeechMessage.Dynamics.Tests --no-restore`: 62 passed, 0 failed.
- `dotnet build SpeechMessageProducts.sln --configuration Release --no-restore`:
  0 errors; the ten NuGet vulnerability warnings are existing in legacy
  ToolUtility/PowerPlatform.Dataverse.Client.
- A broader solution test command has pre-existing payment/LINE failures caused
  by test root discovery that looks for absent `ChurchReport.sln`; do not
  attribute those failures to the Dynamics changes.

## Required output

Provide a concrete Critical / Warning / Info report with file and line
references. Mark PASS only if there is no remaining Critical or Warning finding
within the reviewed local Phase 4 hardening scope.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.