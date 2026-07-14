# F01C Scope Manifest

Status: COMPLETE
Module: F01C - Documentation, Tooling, and History
Mode: DIAGNOSIS_ONLY
Authoritative map:
`docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`

## Primary Owner Inventory

The map assigns root documentation, general-purpose tooling, scratch/history
artifacts, tutorial outputs, and non-executable images to F01C. At HEAD
`26781fd452743710aa7d9276f3ec9be50b29bc24`, the inspected inventory is:

| Path group | Tracked files | Git blob bytes | Role |
|---|---:|---:|---|
| `README.md` | 1 | 19 | repository entry point |
| `AGENTS.md` | 1 | 3,652 | root agent and CCG instructions |
| `Data8.png` | 1 | 3,321 | non-executable image |
| `docs/**` | 54 | 1,095,961 | runbooks, plans, handoffs, tutorials, reports |
| `tools/**` | 3 | 26,821 | DOCX generation and merge scripts |
| `scratch/**` | 7 | 16,666,664 | diagnostic logs, replay media, helper page |
| `openspec/**` | 2 | 6,216 | historical specification artifacts |
| `.ccg/tasks/subagent-goal-word-tutorial/docx-generator/**` | 10 | 83,254 | single-path document-generator exception |

Total: 79 tracked files and 17,885,908 Git blob bytes. The `.ccg` exception is
owned by F01C because the module map classifies that project as document
generation tooling rather than product or agent-workflow runtime.

## Owned Executable Surfaces

- `docs/scripts/Start-CcgDualModelRun.ps1`
- `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`
- `docs/scripts/Test-CcgDualModelHealth.ps1`
- `tools/generate_vs2026_git_guide.py`
- `tools/generate_vs2026_ide_steps_doc.py`
- `tools/merge_vs2026_client_version_docs.py`
- `.ccg/tasks/subagent-goal-word-tutorial/docx-generator/**`
- Executable shell and PowerShell examples embedded in F01C-owned Markdown and
  DOCX tutorials.

## Read-Only Dependencies

1. F01B owns `.ccg/dual-model-runs/**` and other generated CCG records. F01C
   owns the runner source that produces those records but does not claim F01B's
   retention conclusions.
2. F01A owns root Git enrollment, ignore rules, repository-history cleanup,
   solution/build governance, and CI recurrence gates.
3. Product modules own documentation stored inside product-project roots.
   Those files are excluded even when they describe documentation or tooling.
4. External host tools include PowerShell, Python, `python-docx`, Git, Claude,
   Gemini, and `codeagent-wrapper`.
5. The diagnostic workflow, module map, active Trellis task documents, and
   other generated module workspaces are read-only orchestration evidence.

## Consumers

- Developers following root onboarding, CCG troubleshooting, plans, and
  handoff documents.
- Agents following `AGENTS.md` and the CCG self-healing runbook.
- F01B review-artifact storage through the F01C CCG runner.
- Operators generating or merging Visual Studio/Git tutorial documents.
- Git clients, search/index tools, backup processes, and repository clones that
  traverse tracked scratch/history artifacts.

## Tests And Validation Surfaces

- No tracked F01C-specific automated test suite was found.
- Static source reopening, Git object inventory, line comparison, and CCG
  reviewer reopening are the only permitted checks in this diagnosis.
- Future implementation should add read-only fixtures for path resolution,
  prompt construction, redaction boundaries, documentation link/canonicality
  checks, and DOCX generation manifest validation.

## Gate And Quarantine

- Governance node: yes.
- Diagnostic gate: READY.
- Quarantine: no.
- Optimization: not authorized.
- Runtime measurement: not required for current issue confirmation.

## Explicit Exclusions

- Product `.cs`, `.csproj`, `.cshtml`, JavaScript, CSS, configuration, tests,
  and internal project documentation.
- `.github/**`, solution/build files, root Git policy, and history rewrite.
- F01B-owned agent/workflow source and generated CCG history, except the
  explicit document-generator path above.
- Other module diagnostic conclusions and workspaces.
- The active task, diagnostic workflow, and module map as write targets.
- The seven files in this workspace as diagnostic source; they are outputs.

## Baseline

- Worktree:
  `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion`
- Branch: `1.0.0.1.EvenVersion`
- HEAD: `26781fd452743710aa7d9276f3ec9be50b29bc24`
- Baseline time: `2026-07-10T19:44:34.6269662+08:00`
- Git status lines: 138
- Git status SHA-256:
  `00feab1aa4430da96f889e5481792104ea9f1d5e960d997268c315c761d90810`
- Module map SHA-256:
  `734f417dfd4dc1aabf2b339ae85bd6228d4deeda8d660027646855646788f22d`
- Diagnostic workflow SHA-256:
  `7dc805a9fc76053c42b7fd9c0f8a619e1b9a7cbec8e004a5231e0d7f1200b175`
- CCG thinking guide SHA-256:
  `20072e941fa0e783334668a5f5e9e24d58c8d6c95e59867cd5b646dc5359ff40`
- All baseline dirty paths were treated as read-only.
