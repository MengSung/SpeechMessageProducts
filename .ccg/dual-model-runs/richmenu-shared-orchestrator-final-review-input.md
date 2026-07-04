# RichMenu Shared Orchestrator Code Review

請審查目前 worktree 的 git diff，角色是 reviewer。

## 背景
- 分支/worktree: Jesus_5.1.7.WorktreeRefactorRichMenu
- 本輪重點：LINE RichMenu 共用化抽離與 CCG 雙模型自我修復穩定化
- 使用者要求：
  - 程式碼易於管理，符合 Linus 原則：少特殊情況、資料流清楚、不藏全域狀態、一個東西只做一件事。
  - 修改過的 .cs 檔案需有深入仔細完整的繁體中文註解。
  - 檔案需是 UTF-8，不是 Big5。
  - 完成後需雙模型 Code Review；Critical 必須修正。

## 請重點檢查
Critical:
1. build/test breakage、DI ambiguity、無法啟動的 service registration。
2. LineMessagingProcessor.RichMenus 是否滲入 ChurchReport、CRM、Controller、DbContext、IActionResult、SpeechMessage.Payments 等產品相依。
3. 是否重新引入舊 RichMenu 特殊路徑：HandleTextAsync、RichMenuTextContext、RichMenuTextDecision、舊 response DTO、sync-over-async。
4. CCG 自我修復腳本是否有明顯會讓 Gemini/Claude review 失敗或誤判成功的錯誤。
5. 修改過的 .cs 註解是否會誤導未來產品整合。

Warning:
1. RichMenu 共用架構是否有可維護性風險、狀態儲存誤導、過度耦合或責任不清。
2. CCG fallback 文件與腳本是否清楚區分「雙模型成功」與「單模型降級」。
3. UTF-8 / CRLF / 生成資料夾清理是否仍有缺口。

Info:
1. 命名、註解、可讀性、未來產品整合建議。

## 已本機驗證
- Encoding OK: touched text files are UTF-8 without BOM + CRLF.
- Boundary OK: LineMessagingProcessor.RichMenus has no product-specific references.
- Legacy RichMenu leftover scan OK in shared projects.
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