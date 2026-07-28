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
