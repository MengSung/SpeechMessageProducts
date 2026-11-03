# Duplicate Source Ratchet

Run `powershell -NoProfile -File tools/RepoQuality/Find-DuplicateCode.ps1 -Baseline tools/RepoQuality/duplicate-baseline.json` from the current worktree after P0-06.

- Enumerate only C# projects declared in the selected solution. Validate absolute repository boundaries before reading; never search parent worktrees or traverse reparse points. Exclude build output, generated C#, documentation/reference snapshots and other worktrees.
- Content identity is normalized SHA256. Type identity is namespace plus the complete enclosing type chain and class/record/struct declaration, with comment/string masking and block/file-scoped namespace tracking. Only cross-project groups count.
- Baseline entries are stable kind/key identities with unique source locations. Reject repeated baseline group identities and locations before using the dictionary. New groups or additional locations fail; removed locations produce BaselineStale reminders.
- CLI invocation owns all scan state. Keep only one source text at a time (16 MiB limit), then retain metadata only. Existing legacy encodings may be decoded strictly for read-only inventory; the encoding gate owns publishing rules.
- Tests use isolated synthetic solutions and both PowerShell engines. Assert specific diagnostics, not merely any nonzero exit. Never change products to satisfy the scanner or rewrite existing source as part of baseline creation.

Nested types with different enclosing types are distinct identities. Track type
frames separately from namespace frames; restore siblings on closing braces and
clear body-less record declarations at semicolons. Keep positive same-owner and
negative different-owner fixtures. Do not regenerate the baseline to hide a
parser mistake.
