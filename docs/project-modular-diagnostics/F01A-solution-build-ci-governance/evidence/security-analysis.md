# F01A Security Analysis

Status: COMPLETE
Module: F01A - Solution, Build, and CI Governance
Mode: DIAGNOSIS_ONLY
Diagnostic agent: F01A Workspace Diagnostic Subagent

## Method

This agent independently reopened every F01A-owned source. The previous
investigator-authored content in this file was not accepted as evidence. Git,
solution, workflow, and binary-metadata checks were repeated by this agent.

The review traced CI code sources to runner execution and Git-governed key
material to repository readers. It also checked counter-evidence: event type,
explicit secrets, workflow permissions, project enrollment, and whether an
active trust consumer could be proved.

## Confirmed Finding: F01A-SEC-001

### CI Executes Mutable Or Floating External Code

Evidence:

- `.github/workflows/toolutility-tests.yml:21` uses
  `actions/checkout@v4`.
- `.github/workflows/toolutility-tests.yml:24` uses
  `actions/setup-dotnet@v4`.
- `.github/workflows/toolutility-tests.yml:40` installs
  `dotnet-reportgenerator-globaltool` without `--version`.
- `.github/workflows/toolutility-tests.yml:45` uses
  `codecov/codecov-action@v3`.
- `.github/workflows/toolutility-tests.yml:54` uses
  `actions/upload-artifact@v3`.
- The repository remote is `https://github.com/MengSung/SpeechMessageProducts.git`.
  GitHub's official "Deprecation notice: v3 of the artifact actions" states
  that `upload-artifact@v3` was scheduled to stop working on GitHub.com on
  January 30, 2025. The workflow history shows this file was still committed
  in this form on 2025-11-20.

Source/control/sink flow:

1. Mutable major tags or an unversioned NuGet tool select executable code
   outside the repository's reviewed commit.
2. GitHub Actions downloads or installs that code on `windows-latest`.
3. The code receives runner process access, checkout contents, coverage data,
   and the job environment.
4. The retired artifact action also creates a deterministic CI availability
   failure on GitHub.com.

Existing guards and counter-evidence:

- The workflow uses `pull_request`, not `pull_request_target`, at
  `.github/workflows/toolutility-tests.yml:9-13`.
- No repository secret is explicitly passed to Codecov.
- `actions/*` are GitHub-owned and the tags constrain release families.
- No evidence says the currently resolved checkout/setup commits are
  malicious.
- These guards do not make a mutable tag or floating tool reproducible, and
  they do not restore support for `upload-artifact@v3`.

Ownership:

- F01A owns all affected workflow lines.
- X04B owns package-source trust and package provenance, not the decision to
  float the tool version in CI.

Recommended control:

- Replace every action tag with a reviewed full commit SHA and retain a version
  comment.
- Upgrade the artifact action to a supported release before pinning its SHA.
- Move ReportGenerator to an exact-version tool manifest or install an exact
  reviewed version.
- Add an automated dependency-update path so immutable pins remain maintained.

## Confirmed Finding: F01A-SEC-002

### Private Strong-Name Key Blobs Are Tracked Without Repository Prevention

Evidence:

- `.gitignore:186-195` ignores `*.pfx` and publish settings but not `.snk`.
- `git ls-files *.snk` returns:
  - `LinePayCSharp/SpeechMessageCrmKey.snk`
  - `PowerPlatform.Dataverse.Client/NSspi/nsspi key.snk`
  - `Trace/SpeechMessageCrmKey.snk`
- `git check-ignore -v --no-index` confirms none of the three paths is ignored.
- Each file is 596 bytes and begins with
  `07 02 00 00 00 24 00 00 52 53 41 32`, a CAPI private-key
  `PRIVATEKEYBLOB`/`RSA2` header.
- The LinePay and Trace files have the same SHA-256,
  `2B338399FEFBA7F82B072FD606C2DBB8AF1D1F3E776F466FAB585F278C3F176D`.

Source/control/sink flow:

1. Private RSA key-pair blobs are present in tracked Git objects.
2. Every repository clone and reader receives the private material.
3. A reader can produce assemblies with the same strong-name identity.

Impact boundary:

- Confidentiality of these key pairs is already lost.
- Strong names are not Authenticode and are not automatically a runtime
  authorization boundary.
- `LinePayCSharp`, NSspi, and all Trace definitions are not enrolled in
  `SpeechMessageProducts.sln:6-40`; no active deployment or trust decision
  using their public key tokens was proved.
- Therefore the confirmed issue is repository key governance and identity
  reuse, not proven production code execution.

Ownership:

- F01A owns Git prevention and repository response.
- F02, F08, and X02Q own the key-bearing project families and any rotation,
  retirement, or project-file changes.

Recommended control:

- Classify whether each key is an intentionally public test key or a retained
  signing identity.
- For retained identities, rotate through the owning module, remove private
  material from the repository, and decide whether history rewriting is
  required.
- Add `.snk` prevention plus secret scanning/pre-commit detection. An ignore
  rule alone does not remove tracked history.

## Rejected Or Downgraded Security Candidates

### Missing Explicit Workflow Permissions

`.github/workflows/toolutility-tests.yml:1-69` has no `permissions` declaration,
and checkout at line 21 has no `persist-credentials: false`. This is valid
hardening, but repository/organization token defaults and branch-protection
settings are not present in Git. The remote repository is public and the
workflow does not use `pull_request_target` or explicit secrets. The candidate
was not retained as a separate confirmed issue; it is a recommended companion
control for F01A-SEC-001.

### Codecov Secret Exfiltration

Rejected. Lines 43-50 pass coverage configuration and no explicit secret.
Coverage is an external data boundary, but no sensitive fixture or generated
artifact content was proved.

### Missing Generic Secret Ignore Patterns

Rejected as a standalone issue. No tracked `.env`, `.pem`, or generic `.key`
file was found. Ignore rules are prevention aids rather than a secret boundary.
The tracked private `.snk` material is handled specifically by F01A-SEC-002.

### Copilot Guidance As A Product Vulnerability

Rejected and handed to F01B. The `.github/copilot-*.md` files contain generic
agent instructions, including explicit secret and authorization rules. Their
accuracy can affect generated suggestions, but no product source/sink is
established inside F01A.

## Cross-Module Handoffs

- F01B: semantic accuracy and lifecycle of GitHub Copilot instruction content.
- F02/F08/X02Q: classify, rotate, or retire the tracked strong-name identities.
- X04B: trusted package source and provenance for the CI-installed tool.
