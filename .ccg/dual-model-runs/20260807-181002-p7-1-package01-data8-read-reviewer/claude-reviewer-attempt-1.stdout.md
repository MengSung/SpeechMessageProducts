已完成對 P7.1 範圍內檔案的審查（`Package01Data8ReadOperations.cs`、`LivePackage01Data8ReadEvidenceTests.cs`、`Invoke-Package01Data8ReadEvidence.ps1` / `.Tests.ps1`、`Package01OperationRegistry.cs`/`OperationIds.cs` 作為交叉比對基準）。以下為驗證後的具體發現。

## Review: P7.1 Package01 Data8 Read Slice

### Critical 🔴
無。核心邊界（六個 operation 皆為 registry allowlist、無 FetchXML/CRUD/credential 逃逸、`Package01FeeReadsEnabled` 未被觸碰、evidence 只回傳 sanitized JSON、CE 版本/profile 為固定值）皆有程式碼佐證，未發現違反。

### Warning 🟡

- **`docs/scripts/Invoke-Package01Data8ReadEvidence.ps1:342-373`、`:455-463`** — early-exit 路徑會清掉呼叫端既有的環境變數，而非「保留原值」。
  - 現況：`$previousEnvironment` 的擷取迴圈在第 371-373 行才執行，但 repository 檢查（343-350 行）與 fixture 驗證（352-361 行）失敗時會在此之前就 `throw 'result-written'` 離開。這兩個 early-exit 分支下 `$previousEnvironment` 永遠是空 hashtable。
  - 影響：`finally`（455-463 行）對 `$inputEnvironmentNames` 中 8 個變數逐一檢查 `$previousEnvironment.ContainsKey($name)`；因為 hashtable 是空的，全部走 `else` 分支，把 `CRM_PASSWORD`、`SPEECHMESSAGE_P7_1_LIVE`、六個 `P7_1_*` 變數在目前 process 中強制設為 `$null`——即使這些變數在呼叫這支 script 之前就已經由外層 process/wrapper 設定過、且這支 script 從未讀取或動用過它們。這與檔案開頭註解「既有 process environment 也會在 finally 還原」的契約矛盾：在這兩個分支下不是「還原」而是「清空」。
  - 建議：把第 371-373 行的環境變數快照迴圈移到 try 區塊最開頭（repository/fixture 驗證之前），確保任何 early-exit 路徑都先有正確快照可還原。

- **`SpeechMessage.Dynamics.Connectors.Data8/Package01Data8ReadOperations.cs:400-436`、`:451-487`** — registry 宣告的單頁 `MaximumPageBytes`（64 KiB，見 `Package01OperationRegistry.cs:36,260`）從未在此檔案中被檢查。
  - `RetrieveFeeRecords`/`RetrieveStorLessonRecords` 只用 `MaximumRowsPerPage`（列數）、`definition.MaximumResultItemCount`、`definition.MaximumCumulativeResponseBytes`（累積 4 頁共 256 KiB）做把關，`OperationDefinition.MaximumPageBytes` 這個欄位在整支 connector 中完全未被讀取（已用 grep 確認全 repo 唯一使用處是 registry 定義本身與測試檔）。
  - 影響：目前仍在整體 256 KiB / 4096 筆的保守上限內，非「無界」，但 registry 明確定義的「單頁 64 KiB」子限制形同虛設——單一頁（例如第一頁就塞滿 128 列）可一次吃掉遠超 64 KiB、逼近整個 256 KiB 累積預算，而不會提早在頁層級失敗關閉。這與 registry 檔頭註解「每個作業使用相同的四頁/64 KiB/256 KiB 保守預設」的三層防線設計意圖不一致。
  - 建議：在逐筆累積前，先以單頁位元組數與 `definition.MaximumPageBytes` 比較，超過即在該頁失敗關閉。

### Info 🟢

- **`docs/scripts/Invoke-Package01Data8ReadEvidence.Tests.ps1:51-135`** — 只有 `repository-invalid` 分支被實際執行驗證（呼叫 script 並檢查 exit code / JSON）；`fixture-input-invalid`、`credential-unavailable`、`test-timeout`、`evidence-result-unavailable` 四個分支僅以「原始碼是否包含特定字串」的靜態檢查代替行為驗證（100-130 行）。若 `Test-NonEmptyGuid`/`Test-UtcDate`/`Test-PaidPeriod` 的判斷邏輯本身有誤（例如 off-by-one 或條件寫反），目前測試套件無法偵測到。建議至少為 `fixture-input-invalid` 加一組端到端呼叫（用不合法 GUID）。
- 4096（`ConservativeMaximumResultItemCount`）與 128（`PaidPeriod`/`ContactId` 等長度上限）等魔術數字在 C#（`Package01OperationRegistry.cs`）與 PowerShell（`Invoke-Package01Data8ReadEvidence.ps1:120-126`、`Get-StrictEvidenceFromTrx` 317 行）兩處各自硬編一份，非阻塞問題，但兩處未來若其中一處調整而漏改另一處，不會有編譯期或測試期提示。

### Summary
未發現洩漏機密、CRM 原始資料、cross-profile 狀態、非預期 CE 寫入或 feature-flag 啟用等 Critical 問題；六個 read capability 的固定查詢、投影與分頁防線設計合理，evidence handoff 的「先驗證、後讀憑證、只給 bounded child、finally 清理」骨架正確。兩項 Warning 建議在合併前修正（尤其是環境變數還原的 early-exit 缺陷，因為這是一支專門處理憑證的腳本，狀態副作用應嚴格符合其自身文件承諾）；Info 項目可視團隊節奏擇期處理。

---
SESSION_ID: ceeab336-2bc3-4750-a104-0a06fe8b8980
