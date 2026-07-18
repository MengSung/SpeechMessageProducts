# MemberInfo Portable Deployment Plan

## 1. Inventory And Validate Inputs

- Read the complete migration runbook, package manifest, acceptance checklist,
  privacy rules, authoritative context, and related superpowers documents.
- Inventory ZIP, executable/script, patch, reference implementation, and test
  artifacts with hashes, signatures, sizes, and timestamps.
- Run only documented package verification steps before migration.

## 2. Compare Target And Reference

- Load Trellis backend/frontend/shared specifications.
- Map current branch files against the portable source map and dependency
  matrix.
- Identify exact additions, modifications, already-present behavior, conflicts,
  and excluded paths. Freeze an implementation/rollback checklist.

## 3. Execute Migration

- Follow runbook order and test-first requirements.
- Apply reference feature files and host-integration patches conservatively,
  adapting only where the target branch differs.
- Preserve existing user changes and portable input artifacts.

## 4. Verify And Review

- Run package verification, focused MemberInfo tests, relevant project tests,
  build/type/static checks, privacy scans, encoding/line-ending checks, and
  exact-scope review.
- Exercise the deployable MemberInfo workflow locally where supported.
- Do not invoke external models. Perform an inline zero-trust review of the
  complete diff, resolve findings, and repeat verification; record the
  owner-approved quota waiver.

## 5. Commit And Close

- Commit coherent batches with Traditional Chinese subjects and bodies.
- Push `1.0.0.1.WorkTreeMemberInfo` and verify remote synchronization.
- Record final evidence, update Trellis/CCG state, archive the CCG task, and
  leave the worktree clean.
