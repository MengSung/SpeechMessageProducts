## Analysis: Dynamics Gateway 成功回應洩露 `approvedWebApiRoot`（唯讀分析，未修改任何檔案）

### Current State（已用程式碼證實的事實鏈）

- `DynamicsWebApiClient.SendJsonGetAsync`（`SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs:369-375`）在每一次成功呼叫（WhoAmI、OData route、FetchXML）都回傳：
  ```csharp
  return OperationExecutionResult.Success(new
  {
      operationId,
      ceVersion = approvedRoot.CeVersion,
      approvedWebApiRoot = approvedRoot.Value.ToString(),   // ← 洩露點
      data
  });
  ```
  `approvedRoot.Value` 是 `ApprovedWebApiRootFactory` 驗證過的絕對 HTTPS URI，型如 `https://<crm-hostname>/<org>/api/data/v8.2/`（見 `ApprovedWebApiRootFactory.cs:70-108`）。
- `OperationExecutionResult.Data` 型別是 `object?`（`OperationExecutionResult.cs:27`），沒有任何投影／白名單機制，序列化時整個匿名物件都會被輸出。
- Gateway HTTP 層（`SpeechMessage.Dynamics.Gateway/Program.cs:267-270`）直接 `Results.Ok(result)`／`Results.BadRequest(result)`，把整個 `OperationExecutionResult`（含 `Data`）序列化回傳給呼叫端。`ControlledOperationExecutor.cs` 沒有對 `Data` 做任何過濾（grep 無 `Data`/`approvedWebApiRoot` 命中），是單純 pass-through。
- 結論：**任何已通過 principal→workload→alias→operation 授權的成功呼叫，其 HTTP 回應都會外洩 CRM hostname 與 `/api/data/v8.2|v9.1/` 內部路徑**，直接違反 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md:115`（"Product JSON must not contain a CRM organization-service URL, raw CRM Web API URL..."）與第 61 行的路由權威邊界宣告。
- `approvedRoot`（含 `.Value`）在 `DynamicsWebApiClient` 內部仍被用於：outbound URI 組裝、`ApprovedWebApiRootFactory.IsUnderApprovedRoot` allowlist 檢查、FetchXML 最終 URI 的 scheme/host/port/path 再驗證。這些用途都發生在方法回傳之前，**與回應 payload 無耦合**，移除 `Data` 裡的 `approvedWebApiRoot` 不會影響這些安全檢查。

### Options Evaluated

| Option | Pros | Cons | Effort |
|---|---|---|---|
| A. 從 `SendJsonGetAsync` 的成功匿名物件移除 `approvedWebApiRoot`，只保留 `operationId`、`ceVersion`、`data` | 修正點單一、最小、直接命中缺口所在行；不觸碰 allowlist／nextLink 邏輯 | 無實質缺點 | Low |
| B. 在 `OperationExecutionResult` 或 Gateway HTTP 層加一層通用「回應投影/allowlist」機制 | 一次性防止未來類似欄位外洩 | 超出目前確認缺口範圍，屬於架構擴張；違反最小修正原則與規格「先 RED 後最小 GREEN」要求 | Medium-High |
| C. 保留 `approvedWebApiRoot` 但做遮罩（只留 host 不留 path，或雜湊） | 兼顧除錯需求 | 規格明確禁止「CRM hostname」洩露，遮罩仍可能殘留可辨識資訊；仍是不必要的複雜度 | Medium |

### Recommendation

**同意 Option A（最小修正方向）**：直接在 `SendJsonGetAsync` 成功分支移除 `approvedWebApiRoot` 欄位，只保留 `operationId`、`ceVersion`、`data`。理由：
1. 這是缺口的唯一來源（單一 return 陳述式），符合「最小 Production 變更轉綠」要求。
2. `ApprovedWebApiRoot` 本身作為型別、`.Value` 作為 allowlist/nextLink 驗證輸入，用途完全獨立於回應序列化，移除輸出欄位不會弱化 outbound URI 邊界（§2 契約要求）。
3. `ceVersion` 是 profile 已知、非敏感的版本標籤（不含 hostname/path），予以保留符合契約允許項；若要更嚴謹，可評估 `ceVersion` 是否也算「內部路由資訊」，但規格只點名 CRM hostname 與 `/api/data/` 路徑，`ceVersion` 不在禁止清單內，建議保留。

### 建議的精確 RED Assertions

**新增於 `DynamicsWebApiClientTests.cs`（直接 Client 層，最優先）：**

1. `WhoAmI_success_result_does_not_expose_approved_web_api_root`
   - 呼叫 `client.WhoAmIAsync()`，斷言 `result.Succeeded == true`。
   - 將 `result.Data` 序列化為 JSON（`JsonSerializer.Serialize(result.Data)`），斷言：
     - 不包含 `"approvedWebApiRoot"` 屬性名稱。
     - 不包含測試設定的 base URI host（`crm.example.local`）字串。
     - 不包含 `"/api/data/v8.2/"` 或 `"/api/data/v9.1/"` 子字串。
   - 正向斷言：JSON 仍包含 `"operationId"`、`"ceVersion"`、`"data"`（避免修正時誤刪合法欄位）。

2. `Fee_dedication_success_result_does_not_expose_approved_web_api_root`
   - 同上，用 FetchXML 路徑（`FeeDedicationRetrieveByContact`）驗證，因為 `ExecuteFetchXmlAsync` 與 `ExecuteODataGetAsync` 共用同一個 `SendJsonGetAsync`，但用兩條路徑各留一個 RED test 可同時覆蓋 `odata-route`/`fetchxml` 兩種 template kind、避免只改一處程式碼卻遺漏另一條呼叫路徑的假陽性。

3.（建議，強化）`AdfsOAuth_success_result_does_not_expose_approved_web_api_root`
   - 重用既有 `Adfs_oauth_sends_bearer_token_from_secret_reference` 的 client 建置方式（CeVersion 9.1），確認 9.1 路徑同樣不外洩，因為 `.trellis` 規格對 8.2/9.1 都適用。

**若 Gateway HTTP fixture 足夠重用（`GatewayWorkloadBoundaryTests.cs` 目前用 `RecordingExecutor` test double，回傳固定 `new { value = Array.Empty<object>() }`，不會經過真正的 `DynamicsWebApiClient`）：**

- 目前這個檔案的 fixture **不足以**直接重用做 HTTP serialization regression，因為 executor 被整個替換掉，不會經過 `DynamicsWebApiClient.SendJsonGetAsync`。若要在 Gateway HTTP 層補 regression test，`RecordingExecutor.ExecuteAsync` 必須先被改成回傳「模擬修正前／修正後」的真實形狀（例如可設定回傳值），才能斷言 HTTP response body 不含 `approvedWebApiRoot`／CRM host。這超出「重用現有 fixture」的最小範圍，**建議依規格第 3 點指示：先做直接 Client RED test 使其轉綠即可，Gateway HTTP regression 為次要、可選**，因為 Client 層測試已經是產生 `Data` payload 的唯一來源，HTTP 層只是無過濾 pass-through，不需要重複驗證同一件事。

### 可能的相容性或序列化風險

1. **既有測試相容性**：檢視 `DynamicsWebApiClientTests.cs` 全部斷言後，**沒有任何現有測試依賴 `result.Data` 內含 `approvedWebApiRoot`**（既有測試只檢查 `seen.RequestUri`、`result.Succeeded`、`result.ErrorCode` 等），移除該欄位不會破壞既有綠燈測試。
2. **`GatewayWorkloadBoundaryTests.cs` 的 `RecordingExecutor`** 用自訂 `new { value = Array.Empty<object>() }`，與 `DynamicsWebApiClient` 的真實 payload 完全脫鉤，不受此修正影響。
3. **下游消費者風險（需額外確認，超出本次唯讀範圍的檔案）**：若 Gateway 之外有任何產品端程式碼曾經解析 `Data.approvedWebApiRoot`（例如用於除錯訊息、日誌關聯、或未來 nextLink 續頁邏輯的呼叫端組裝），移除此欄位會是破壞性變更。但在本次限定掃描範圍（`DynamicsWebApiClient.cs`、兩個測試檔、規格文件）內找不到任何讀取端引用 `approvedWebApiRoot` 字串鍵值；`ApprovedWebApiRoot` 型別本身的其他消費者（`ApprovedWebApiRootFactoryTests.cs`、`DynamicsProfileRuntimeFactory.cs`）操作的是強型別物件而非序列化後的 JSON 鍵，不受影響。**建議在實作前用全域 grep 確認 `SpeechMessage.Dynamics.*` 之外（若存在的產品呼叫端）沒有依賴此欄位**，因為分析範圍限定不含產品消費端程式碼。
4. **JSON 屬性名稱大小寫**：`JsonOptions`（Client 內）設 `PropertyNameCaseInsensitive = true` 是用於反序列化上游回應，與這裡的匿名物件序列化（成功結果輸出）無關，不影響斷言設計，但撰寫 RED test 時應用與 Gateway 實際使用的 `JsonSerializerOptions`（可能是 System.Text.Json 預設 camelCase）一致的序列化選項，避免屬性名稱大小寫造成字串比對誤判。
5. **`nextLink` 分頁**：規格與程式碼中都提到 `nextLink` 驗證屬於 Client/Transport 內部關注點；目前 `SendJsonGetAsync` 回傳的 `data` 是原始 upstream JSON（含 `@odata.nextLink` 等 OData 標準欄位，若上游有回傳）。這部分不在本次缺口範圍內，但要注意：**`data` 欄位本身可能已經包含 OData annotation 中的完整 CRM URL（例如 `@odata.nextLink` 常是完整絕對 URL）**，這是一個規格要求範圍外、但性質相同的潛在洩露面，建議列為 Info 級發現供後續任務追蹤，不在本次最小修正範圍內處理。

### Risks & Mitigations

1. Risk：修正範圍蔓延到通用 payload 投影框架 → Mitigation：嚴格限定只刪除該行匿名物件的 `approvedWebApiRoot` 屬性，不新增中介層。
2. Risk：RED test 斷言方式過於寬鬆（例如只檢查屬性名稱不檢查字串內容）導致無法真正證明洩露被封堵 → Mitigation：斷言需同時涵蓋屬性鍵名與序列化後字串內容（host/path）雙重檢查。
3. Risk：`data` 欄位內的 OData annotation（`@odata.nextLink` 等）仍可能夾帶完整 CRM URL，修正後被誤以為「缺口已完全消除」→ Mitigation：在發現清單中明確標註為獨立、範圍外項目。

### Action Items

1. [ ] 在 `DynamicsWebApiClient.SendJsonGetAsync` 成功回傳的匿名物件中移除 `approvedWebApiRoot` 屬性，只保留 `operationId`、`ceVersion`、`data`。
2. [ ] 依上述精確 RED assertions，先在 `DynamicsWebApiClientTests.cs` 新增測試並確認為紅燈，再套用最小修正轉綠。
3. [ ] 對新增/修改的方法補充繁體中文 XML 文件註解，說明信任邊界（`approvedRoot.Value` 只能內部使用、不得序列化外流）、為何 `ceVersion` 可保留而 `approvedWebApiRoot` 不可、以及此變更與 `.trellis` 規格第 115 行條款的對應關係。
4. [ ] 修改檔案存為 UTF-8 without BOM、CRLF、final CRLF，並執行 `git diff --check`。
5. [ ] （範圍外，建議另立任務）確認 `data` 內 OData 系統標註（`@odata.nextLink` 等）是否也需要類似的欄位級過濾。

---

## 分級發現總結

**Critical**
- `DynamicsWebApiClient.cs:369-375` — 所有成功操作回應（WhoAmI、OData route、FetchXML，含 8.2 與 9.1 profile）都會在 `Data.approvedWebApiRoot` 洩露完整 CRM hostname 與 `/api/data/v8.2|v9.1/` 內部路徑，經 Gateway `Program.cs:267-270` 的 `Results.Ok(result)` 直接透傳給任何已授權呼叫端。此為對規格明文禁止項（`.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md:61,115`）的實質違反，且目前完全沒有測試覆蓋此邊界。

**Warning**
- 目前 `DynamicsWebApiClientTests.cs`／`GatewayWorkloadBoundaryTests.cs` 均無任何斷言驗證「成功回應 payload 形狀」，這是本次缺口能存活至今未被發現的根因；修正後若無對應 RED/GREEN 測試常駐，日後重構仍可能無聲重新引入類似欄位（例如把 `ApprovedWebApiRoot` 物件整個放進 `data`）。
- `GatewayWorkloadBoundaryTests.cs` 的 `RecordingExecutor` 是完全脫鉤的 test double，不能作為「Gateway HTTP serialization regression」的可信 fixture；若要在該層補測試，需先擴充 fixture 讓其可注入真實/半真實 payload 形狀。

**Info**
- `data` 欄位轉傳的原始上游 JSON 可能自帶 OData 系統標註（如 `@odata.nextLink`）內含完整 CRM 絕對 URL，性質與本次缺口相同但不在目前確認範圍內，建議另立追蹤項目而非併入本次最小修正。
- `ceVersion` 屬於已知、非路由敏感的版本標籤，建議明確保留於契約中，避免未來被誤刪。

---
SESSION_ID: dfda8ea2-898a-451f-b738-4c55ca21459e
