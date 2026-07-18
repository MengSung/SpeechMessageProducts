# Deploy MemberInfo Portable Kit

## Objective

Deploy the complete MemberInfo feature set from the user-provided portable kit
into branch `1.0.0.1.WorkTreeMemberInfo`, following the authoritative migration
runbook and the related `docs/superpowers` specifications and plans.

## Inputs

- `docs/portable/member-info-portable-kit/05-MIGRATION-RUNBOOK.md`
- `docs/portable/member-info-portable-kit/**`
- `docs/portable/member-info-portable-kit.zip`
- relevant executable/package artifacts under `docs/portable/**`
- `docs/superpowers/**`

All input artifacts are user-provided untracked files. Preserve them and do not
replace, delete, or silently rewrite them.

## Required Outcome

- Validate package integrity, manifest, privacy constraints, and migration
  prerequisites before executing any installer or patch.
- Apply the complete MemberInfo feature without overwriting unrelated branch
  work or weakening existing behavior.
- Follow test-driven and runbook-defined ordering, including host integration,
  feature files, tests, configuration/project changes, and acceptance checks.
- Run focused and broad validation appropriate to the changed surface.
- The owner explicitly waived external Gemini/Claude review for this task
  because both providers have no remaining quota. Do not invoke either model.
  Perform a value-conscious, zero-trust inline review backed by complete local
  verification and record the waiver truthfully.
- Commit in coherent batches with Traditional Chinese subjects and bodies,
  push the branch, update task evidence, and archive the CCG task.

## Safety Boundaries

- Do not execute an unknown binary before checking its file type, hash,
  signature, manifest relationship, and documented command line.
- Do not deploy to a production service, mutate external CRM data, or use
  credentials unless the runbook explicitly requires it and the environment is
  demonstrably non-production or locally isolated.
- Never expose secrets, personal data, portable-kit redactions, or user data in
  logs, prompts, reviews, commits, or durable evidence.
