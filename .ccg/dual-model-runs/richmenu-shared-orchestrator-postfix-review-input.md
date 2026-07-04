# RichMenu Shared Orchestrator Final Post-Fix Code Review

請審查目前 worktree 的 git diff，角色是 reviewer。

## 背景
- 分支/worktree: Jesus_5.1.7.WorktreeRefactorRichMenu
- 本輪重點：LINE RichMenu 共用化抽離、RichMenu 共用層保母級繁中註解、CCG Gemini + Claude 自我修復入口穩定化。
- 前一輪 Claude review 回報 No Critical findings，但有 Warning：
  1. CCG exit code 0 可能讓人混淆 full dual-model success 與 degraded fallback。
  2. AGENTS.md 與 Standing Fallback Policy 文字不一致。
  3. RichMenuOrchestrator 註解列出太具體的未來產品情境。
- 已依建議修正：
  1. AGENTS.md 與 .trellis guide 統一成「專案層級已核准 quota/session fallback，但必須標示為 degraded fallback，不可稱 full dual-model success」。
  2. Start-CcgDualModelRun.ps1 執行 runner 後會讀 summary.json，主動輸出 full dual-model success / degraded fallback / quota blocked 狀態。
  3. RichMenu 共用層註解把詳細說明移入 <remarks>，並將具體未來產品例子改成抽象角色、租戶、狀態、文字觸發等通用描述。

## 請重點檢查
Critical:
1. build/test breakage、DI ambiguity、無法啟動的 service registration。
2. LineMessagingProcessor.RichMenus 是否滲入 ChurchReport、CRM、Controller、DbContext、IActionResult、SpeechMessage.Payments 等產品相依。
3. 是否重新引入舊 RichMenu 特殊路徑：HandleTextAsync、RichMenuTextContext、RichMenuTextDecision、舊 response DTO、sync-over-async。
4. Start-CcgDualModelRun.ps1 或 Invoke-CcgDualModelWithSelfHealing.ps1 是否有語法錯誤、錯誤 exit code、或會誤導 full dual-model success / degraded fallback 的問題。
5. 修改過的 .cs 註解是否仍會誤導未來產品整合。

Warning:
1. RichMenu 共用架構是否仍有可維護性風險、狀態儲存誤導、過度耦合或責任不清。
2. CCG fallback 文件與腳本是否已清楚區分「雙模型成功」與「單模型降級」。
3. UTF-8 / CRLF / 生成資料夾清理是否仍有缺口。

Info:
1. 命名、註解、可讀性、未來產品整合建議。

## 已重新本機驗證
- Encoding OK: touched text files are UTF-8 without BOM + CRLF.
- Boundary OK: LineMessagingProcessor.RichMenus has no product-specific references.
- Legacy RichMenu leftover scan OK in shared projects.
- PowerShell script parse OK for Start-CcgDualModelRun.ps1 and Invoke-CcgDualModelWithSelfHealing.ps1.
- dotnet test LineMessagingProcessor.RichMenus.Tests: Passed 34.
- dotnet test LineMessagingProcessor.AspNetCore.Tests: Passed 4.
- dotnet test LineMessagingProcessor.Tests: Passed 33.
- dotnet test ChurchReport.MemberInfo.Tests focused LINE filters: Passed 34.
- dotnet build ChurchReport.sln: 0 warnings, 0 errors.
- bin/ obj/ artifacts/ folders cleaned after verification.

## 請輸出
請用以下格式：
1. Critical findings（若無請明確寫 No Critical findings）
2. Warning findings
3. Info findings
4. 是否建議合併/提交