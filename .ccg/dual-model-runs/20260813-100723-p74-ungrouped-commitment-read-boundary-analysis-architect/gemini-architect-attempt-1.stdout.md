# P7.4 ORG-CALL-00024 本地唯讀邊界分析報告 (local-only read-boundary analysis)

本報告針對 `ORG-CALL-00024` (`memberinfo.contact.count.ungrouped.commitment`) 的本地 ChurchReport 遷移計畫進行架構與安全邊界審查。審查重點在於確保新舊路徑的隔離性、授權順序、取消機制、生命週期管理以及防禦性 Fail-Closed 設計。

---

## 關鍵發現與審查結果

### Critical (嚴重缺陷 / 阻礙性風險)

1. **子閘門 (Sub-gate) 預設值與隔離性驗證**
   - **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
   - **風險說明**：根據 PRD 規範，Package02 的基礎閘門 (`IsPackage02ContactProfileOperationsEnabled`) 與新設計的子閘門 (`Package02UngroupedCommitmentReadEnabled`) 必須**預設為 false**。此判斷必須在任何 Session 建立、Access 檢查、ProductClient 實例化、Process Host 啟動、連線池分配或外部 I/O 發生之前執行。
   - **審查建議**：在 `DonationDynamicsAccessBootstrap` 中新增子閘門判斷時，必須嚴格遵循以下結構，確保在閘門關閉時立即短路（Short-circuit）返回，不觸發任何下游資源初始化：
     ```csharp
     public static bool IsPackage02UngroupedCommitmentReadEnabled(IConfiguration configuration)
     {
         ArgumentNullException.ThrowIfNull(configuration);
         if (!IsPackage02ContactProfileOperationsEnabled(configuration))
         {
             return false;
         }
         var raw = configuration["DynamicsAccess:Package02UngroupedCommitmentReadEnabled"];
         return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
     }
     ```

2. **防禦性 Fail-Closed 與禁止 Fallback 至 Legacy 聚合查詢**
   - **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
   - **風險說明**：當子閘門啟用 (`gate=true`) 且調用 `IPackage02ContactProfileClient.CountUngroupedCommitmentAsync` 時，若發生任何類型錯誤（Typed Fault）、逾時或取消（Cancellation），**絕對禁止**回退（Fallback）至舊有的 `QueryExpressionToFetchXmlRequest` 或 `RetrieveMultiple` 聚合查詢。
   - **審查建議**：在 `CountUngroupedCommitmentValues` 的新路徑中，必須使用 `try-catch` 包覆 ProductClient 調用。一旦捕獲異常，必須直接拋出異常或返回空結果以觸發 Fail-Closed，絕不能在 Catch 區塊中調用舊版 FetchXML 查詢，以避免產生非預期的混合流量與安全漏洞。

---

### Warning (警告事項 / 潛在隱患)

1. **HttpContext.RequestAborted 取消權杖傳遞**
   - **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
   - **風險說明**：控制器在調用 `CountUngroupedCommitmentAsync` 時，必須顯式傳遞當前請求的 `HttpContext.RequestAborted` 作為 `CancellationToken`。若未傳遞或誤用 `CancellationToken.None`，將導致客戶端斷開連線時後端無法及時釋放 Dynamics 連線與進程資源，造成連線池耗盡。
   - **審查建議**：確保調用點明確寫入 `cancellationToken: HttpContext.RequestAborted`。

2. **請求本地 DTO 驗證服務的防禦性檢查**
   - **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
   - **風險說明**：從 `IPackage02ContactProfileClient` 返回的 `UngroupedCommitmentCountResult` 包含 `Counts` 列表。該數據必須在請求本地（Request-local）進行嚴格驗證。
   - **審查建議**：驗證邏輯必須排除：
     - 重複的 OptionSet `Value`。
     - 負數的 `Count` 值。
     - `Null` 元素。
     若偵測到上述異常，應視為無效 DTO 並立即 Fail-Closed，不得將部分損壞的數據（Partial results）寫入 View Model 或 Response 中。

3. **固定 ProfileAlias 與 WorkloadSubjectId 的綁定**
   - **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
   - **風險說明**：啟用路徑必須使用固定的部署配置，不得接受來自瀏覽器或外部請求傳入的 Profile、Connector、Owner、FetchXML、CRM Entity 或憑證。
   - **審查建議**：構造 `UngroupedCommitmentCountRequest` 時，`ProfileAlias` 必須直接讀取自 `ProductDynamicsOptions.ProfileAlias`，而 `WorkloadSubjectId` 必須硬編碼為固定的系統識別碼（例如 `"church-report-memberinfo"`），以確保多租戶與權限邊界的隔離。

---

### Info (架構資訊 / 最佳實踐)

1. **共存路徑的明確劃分**
   - **說明**：本項工作僅遷移「非空聚合計數（non-empty aggregate count）」。其餘如「空值計數（empty count）」、元數據排序（metadata ordering）、分頁獲取（page retrieve）以及聯絡人授權（contact authorization）仍維持舊有路徑。此共存狀態為預期設計，不應被視為架構缺陷，但需在程式碼註釋中明確標註，避免後續維護人員誤將其合併或誤判為遷移未完成。
2. **編碼與換行符規範**
   - **說明**：所有修改的 C# 原始碼檔案必須嚴格遵守 `UTF-8 without BOM` 編碼格式，並使用 `CRLF` 作為換行符，以通過 `git diff --check` 與自動化 Trellis 檢驗。
