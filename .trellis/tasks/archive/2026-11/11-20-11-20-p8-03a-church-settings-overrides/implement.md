# P8-03a 實施計畫

## 固定邊界

- 只執行 P8-03a；單元內依序完成，不平行派工，不連 D365／LINE。
- 不執行單元級 CCG 或外部審查。階段審查留待全部階段單元完成後依 ClaudeOnly 規則執行一次。
- 不修改 08A 以外計畫文件、進度頁規劃文字、系統設定或 `C:\smv\guard`。
- 不新增守門 clone／工作樹。所有守門在唯一 `C:\smv\guard-p8`，紀錄只到 `C:\smv\guard-p8-evidence`。

## 步驟

1. 核對分支與工作樹；保留本任務建立前已存在的父任務連結變更。確認 P8-02 與 P8-01 整包已驗收；依使用者最新更正，不再沿用 P8-01c 阻擋紀錄。
2. 完成本任務 PRD、設計與本實施計畫；以 `task.py start` 啟動子任務並記錄階段。
3. 對照 08A §4.3、D8-13～D8-15 及 backend cache、tenancy、localization、error、file-documentation specs；搜尋既有 factory、lease、polling、host registration 與測試替身，重用既有抽象。
4. 先逐一新增 PRD 所列 18 個精確名稱的測試。Core 測試 net48、net10.0 皆包含；執行指定測試並保存預期失敗證據。
5. 新增核心白名單、授權教會值型別及設定服務；實作覆寫儲存／還原、模板 placeholder 完整性、長度、類別維護、伺服器唯一代碼、有限稽核查詢。每次改動先跑最小測試。
6. 依既有連線 owner、source factory 和 lease contract 接入 ChurchReport；`Program.cs`、`appsettings.json`、`appsettings.Development.json` 及適用伴隨說明同步。用既有 sender 限制或 `limits.json` 取得 longestChurchName，若採後者將固定決策寫入 `wp-reports/P8-decisions.md`。
7. 接入 LineWebhook，新增開關驗證；確認 disabled 時不註冊 source／polling，enabled 時替換空 provider。保證 polling Stop/Drain、cache Dispose、既有連線 owner Dispose 次序。
8. 重跑 18 個指定測試確認轉綠；執行 Core 兩 target、ChurchReport、LineWebhook 測試與完整 Release solution tests。不得以 NoWarn 或放寬檢查處理失敗。
9. 執行文件說明、I18n／靜態檢查與適用位元組驗證；新增／修改自行維護 C# UTF-8 無 BOM、CRLF、末尾 CRLF。更新兩個 `Program.cs` 檔首說明及 appsettings 伴隨文件。
10. 最終檢閱 diff、錯誤輸出、隔離與生命週期路徑；評估是否有值得寫入 spec 的新契約，無則記錄不需更新。
11. 依階段 8 單元規則建立以 `P8-03` 開頭的提交；在唯一 `C:\smv\guard-p8` 對提交 SHA checkout 並跑完整守門，證據存 `C:\smv\guard-p8-evidence`。
12. 撰寫 `wp-reports/P8-03a-教會覆寫接線與設定服務.md` 一頁回報，標記驗收；推送 P8 分支並用 `git ls-remote` 核對遠端 SHA；進度頁只更新階段 8 DATA。

## 完成核對

- [ ] PRD 指定 18 個測試名稱全數存在且通過。
- [ ] Core net48、net10.0 與 ChurchReport、LineWebhook、完整方案 Release 測試通過。
- [ ] 主機開關缺省、布林驗證、disabled／enabled 註冊與停止清理契約通過。
- [ ] 所有新增／修改程式檔有真實繁中整體說明；UTF-8 無 BOM、CRLF、末尾 CRLF。
- [ ] 文件檢查、完整守門、單元回報、驗收標記、push 與遠端 SHA 核對完成。
