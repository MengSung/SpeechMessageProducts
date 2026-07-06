# Review: Subagent 與 Goal 保母級 Word 教學

## 結論
- Status: PASSED with degraded external review.
- Deliverable: `docs/Subagent_Goal_保母級教學手冊.docx`.
- Structural QA: required DOCX package parts exist and `word/document.xml` / `word/styles.xml` parse successfully.

## Critical
- 無 Critical 阻斷項。

## Warning
- 本機環境缺少可用的 Python、LibreOffice、pdftoppm，且 Word COM 啟動失敗，因此無法完成 DOCX -> PNG 的視覺渲染 QA。已以 Open XML 結構驗證降級替代，並在 `docx-validation.json` 中記錄。
- CCG 外部 review 只有 Gemini 完成；Claude 因 session limit 被配額阻擋，所以不是完整雙模型審查。

## Info
- 文件已涵蓋：subagent 心智模型、使用/不使用判斷、brainstorming 階段預判、writeplan 階段拆分、保母級 goal 欄位、可複製 prompt 範本、常見錯誤與 dispatch 前檢查清單。
- DOCX 透過 C# Open XML 產生，避免 PowerShell 中文編碼造成的腳本解析問題。
