ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: x04b-issue-review-r1

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion

## Request
# X04B Issue Review Round 1

Role: reviewer
Title: x04b-issue-review-r1
Repository: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion`

Review the X04B deployment/package-source diagnostic output. This is a diagnosis-only review. Do not run restore, build, publish, test, format, migration, package restore, or any command that writes generated, ignored, cache, lockfile, bin, obj, or test-output files.

Allowed read scope:

- `docs/project-modular-diagnostics/X04B-deployment-package-sources/**`
- `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`
- X04B-owned source evidence:
  - `SpeechMessageProducts.ChurchReport/Properties/launchSettings.json`
  - `SpeechMessageProducts.ChurchReport/DotNetPublish/**`
  - `SpeechMessageProducts.ChurchReport/DotNetPublish-*.bat`
  - `SpeechMessageProducts.ChurchReport/Tools/verify-release-noperf.ps1`
  - `SpeechMessageProducts.ChurchReport/NuGet.config`
  - `SpeechMessageProducts.ChurchReport/NuGet.config.bak`
  - `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj`

Review goals:

1. Decide whether each retained issue should be KEEP, REWRITE, DELETE, or NEEDS_RUNTIME_VALIDATION.
2. Check whether evidence supports the security, performance, and extraction conclusions.
3. Check whether issue priority and owner boundaries are reasonable for X04B.
4. Identify any Critical or Warning that must be reflected before final acceptance.
5. Do not propose product code changes; this stage is diagnosis only.

Expected output:

- Critical / Warning / Info sections.
- Per-issue verdict for X04B-SEC-001, X04B-SEC-002, X04B-SEC-003, X04B-PERF-001, X04B-PERF-002, X04B-EXT-001.
- Overall verdict: APPROVE, APPROVE_DEGRADED, REVISE, or BLOCK.

Diagnostic documents to review:

- `docs/project-modular-diagnostics/X04B-deployment-package-sources/issue.md`
- `docs/project-modular-diagnostics/X04B-deployment-package-sources/review-log.md`
- `docs/project-modular-diagnostics/X04B-deployment-package-sources/evidence/scope-manifest.md`
- `docs/project-modular-diagnostics/X04B-deployment-package-sources/evidence/security-analysis.md`
- `docs/project-modular-diagnostics/X04B-deployment-package-sources/evidence/performance-analysis.md`
- `docs/project-modular-diagnostics/X04B-deployment-package-sources/evidence/extraction-analysis.md`
- `docs/project-modular-diagnostics/X04B-deployment-package-sources/evidence/runtime-validation-plan.md`


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