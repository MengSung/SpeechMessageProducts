ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: subagent-goal-word-tutorial-analysis

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport

## Request
請針對以下文件任務做分析，輸出一份 Word 教學文件的大綱與內容建議。

任務：撰寫「Subagent 與 Goal 保母級 Word 教學」。
使用者要求：
1. 教導如何調用 subagent。
2. goal 要非常詳細、完整、保母級。
3. 回答 subagent 是否應在 brainstorming、writeplan 時就預備考慮進來。
4. 產出深入、仔細、完整的 Word 檔教學。

請特別分析：
- 讀者需要先懂哪些概念。
- subagent 適合與不適合使用的情境。
- brainstorming 階段要如何預先判斷 subagent 需求。
- writeplan 階段要如何拆分 subagent 工作包。
- goal 應包含哪些欄位、粒度、驗收標準、邊界與回報格式。
- 如何避免常見錯誤：任務重疊、上下文不足、沒有驗收標準、沒有關閉代理、過早 dispatch。
- 建議的文件章節、範本、表格與檢查清單。

輸出格式：
- 繁體中文。
- 使用 Critical / Warning / Info 分級指出教學中不可漏掉的內容。
- 提供一份推薦章節大綱。
- 提供 3 個可直接放入文件的 prompt 範本。


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