# F01C Performance Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed Findings

### F01C-PERF-001 Executable Guidance Conflicts With The Canonical CCG Workflow

- Confirmed: true
- Primary owner: F01C
- Canonical source:
  - `AGENTS.md:26`
  - `AGENTS.md:39`
  - `AGENTS.md:52`
  - `AGENTS.md:70`
  - `docs/ccg-dual-model-health-permanent-fix.md:20`
  - `docs/ccg-dual-model-health-permanent-fix.md:29`
- Conflicting executable guidance:
  - `docs/ccg-gemini-claude-review-troubleshooting.md:48`
  - `docs/ccg-gemini-claude-review-troubleshooting.md:55`
  - `docs/superpowers/2026-07-02-line-sdk-next-steps-handoff.md:53`
  - `docs/superpowers/2026-07-02-line-sdk-next-steps-handoff.md:58`
  - `docs/superpowers/2026-07-02-line-sdk-next-steps-handoff.md:59`
  - `docs/superpowers/plans/2026-07-02-line-messaging-sdk-p0-fixes.md:978`
  - `docs/superpowers/plans/2026-07-02-line-messaging-sdk-p0-fixes.md:980`
  - `docs/superpowers/plans/2026-07-02-line-messaging-sdk-p0-fixes.md:994`
  - `docs/superpowers/plans/2026-07-02-line-messaging-sdk-p0-fixes.md:1004`
  - `docs/superpowers/plans/2026-07-02-line-messaging-sdk-p0-fixes.md:1007`
- Trigger frequency: Every operator or agent that searches troubleshooting,
  handoff, or plan history for CCG commands can select an obsolete but still
  executable recipe. The repository has no visible deprecation marker,
  canonical-doc metadata, or validation gate distinguishing history from
  current instructions.
- Loop/cost flow: Search documentation -> choose direct wrapper recipe -> lose
  self-healing prompt/summary/retry semantics or use `--progress` -> encounter
  trust, PATH, provider, or Windows wrapper behavior -> return to
  troubleshooting -> rerun/rewrite review. One plan also rejects the approved
  single-backend quota fallback, so a valid degraded result can be repeated
  indefinitely waiting for both providers.
- Related unsafe example: The historical cleanup recipe at
  `docs/superpowers/plans/2026-07-02-line-identity-profile-adapter.md:326`
  through `:334` recursively removes generated directories without resolving
  and proving that every target remains under the intended worktree. The same
  repository already contains the safer root-check pattern at
  `docs/superpowers/plans/2026-06-29-neutralize-qpay-product-workflow-names.md:1181`
  through `:1193`.
- Packaged-output corroboration: The tracked tutorial
  `docs/Prompt_Template_Library_Render/胡夢嵩自學-Prompt_Template_Library_CCG_Trellis_Superpowers.docx`
  contains direct `codeagent-wrapper --progress` templates in paragraphs 37
  and 123. This binary copy cannot automatically inherit Markdown corrections.
- Static impact: Competing operational contracts cause repeated provider calls,
  manual PATH/trust repair, inconsistent approval claims, and unsafe command
  reuse. Exact elapsed-time/provider-cost savings require implementation-time
  measurement and are not part of confirmation.
- Existing guard/counter-evidence: `AGENTS.md` and the permanent-fix runbook are
  clear and current. They do not prevent older documents and packaged copies
  from being discovered or executed.
- Recommended action: Create one canonical executable CCG runbook and reusable
  command source. Mark history as historical/non-executable, replace live
  command blocks with links or generated snippets, add `superseded-by`
  metadata, validate direct-wrapper/deprecated-flag patterns, and regenerate
  packaged tutorials from the canonical source. Apply the guarded cleanup
  helper to every executable deletion example.
- Validation: A repository documentation check finds no unapproved direct
  Gemini/Claude/wrapper invocation or unguarded recursive deletion recipe.
  Every current runbook links to the canonical entrypoint and states degraded
  fallback semantics consistently.
- Rollback boundary: Canonical source, history labeling, lint rules, and binary
  tutorial regeneration are independently reversible.

### F01C-PERF-002 Tracked Scratch Outputs Dominate The Documentation Module

- Confirmed: true
- Primary owner: F01C
- Files:
  - `scratch/memberinfo-build-diag.txt:1`
  - `scratch/memberinfo-build-diag2.txt:1`
  - `scratch/memberinfo-main-nodeps-diag.txt:1`
  - `scratch/memberinfo-prjref-single-diag.txt:1`
  - `scratch/memberinfo-build.log%3Bverbosity:1`
  - `scratch/member_replay.mp4`
- Static inventory: HEAD stores 16,666,664 Git blob bytes in seven
  `scratch/**` files. That is 93.2% of the 17,885,908-byte inspected F01C
  inventory.
- Largest repeated artifacts:
  - `scratch/memberinfo-build-diag.txt`: 4,343,184 bytes.
  - `scratch/memberinfo-build-diag2.txt`: 4,342,702 bytes.
  - `scratch/memberinfo-main-nodeps-diag.txt`: 4,341,712 bytes.
  - `scratch/member_replay.mp4`: 1,920,716 bytes.
  - `scratch/memberinfo-build.log%3Bverbosity`: 881,465 bytes.
  - `scratch/memberinfo-prjref-single-diag.txt`: 836,214 bytes.
- Cost source: Multiple near-size-identical diagnostic logs retain verbose
  build-environment and evaluation traces. Git, clone, backup, text search,
  indexing, and context discovery must carry durable blobs whose names and
  contents identify them as investigation outputs.
- Trigger frequency: Storage cost affects every clone/history transfer.
  Search/index cost depends on client configuration; no latency value is
  claimed without measurement.
- Existing guard/counter-evidence: The artifacts may preserve useful incident
  history, and the video may be required for reproduction. No manifest,
  retention period, checksum/index, external archive pointer, or rationale was
  found that distinguishes durable evidence from disposable scratch.
- Recommended action: Define artifact classes and budgets. Keep a small
  redacted incident summary and manifest in Git; move large raw logs/media to a
  content-addressed external archive with retention/owner metadata; prevent new
  raw scratch enrollment; coordinate any history rewrite with F01A.
- Validation: Fresh diagnosis outputs remain untracked or go to the approved
  archive; a repository check enforces file-size/path budgets; retained
  summaries can locate archived evidence by hash; clone size and search/index
  timings are recorded before and after migration.
- Rollback boundary: New-file enrollment policy, working-tree migration,
  external archival, and Git-history rewrite are separate changes.

## Counter-Evidence And Rejected Candidates

- No measured editor, Git-status, or search latency is attributed to
  `scratch/**`; PERF-002 is confirmed by durable storage and repeated-artifact
  inventory only.
- The three large diagnostic logs are not claimed byte-identical. Their close
  sizes and shared diagnostic purpose establish repeated retained output, not
  exact duplication.
- `README.md:1` contains only the repository name. This is weak navigation and
  lifecycle evidence, but it is folded into PERF-001 rather than inflated into
  an independent issue.
- Historical documents are not wrong merely because they describe a past
  state. The defect is that executable history is discoverable without a
  durable historical/canonical distinction and directly conflicts with current
  root instructions.
