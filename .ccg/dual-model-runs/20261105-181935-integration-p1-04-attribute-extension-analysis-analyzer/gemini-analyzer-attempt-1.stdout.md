# 整合分析報告：P1-04 屬性擴充與資料查詢安全特化 (Integration P1-04 Attribute & Extension Analysis)

依據任務指示 (`.trellis/tasks/11-05-integration-p1`) 與相關檔案檢查，現就 **`EntityAttributeUtilityService`**、**`IEntityAttributeUtilityService`**、**`OrganizationServiceExtensions`** 進行唯讀雙模型架構與設計分析，產出結構化分析報告如下：

---

## 一、 是否有需求或相容重大衝突 (Compatibility & Requirements Analysis)

1. **介面與公開方法合約保留**：
   - `IEntityAttributeUtilityService` 維持三大公開方法簽章：`GetAttributeValue`、`RemoveAttribute`、`SetEntityAttributeToNull`，確保現有 `ToolUtilityFacade` 與外部呼叫端相容。
2. **建構子無縫過渡（No-Logger Sandbox 支援）**：
   - 既有 `EntityAttributeUtilityService(object logger)` 保留 `logger == null` 拒絕（`ArgumentNullException`）以維持舊有依賴。
   - 新增無參數/無 logger 建構子（支援純 Core 既有模式與沙箱測試）。
3. **例外與安全性調整**：
   - 原先例外訊息夾帶動態 `ex.Message`（可能洩漏內部資料庫或元件細節），依專案「固定安全 diagnostic」規則，外層訊息改為固定的安全字串（如 `"An error occurred while processing entity attribute operation."` 或類似固定訊息），並透過 `InnerException` 保留原始 `ex` 供偵錯，符合不洩漏機密原則。
4. **ExecuteRetrieveMultiple 歷史相容性 vs. 失敗封閉 (Fail-Closed) 衝突**：
   - 說明：舊實作 `if (service == null || request == null) return new EntityCollection();` 將 `null` 參數直接當作成功並回傳空集合，容易掩蓋呼叫錯誤。
   - 生產依賴調查：檢查目前專案中是否有依賴 `null` 回傳空集合的寫法。由於本切片不開全 repo 掃描，但基於 Dataverse 整合慣例，`null` 服務或請求屬於呼叫端嚴重合約違規，應當即失敗 (Fail-Closed) 或採用 `SdkDataQuery.RequireService` 驗證。
   - 結論：透過特性綠（先鎖成功 Query/response/paging）再補安全紅燈（`null` 參數、Query fail before I/O、wrong/null response、invalid IDs、跨實體/duplicate ID 拒絕等），與既有 `SdkDataQuery.ValidatePage` 對齊，能兼顧安全與架構一致性。

---

## 二、 最小正確抽取與例外策略 (Minimal Extraction & Exception Strategy)

1. **EntityAttributeUtilityService 策略**：
   - **實體擁有權**：Entity 由呼叫端獨占擁有，不進行任何快取或擅自 Dispose，符合資料記憶體邊界。
   - **數值解析**：`GetAttributeValue` 繼續支援 `plain` 與 `AliasedValue`，`null` 或 `missing` 屬性安全回傳 `string.Empty`。
   - **就地操作 (In-place Mutation)**：`RemoveAttribute` 與 `SetEntityAttributeToNull` 採用 `ref Entity entity` 進行就地修改。
   - **例外包裝**：統一外發 `InvalidOperationException`，但外層訊息改為安全字串，並附帶 `InnerException`。
2. **ExecuteRetrieveMultiple / SdkDataQuery 策略**：
   - **共用驗證邏輯**：直接使用既有 `SdkDataQuery.RequireService` 與 `SdkDataQuery.ValidatePage`，避免重複複製相同邏輯。
   - **Paging 保持**：合法空的 `EntityCollection`（無資料但查詢成功、分頁符號有效）必須正常保留 paging 資訊，不因無資料而擲回例外。
   - **SDK 例外上拋**：SDK 執行層面的原生例外（如 `FaultException`、`TimeoutException`）不被攔截，維持最終通知與外層責任。

---

## 三、 必須測試的 fault / 隔離 / 生命週期契約 (Required Test Contracts)

1. **隔離與生命週期契約**：
   - **跨使用者隔離**：驗證 `EntityAttributeUtilityService` 在不同實體個體與執行執行緒中不共用非安全狀態，且不快取 Entity。
   - **資源確定釋放**：無 unmanaged 資源配置，但確保沒有隱式持有所謂的 Service/Logger 參考造成記憶體流失。
2. **Fault 隔離契約 (Fail-Closed Tests)**：
   - 當傳入 `null` Entity 或無效屬性名稱時，型別合約是否正確拋出預期例外（且不洩漏敏感內部路徑或堆疊細節）。
   - 當 `ExecuteRetrieveMultiple` 收到 `null` 服務、`null` 請求、或是回傳 `null` / 包含重複 ID / 跨實體 ID 的 `EntityCollection` 時，是否正確觸發 `InvalidOperationException`（Fail-Closed）。
   - 當合法查詢回傳空集合但帶有有效的 Paging Cookie 時，確保分頁資訊未被錯誤丟棄。

---

## 四、 評估發現分類 (Findings Classification)

### [Critical]
- **無（目前程式碼結構符合既有 Core 模式設計，暫無高風險阻礙）**。

### [Warning]
- **Info/Warning：例外訊息安全性**
  - **路徑**：`ToolUtility/AttributeOperations/EntityAttributeUtilityService.cs`
  - **說明**：舊的例外訊息包含 `ex.Message` 及動態實體名稱、屬性名稱。依專案永久規則「固定安全 diagnostic」，應確保外層錯誤訊息採用固定安全字串，避免在日誌或外發例外中暴露過多內部欄位結構。

### [Info]
- **架構一致性**：
  - `OrganizationServiceExtensions.ExecuteRetrieveMultiple` 若改用 `SdkDataQuery.RequireService` 及 `SdkDataQuery.ValidatePage`，需確保其方法簽章（`this IOrganizationService service, RetrieveMultipleRequest request`）與回傳型別（`EntityCollection`）保持不變，維持對舊版呼叫端的無縫支援。
  - 記得在完成修改後依規則執行 `git mv`（若有檔案位移需求）與確保繁中 XML 與 UTF-8 without BOM / CRLF 格式正確。

---

## 五、 結論與建議 (Recommendation)

- **偏好做法**：
  1. 保持 `IEntityAttributeUtilityService` 介面與三個公開方法簽章不變。
  2. `EntityAttributeUtilityService` 移除對 `logger` 的強制依賴，新增無參數建構子（支援純 Core 既有模式）；保留舊建構子相容。將例外訊息改為固定安全字串。
  3. `OrganizationServiceExtensions.ExecuteRetrieveMultiple` 委由既有 `SdkDataQuery` 的驗證規則處理（先通過成功 Query/response/paging 測試，再補齊 null 參數、錯漏回應與重複 ID 的 Fail-Closed 紅燈測試）。
  4. 遵守專案永久規則：不建 logger/LINE、不攔截最終通知、繁中 XML 與 UTF-8 without BOM/CRLF、保留未提交檔案。
