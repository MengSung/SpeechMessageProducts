# P7.4 MemberInfo 小組樹授權來源稽核：架構分析報告

根據對 `1.0.0.3.Gateway&Embedded.Worktree` 儲存庫中 `MemberInfoController.cs` 與 `BaseChurchController.cs` 的原始碼稽核，針對 `ORG-CALL-00031` (`memberinfo.smallgroup.retrieve.descriptors`) 與 `ORG-CALL-00032` (`memberinfo.smallgroup.retrieve.memberships`) 進入新 Gateway 本地實作的安全架構分析如下：

---

## 1. 判定結論：Source-Only Local Design NO-GO

目前來源設計**無法**證明授權範圍在 Session/cache/client/CRM I/O 前為 server-derived、immutable 且 request-local。因此，本案判定為 **source-only local design no-go**。在滿足下方所述之最小恢復條件前，不得將此設計遷移至 Gateway 本地實作。

---

## 2. 具體稽核發現與安全阻礙 (Blockers)

### 【Critical】Shepherd 憑證與共享狀態依賴風險
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\MemberInfoController.cs` (第 2790-2806 行)
* **原理解釋**：
  `EnsureShepherdListsLoaded()` 在牧養名單未載入時，會直接讀取並寫入共享的 `InMemoryContext.ListManager`，並使用其中保存的明文帳密（`listManager.m_Account` 與 `listManager.m_Password`，通常為 LINE ID 或登入密碼）呼叫 `SetupListManager()`。
  這屬於嚴重的 **cross-user/credential 洩漏風險**。`ListManager` 屬於 mutable 且非 request-local 的共享狀態，在 Gateway 的無狀態、高並發環境下，會導致嚴重的會話串連（Session Bleeding）與越權存取。

### 【Critical】授權來源非 Request-Local 且依賴 Session
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\MemberInfoController.cs` (第 1629-1656 行 `GetAccess()`)
* **原理解釋**：
  `GetAccess()` 優先從 Session `_MemberInfoAccess` 讀取授權層級，若發生 cache miss，則從 `InMemoryContext.PersonalInfomationModel.m_LoginContact` 讀取登入聯絡人，再透過 `ToolUtility` 查詢 CRM 欄位 `new_church_jobtitle`。
  此設計將 Session 狀態與 `InMemoryContext` 視為授權的權威來源（Authority），違反了 Gateway 授權必須在 request-local 且 immutable 邊界內完成的安全性合約。

### 【Warning】快取污染與生命週期管理問題
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\MemberInfoController.cs` (第 255-262 行、第 293-305 行)
* **原理解釋**：
  在 `LoadDistrictTree` 中，當存取層級為 `Church` 時，系統會使用 `IMemoryCache` 快取整棵樹（`ChurchTreeCacheKey`）與當前成員 ID 集合（`ChurchGroupedCurrentIdsCacheKey`）。
  由於此快取鍵未區分用戶或租戶，且依賴於 legacy `IOrganizationService` 與 `Entity` 的生命週期，在多用戶並發存取時，極易造成跨用戶的快取污染與資料外洩。

---

## 3. Church 與 Shepherd Capability 安全共用判定

* **判定結果**：**不可安全共用，必須拆分為獨立的 Capability。**
* **原理解釋**：
  * **Church Branch**：授權邏輯相對單純，主要透過固定 SDK 查詢 app-named 小組 descriptor，不依賴特定用戶的動態名單。
  * **Shepherd Branch**：高度依賴動態的 `GetShepherdListIds()`，其名單來源完全取決於當前登入者在 `ListManager` 中載入的 `m_MultiGroupList`。
  由於兩者的授權生命週期、資料邊界與憑證依賴完全不同，若強行共用同一套 Capability，將導致 Church 路由被迫暴露於 Shepherd 的 mutable 狀態風險中。

---

## 4. 最小恢復條件 (Minimum Recovery Conditions)

若要將此設計轉為可安全遷移的狀態，必須滿足以下最小恢復條件：

1. **建立 Server-Derived Immutable 授權上下文**：
   徹底移除對 `Session`、`InMemoryContext` 與 `ListManager` 的依賴。授權決策（例如：當前用戶是 Church 還是 Shepherd、允許存取的 List ID 列表）必須在請求到達 Gateway 路由前，由無狀態的認證機制（如 JWT Claims 或 Request-Local Principal）推導完成，且該上下文在請求生命週期內必須是 **immutable** 的。

2. **Shepherd 憑證與狀態解耦**：
   重構 `EnsureShepherdListsLoaded()`。禁止在執行期使用保存的帳密呼叫 `SetupListManager()`。Shepherd 的名單查詢必須改由無狀態的、以當前經認證的 request-local token 驅動的專屬服務來完成。

3. **快取隔離與安全邊界把關**：
   若需保留快取機制，快取鍵必須包含用戶識別碼（User-Scoped Isolation），防止跨用戶快取污染。

4. **DTO 與 SDK 隔離**：
   所有小組樹的 descriptor 與 membership 查詢，必須在 Data8 執行器內部完成，並將 legacy `Entity`、`QueryExpression` 封裝，僅向外傳遞 immutable DTO。前端傳入的 `listId` 僅能作為定位器（Locator），必須在伺服器端以推導出的允許名單（Allowlist）進行嚴格的二重驗證（Double-Pass Validation）。
