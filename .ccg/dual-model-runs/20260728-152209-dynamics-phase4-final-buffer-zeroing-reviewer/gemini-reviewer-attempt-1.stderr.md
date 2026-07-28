[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-phase4-final-buffer-zeroing

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Dynamics Phase 4 final isolation hardening review

Review the current uncommitted Phase 4 diff in this repository. Inspect `git
diff` and relevant source/tests only. Do not change repository files, VMs, or
remote systems.

## Strict safety boundary

- This review contains no credentials, tokens, cookies, authorization headers,
  user/session identifiers, browser storage, or response bodies. Do not request
  or infer any.
- `DynamicsAccess:Package01FeeReadsEnabled` must remain `false`. No feature
  enablement, consumer migration, credential extraction, or password flow is
  permitted.
- This increment must not claim that a process-local coordinator is a durable
  multi-host solution.

## Changes to assess

1. Admission now atomically bounds in-flight plus queued work and releases every
   reservation on cancellation, timeout, exception, and permit disposal.
2. The in-memory coordinator serializes its own transitions and fences expiry.
3. A singleton manager serializes its own lease acquire/renew operations; its
   shutdown cancellation is linked to pending coordinator work.
4. ADFS and Dynamics HTTP handlers disable cookies, redirects, proxies,
   decompression, and pre-authentication. ADFS token wrappers are bounded-time,
   explicitly disposed, do not echo failure bodies, and read successful token
   documents into a cleared 32 KiB maximum buffer.

## Required review

Give a Traditional Chinese Critical / Warning / Info report and PASS or FAIL.
Focus on session/profile/token leakage, memory/resource leakage, race conditions,
semaphore/lease lifecycle, cancellation, response parsing limits, handler reuse,
test effectiveness, and whether the reported Phase 4 completion is confined to
the local hardening increment. Cite exact files and lines for each finding.


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
  PID: 42532
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-42532.log
