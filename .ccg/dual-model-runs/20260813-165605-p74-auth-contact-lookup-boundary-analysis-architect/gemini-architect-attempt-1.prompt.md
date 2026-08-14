ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p74-auth-contact-lookup-boundary-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 authentication contact lookup boundary analysis

Review the proposed local-only design for authoritative matrix ORG-CALL-00055
(`auth.contact.retrieve.by.account`) and ORG-CALL-00056
(`auth.contact.retrieve.by.lineid`).

Constraints:
- Existing legacy account lookup exposes a plaintext password comparison risk.
- New path must be disabled-by-default, DTO-only, asynchronous and request-local.
- No CE request/mutation, feature enablement, traffic change, P7.5 or P8 work.
- Do not connect the new typed API to legacy login, QR, payment, or session flows.
- No password, hash, token, cookie, Entity, raw exception, endpoint, credential,
  caller-selected profile, connector or FetchXML may cross the new API boundary.
- Gate=false must do no host/client/pool/handler construction or outbound I/O.
- Empty/multiple/malformed results must fail closed with a fixed classification.

Assess only: operation/wire/DTO shape, fixed-query validation, disabled bootstrap,
cancellation, A/B isolation, resource lifetime, testing, and migration risks.
OUTPUT: Critical/Warning/Info findings plus concrete local-only recommendations.


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