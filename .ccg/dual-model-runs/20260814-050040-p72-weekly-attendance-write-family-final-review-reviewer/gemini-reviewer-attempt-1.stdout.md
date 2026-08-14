# 審查報告：p72-weekly-attendance-write-family-final-review

本審查針對目前工作樹中 P7.2 weekly attendance write family 相關文件與本機出席契約的關係進行評估。

---

## 審查問題回答

### 1. task 文件是否正確維持證據階層，沒有把 local reducer/plan 當作 CE、consumer、traffic、P7.5 或 P8 evidence？
**是。** 
- 在 `.trellis/tasks/08-14-p72-weekly-attendance-write-family/local-no-go.md` 及 `prd.md` 中，明確將此 child 任務定位為 **local design no-go**。
- 文件中明確指出「沒有啟用 CE preflight、fixture provision、Create、Update、Assign、Delete、Associate、Disassociate、feature gate、流量或 cleanup 操作」，且歷史 Slice C 的 nonce、ledger、fixture、descriptor 均已被清理且不可重試。
- 本地決策（`P72AttendanceWeeklyReportDecision`、`P72AttendanceUpsertLocalDecision`）與計畫建立器（`P72AttendanceLocalPlanBuilder`）均被限制為 `CeDispatchAllowed=false` 與 `ProductConsumerAllowed=false`，正確維持了 local-only 的邊界，未將其作為 live CE 或 consumer 啟用的證據。

### 2. local no-go 是否由可驗證的跨使用者／跨 profile isolation 與 mutation graph 根因支持？
**是。**
- **跨使用者／跨 profile 隔離根因**：`QrCodeController.PersonalQrCodeGetLineId` 呼叫 `SetupLineContext` 時，將瀏覽器傳入的 `UserLineId`、`GroupId`、`RoomId` 等寫入 process-wide 的 `InMemoryContext`，隨後 `PersonalQrCodeUtility` 直接讀取此共享上下文。這導致缺乏 request-local 且由伺服器端衍生的驗證邊界，Data8/ProductClient 會直接信任 caller 提供的 locator，違反了 profile 隔離契約。
- **Mutation Graph 根因**：`PersonalQrCodeUtility.SetupQrCodeIdString` 執行的寫入流程（查詢/建立 present record -> 寫入時間與出席標記 -> 更新週報 `new_saved_flag` -> 呼叫 `CreateWeeklyReportAndPresentRecord` -> 發送 LINE 通知）缺乏單一寫入者帳本（single-writer ledger）、前像/後像（preimage/postimage）比對、精確圖 read-back 與確定性清理機制。雖然使用了 static lock，但 static lock 無法作為跨 host/process 的並行權威。
- 這些根因在 `local-no-go.md` 中有詳細且可驗證的分析支持。

### 3. zero-active、exactly-one-active、duplicate/unavailable weekly-report 契約是否被準確表達，沒有誤要求全組織唯一週報？
**是。**
- 在 `P72AttendanceWeeklyReportDecision.cs` 中，`ActiveReportCount` 屬於 `P72AttendanceWeeklyReportObservation`，其定義與註解明確指出這是針對**特定 list/週報**（特定 contact list 與特定 Sunday）的 active report 數量，而非全組織唯一週報。
- 決策邏輯準確表達了以下契約：
  - `0` (zero-active) -> `ProceedUnlinked` (允許建立未連結的出席記錄)
  - `1` (exactly-one-active) -> `ProceedWithExactLink` (要求精確關聯 read-back)
  - `> 1` (duplicate-active) -> `NoGo` (FailureCategory: `DuplicateActive`)
  - `null` / `!IsComplete` / `< 0` (unavailable) -> `NoGo` (FailureCategory: `Unavailable`)
- 這些契約在 `P72AttendanceWeeklyReportDecisionTests.cs` 中已通過完整測試驗證。

---

## 審查發現分類

### Critical
無 Critical

### Warning
無 Warning

### Info
1. **檔案編碼與顯示**
   - **具體檔案**：
     - `SpeechMessage.Dynamics.Abstractions/Operations/P72AttendanceWeeklyReportDecision.cs`
     - `SpeechMessage.Dynamics.Abstractions/Operations/P72AttendanceUpsertLocalDecision.cs`
     - `SpeechMessage.Dynamics.Abstractions/Operations/P72AttendanceLocalPlanBuilder.cs`
     - 以及對應的測試檔案。
   - **根據**：部分讀取工具在解碼檔案中的繁體中文註解時可能會出現亂碼顯示。經查證，檔案本身為標準的 UTF-8 without BOM 編碼，此現象僅為工具鏈顯示問題，不影響編譯與執行。
2. **清理政策宣告**
   - **具體檔案**：`SpeechMessage.Dynamics.Abstractions/Operations/P72AttendanceUpsertLocalDecision.cs` (第 125 行)
   - **根據**：`CleanupPolicy` 屬性固定回傳 `P72LocalCleanupPolicy.ReverseKnownKeys`。由於目前為 local-no-go 狀態，此政策僅作為 metadata 宣告，供未來 executor 參考，目前不涉及任何實際的 CE 清理操作。
