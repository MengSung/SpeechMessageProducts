# Dynamics Gateway 成功回應端點洩露：實作前分析報告

本報告針對 `SpeechMessage.Dynamics` 專案中 `DynamicsWebApiClient.SendJsonGetAsync` 成功回應時洩露內部 CRM 路由與信任邊界資料（`approvedWebApiRoot`）的安全性缺口進行實作前分析。

---

## 1. UX Analysis (使用者影響評估)

- **安全性與信任邊界隔離**：
  `approvedWebApiRoot` 包含了 CRM 的內部 Hostname（例如 `crm.example.local`）與 Web API 版本路徑（例如 `/api/data/v8.2/`）。將此資訊暴露給產品呼叫端（Outbound），會使攻擊者能夠探知內部網路架構，增加 SSRF（伺服器端請求偽造）或針對內部 CRM 系統的定向攻擊風險。
- **使用者體驗與開發者體驗 (DX)**：
  產品開發者不需要、也不應該依賴 `approvedWebApiRoot` 來進行任何邏輯處理。將其移除可以避免開發者誤用該欄位，從而確保系統的低耦合性。

---

## 2. Design Evaluation (設計評估與一致性)

- **符合架構合約**：
  根據 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 的「Product boundary」規範：
  > *Products know ExecutionMode, ProfileAlias, Gateway endpoint, API prefix, and typed operation parameters only.*
  > *Product JSON must not contain a CRM organization-service URL, raw CRM Web API URL...*
  
  目前將 `approvedWebApiRoot` 傳回給產品端，直接違反了上述合約。因此，將其從成功 payload 中移除是符合既定架構設計的。

---

## 3. Technical Considerations (技術與架構影響)

- **ProductClient 端相容性**：
  經搜尋整個 repository，除了 `DynamicsWebApiClient.cs` 和 `CapacityKeys.cs` 之外，沒有其他 C# 程式碼使用 `approvedWebApiRoot`。這意味著沒有其他地方在反序列化時顯式讀取此欄位。因此，移除此欄位對現有程式碼的影響極低，相容性風險非常小。
- **序列化風險**：
  `OperationExecutionResult.Success` 接收一個 `object? data`。在 JSON 序列化時，它只會少輸出一個 `approvedWebApiRoot` 欄位。這不會導致反序列化失敗或拋出異常，因為 JSON 反序列化器預設會忽略目標 DTO 中不存在的欄位。

---

## 4. Options (替代方案與權衡)

### 方案 A：直接移除 `approvedWebApiRoot`（推薦）
- **做法**：直接在 `DynamicsWebApiClient.SendJsonGetAsync` 中，將 `approvedWebApiRoot` 從 `OperationExecutionResult.Success` 的匿名物件中移除。
- **優點**：最乾淨、最符合安全合約，完全消除端點洩露風險。
- **缺點**：無明顯缺點。

### 方案 B：對 `approvedWebApiRoot` 進行遮罩（Masking）
- **做法**：保留 `approvedWebApiRoot` 欄位，但將其值進行遮罩（例如返回 `https://[REDACTED]/`）。
- **優點**：若有潛在的未記錄依賴，可避免欄位缺失導致的錯誤。
- **缺點**：仍然殘留了不必要的欄位，且增加了遮罩邏輯的維護成本，不符合最小修正原則。

---

## 5. Recommendation (建議方案與實作步驟)

我們強烈建議採用 **方案 A**。以下是具體的實作與測試步驟：

### 步驟 1：新增直接 Client RED test
在 `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs` 中新增以下測試方法。在尚未修改 `DynamicsWebApiClient.cs` 前，此測試會因為 `approvedWebApiRoot` 屬性存在而失敗（RED）；修改後則會通過（GREEN）。

```csharp
    /// <summary>
    /// 驗證當 Dynamics Web API 呼叫成功時，回傳的 OperationExecutionResult.Data 成功 payload
    /// 只保留產品契約所需欄位（如 operationId、ceVersion 與 data），
    /// 且絕不洩露內部 CRM 路由資訊（如 approvedWebApiRoot、CRM 主機名稱或 /api/data/ 路徑），
    /// 以確保 Gateway 內部路由與信任邊界資料不會外洩至產品呼叫端。
    /// </summary>
    [Fact]
    public async Task WhoAmI_success_payload_does_not_leak_approved_web_api_root()
    {
        // Arrange
        var client = CreateClient(request => JsonResponse("""{"BusinessUnitId":"22222222-2222-2222-2222-222222222222"}"""));

        // Act
        var result = await client.WhoAmIAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data.Should().NotBeNull();

        var json = JsonSerializer.Serialize(result.Data);
        using var doc = JsonDocument.Parse(json);
        
        // 驗證不包含敏感的內部路由欄位與 CRM 資訊
        doc.RootElement.TryGetProperty("approvedWebApiRoot", out _).Should().BeFalse(
            "approvedWebApiRoot 屬於內部路由與信任邊界資料，不得洩露給產品呼叫端。");
        
        var jsonString = doc.RootElement.GetRawText();
        jsonString.Should().NotContain("crm.example.local", "CRM 主機名稱不得洩露給產品呼叫端。");
        jsonString.Should().NotContain("/api/data/", "CRM Web API 路徑結構不得洩露給產品呼叫端。");

        // 驗證包含產品契約所需的必要欄位
        doc.RootElement.TryGetProperty("operationId", out var opIdProp).Should().BeTrue();
        opIdProp.GetString().Should().Be(OperationIds.RuntimeHealthWhoAmI);

        doc.RootElement.TryGetProperty("ceVersion", out var ceVersionProp).Should().BeTrue();
        ceVersionProp.GetString().Should().Be("8.2");

        doc.RootElement.TryGetProperty("data", out var dataProp).Should().BeTrue();
        dataProp.GetProperty("BusinessUnitId").GetString().Should().Be("22222222-2222-2222-2222-222222222222");
    }
```

### 步驟 2：實施最小 Production 變更
修改 `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs` 的第 369-375 行：

**修改前**：
```csharp
                return OperationExecutionResult.Success(new
                {
                    operationId,
                    ceVersion = approvedRoot.CeVersion,
                    approvedWebApiRoot = approvedRoot.Value.ToString(),
                    data
                });
```

**修改後**：
```csharp
                // 回傳成功結果，僅保留產品契約所需欄位，移除內部路由資訊以防端點洩露。
                return OperationExecutionResult.Success(new
                {
                    operationId,
                    ceVersion = approvedRoot.CeVersion,
                    data
                });
```

---

## 6. Findings Classification (發現分類)

### Critical
- **內部網路拓撲與信任邊界洩露 (Dynamics Web API Endpoint Disclosure)**
  - **檔案路徑**：`SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs` (第 373 行)
  - **說明**：在 `SendJsonGetAsync` 方法成功執行後，回傳的 `OperationExecutionResult.Data` 匿名物件中包含了 `approvedWebApiRoot = approvedRoot.Value.ToString()`。這會導致 CRM 的內部 Hostname 與 Web API 版本路徑被序列化並洩露給產品呼叫端。這違反了信任邊界原則，因為產品呼叫端不需要知道 Gateway 內部的 CRM 路由資訊。

### Warning
- **無**

### Info
- **測試覆蓋率與回歸防護**
  - **檔案路徑**：`SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`
  - **說明**：現有的單元測試並未驗證 `OperationExecutionResult.Data` 的內容結構，因此無法在編譯或測試階段攔截此洩露。建議新增專屬的單元測試以確保未來不會再次引入類似的洩露。
