# Review

## Critical

- None.

## Warning

- External dual-model review was not run because this is an S/low-risk repository formatting rule change. The change is still verified with deterministic byte-level checks.

## Info

- `.editorconfig` now applies UTF-8 without BOM and CRLF to common source, view, config, script, and documentation files.
- `.gitattributes` now pins the same common text file types to CRLF checkout behavior.
- `.trellis/spec/backend/quality-guidelines.md` now records the required pre-completion byte scan for modified text files.

## Verification

- `git diff --check`: passed.
- Modified text files byte scan: `UTF8BOM=False`, `LFOnly=0`.
