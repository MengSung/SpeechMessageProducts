ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p74-package03-contact-image-implementation-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 Package03 contact-image implementation review

Review the current uncommitted implementation only. Do not edit files.

## Scope
- A new disabled-by-default `/MemberInfo/Package03ContactImage` route.
- Package03 typed image service with fixed deployment profile/workload and defensive image bytes.
- Bootstrap flag/client composition.
- Contract/unit tests and false configuration flags.

## Non-negotiable requirements
1. Gate=false must stop before user/session authorization, GUID parsing, typed client/host creation or I/O.
2. Gate=true: server scope authorization -> GUID parse -> target authorization -> typed client -> typed read with RequestAborted.
3. No changes to existing GetContactImage semantics; no legacy CRM, cache, Entity, fallback, retry, redirect or raw errors in new route.
4. No session/memory/resource leakage; no caller-selected profile/connector/endpoint.
5. No CE, traffic, flag enablement, P7.5 or P8 claim.

Inspect git diff and relevant changed files. Report Critical/Warning/Info only, with exact evidence.


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