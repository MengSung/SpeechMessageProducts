# LINE Messaging API External CCG Review Requirements

## Scope

- Review branch/worktree: `Jesus_5.1.6.WorktreeRefactorLine`
- Review range: `origin/Jesus_5.1.6.WorktreeRefactorLine..HEAD`
- Primary deliverable under review:
  - `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`
  - `.ccg/tasks/archive/2026-07/line-messaging-api-official-matrix/*`
  - `docs/superpowers/plans/2026-07-02-line-messaging-api-official-matrix.md`
- Supporting infrastructure change:
  - `.gitignore`
  - `.ccg/tasks/archive/2026-07/fix-ccg-external-review-cli-path/*`

## Review Questions

- Does the matrix truthfully describe what the current `Line.Messaging` and `LineMessagingProcessor` projects implement?
- Are any high-priority LINE Messaging API gaps missing from the matrix?
- Are priorities, evidence notes, and next-step recommendations clear enough to guide future development?
- Did the CCG CLI repair task introduce any risky or misleading project changes?

## Required Review Method

- Call both CCG external reviewers:
  - `codeagent-wrapper --backend gemini` with reviewer role.
  - `codeagent-wrapper --backend claude` with reviewer role.
- Record Critical, Warning, and Info findings in `review.md`.
- If either reviewer cannot run, record exact non-secret blocker evidence and do not claim dual-model review is complete.
