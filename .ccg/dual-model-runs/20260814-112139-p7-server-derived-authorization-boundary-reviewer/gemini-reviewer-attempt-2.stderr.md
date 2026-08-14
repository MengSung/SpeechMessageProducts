[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p7-server-derived-authorization-boundary

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7 server-derived immutable authorization-boundary review

Review the current unstaged, task-owned changes only:

- `SpeechMessageProducts.ChurchReport/Security/P7GatewayRequestScope.cs`
- `ChurchReport.MemberInfo.Tests/Security/P7GatewayRequestScopeResolverTests.cs`

The task is a local-only prerequisite. It must not add controller wiring, CE work, feature gates,
traffic changes, ToolUtility changes, caches, DI resolution, Session, `HttpContext`, CRM access,
or external I/O.

Verify the following security contract against the actual code and tests:

1. Accept exactly one authenticated identity whose authentication type is the Cookie scheme.
2. Require exactly one non-empty GUID `D` `NameIdentifier` and exactly one matching
   `church:contactId`; reject missing, duplicate, malformed, empty, or conflicting values.
3. Allow only `ACCOUNT` and `LINE` login types; do not use or publish account/password-key claims.
4. Publish only immutable Contact ID, constant `ChurchReport` product boundary, and login-kind scalar.
   No principal, claim, `HttpContext`, Session, credential, CRM entity, collection, cache, static mutable
   state, resource owner, retry, fallback, connector or I/O may be retained or invoked.
5. Confirm A/B interleaving, ambiguous identity, malformed claims, and public-contract tests are relevant
   and not tautological.
6. Report only evidence-backed Critical, Warning, and Info findings. Do not suggest expanding scope into
   consumer migration, CE evidence, traffic, P7.5, or P8.

OUTPUT: Concise Critical / Warning / Info report with file and line references.


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
  PID: 40096
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-40096.log
