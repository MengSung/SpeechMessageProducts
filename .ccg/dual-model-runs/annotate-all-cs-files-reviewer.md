# CCG reviewer Task: annotate-all-cs-files

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.7.WorktreeRichMenuAddComment

## Request
# Review: Add detailed Traditional Chinese comments to all C# files

Repository: D:/網頁APP雲端線上版本/DevExpressDevExtreme-21.2.7版本/音訊產品版本/ChurchReport/.worktrees/Jesus_5.1.7.WorktreeRichMenuAddComment
Branch: Jesus_5.1.7.WorktreeRichMenuAddComment

Scope:
- 818 tracked .cs files were annotated with a Traditional Chinese file-level comment block.
- Header includes file path, project area, responsibility, detected main types, detected members, namespaces, reading path, maintenance notes, behavior guard, and encoding requirement.
- Files were normalized to UTF-8 without BOM and CRLF.
- No executable behavior should be changed.

Local verification already passed:
- git diff --check -- '*.cs'
- Strict UTF-8 without BOM + CRLF scan across all tracked .cs files
- Header marker count: 818/818
- Content audit: removing the generated header from each file, then ignoring trailing whitespace and final newline, matches the original HEAD content for 818/818 files
- dotnet build ChurchReport.sln --nologo --verbosity minimal passed with one existing xUnit1012 warning in ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardTests.cs
- Targeted tests passed:
  - Line.Messaging.Tests: 32 passed
  - LineMessagingProcessor.RichMenus.Tests: 34 passed
  - LineMessagingProcessor.AspNetCore.Tests: 4 passed
  - LineMessagingProcessor.Tests: 33 passed
  - ChurchReport.MemberInfo.Tests: 207 passed
  - SpeechMessage.Payments.Tests: 55 passed
  - ToolUtility build: 0 warnings / 0 errors

Please review the broad comment-only diff for:
1. Any behavior-changing edits outside comments/encoding normalization.
2. Any malformed comments that could cause C# syntax or XML documentation issues.
3. Any risk from converting prior BOM/Big5 files to UTF-8 without BOM.
4. Whether the task is safe to hand off for user review.

Output Critical / Warning / Info findings. If no blocking issues, say PASS.

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.