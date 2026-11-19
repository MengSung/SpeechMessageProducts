# P8-01a 執行計畫

## 固定限制

- 只依 08A §4.1、§5 P8-01a 實作；不做 P8-01b ChurchReport 接線。
- 不連線 D365、不讀秘密、不改使用者機器設定；所有組織服務測試均為合成替身。
- 任何未知組織中繼資料不得由預設值補成已知；在決策與回報標為「待授權上線前查證」。
- Core 測試須以 `net48` 與 `net10.0` 分別執行。

## 順序

1. 檢查 16 個指定測試名稱尚未存在，以可編譯的合約測試逐項開始紅燈；不以缺少型別造成的編譯錯誤冒充行為紅燈，保存預期失敗證據。
2. 擴充 schema parser／planner 僅允許精確白名單 `new_fee.new_offeringcategoryid`、`new_dedication_booking.new_offeringcategoryid`；其他既有表／欄仍拒絕。將 schema activePhase 設為 8，更新既有附錄A計數測試及 `.trellis/spec/backend/schema-deployment-contract.md`，新增兩 lookup 的合成中繼資料契約測試。
3. 建立 Core OfferingCategory、OfferingCategoryList、OfferingCategoryReader、OfferingCategoryText；先通過 4 個核心契約測試，驗證兩目標與集合防重複／有界行為。
4. 建立 Deployment OfferingMigration 的純模型、完整分頁來源投影、穩定主鍵摘要、plan/apply/verify 協調與 CLI 路由；套用鎖內重驗、每筆檢查批次回應、失敗後停止與安全續跑。以合成 SDK 實作指定故障注入及冪等／中斷測試。
5. 補齊全部指定測試；執行 Deployment Release 與 Core net48、net10.0 Release 測試及必要建置。
6. 更新 CLI help、Deployment spec 與 JSON 伴隨說明；按檔案文件規範補足繁中說明，檢查 C# 位元組格式。
7. 審閱差異，執行 `python tools/RepoQuality/check_file_documentation.py`、進度頁契約測試及本單元適用的編碼檢查。
8. 建立 P8-01 單元提交，於 `C:\smv\guard-p8` 完成 00 §8.1 全套守門；記錄一頁回報與驗收。
9. 首次推送 `1.0.0.11.Integration.P8.Worktree`，以 `git ls-remote origin 1.0.0.11.Integration.P8.Worktree` 比對提交 SHA；只更新現行 3.5 `實作進度.html` 的 DATA。

## 回復點

- 測試骨架階段只增加測試，不觸碰正式組織或產物。
- schema 與程式變更在單元提交前可逐檔審查；不以重設或還原整個工作樹回復。
- 不執行 apply 或任何 live 指令；因此本單元沒有組織狀態回復步驟。
- Guard 僅使用 `C:\smv\guard-p8`，不得清理、修改或移除 `C:\smv\guard`。

## 預先核定的既有測試期望更新

- `SpeechMessage.Dynamics.Deployment.Tests/SchemaDefinitionTests.cs` 的 activePhase、受管實體數與附錄A結構數量期望，依實際增加 2 個既有實體與 2 個 phase 8 lookup 更新；原有 phase 1／4／5 欄位和功能斷言維持不變（08A D8-23）。
