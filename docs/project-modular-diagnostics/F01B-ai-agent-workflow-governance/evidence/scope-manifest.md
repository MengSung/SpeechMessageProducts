# F01B Scope Manifest

Status: COMPLETE
Module: F01B - AI Agent and Development Workflow Governance
Mode: DIAGNOSIS_ONLY
Authoritative map:
`docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`

## Primary Owner Paths

The map assigns every tracked file under these roots to F01B:

| Root | Tracked files at HEAD | Approximate bytes at HEAD | Role |
|---|---:|---:|---|
| `.agents/**` | 46 | 0.22 MiB | platform-neutral Trellis skills |
| `.ccg/**` | 1,046 | 6.88 MiB | CCG tasks, archives, prompts, outputs, summaries |
| `.claude/**` | 53 | 0.30 MiB | Claude agents, commands, hooks, settings, skills |
| `.codex/**` | 7 | 0.04 MiB | Codex agents, hooks, project config |
| `.gemini/**` | 8 | 0.06 MiB | Gemini agents, commands, hooks, settings |
| `.opencode/**` | 54 | 0.28 MiB | OpenCode plugins, libraries, agents, skills |
| `.serena/**` | 4 | 7.56 MiB | Serena project config and tracked symbol caches |
| `.trellis/**` | 71 | 0.43 MiB | workflow runtime, specs, tasks, workspace records |

Total tracked F01B inventory at HEAD: 1,289 files, approximately 15.77 MiB.

## Owned Executable Governance Surfaces

- `.trellis/scripts/**/*.py`
- `.claude/hooks/*.py`
- `.codex/hooks/*.py`
- `.gemini/hooks/*.py`
- `.opencode/lib/*.js`
- `.opencode/plugins/*.js`
- `.ccg/tasks/**/*.py`
- `.ccg/tasks/**/*.ps1`
- `.ccg/tasks/**/docx-generator/**/*.cs`

Generated task history and CCG output remain F01B-owned data, but were not
treated as equivalent to executable source. They were inspected for retention,
secret, size, and provenance evidence.

## Single-File And Generated-State Rules

- `.serena/.gitignore` owns the cache-ignore rule; already tracked cache blobs
  remain F01B until untracked.
- `.trellis/.runtime/**`, `.trellis/.developer`, caches, temp files, and agent
  runtime state are F01B but intentionally local-only by
  `.trellis/.gitignore`.
- `.trellis/tasks/**` and `.trellis/workspace/**` are durable F01B workflow
  records.
- `.ccg/dual-model-runs/**` and `.ccg/tasks/**` are F01B records. This diagnosis
  found no policy that separates raw local run artifacts from durable summaries.

## Read-Only Dependencies

1. F01A - Solution, build, Git, and CI governance.
   - Git tracking, ignore policy at repository root, history rewrite, clone
     cost, and CI recurrence gates.
2. F01C - Documentation and tooling.
   - `docs/scripts/Start-CcgDualModelRun.ps1`
   - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`
   - `docs/scripts/Test-CcgDualModelHealth.ps1`
   These scripts produce F01B-owned `.ccg` artifacts but are F01C-owned source.
3. Product and test files referenced inside archived CCG diffs.
   - Read only as retained payload evidence; no product diagnosis or ownership
     was claimed.
4. External host CLIs and user configuration.
   - Codex, Claude, Gemini, OpenCode, Serena, Python, Git, Trellis CLI, and
     `codeagent-wrapper`.

## Consumers

- Developers using Trellis task/session commands.
- Codex, Claude, Gemini, and OpenCode session-start, per-turn, and subagent
  hooks.
- All 35 diagnostic modules through task state, specs, CCG review, and workflow
  instructions.
- F01A/F01C through Git enrollment and CCG runner integration.
- Serena tooling through project configuration and local symbol indexes.

## Explicit Exclusions

- Product `.cs`, `.csproj`, `.cshtml`, JavaScript, CSS, configuration, and test
  behavior outside the F01B roots.
- Root `AGENTS.md`, `docs/**`, `tools/**`, `scratch/**`, and `openspec/**`
  except read-only CCG runner dependencies; these are F01C.
- `.github/**`, solution/build metadata, and root Git rules; these are F01A.
- Product documentation within product projects.
- Other module diagnostic workspaces.
- Parent `.ccg/tasks/project-modular-analysis-diagnosis-optimization/**` and
  `.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/**`,
  which are read-only for this subagent despite F01B ownership.

## Gate And Quarantine Status

- Governance node: yes.
- Quarantine: no.
- Diagnostic gate: READY.
- Optimization gate: not authorized.
- Existing automated F01B conformance tests: none found under the owned roots by
  tracked test-path/name scan.
- Future implementation minimum gate:
  - active-task resolver fixture matrix across platforms;
  - artifact redaction and retention fixtures;
  - lifecycle hook timeout/process-tree tests;
  - Git recurrence checks for raw CCG and Serena cache paths;
  - rollback per governance change.

## Cross-Module Handoffs

- F01A: Git ignore/enrollment, history cleanup, repository size, and recurrence
  gates.
- F01C: CCG output redaction/retention and persistent user-environment mutation
  in the runner.

## Baseline

- Branch: `1.0.0.1.EvenVersion`
- HEAD: `26781fd452743710aa7d9276f3ec9be50b29bc24`
- Git baseline lines: 64
- Git baseline SHA-256:
  `D3E86B3929618F87D437514532F10069B03C2C4C6570CCA3B4D3D04AD3A4C31D`
- All pre-existing dirty paths were treated as read-only.
