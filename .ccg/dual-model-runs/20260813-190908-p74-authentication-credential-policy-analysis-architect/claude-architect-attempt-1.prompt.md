ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p74-authentication-credential-policy-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# CCG architecture/security analysis: P7.4 Authentication credential-policy boundary

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Current verified facts
- P7.4 has a disabled-by-default, local-only typed read boundary for `auth.contact.retrieve.by.account` and `auth.contact.retrieve.by.lineid`.
- That boundary intentionally returns no password, hash, token, cookie, raw CRM Entity, raw exception, or credential-verification API.
- The legacy `AuthenticationController.ValidateUserCredentials` directly projects CRM `new_app_pass` and compares it with browser input, then a separate legacy retrieval/session-initialization chain builds a CRM Entity and stores legacy credentials in session-backed managers.
- P7.2 Slice C is permanently closed. No old CE cycle may be retried.
- All checked-in gates remain false. No CE request or mutation, traffic switch, P7.5 ToolUtility removal, P8 deployment, push, or PR is authorized.

## Requested decision
Determine the smallest safe next P7.4 deliverable toward eventually removing ChurchReport's legacy authentication CRM dependency without downgrading credential security.

Analyze three alternatives:
A. wire the existing typed contact-read DTO directly into account-password login;
B. introduce an account-password credential-verification operation that returns a non-secret allowlisted outcome only;
C. keep account-password login legacy and only migrate the LINE lookup route.

## Non-negotiable safety constraints
- No plaintext password, password hash, secret-presence detail, token, cookie, CRM Entity, raw response, endpoint, credential, or raw exception may appear in ProductClient wire DTOs, logs, task artifacts, or browser output.
- Caller controls only untrusted account/password or LINE locator input; it never controls profile, organization, endpoint, credential, operation, owner, authorization scope, connector or session identity.
- Ambiguous account/LINE matches fail closed; no first-match selection, no retry, no request-time legacy fallback after typed dispatch, no dual-auth success path.
- Authentication and authorization must occur before cache/profile/client allocation/outbound I/O. No user-specific static/cache/queue/background retention; session hand-off, if any, must preserve A/B isolation and deterministic cleanup.
- Gate=false must be zero typed I/O. Any eventual gate=true must be independently rollbackable and must not use an old `new_app_pass` plaintext comparison.

## Output
Traditional Chinese. State which alternative is viable now, a minimal capability boundary and exact prerequisites; classify Critical/Warning/Info. Do not propose CE execution or feature enablement.

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