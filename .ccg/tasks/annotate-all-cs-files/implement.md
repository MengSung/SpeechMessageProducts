# Implementation Plan

1. Confirm worktree branch, tracked C# scope, and clean baseline.
2. Run dual-model analysis through the project CCG self-healing entrypoint.
3. Add broad Traditional Chinese file-level comments to all tracked `.cs` files using a deterministic mechanical pass.
4. Re-read representative samples from production, SDK, infrastructure, workflow, and test projects.
5. Run verification:
   - `git diff --check -- '*.cs'`
   - UTF-8 without BOM + CRLF byte scan on modified `.cs` files
   - added-comment CJK scan
   - non-comment executable diff scan
   - solution/project build and test commands that are compatible with the current target frameworks
6. Run CCG review if the broad diff is accepted by local verification.
