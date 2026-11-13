# P4-06 實作與驗收順序

依 04A §5 固定範圍執行；所有外部寫入只限 `goodshepherdback` 測試組織，金流呼叫只限永豐沙箱。

1. 依 trellis-before-dev 讀取 backend、development configuration safety、deployment、schema deployment、duplicate-row 與 async lifecycle 規範；核對 P4-00 `preflight.md` 的未驗證項。
2. `python ./.trellis/scripts/task.py start .trellis/tasks/11-13-11-13-p4-06-sinopac-sandbox-live`，並更新進度頁 DATA 記錄 P4-06 開始。
3. 唯讀確認 User Secrets 中的必要鍵只回報鍵名與存在狀態；核對 `Environment=Sandbox`、CRM 主機包含 `goodshepherdback`、測試教會 GUID 與 `new_timezone`。對 P4-00 曾以退出碼 1 結束的 `verify` 先排查原因，再依既定重試界線唯讀執行。
4. 新增測試專案 `UserSecretsId`（新 GUID）及 `PaymentsLive.runsettings`；一般測試必須排除 `Category=PaymentsLive`。
5. 新增 `PaymentsLiveAcceptanceTests.cs`，只包含三個計畫名稱：`Live_AllotQuery_SandboxRoundTrip`、`Live_AllotWriteBack_SyntheticBatch_MatchesLegacy`、`Live_SameFeeEnqueuedTwice_ExecutesOnce`。fixture 僅以本次建立的正式 ID 管理 Contact、Fee、Task，並以 `finally` 完成刪除與 profile 還原。
6. 以同程序的標準輸入把核准的 Development／Sandbox 設定移入 LineWebhook User Secrets；確認測試只讀 LineWebhook secrets，不修改或輸出 ChurchReport 設定值。
7. 保存 apply 前 metadata before 快照；執行部署專案 `plan --org goodshepherdback`。只有 `account.new_sinopacprofile` 建立與納入方案時才 apply；其他任何差異均停止 apply。之後執行 `verify` 與 `export-metadata`。
8. 執行 `dotnet test SpeechMessageProducts.LineWebhook.Tests -c Release --filter "Category=PaymentsLive"`；再執行一般 `dotnet test`，確認三個 live 案例均未被執行。
9. 唯讀確認三類本次測試資料殘留為 0、教會 A 原 profile 已還原、欄位仍存在。若清理失敗，按既有資料 ID 續清，不廣域刪除。
10. 寫入計畫指定的 `驗收/P4_金流驗收步驟.md`；在子任務本機 evidence 建立 90 附錄 I 一頁操作紀錄，標明第二案例使用合成永豐回應。TRX、log、plan、快照與紀錄留本機，只把檔名與 SHA-256 寫入一頁 `wp-reports/P4-06.md`。
11. 完成程式提交後，在固定 `C:\smv\guard` 執行 00 §8.1 全部檢查；確認文件位元組、指定 live tests、一般測試排除狀態、D365 `verify=0` 及殘留 0。
12. 更新進度頁 DATA，封存 Trellis 子任務；紀錄型提交跑 00 §8.3 三項檢查。全包驗收後才推送 P4 分支並以 `git ls-remote` 核對 SHA；本階段後停止，不啟動階段 5。

## 禁止事項

- 不做真實付款，不碰正式組織／商店／LINE Channel。
- 不重新部署 `LineWebhookP309`，不在本機啟動完整 LineWebhook。
- 不修改 ChurchReport `appsettings*.json` 內既有金鑰，不把秘密值放進命令列參數、repo、log、回報或進度頁。
- 不新增專案、不擴充 `tools/LegacyGolden`，不修改 3.5 計畫正文。

