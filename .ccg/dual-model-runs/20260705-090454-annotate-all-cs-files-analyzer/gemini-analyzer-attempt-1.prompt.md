ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: annotate-all-cs-files

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.7.WorktreeRichMenuAddComment

## Request
# Task: Add detailed Traditional Chinese comments to all C# files

Repository: D:/網頁APP雲端線上版本/DevExpressDevExtreme-21.2.7版本/音訊產品版本/ChurchReport/.worktrees/Jesus_5.1.7.WorktreeRichMenuAddComment
Branch: Jesus_5.1.7.WorktreeRichMenuAddComment
Tracked C# files in scope: 818

User requirement:
- Add detailed, deep, complete comments to all .cs files.
- Comments must be Traditional Chinese.
- File encoding must be UTF-8 without BOM.
- Preserve behavior; comment/documentation-only changes.
- Work in the current worktree only.

Please analyze and return:
1. Practical implementation strategy for annotating 818 C# files without changing behavior.
2. High-risk file categories where XML documentation placement may cause compiler warnings.
3. Verification commands for UTF-8/no-BOM, CRLF, comment language, XML doc placement, build, and tests.
4. Any recommendations to avoid over-commenting generated or trivial code while still satisfying the user's request.

Output concise actionable guidance with Critical/Warning/Info sections if relevant.

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.