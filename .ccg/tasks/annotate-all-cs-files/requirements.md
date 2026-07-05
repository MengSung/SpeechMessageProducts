# Requirements

User request: after the prior commit, add detailed, deep, complete Traditional Chinese comments to every tracked `.cs` file in the current worktree.

## Explicit requirements

- Scope is all tracked C# source files under the current worktree, excluding generated build output such as `bin/`, `obj/`, `.git/`, and nested worktrees.
- Comments must be written in Traditional Chinese.
- Preserve behavior: comment/documentation-only edits, no executable logic changes.
- Preserve text format: UTF-8 without BOM and CRLF.
- Work only on branch `Jesus_5.1.7.WorktreeRichMenuAddComment` in `.worktrees/Jesus_5.1.7.WorktreeRichMenuAddComment`.

## Implementation strategy

- Use general `//` comments for broad automated annotations to avoid XML documentation compiler warnings from malformed or misplaced `///` blocks.
- Add a structured file-level comment block to every `.cs` file that explains the file's project context, maintenance intent, and behavior-preservation constraint.
- Generate project-aware wording from path segments so comments are useful without touching code semantics.
- Normalize modified files to UTF-8 without BOM and CRLF after each batch.
- Verify with diff checks, encoding checks, comment language checks, and targeted builds/tests.
