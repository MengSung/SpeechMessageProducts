以下是針對 **P7.2 continuation Slice F-H 本機契約與隔離邊界** 的審查報告：

### 審查結論摘要
經審查目前工作樹的程式碼與測試實作，本機契約與隔離邊界設計極為嚴密，完全符合 fail-closed 隔離原則。所有 Slice D-H 的能力定義均已硬編碼關閉 CE 執行與消費端點，且 Data8 執行器會在資源配置前予以攔截，未發現任何繞過 P7.4 Gateway cutover 或 P7.5 ToolUtility 移除閘門的路徑。

---

### 具體審查發現

#### Critical
* **無任何 Critical 級別問題。**
  本機隔離邊界設計完整，無安全漏洞。

#### Warning
* **無任何 Warning 級別問題。**

#### Info
1. **敏感狀態與權限隔離驗證**
   * **具體檔案**：`SpeechMessage.Dynamics.Abstractions\Operations\P72ContinuationLocalOnlyCatalog.cs`
   * **說明**：
     * 該類別的 `ContainsForbiddenInputAuthority` 方法嚴格過濾了 `"owner"`, `"endpoint"`, `"credential"`, `"entity"`, `"fetch"`, `"token"`, `"organization"`, `"profile"` 等關鍵字。若輸入名稱包含上述敏感字眼，將於型別初始化時直接拋出 `InvalidOperationException`，阻斷任何將 Session、HttpContext 或憑證帶入本機契約的路徑。
     - 所有 Slice D-H 的能力定義中，`CeExecutorEnabled` 與 `ConsumerEnabled` 均硬編碼為 `false`。

2. **輸入防禦性拷貝與長度限制**
   * **具體檔案**：`SpeechMessage.Dynamics.Abstractions\Operations\P72ContinuationLocalOnlyPlanBuilder.cs`
   * **說明**：
     * `Build` 方法在建立 Plan 時，會對輸入參數進行深拷貝（`new Dictionary<string, string?>`）並包裝為唯讀的 `ReadOnlyDictionary`，防止外部 mutable state 污染。
     * 限制單一輸入值長度上限為 `MaximumInputValueCharacters = 256`，避免過大 payload 帶入本機契約。

3. **執行器前置攔截（Fail-Closed）**
   * **具體檔案**：`SpeechMessage.Dynamics.Connectors.Data8\Data8ProfileOperationExecutor.cs`
   * **說明**：
     * `IsData8SupportedOperation` 採用明確的白名單機制，Slice D-H 的所有 Operation ID 均不在白名單內。
     * 測試 `Data8ProfileOperationExecutorTests.cs` 已驗證，當呼叫 Slice D-H 的操作時，執行器會在建立 admission、lease 或 client 之前，決定性地回傳 `operation.not-supported` 錯誤，確保不會繞過 P7.4 Gateway cutover 或 P7.5 ToolUtility 移除閘門。

4. **Slice F、G、H 契約行為合規性**
   * **具體檔案**：
     * `SpeechMessage.Dynamics.Abstractions\Operations\P72ContinuationLocalOnlyCatalog.cs`
     * `SpeechMessage.Dynamics.Abstractions\Operations\P72AttendanceWeeklyReportDecision.cs`
     * `SpeechMessage.Dynamics.Abstractions\Operations\P72AttendanceLocalPlanBuilder.cs`
   * **說明**：
     * **Slice F (ContactOnboarding)**：`NewPersonContactCreateFullOnboarding` 契約之 `CleanupPolicy` 設定為 `ReverseKnownKeys`，符合以 known keys 逆序處理的設計。
     * **Slice G (FeeLessons)**：`FeesEditorStageInmemoryChange` 契約之 `MutationPolicy` 設定為 `OperationLocalInMemoryOnly`，且 `CleanupPolicy` 為 `DiscardOperationLocalState`，確保 draft 僅在單次操作的記憶體中有效，且於 cleanup 時直接丟棄。
     * **Slice H (Attendance)**：`P72AttendanceWeeklyReportDecision` 嚴格實作了基於基數（Cardinality）的決策邏輯：
       * `ActiveReportCount == 0` $\rightarrow$ `ProceedUnlinked` (zero-active 不關聯)
       * `ActiveReportCount == 1` $\rightarrow$ `ProceedWithExactLink` (exactly-one 精確關聯)
       * `ActiveReportCount > 1` 或資料不完整 $\rightarrow$ `NoGo` (duplicate/unavailable fail-closed)。
       * `P72AttendanceLocalPlanBuilder` 亦嚴格限制 `weekStartDate` 必須為 ISO Sunday，且 `presentState` 必須在預設的 allowlist 中，否則一律回傳 `InputValueInvalid`。
