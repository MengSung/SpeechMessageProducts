# P7.2 Slice C zero-active fixture-store 最終審查報告

## 1. 審查摘要 (Summary)
本審查針對 P7.2 Slice C 修正後的週報基數（weekly-report cardinality）邏輯、測試覆蓋率、資源生命週期管理、安全隔離性以及效能進行了全面的靜態程式碼分析。

經審查，本次 corrective scope 內的變更完全符合業務合約與安全限制。程式碼在處理 `zero-active` 與 `exactly-one-active` 分支時展現了極高的嚴謹性，無任何資料變更（mutation）風險，且精確區分了正常分支與異常分支（如 `duplicate-active`），並在偵測到重複或異常資料時立即 fail closed。

本審查判定結果為 **PASS**，未發現任何 Critical 或 Warning 等級的缺陷。

---

## 2. 審查問題逐項驗證 (Review Questions Verification)

1. **週報解析器有界性與 Nullable 處理**：
   * `ResolveWeeklyReport` 精確保留了目標清單（`WeeklyReportListAttribute`）、啟用狀態（`statecode=0`）與 UTC 週日日期（`DateAttribute`）的過濾條件，並設定 `TopCount=2` 與 `NoLock=true`。
   * 當查詢結果為 0 筆時，安全地回傳 `null`（代表 `zero-active` 分支）；當結果大於 1 筆或有分頁（`MoreRecords=true`）時，立即拋出 `InvalidOperationException` 進行 fail-closed，不進行任何重試或修補。

2. **`zero-active` 出席紀錄查詢與衝突偵測**：
   * `ReadPresentRecord` 在 `weeklyReportId` 為 `null` 時，刻意不加入週報 lookup 條件，但仍保留聯絡人、日期、狀態與 `TopCount=2` 的限制。
   * 這確保了若 CRM 中已存在同聯絡人、同日期但關聯到錯誤週報的出席紀錄時，該紀錄能被查詢出來，並在 `ReadTransferGraph` 中因 `presentRecord.WeeklyReportId == weeklyReportId`（`null` 與非 `null` 比對）不相等而被判定為 `PresentRecordMatches = false`，從而拒絕該 baseline，防止錯誤關聯被漏讀。

3. **讀回與清理的精確 Nullable 等價性與有序 Rollback**：
   * `RestoreTransferGraph` 在執行清理前，會重新解析週報並讀取出席紀錄，使用精確的 nullable equality 比對所有欄位（包括 `WeeklyReportId`）。
   * 清理過程使用預先證明的唯一 `presentRecordId` 進行 `Delete`，並依序還原聯絡人的 primary-list、owner，以及 source/target 的小組成員資格。整個過程不猜測資料，且任一步驟失敗皆直接拋出異常。

4. **異常狀態的 Fail-Closed 拒絕**：
   * 重複週報、分頁、格式錯誤或多筆出席紀錄等狀態，皆在執行任何 mutation 之前被攔截並拋出異常，絕不嘗試自動修復、合併或選擇週報。

5. **敏感資訊與狀態洩漏防範**：
   * 週報 ID 與出席紀錄 ID 僅在當前呼叫的 method-local 變數中傳遞，未保存在任何類別欄位、靜態變數或快取中。
   * 拋出的異常訊息均為 sanitized 靜態字串，不包含任何 CRM 實體 ID、認證、端點或原始異常細節。

6. **資源與連線生命週期管理**：
   * `OfficialCrmServiceClientFactory` 與 `OfficialCrmServiceClientAdapter` 採用嚴格的 `try-finally` 結構與 `Interlocked.Exchange`，確保 client 與認證資源在失敗或釋放時以反向順序確定釋放。
   * `WorkerEnvelopeCodec` 與 `WorkerFrameCodec` 亦在局部 scope 內管理 Stream 與緩衝區，無資源洩漏風險。

7. **測試合約嚴格性**：
   * `P72Data8ListManagementFixtureStoreTests.cs` 實作了封閉的 `TransferGraphRecordingOrganizationService` 替身，精確記錄並斷言讀取次數與寫入次數。
   * 測試涵蓋了 `zero-active` 正常分支、`exactly-one-active` 正常分支、重複週報拒絕、分頁拒絕、格式錯誤拒絕、錯誤週報關聯偵測以及 `zero-active` 清理 rollback 序列，確保 mutation 呼叫次數完全符合預期（讀取測試中 mutation 為 0，清理測試中 mutation 僅限於預期的 rollback 動作）。

8. **診斷階段標記（`probeStage`）**：
   * 在 `LivePackage02Data8ListManagementEvidenceTests.cs` 中，呼叫 `ReadTransferGraph` 前立即將 `probeStage` 設為 `"transfer-read"`。
   * 這確保了轉移圖讀取過程中的任何 fail-closed 異常都不會被誤報為前一個 `"contact-owner-read"` 階段，且該階段僅用於診斷定位，不作為重試授權。

9. **繁體中文文件與註解品質**：
   * 所有新加入與修改的 C# 程式碼區域、測試案例以及 `.trellis` 設計與思維指南，皆附有詳盡且高品質的繁體中文註解，清晰說明了所有權、隔離性、fail-closed、清理與故障注入機制。

10. **效能與運行效率**：
    * 查詢皆使用 `TopCount=2` 與 `NoLock=true`，為 O(1) 複雜度，無 unbounded scan，且無任何跨請求的共享可變狀態。

---

## 3. 審查發現分類 (Findings)

### Critical
* 無。

### Warning
* 無。

### Info
* 無。

---

## 4. 驗證評分 (Validation Score)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 正常分支與異常分支處理清晰，去識別化證據不洩漏敏感資訊，UX 語意明確。
Visual Consistency: 20/20 - 設計文件、程式碼註解與 PowerShell 驗證合約高度一致。
Accessibility: 20/20 - 嚴格的唯讀探針與 fail-closed 機制，確保系統在異常資料狀態下的安全可達性。
Performance: 20/20 - 查詢使用 TopCount=2 與 NoLock，無多餘的資料庫掃描或分頁開銷。
Browser Compatibility: 20/20 - 後端 API 與 PowerShell 腳本相容性良好，無平台相容性問題。

TOTAL SCORE: 100/100

ISSUES FOUND:
- None (無任何發現)

RECOMMENDATION: PASS
```
