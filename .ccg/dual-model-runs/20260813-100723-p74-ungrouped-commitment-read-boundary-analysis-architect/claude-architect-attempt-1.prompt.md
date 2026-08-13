ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p74-ungrouped-commitment-read-boundary-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 ORG-CALL-00024 local-only read-boundary analysis

## Scope

Review the planned local-only ChurchReport migration of **only** the non-empty
ungrouped commitment aggregate count (`ORG-CALL-00024`,
`memberinfo.contact.count.ungrouped.commitment`). The existing typed Data8
executor and `IPackage02ContactProfileClient.CountUngroupedCommitmentAsync`
already exist. The intended work adds a disabled-by-default child gate and a
request-local DTO validation service, then selects the typed count only for the
non-empty aggregate count used by `LoadUngroupedCommitmentTypePage`.

## Required invariants

- Both Package02 base gate and new sub-gate default false. False occurs before
  session/access, ProductClient, process host, pool/handler or external I/O.
- Enabled path uses a fixed deployment ProfileAlias, fixed workload subject and
  `HttpContext.RequestAborted`; it accepts no caller profile, connector, owner,
  FetchXML, CRM Entity or credential.
- Result values are bounded request-local scalar count data. Null records,
  duplicate values, negative count and typed failure fail closed. No cache,
  retry, legacy aggregate fallback or partial result may be published.
- The empty count, metadata ordering, page retrieve and contact authorization
  remain separate legacy capability paths. Their coexistence must not be
  described as typed fault fallback or as full page migration.
- No CE request/mutation, feature enablement, traffic switch, ToolUtility
  removal, P7.5 or P8 is allowed.

## Key files

- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
- `SpeechMessage.Dynamics.ProductClient/MemberInfo/IPackage02ContactProfileClient.cs`
- `.trellis/tasks/08-13-p74-ungrouped-commitment-read-boundary/{prd,design,implement}.md`

## Output

Return Critical / Warning / Info findings only, with file references. Verify
authorization sequencing, isolation, cancellation, lifecycle, typed/legacy
boundary and regression risks. Do not request network, CRM, deployment or
feature enablement.


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