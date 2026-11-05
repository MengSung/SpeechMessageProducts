# CCG reviewer Task: integration-progress-stages-2-8-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# Reviewer Request: Integration Progress Page Stages 2-8

Review the current implementation of the requested progress-page update. This is
a documentation/HTML rendering and automated-test change; it does not implement
any Stage 2-8 product work.

## Files in scope

- `文件/規劃設計書/實作計畫-3.3/實作進度.html`
- `tools/RepoQuality/Tests/Test-ProgressPage.cjs`
- `.trellis/tasks/11-05-integration-p1/research/execution-ledger.md`

Ignore unrelated changes in other files. Within the HTML and test files, focus
on the Stage 2-8 work-package addition and verify that it does not misstate or
advance existing P0/P1 status.

## Authoritative references

- `文件/規劃設計書/實作計畫-3.3/00_總覽與執行規則.md`
- `文件/規劃設計書/實作計畫-3.3/03_階段2-4_共用送訊_Webhook_金流.md`
- `文件/規劃設計書/實作計畫-3.3/04_階段5-8_Plugin重建_切換_收尾.md`
- `AGENTS.md`, especially cross-user isolation, stable row identity, external
  operation, and secret-handling requirements.

## Review requirements

1. Independently verify all 53 IDs and stage counts: P2 12, P3 9, P4 6, P5 13,
   P6 6, P7 2, P8 5. Check for missing/duplicate IDs and inaccurate titles,
   scope, plan-specified tests, dependencies, estimates, or completion criteria.
2. Ensure every Stage 2-8 package remains `todo`, with phase `done=0` and
   `active=0`; the full plan remains 83 packages and existing accepted totals
   remain P0 18 plus P1 2 (20 total).
3. Verify the stop/safety boundaries are accurately preserved, especially
   P2-04 test credentials and user-operated sandbox push, P3-04 platform SQL,
   P3-09 user-operated test host, P5-02 old-source immutability and P5 D365
   actions, P7 production-organization restrictions, and P8 stable IDs,
   server-derived church authorization, irreversible migration, and manual
   pseudo-locale login.
4. Review HTML rendering for escaping, semantic/keyboard-operable disclosure,
   narrow-screen layout, and consistency of displayed counts. Check tests for
   meaningful assertions rather than merely asserting the source text copied
   into the page.
5. Treat the present repository files and fresh test output as evidence. An
   earlier analyzer report was generated against an older page snapshot; do not
   reuse its stale-state claims.
6. Do not edit files. Report findings as Critical / Warning / Info with precise
   file and line references. State explicitly if no actionable findings remain.

Local verification command:

```text
node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs
```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.