# CCG reviewer Task: ccg-auto-recovery-entrypoint-smoke

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.7.WorktreeRefactorRichMenu

## Request
請用 reviewer 角色做最小驗證：確認這是一個 CCG self-healing runner smoke test。只需要回覆：
- 是否收到任務
- 是否理解這是自動入口驗證
- 不需要檢查任何產品程式碼

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when explicitly allowed.