# CCG reviewer Task: subagent-goal-word-tutorial-review

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport

## Request
請審查這次文件產出任務的變更。

任務：Subagent 與 Goal 保母級 Word 教學。
主要交付物：.ccg/tasks/subagent-goal-word-tutorial/Subagent_Goal_保母級教學手冊.docx
產生器：.ccg/tasks/subagent-goal-word-tutorial/docx-generator/Program.cs
驗證：.ccg/tasks/subagent-goal-word-tutorial/docx-validation.json

請檢查：
1. 文件內容是否回答使用者需求：如何調用 subagent、goal 如何寫得完整保母級、brainstorming/writeplan 階段是否應預先考慮 subagent。
2. 文件是否包含實務流程、判斷矩陣、prompt 範本、反例/常見錯誤、檢查清單。
3. DOCX 產生方式是否有明顯風險，例如 Open XML 結構問題、輸出路徑錯誤、缺少驗證資訊。
4. 若因環境缺少 Python/LibreOffice/pdftoppm/Word COM 無法 render QA，是否有清楚降級說明。

輸出 Critical / Warning / Info 分級審查報告。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.