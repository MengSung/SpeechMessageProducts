[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-phase4-final-lease-lifecycle

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Phase 4 final lease lifecycle review

Review the current uncommitted Phase 4 isolation-hardening diff in this
repository. This is a high-risk Dynamics integration boundary. Do not expose
or request secrets, credentials, tokens, cookies, browser/session storage, or
user identifiers.

## Required behavior

- A runtime host-slot lease must never release in an unobserved fire-and-forget
  task. A synchronous `IDisposable.Dispose()` call must not return until its
  coordinator release has completed or faulted. The preferred production path
  uses `await using` / `DisposeAsync`.
- The implementation must prevent session, token, credential, handler, timer,
  socket, queue, and memory leakage. It must not allow cross-host capacity
  leakage.
- ADFS token responses must be bounded to 32 KiB, error bodies must not be
  surfaced, and successful response bytes must be cleared after parsing.
- ADFS/CRM HTTP isolation remains cookies=false, redirects=false, proxies=false,
  automatic decompression=false, and pre-authentication=false.
- Do not enable consumer CRM traffic;
  `DynamicsAccess:Package01FeeReadsEnabled` must remain false.

## Review focus

1. Inspect `RuntimeHostSlotLease.Dispose()` versus `DisposeAsync()` for resource
   leaks, exception behavior, deadlocks/synchronization-context risks, and
   whether the synchronous contract is a reasonable safe fallback while
   asynchronous disposal remains the normal path.
2. Inspect the new regression test that blocks `ReleaseAsync` and proves that a
   synchronous dispose waits for completion. Identify test blind spots.
3. Inspect the ADFS bounded parsing/zeroing change for buffer retention, parser
   correctness, unbounded allocations, sensitive-data exposure, and performance.
4. Inspect all uncommitted diff for capacity, isolation, performance, and
   compatibility regressions.

## Required output

Return a concrete `Critical` / `Warning` / `Info` report with file and line
references. State `PASS` only when no Critical or Warning findings remain.


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
  PID: 34364
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-34364.log
