# Dynamics Gateway 成功回應端點洩露分析報告

本報告針對 `SpeechMessage.Dynamics` 專案中 `DynamicsWebApiClient.SendJsonGetAsync` 成功回應時洩露內部 CRM 路由與信任邊界資料（`approvedWebApiRoot`）的安全性缺口進行實作前分析。

---

## 1. UX Analysis (使用者與安全影響評估)

- **安全與合規性影響**：
  - **內部網路拓撲洩露**：`approvedWebApiRoot` 包含了 CRM 的內部 Hostname（例如 `crm.example.local`）與 Web API 版本路徑（例如 `/api/data/v8.2/`）。將此資訊暴露給產品呼叫端（Outbound），會使攻擊者能夠探知內部網路架構，增加 SSRF（伺服器端請求偽造）或針對內部 CRM 系統的定向攻擊風險。
  - **信任邊界模糊**：產品呼叫端（如 `ChurchReport`）僅需知道 Gateway 的公開端點，而不應知曉後端 CRM 的實際位置。移除此欄位能強化 Gateway 作為單一信任邊界（Trust Boundary）的角色。
- **開發者體驗 (Developer Experience)**：
  - 產品開發者不需要、也不應該依賴 `approvedWebApiRoot` 來進行任何邏輯處理。將其移除可以避免開發者誤用該欄位，從而確保系統的低耦合性。

---

## 2. Design Evaluation (設計系統與合約一致性評估)

- **合約一致性**：
  - 根據 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 的 **Product boundary** 規範：
    > *"Products know ExecutionMode, ProfileAlias, Gateway endpoint, API prefix, and typed operation parameters only. Product JSON must not contain a CRM organization-service URL, raw CRM Web API URL..."*
  - 目前將 `approvedWebApiRoot` 傳回給產品端，直接違反了上述合約。因此，將其從成功 payload 中移除是符合既定架構設計的。
- **安全路由機制保留**：
  - 修正方案僅移除回傳 payload 中的屬性，而 `ApprovedWebApiRoot` 在 `DynamicsWebApiClient` 內部（如 `ExecuteODataGetAsync` 與 `ExecuteFetchXmlAsync`）的安全路由驗證、SSRF 防護與 `nextLink` 驗證邏輯將**完全保留且不被弱化**。

---

## 3. Technical Considerations (技術與架構考量)

- **序列化與反序列化風險**：
  - **ProductClient 端相容性**：在 `GatewayDynamicsOperationExecutor.cs` 中，`OperationExecutionResultDto` 將 `Data` 反序列化為 `JsonElement?`。移除 `approvedWebApiRoot` 屬性只會使該 `JsonElement` 少一個屬性，不會導致反序列化失敗或拋出異常。
  - **下游相容性**：需確認是否有任何現有產品代碼直接讀取了 `result.Data` 中的 `approvedWebApiRoot`。根據合約，這是不被允許的，但若有遺留代碼（Legacy Code）違反合約進行了讀取，移除該欄位可能會導致 `NullReferenceException` 或屬性缺失。
- **效能與記憶體取捨**：
  - 移除匿名物件中的一個字串屬性，可微幅減少 JSON 序列化時的記憶體分配（Memory Allocation）與 CPU 運算開銷，對高併發的 Gateway 來說是有利的。
- **編碼與註解規範**：
  - 修改的檔案必須嚴格遵守 `UTF-8 without BOM`、`CRLF` 換行格式。
  - 新增的測試與修改的程式碼必須撰寫詳盡的**繁體中文註解**，說明信任邊界、防範洩露的設計意圖。

---

## 4. Options (替代方案評估)

### 方案 A：最小修正（推薦）
直接在 `DynamicsWebApiClient.SendJsonGetAsync` 中，將 `approvedWebApiRoot` 從 `OperationExecutionResult.Success` 的匿名物件中移除。
- **優點**：變更範圍最小，完全符合安全合約，無額外效能開銷。
- **缺點**：若有未記錄的下游系統依賴此欄位，可能會造成中斷（但此依賴本身即違反合約）。

### 方案 B：遮罩/混淆（Masking）
保留 `approvedWebApiRoot` 欄位，但將其值進行遮罩（例如返回 `https://[REDACTED]/`）。
- **優點**：保留欄位結構，避免下游因屬性缺失而崩潰。
- **缺點**：傳遞無用資料，增加傳輸開銷，且未徹底解決「產品端不需要知道此欄位」的合約違背問題。

---

## 5. Recommendation (建議修正方向與精確 RED Assertions)

### 同意最小修正方向
我們**同意**採用方案 A（最小修正方向），在 `DynamicsWebApiClient.cs` 中移除該屬性，並在 `DynamicsWebApiClientTests.cs` 中新增直接的 Client RED 測試。

### 建議的精確 RED Assertions
在 `DynamicsWebApiClientTests.cs` 中新增以下測試方法。在尚未修改 `DynamicsWebApiClient.cs` 前，此測試會因為 `approvedWebApiRoot` 屬性存在而失敗（RED）；修改後則會通過（GREEN）。

```csharp
    /// <summary>
    /// 驗證成功執行操作後的回傳 Payload (OperationExecutionResult.Data) 僅包含產品契約所需的欄位，
    /// 且絕不洩露內部 CRM 路由資訊（如 approvedWebApiRoot、CRM 主機名稱或 /api/data/ 路徑），
    /// 以維護 Gateway 與產品之間的信任邊界安全。
    /// </summary>
    [Fact]
    public async Task Success_payload_does_not_disclose_approved_web_api_root()
    {
        // Arrange: 建立模擬的 Web API 用戶端，回傳標準的 JSON 回應
        var client = CreateClient(request =>
        {
            return JsonResponse("""{"value":[{"id":"1"}]}""");
        });

        // Act: 執行 WhoAmI 操作
        var result = await client.WhoAmIAsync();

        // Assert: 驗證執行結果成功且 Data 不為空
        result.Succeeded.Should().BeTrue();
        result.Data.Should().NotBeNull();

        // 將 Data 序列化為 JSON 以模擬 Gateway 的 HTTP 序列化行為
        var json = System.Text.Json.JsonSerializer.Serialize(result.Data);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        
        // 斷言：approvedWebApiRoot 絕不能出現在序列化後的 JSON 中，防止端點洩露
        doc.RootElement.TryGetProperty("approvedWebApiRoot", out _).Should().BeFalse(
            "approvedWebApiRoot is internal routing metadata and must not be disclosed to the product caller.");
        
        // 斷言：產品契約所需的欄位必須完整保留
        doc.RootElement.TryGetProperty("operationId", out var opId).Should().BeTrue();
        opId.GetString().Should().Be(OperationIds.RuntimeHealthWhoAmI);
        
        doc.RootElement.TryGetProperty("ceVersion", out var ceVer).Should().BeTrue();
        ceVer.GetString().Should().Be("8.2");
        
        doc.RootElement.TryGetProperty("data", out _).Should().BeTrue();
    }
```

---

## 6. Findings (分級發現)

### Critical
- **CRM 內部路由與信任邊界資料洩露 (Endpoint Disclosure)**
  - **檔案路徑**：`SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs` (第 369-375 行)
  - **說明**：`DynamicsWebApiClient.SendJsonGetAsync` 在成功時，回傳的 `OperationExecutionResult.Data` 匿名物件中包含了 `approvedWebApiRoot = approvedRoot.Value.ToString()`。這會導致 CRM 的內部 Hostname 與 `/api/data/v8.2|v9.1/` 路徑被序列化並洩露給產品呼叫端。這違反了信任邊界原則，因為產品呼叫端不需要知道 Gateway 內部的 CRM 路由資訊。

### Warning
- **測試覆蓋率不足 (Missing Regression Test)**
  - **檔案路徑**：`SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`
  - **說明**：現有的單元測試（如 `WhoAmI_calls_approved_root_function`）雖然驗證了 `result.Succeeded` 為 `true`，但並未斷言 `result.Data` 的 JSON 結構，導致此洩露問題未能被測試捕捉。需要新增 RED 測試來確保此欄位不會出現在回傳的 payload 中。

### Info
- **編碼與註解規範 (Encoding and Commenting Standards)**
  - **檔案路徑**：所有修改的檔案
  - **說明**：根據 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 的規範，所有新增或修改的程式碼與測試必須使用 UTF-8 without BOM、CRLF、final CRLF，且必須包含詳細的繁體中文註解，說明信任邊界、失敗行為、資源 owner、取消／釋放，以及效能／記憶體取捨。
