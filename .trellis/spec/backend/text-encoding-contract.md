# Text Encoding Contract

## 1. Scope / Trigger

Use this contract before publishing source, configuration, Markdown, or script
files, or when converting legacy text. P0-05 owns the guard and converter; later
WP validation invokes the Changed scope against the phase base.

## 2. Signatures

```powershell
powershell -NoProfile -File tools/RepoQuality/Test-TextEncoding.ps1 -Scope Changed -Base <ref>
powershell -NoProfile -File tools/RepoQuality/Test-TextEncoding.ps1 -Scope All -ReportPath tools/RepoQuality/reports/encoding-baseline.md
powershell -NoProfile -File tools/RepoQuality/Convert-TextEncoding.ps1 -Path <file> -ManifestPath <manifest> [-Apply]
```

## 3. Contracts

- Published text is strict UTF-8 without BOM, CRLF, and final CRLF. U+FFFD,
  invalid UTF-8, mixed/LF-only endings, and BOM are failures in Changed scope.
- PowerShell executable tokens are ASCII; non-ASCII is allowed only in comment
  tokens. Parse errors are failures. The check uses Git tracked paths for All,
  never parent worktrees, build output, or external content reached through a junction/reparse point.
- Timestamped `.ccg/dual-model-runs/<timestamp>-*/` raw CCG prompt/stdout/stderr is excluded from the
  encoding gate and preserved as machine evidence; its exclusion is reported in
  the count and does not alter its bytes.
- Conversion defaults to dry-run. Apply requires an explicit switch, validates
  encoding, literal segments, candidate bytes, and SHA-256 before atomic replace.
  Each rejected file stays unchanged; each accepted file is replaced atomically. The manifest records metadata without source text.
- Duplicate source paths and a manifest path equal to a source fail before any
  write. Temporary files are owned by one invocation and removed after replacement
  or failure.

## 4. Validation & Error Matrix

| Condition | Result |
| --- | --- |
| Valid UTF-8 no BOM CRLF | Changed check passes |
| BOM/LF-only/invalid UTF-8/no final CRLF/U+FFFD | Changed check fails with named issue |
| Non-ASCII PowerShell string/token | Fails; comment token is allowed |
| All scope | Reads only `git ls-files --cached`; emits report and exit 0 for old issues |
| CP950 or UTF BOM conversion | Dry-run by default; Apply writes UTF-8 only after literal checks |
| Literal count/content changes, invalid bytes, duplicate source, or manifest collision | Exit 1; rejected source hashes remain unchanged |

## 5. Good / Base / Bad Cases

- Good: CP950 Chinese source converts to exact UTF-8 bytes and the manifest records
  before/after hashes plus unchanged literal status.
- Base: All inventory reports existing Trace CP950, Line.Messaging BOM, and other
  legacy issues without changing them.
- Bad: recursively scanning `.worktrees/`, treating an untracked outside file as
  a repository source, or writing the first converted file before validating a
  later duplicate path.

## 6. Tests Required

The Pester suite covers valid input, each byte failure, PowerShell token rules,
tracked-only All scope, CP950/UTF BOM conversion, dry-run, literal mutation,
invalid input, duplicate paths, manifest collision, and source immutability.
Run the suite when changing the encoding tools or their tests; run the Changed gate before every remaining phase-0 package.

## 7. Wrong vs Correct

Wrong: use `Get-ChildItem -Recurse` from the worktree or decode CP950 with a
replacement fallback and continue.

Correct: enumerate Git tracked paths, use strict decoders, preserve raw CCG
machine evidence by explicit path exclusion, and fail closed before writing.
