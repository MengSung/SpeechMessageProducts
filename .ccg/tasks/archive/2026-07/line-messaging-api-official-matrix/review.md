# LINE Messaging API 官方對照矩陣 Review

## 本地驗證

- `git diff --check`：通過。
- `dotnet build ChurchReport.sln --no-restore`：通過，0 errors。
- 既有 warning：`ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardTests.cs(33,17)` 的 `xUnit1012`，本次沒有修改該測試。
- 矩陣文件列數：139 個資料列。
- 優先級統計：`P0` 27、`P1` 43、`P2` 69、`P3` 0。
- 文字檔格式：`Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md` 已確認 UTF-8 without BOM，且沒有 LF-only 行。

## CCG 雙模型 Review 嘗試結果

依 CCG 規則，已嘗試透過 `C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe` 呼叫 Gemini 與 Claude。

結果：

- Gemini backend 失敗原因：`gemini command not found in PATH`。
- Claude backend 失敗原因：`claude command not found in PATH`。
- PATH 中雖包含 `C:\Users\Administrator\AppData\Roaming\Claude\claude-code\2.1.92`，但該路徑目前不存在。
- `C:\Users\Administrator\AppData\Roaming\npm` 目前沒有 `claude` 或 `gemini` shim。

結論：本次矩陣文件已完成本地驗證；初版外部 Gemini/Claude CCG review 曾因本機 CLI/PATH 安裝狀態尚未完成而受阻。後續 `line-messaging-api-external-review` 任務已在 CLI 修復後完成雙模型 review，並依 Claude reviewer 發現補上 webhook endpoint management 與 `X-Line-Signature` 驗章列。

## 自我審查結果

### Critical

未發現 source code 被修改；本次只新增與修改文件及 CCG 任務紀錄。

### Warning

- 矩陣是官方文件導向的事實表，但官方 LINE 文件會持續更新；下一階段開始寫 SDK 修正前，仍應重新開啟官方 reference 逐項核對 `P0` endpoint。
- `OAuth / Token` 區列出目前 SDK 缺漏與過時語意，下一階段不應一次重寫所有 token 流程，應先分 legacy/v2.1/stateless 三條小路徑。

### Info

- 本矩陣刻意不修 SDK source code，符合本階段「先建立官方對照矩陣，再導出修正 plan」的邊界。
- 後續 SDK 修正建議先處理 `P0`：硬編碼 token、`api-data.line.me` host、`/v2/v2` path、mark-as-read、rich menu batch endpoint。
