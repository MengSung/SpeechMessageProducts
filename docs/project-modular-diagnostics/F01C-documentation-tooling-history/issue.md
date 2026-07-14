# F01C Documentation, Tooling, and History Diagnostic Issues

Status: APPROVED_DEGRADED
Module: F01C
Workspace: F01C-documentation-tooling-history
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: READY
Issue document SHA-256: 4a19976c8359bb7ab49a745e1ac32034e582ff3790945d3ea7f955070fe9b6ed

9FC35DB1A38E50D2DF361F5DAF6FEE334BB9BC5C1C0FA579DAFC791B1024FAC1

## Executive Summary

Four confirmed issues survived source reopening. The CCG self-healing tooling
changes persistent User PATH and launches direct Claude probes with normal
permission checks disabled. Current root instructions require that tooling,
while older troubleshooting, plans, handoffs, and packaged tutorials continue
to publish contradictory direct-wrapper workflows and an unsafe recursive
cleanup example.

Tracked scratch outputs account for 16.67 MB, 93.2% of the inspected F01C Git
blob inventory. DOCX generation also duplicates a 49-line rendering helper and
binds document merging to one workstation drive. No retained issue depends on
runtime validation, and no live secret or PII disclosure was confirmed.

## Ranked Confirmed Issues

### F01C-SEC-001 CCG Self-Healing Mutates Persistent User State And Bypasses The Normal Permission Boundary

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 79
- Confirmed: true
- Evidence confidence: 20
- Impact score: 22
- Likelihood/frequency score: 11
- Security urgency score: 15
- Performance gain score: 0
- Loop leverage score: 7
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F01C
- Cross-module: F01B consumes generated CCG artifacts
- Gate blocked: false
- Files:
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:22`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:67`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:145`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:148`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:278`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:284`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:432`
  - `docs/scripts/Test-CcgDualModelHealth.ps1:250`
  - `docs/scripts/Test-CcgDualModelHealth.ps1:355`
  - `docs/scripts/Test-CcgDualModelHealth.ps1:368`
- Evidence: The normal self-healing and health paths independently append
  hard-coded `Administrator` tool locations to persistent Windows User PATH.
  Role files and fallback commands are also profile-bound. Both direct Claude
  probes append `--dangerously-skip-permissions`. De-duplication and
  fixed-response smoke prompts do not make persistent mutation opt-in, restore
  state, provide rollback, make paths portable, or preserve permission checks.
- Control/data/lifetime flow: Repository review request -> F01C start script ->
  self-healing/health path -> process and User PATH repair plus direct backend
  probe -> persistent operator environment and permission-bypassing child
  process outlive the repository review boundary.
- Impact: A review can permanently change future tool resolution and can launch
  a host probe outside its normal permission boundary. No exploit, credential
  theft, or attacker-controlled prompt is claimed.
- Why this is necessary: F01C owns the executable runner and health scripts;
  environment repair and backend permission policy are documentation-tooling
  responsibilities.
- Recommended action: Use explicit parameters, current PATH, and portable home
  discovery; keep repair process-local by default; require consent and rollback
  for a separate persistent-repair command; remove permission bypass from
  health probes or isolate them under an explicit restricted policy.
- Validation: Fake-backend fixtures prove User PATH remains byte-identical,
  process state is restored, active-profile paths resolve, and backend argv has
  no permission-bypass flag.
- Rollback boundary: Path resolution, persistent repair, and probe permission
  policy are separate reversible changes.
- Extraction contract: Review request -> portable tool resolver -> process-local
  environment -> bounded backend probe -> structured health result.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

### F01C-PERF-001 Executable Guidance Conflicts With The Canonical CCG Workflow

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 76
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 13
- Security urgency score: 3
- Performance gain score: 6
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F01C
- Cross-module: false
- Gate blocked: false
- Files:
  - `AGENTS.md:26`
  - `AGENTS.md:39`
  - `AGENTS.md:52`
  - `AGENTS.md:70`
  - `docs/ccg-dual-model-health-permanent-fix.md:20`
  - `docs/ccg-dual-model-health-permanent-fix.md:29`
  - `docs/ccg-gemini-claude-review-troubleshooting.md:48`
  - `docs/ccg-gemini-claude-review-troubleshooting.md:55`
  - `docs/superpowers/2026-07-02-line-sdk-next-steps-handoff.md:58`
  - `docs/superpowers/2026-07-02-line-sdk-next-steps-handoff.md:59`
  - `docs/superpowers/plans/2026-07-02-line-messaging-sdk-p0-fixes.md:980`
  - `docs/superpowers/plans/2026-07-02-line-messaging-sdk-p0-fixes.md:1007`
  - `docs/superpowers/plans/2026-07-02-line-identity-profile-adapter.md:326`
  - `docs/superpowers/plans/2026-07-02-line-identity-profile-adapter.md:331`
- Evidence: Root instructions and the permanent-fix runbook prohibit direct
  Gemini/Claude/wrapper calls and explicitly accept a marked single-backend
  quota fallback. Older troubleshooting, handoff, and plan documents still
  publish direct wrapper calls, `--progress`, hard-coded profile paths, and a
  requirement that both providers succeed. A historical cleanup block
  recursively deletes `bin`, `obj`, and `artifacts` without first proving every
  resolved target remains under the worktree. A guarded repository example
  exists at
  `docs/superpowers/plans/2026-06-29-neutralize-qpay-product-workflow-names.md:1181`.
- Control/data/lifetime flow: Documentation search -> obsolete executable block
  selected -> direct/provider-specific review or unguarded cleanup -> missing
  self-healing/summary/fallback semantics or unsafe deletion -> repeated
  troubleshooting and review. Packaged DOCX copies preserve obsolete commands
  after Markdown changes.
- Impact: Operators and agents can repeat provider calls and manual repairs,
  reject valid degraded reviews, report inconsistent approval states, or reuse
  unsafe cleanup commands. Exact elapsed-time savings are not claimed.
- Why this is necessary: F01C owns the root/runbook/history lifecycle and must
  distinguish canonical executable guidance from historical records.
- Recommended action: Generate all executable snippets from one canonical
  source; mark history non-executable with `superseded-by` metadata; lint direct
  backend/deprecated flags and unguarded recursive deletion; regenerate
  packaged tutorials from the same source.
- Validation: A documentation check finds no unapproved direct backend command,
  inconsistent fallback contract, deprecated `--progress`, hard-coded profile
  recipe, or unguarded recursive deletion example.
- Rollback boundary: Canonical source, history labeling, linting, and packaged
  tutorial regeneration can roll back independently.
- Extraction contract: Canonical operational contract -> generated snippets ->
  current runbooks/tutorials; immutable history -> non-executable references.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

### F01C-PERF-002 Tracked Scratch Outputs Dominate The Documentation Module

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 69
- Confirmed: true
- Evidence confidence: 20
- Impact score: 15
- Likelihood/frequency score: 12
- Security urgency score: 2
- Performance gain score: 10
- Loop leverage score: 7
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F01C
- Cross-module: F01A owns Git enrollment and history cleanup
- Gate blocked: false
- Files:
  - `scratch/memberinfo-build-diag.txt:1`
  - `scratch/memberinfo-build-diag2.txt:1`
  - `scratch/memberinfo-main-nodeps-diag.txt:1`
  - `scratch/memberinfo-prjref-single-diag.txt:1`
  - `scratch/memberinfo-build.log%3Bverbosity:1`
  - `scratch/member_replay.mp4:1`
- Evidence: Seven tracked scratch files occupy 16,666,664 Git blob bytes,
  93.2% of the 17,885,908-byte inspected F01C inventory. Three diagnostic logs
  are each about 4.34 MB and retain closely related verbose build traces. The
  replay is 1.92 MB. No manifest, retention period, external archive pointer,
  or rationale distinguishes durable evidence from disposable output.
- Control/data/lifetime flow: Investigation build/replay -> raw verbose
  log/media -> tracked `scratch/**` -> every clone, history transfer, backup,
  search, index, and context traversal.
- Impact: Generated investigation output dominates durable module storage and
  expands repository operations. No specific search or Git latency is claimed
  without runtime measurement.
- Why this is necessary: F01C owns scratch/history lifecycle. F01A is needed
  only for enrollment enforcement and any history rewrite.
- Recommended action: Keep a redacted incident summary and hash manifest in
  Git; archive large raw evidence externally with owner/TTL; enforce size/path
  budgets; separate new-file policy from optional history cleanup.
- Validation: New raw scratch output is not tracked; retained summaries resolve
  archive hashes; size budgets pass; clone/search/index metrics are recorded
  during implementation.
- Rollback boundary: Enrollment rule, working-tree migration, external archive,
  and history rewrite are independent.
- Extraction contract: Raw incident output -> redaction/classification ->
  compact durable summary + content hash -> external evidence archive.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

### F01C-EXT-001 DOCX Generation Is Duplicated And Bound To One Workstation

- Category: Extraction
- Severity: Medium
- Priority: P2
- Priority score: 61
- Confirmed: true
- Evidence confidence: 20
- Impact score: 15
- Likelihood/frequency score: 9
- Security urgency score: 0
- Performance gain score: 4
- Loop leverage score: 8
- Ease/reversibility score: 5
- Effort: M
- Primary owner: F01C
- Cross-module: false
- Gate blocked: false
- Files:
  - `tools/generate_vs2026_git_guide.py:13`
  - `tools/generate_vs2026_git_guide.py:49`
  - `tools/generate_vs2026_git_guide.py:245`
  - `tools/generate_vs2026_ide_steps_doc.py:13`
  - `tools/generate_vs2026_ide_steps_doc.py:49`
  - `tools/generate_vs2026_ide_steps_doc.py:183`
  - `tools/merge_vs2026_client_version_docs.py:11`
  - `tools/merge_vs2026_client_version_docs.py:18`
  - `tools/merge_vs2026_client_version_docs.py:358`
- Evidence: Lines 13-61 are identical in both generator scripts, duplicating
  49 of 49 same-position helper/style lines. Each script constructs and saves a
  document at import/module scope. The merge script hard-codes all inputs and
  output beneath `E:\電子書籍\改善 GitHub 多客戶版本上線追蹤`.
- Control/data/lifetime flow: Tutorial-specific script -> private copy of DOCX
  rendering policy -> immediate repository/workstation write. Merge inputs and
  output cannot be selected without editing source.
- Impact: Style fixes drift across scripts, imports have filesystem side
  effects, generated provenance is absent, and document merging is not portable.
- Why this is necessary: Rendering/style mechanics form a cohesive shared
  responsibility with three concrete consumers; tutorial prose remains
  separate.
- Recommended action: Extract a small `docx_rendering` library and explicit CLI
  with `--input`, `--output`, and provenance manifest; keep existing script
  names as adapters while migrating one consumer at a time.
- Validation: Import writes no file; temporary-directory CLI fixtures work
  without a fixed drive; rendered style/section comparisons pass; manifest
  hashes identify source and output versions.
- Rollback boundary: Add the helper behind existing scripts, migrate one
  consumer at a time, and retain old DOCX outputs until visual acceptance.
- Extraction contract: Content/config + input paths -> shared DOCX
  style/merge library -> explicit CLI -> document + provenance manifest.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

## Runtime Validation Pending

None. Future acceptance measurements are recorded in
`evidence/runtime-validation-plan.md` and do not affect current confirmation.

## Deleted Or Rejected Candidates

- Live secrets in F01C docs/tutorials: rejected. No usable secret value was
  confirmed.
- Scratch logs contain credentials: rejected. Machine/tool/session metadata is
  present, but no credential value was proved.
- `scratch/member_replay.mp4` contains PII: rejected. Static inventory cannot
  establish its visual content.
- Every DOCX script should become one product: rejected. Only rendering,
  merging, path, and provenance mechanics form the proposed shared boundary.
- The generator scripts are 98% identical: rejected and corrected. The exact
  shared block is 49/49 identical lines; whole-file content differs.
- README inadequacy as a standalone issue: merged into PERF-001's documentation
  lifecycle and discoverability problem.

## Cross-Module Handoffs

1. F01A: define Git enrollment/size gates and decide whether historical scratch
   blobs should be rewritten after F01C defines durable evidence policy.
2. F01B: consume the F01C runner's future process-local/redacted output
   contract when setting `.ccg` retention policy. F01C does not claim F01B's
   generated-artifact findings.

## Final CCG Approval

Substantive issue verdict: `APPROVED_DEGRADED`.

- Round 1 submitted SHA-256:
  `9FC35DB1A38E50D2DF361F5DAF6FEE334BB9BC5C1C0FA579DAFC791B1024FAC1`.
- Run ID: `20260710-195321-f01c-issue-review-r1-reviewer`.
- Summary:
  `.ccg/dual-model-runs/20260710-195321-f01c-issue-review-r1-reviewer/summary.json`.
- Claude reopened the original files and returned KEEP for all four issues,
  with no unresolved Critical or Warning, no runtime-validation verdict, and
  `MODULE_VERDICT: APPROVE`.
- Gemini produced no usable output because provider quota/billing returned
  HTTP 403 with insufficient balance.
- The summary has `degradedFallback=true`, `fallbackAccepted=true`, and
  `quotaBlocked=true`; this is not a completed dual-model review.
- The diagnostic agent reopened every retained issue after review and
  reconfirmed source lines, ownership, score sums, Git blob sizes, and the
  duplicated helper block.
- Claude's Info-level note that troubleshooting line 41 was only a section
  heading was resolved by removing that non-operative citation. No issue
  evidence, score, severity, or verdict changed.
- Retained: 4. Deleted: 0. Runtime pending: 0. Cross-module handoff groups: 2.
- Nested agent count: 0.
