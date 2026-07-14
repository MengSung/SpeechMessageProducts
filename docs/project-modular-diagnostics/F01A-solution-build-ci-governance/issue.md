# F01A Solution, Build, and CI Governance Diagnostic Issues

Status: HUMAN_DECISION_REQUIRED
Module: F01A
Workspace: F01A-solution-build-ci-governance
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: READY
Issue document SHA-256: 312d6da27a3895aa8c6f4fd4dd9ba5ad16f6537407595c35d72fbff02d644c76

## Executive Summary

Five confirmed governance issues survived this agent's source reopening:
non-reproducible CI executable dependencies, an unusable ToolUtility test gate,
missing provider/consumer CI coverage, tracked private strong-name keys, and
unmanaged divergent project alternatives. No standalone performance issue
survived; one deterministic CI cost is included in F01A-SEC-001 and the legacy
solution-matrix candidate remains rejected pending measurement.

## Ranked Confirmed Issues

### F01A-SEC-001 CI Executes Mutable Or Floating External Code

- Category: Security
- Severity: High
- Priority: P0
- Priority score: 86
- Confirmed: true
- Evidence confidence: 20
- Impact score: 22
- Likelihood/frequency score: 15
- Security urgency score: 13
- Performance gain score: 4
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: S
- Primary owner: F01A
- Cross-module: X04B package-source/provenance handoff
- Gate blocked: false
- Files:
  - `.github/workflows/toolutility-tests.yml:21`
  - `.github/workflows/toolutility-tests.yml:24`
  - `.github/workflows/toolutility-tests.yml:40`
  - `.github/workflows/toolutility-tests.yml:45`
  - `.github/workflows/toolutility-tests.yml:54`
- Evidence: Four actions use mutable major tags; ReportGenerator is installed
  without a version. `upload-artifact@v3` is retired on GitHub.com, while the
  repository remote is GitHub.com.
- Control/data/lifetime flow: External action/tool identifier -> hosted runner
  download/install -> executable process with checkout, coverage, and job
  environment access.
- Impact: Upstream tag movement or floating package selection can change
  executable CI code without a repository commit. The retired artifact action
  also prevents the workflow from completing on GitHub.com.
- Why this is necessary: The only tracked workflow cannot be reproducible or
  dependable while executable dependencies are mutable or unsupported.
- Recommended action: Upgrade to supported actions, pin all actions to reviewed
  commit SHAs, use an exact ReportGenerator tool version/manifest, and add an
  automated reviewed update path.
- Validation: Run a GitHub.com test branch and record all resolved SHAs/tool
  versions; require a green artifact upload.
- Rollback boundary: Revert only the workflow dependency-pin commit.
- Extraction contract: CI dependency manifest -> GitHub runner -> immutable
  action/tool identities -> branch protection evidence.
- CCG round history:
  - Round 1: Gemini quota blocked; Claude KEEP; source rechecked true
  - Round 2: Gemini quota blocked; Claude KEEP; source rechecked true

### F01A-EXT-002 Tracked CI Does Not Enforce The Solution Or Consumer Matrix

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 79
- Confirmed: true
- Evidence confidence: 20
- Impact score: 25
- Likelihood/frequency score: 15
- Security urgency score: 2
- Performance gain score: 3
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F01A
- Cross-module: F02, F04, and all enrolled project owners
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.sln:6`
  - `SpeechMessageProducts.sln:40`
  - `.github/workflows/toolutility-tests.yml:4`
  - `.github/workflows/toolutility-tests.yml:13`
  - `ToolUtility/ToolUtility.csproj:51`
- Evidence: The solution enrolls 18 projects, but the only tracked workflow is
  path-filtered to ToolUtility and ToolUtility.Tests. ToolUtility directly
  references Line.Messaging and Dataverse, whose paths do not trigger the
  consumer gate. Solution changes also do not trigger it.
- Control/data/lifetime flow: Provider or solution change -> path filter false
  -> no consumer/solution validation -> merge decision lacks repository-owned
  compile/test evidence.
- Impact: Provider contract regressions, solution enrollment errors, and
  changes to most projects can merge without a tracked CI gate.
- Why this is necessary: Independent module extraction requires enforceable
  provider and consumer checks; a single isolated workflow does not implement
  the approved matrix.
- Recommended action: Add a minimal solution gate plus owner-aware
  provider/consumer workflows and stable required-check names.
- Validation: Test pull requests changing only the solution, each direct
  provider path, and a representative unrelated enrolled module.
- Rollback boundary: Each provider/consumer gate is an independent workflow
  commit.
- Extraction contract: Provider change -> provider gate -> declared consumer
  compile/test -> stable required check.
- CCG round history:
  - Round 1: Gemini quota blocked; Claude KEEP; source rechecked true
  - Round 2: Gemini quota blocked; Claude KEEP; source rechecked true

### F01A-EXT-001 The ToolUtility Test Gate Cannot Establish A Green Baseline

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 20
- Impact score: 23
- Likelihood/frequency score: 15
- Security urgency score: 4
- Performance gain score: 2
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F01A
- Cross-module: F01D test-container lifecycle; F03A/F03B test content
- Gate blocked: true
- Files:
  - `.github/workflows/toolutility-tests.yml:23`
  - `.github/workflows/toolutility-tests.yml:35`
  - `ToolUtility.Tests/ToolUtility.Tests.csproj:4`
  - `ToolUtility.Tests/ToolUtility.Tests.csproj:39`
  - `ToolUtility/ToolUtility.csproj:4`
  - `SpeechMessageProducts.sln:6`
- Evidence: CI selects ToolUtility.Tests, which targets `net8.0` and references
  a `net10.0`-only ToolUtility project. The test project is not enrolled in the
  solution's complete project block.
- Control/data/lifetime flow: Matching change -> workflow restore/build/test ->
  incompatible target-framework reference -> no executable test/coverage
  baseline.
- Impact: The repository's stated ToolUtility gate cannot provide green,
  solution-reproducible evidence. Required-check status is unknown, so the
  result is either merge blockage or a routinely ignored gate.
- Why this is necessary: F03A/F03B cannot enter optimization without an
  executable provider baseline and consumer gate.
- Recommended action: F01D aligns the test target; F01A records an explicit
  enrollment decision and schedules the repaired command.
- Validation: Clean-clone restore/build/test under the supported .NET 10 SDK,
  followed by a green GitHub Actions run.
- Rollback boundary: F01A enrollment/workflow and F01D target changes remain
  separate owner commits.
- Extraction contract: Compatible test container -> ToolUtility provider ->
  executable test/coverage result -> branch gate.
- CCG round history:
  - Round 1: Gemini quota blocked; Claude KEEP; source rechecked true
  - Round 2: Gemini quota blocked; Claude KEEP; source rechecked true

### F01A-SEC-002 Private Strong-Name Key Blobs Are Tracked Without Prevention

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 67
- Confirmed: true
- Evidence confidence: 19
- Impact score: 16
- Likelihood/frequency score: 15
- Security urgency score: 10
- Performance gain score: 0
- Loop leverage score: 5
- Ease/reversibility score: 2
- Effort: M
- Primary owner: F01A
- Cross-module: F02, F08, X02Q key lifecycle
- Gate blocked: false
- Files:
  - `.gitignore:186-195`
  - `SpeechMessageProducts.sln:6`
- Evidence: The complete `.gitignore:186-195` credential/other-file block
  covers `*.pfx` and publish settings but no `*.snk` pattern. Git tracks three
  596-byte `.snk` files with CAPI
  `PRIVATEKEYBLOB`/`RSA2` headers; none is ignored. The LinePay and Trace copies
  are identical. Their project families are not enrolled in the solution.
- Control/data/lifetime flow: Private RSA blobs -> Git objects/clones ->
  repository readers -> ability to produce the same strong-name identity.
- Impact: Key-pair confidentiality is lost. Production trust impact is not
  proved because strong names are not Authenticode and no active consumer of
  the non-enrolled project identities was found.
- Why this is necessary: F01A owns Git prevention and repository response even
  though product owners own key rotation or retirement.
- Recommended action: Classify each key, rotate retained identities, remove
  private material from Git, decide history response, and add secret/key
  prevention beyond `.gitignore`.
- Validation: Derive public tokens without exposing private blobs, search
  release consumers, and verify recurrence detection.
- Rollback boundary: F01A prevention is separate from each owner rotation.
- Extraction contract: Repository key policy -> product owner identity
  decision -> external secret storage or retirement -> recurrence gate.
- CCG round history:
  - Round 1: Gemini quota blocked; Claude REWRITE line citation; source
    rechecked true
  - Round 2: Gemini quota blocked; Claude KEEP; source rechecked true

### F01A-EXT-003 Canonical Project Alternatives Are Unmanaged And Divergent

- Category: Extraction
- Severity: Medium
- Priority: P2
- Priority score: 62
- Confirmed: true
- Evidence confidence: 20
- Impact score: 17
- Likelihood/frequency score: 9
- Security urgency score: 2
- Performance gain score: 1
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F01A
- Cross-module: F02, F04, F05A, F08, X02Q lifecycle decisions
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.sln:8`
  - `SpeechMessageProducts.sln:11`
  - `Line.Messaging/Line.Messaging_Net10.csproj:1`
  - `LineMessagingProcessor/LineMessagingProcessor_Net10.csproj:13`
  - `LineMessagingProcessor/LineMessagingProcessor.csproj:37`
  - `LineMessagingProcessor/LineMessagingProcessor.csproj:47`
- Evidence: Eight project definitions are outside the solution. The
  Line.Messaging alternate is content-identical to the selected project after
  normalizing the canonical file's UTF-8 BOM, while the Processor alternate
  has a materially different compile/dependency graph. No root registry or CI
  rule records their lifecycle.
- Control/data/lifetime flow: Developer/script selects project path -> different
  project graph under the same target/project identity -> noncanonical build
  output outside solution validation.
- Impact: Manual/package consumers can select stale or divergent definitions;
  fixes to the canonical project are not guaranteed to propagate. No active
  alternate consumer was proved.
- Why this is necessary: Canonical project selection is an F01A responsibility
  and is required before safe module extraction or retirement.
- Recommended action: Record one lifecycle state per `.csproj`, have product
  owners migrate/remove/retain alternates, and add an unregistered-project
  check.
- Validation: Enumerate project files against the registry and prove no
  consumer before deletion.
- Rollback boundary: F01A registry/check and product-owner file changes are
  separate commits.
- Extraction contract: Project registry -> one canonical path/status -> owner
  build/test -> consumer migration or retirement proof.
- CCG round history:
  - Round 1: Gemini quota blocked; Claude REWRITE byte-identity wording; source
    rechecked true
  - Round 2: Gemini quota blocked; Claude KEEP; source rechecked true

## Runtime Validation Pending

None. Runtime acceptance tests are documented, but no retained issue depends
exclusively on runtime evidence.

## Deleted Or Rejected Candidates

- F01A-PERF-C01 legacy solution matrix inflation: rejected because no CI matrix,
  usage frequency, or timing evidence proves material performance cost.
- Missing explicit `permissions` and `persist-credentials: false`: retained as
  companion hardening for F01A-SEC-001, not a separate issue because effective
  token settings are external.
- Codecov secret exfiltration: rejected; no explicit secret or sensitive
  artifact content was proved.
- Copilot guidance defects: handed to F01B; no product vulnerability or F01A
  agent-workflow ownership was claimed.
- Enroll every visible project: rejected; quarantine/retirement can justify
  intentional non-enrollment.

## Cross-Module Handoffs

1. F01D: compatible ToolUtility test target and executable green baseline.
2. F02/F04: provider gates for ToolUtility; F04 also decides duplicate
   Line.Messaging project lifecycle.
3. F05A: decide the divergent Processor alternate lifecycle.
4. F02/F08/X02Q: classify and rotate/retire tracked key identities.
5. X04B: CI executable package source and provenance.
6. F01B: GitHub Copilot instruction semantics and CCG artifact policy.

## Final CCG Approval

Substantive issue verdict: `APPROVED_DEGRADED`.

- Round 2 submitted SHA-256:
  `CD6288D54730BAB4097B3C0A44DB71B2B26A11C825B8127D24D0D3A6381FD939`.
- Claude reopened the original files and returned KEEP for all five issues.
- Gemini produced no output because provider quota/billing returned HTTP 403
  `余额不足`.
- Round 2 summary has `degradedFallback=true`,
  `fallbackAccepted=true`, and no unresolved Critical or Warning.
- Retained: 5. Deleted: 0. Runtime pending: 0.

Final workflow status remains `INVALID_WRITE_SCOPE`: the round 1 Claude reviewer
ran `dotnet restore` despite a read-only prompt and updated ignored generated
files under four product-project `obj/**` directories. Round 2 was read-only,
but it cannot erase the earlier side effect. No generated files were deleted or
reverted because their pre-run baseline was not captured.
