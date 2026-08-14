# P7.4 聯絡人目前小組讀取邊界稽核

## Goal

Audit the authoritative `ORG-CALL-00052` (`contact.current.group.retrieve`) source path and
produce a fail-closed local design decision. The child may proceed to implementation only if
the operation can be separated from the NewPerson membership-transfer transaction with a
server-derived request scope, a bounded DTO-only response and deterministic error semantics.
This child never authorizes CE, feature gates, traffic, P7.5 or P8 work.

## Requirements

1. Trace the matrix row from `GetContactCurrentGroup` through every production caller,
   including the contact/list membership lookup and all adjacent writes, owner assignment and
   notifications.
2. Determine whether the current `Entity` parameter, ToolUtility query, first-match selection,
   login/session state or shared list state can constitute a server-owned authorization boundary.
3. Reject any design that would expose CRM `Entity`/`EntityCollection`, accept a caller-selected
   owner/profile/connector, execute an unbounded membership query, or wire a read into the
   existing multi-effect transfer method.
4. If and only if the source audit proves a separable safe boundary, specify a fixed operation,
   bounded request/response DTO, authorization order, cancellation, A/B isolation, resource
   cleanup, rollback owner and disabled-by-default test plan. Otherwise record the exact no-go
   and its prerequisite recovery path without creating runtime code.
5. Do not execute CE requests, create fixtures, mutate CRM, change settings or feature gates,
   cut traffic, modify legacy data, or claim consumer/host/P7.5/P8 evidence.

## Acceptance Criteria

- [x] Matrix row, source callers, authorization inputs and write adjacency are documented with
      repository evidence.
- [x] A deterministic `go` or precise `source-only-local-design-no-go` decision is recorded.
- [x] Any proposed DTO and recovery design contains no raw CRM SDK or shared mutable authority;
      a no-go contains explicit conditions required before re-evaluation.
- [x] No CE, feature-gate, traffic, P7.5, P8 or unrelated product data changed.
- [x] Trellis/CCG records, byte/encoding checks and `git diff --check` are complete before
      scope-only commit and archive.

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
