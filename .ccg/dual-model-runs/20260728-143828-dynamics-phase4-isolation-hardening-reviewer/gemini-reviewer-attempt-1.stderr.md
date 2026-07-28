[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-phase4-isolation-hardening

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Dynamics Phase 4 isolation hardening review

Review the current uncommitted Phase 4 change set in this repository. Inspect
`git diff` and the relevant source and tests. Do not modify repository files,
VMs, or remote systems.

## Owner-authorised scope

- Product-code changes, VM configuration, WinRM reprobe, and browser validation
  are authorised by the owner.
- `DynamicsAccess:Package01FeeReadsEnabled` must remain `false`; no consumer
  migration, credential extraction, password flow, raw token use, or Dynamics
  feature enablement is authorised.
- Never expose, retain, or recommend retaining passwords, private keys, tokens,
  cookies, browser storage, authorization headers, raw session identifiers,
  LINE identifiers, full response bodies, or user identities.

## Review focus

The intended Phase 4 changes atomically bound local admission across in-flight
and queued work, made the process-local host-slot coordinator atomic and
expiry-fenced (while still non-durable), and hardened ADFS and CRM HTTP handler
settings against session/cookie/redirect/proxy/decompression/pre-auth leakage.

Assess correctness, cancellation/disposal, race conditions, permit/queue/lease
leaks, profile/session isolation, security, error-body retention, throughput,
test quality, and scope. In particular, validate that no queue, workload entry,
semaphore reservation, cancellation registration, handler, lease, token, or
HTTP/session state can be retained beyond its necessary lifetime. Call out any
unsafe claim of distributed coordination.

## Required result

Respond in Traditional Chinese with Critical / Warning / Info findings, an
overall PASS or FAIL verdict, exact file-and-line references, and the minimal
required remediation. Clearly list release blockers that remain outside this
narrow local hardening increment.


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
  PID: 23688
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-23688.log
