# CCG architect Task: p7-qr-attendance-command-authorization

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7 QR attendance command admission analysis

Review only current source and archived task evidence. Decide whether existing ChurchReport code provides a server-issued, request-local QR attendance descriptor that can be bound to P7GatewayRequestScope before parsing browser QR/LINE/group/room input, reading InMemoryContext, composing a client or CRM I/O.

Existing routes write caller supplied values to InMemoryContext before invoking legacy QR utilities that mix attendance Create/Update, relationship, weekly report and notification effects. Do not propose a browser DTO, Session, InMemoryContext, TempData, Entity, legacy utility or generic CRUD as authority.

If no safe descriptor exists, report the exact local no-go and the minimal next prerequisite. Do not suggest CE, traffic, feature, consumer, ToolUtility, P7.5 or P8 work.

OUTPUT: Traditional Chinese evidence-backed Critical/Warning/Info design findings.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.