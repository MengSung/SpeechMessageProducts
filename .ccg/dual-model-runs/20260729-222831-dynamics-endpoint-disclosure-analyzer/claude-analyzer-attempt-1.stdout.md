# Dynamics Gateway 成功回應端點洩露：實作前分析報告

## 範圍與方法
唯讀檢視以下四份檔案，未修改任何檔案，未讀取設定檔、密碼、Token、Credential 或實際 Dynamics 位址：
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`
- `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`
- `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`

另外為確認影響面，唯讀查閱了兩份契約型別（非修改範圍，僅用於驗證相容性，未含任何實際位址／秘密）：`SpeechMessage.Dynamics.Abstractions/Operations/OperationExecutionResult.cs`、`SpeechMessage.Dynamics.ProductClient/Gateway/GatewayDynamicsOperationExecutor.cs`。

## 一、是否同意最小修正方向

**同意**，且缺口成立、優先度為 Critical。

證據鏈：
1. `DynamicsWebApiClient.cs:369-375` 的 `SendJsonGetAsync` 在成功路徑上組出
   ```csharp
   return OperationExecutionResult.Success(new
   {
       operationId,
       ceVersion = approvedRoot.CeVersion,
       approvedWebApiRoot = approvedRoot.Value.ToString(),
       data
   });
   ```
   `approvedWebApiRoot` 直接把已驗證的 CRM Web API Root（hostname + `/api/data/v8.2|v9.1/` 路徑）放進回應。
2. `OperationExecutionResult.Data` 型別是 `object?`（`OperationExecutionResult.cs:27`），沒有欄位白名單，序列化行為完全取決於呼叫端塞了什麼進去 —— 也就是說 Gateway HTTP 層本身沒有第二道過濾，Client 層是唯一且正確的修正點。
3. 規格文件明確禁止：`.trellis/spec/.../dynamics-gateway-hosting-version-routing.md:115` 「Product JSON must not contain a CRM organization-service URL, raw CRM Web API URL...」，以及 Validation Matrix（第 195 列）「Product JSON contains ... raw CRM URL ... → Configuration is rejected and secret scanning fails the build/release gate.」現況直接違反此契約。
4. 全 repo 搜尋確認 `approvedWebApiRoot` 這個 **JSON 欄位名稱** 沒有其他 C# 消費者（`CapacityKeys.cs` 裡的同名識別字是另一個方法參數，屬 `Uri` 型別的內部路由驗證邏輯，與這個 JSON 欄位無關，不受影響）。移除它是安全的最小變更，不需要連動修改其他型別。

**唯一需要精確化之處**：任務描述的「只保留 `operationId`、`ceVersion` 與上游 `data`」應理解為「移除 `approvedWebApiRoot`，其餘既有欄位不變」，而不是重新設計整個成功 payload 形狀 —— `data` 本身即上游 CRM JSON（`JsonElement?`），其內部欄位屬於既有業務資料契約，不在本次修正範圍，避免過度修正（over-fix）。

## 二、建議的精確 RED assertions

### A. 直接 Client RED test（`DynamicsWebApiClientTests.cs`，必要）

新增測試，斷言「成功回應序列化後不含內部路由資訊」，同時正向斷言其餘欄位仍在，避免修正過頭：

```csharp
[Fact]
public async Task WhoAmI_success_payload_does_not_expose_approved_web_api_root()
{
    var client = CreateClient(_ =>
        JsonResponse("""{"BusinessUnitId":"22222222-2222-2222-2222-222222222222"}"""));

    var result = await client.WhoAmIAsync();

    result.Succeeded.Should().BeTrue();
    var serialized = JsonSerializer.Serialize(result.Data);
    var doc = JsonDocument.Parse(serialized);

    // 負向斷言：內部路由/信任邊界資料不得外洩
    doc.RootElement.TryGetProperty("approvedWebApiRoot", out _).Should().BeFalse(
        "approvedWebApiRoot 是 Gateway 內部路由與信任邊界資料，產品呼叫端不應取得");
    serialized.Should().NotContain("crm.example.local",
        "序列化後的成功 payload 不得洩露 CRM hostname");
    serialized.Should().NotContain("/api/data/",
        "序列化後的成功 payload 不得洩露 Web API 版本路徑");

    // 正向斷言：契約需要的欄位仍存在，避免過度收斂
    doc.RootElement.TryGetProperty("operationId", out _).Should().BeTrue();
    doc.RootElement.TryGetProperty("ceVersion", out _).Should().BeTrue();
    doc.RootElement.TryGetProperty("data", out _).Should().BeTrue();
}
```

同一份檔案內，建議在既有的 `Fee_dedication_by_contact_uses_server_owned_fetchxml_and_encodes_guid`（`FetchXml` 路徑，走的是 `ExecuteFetchXmlAsync` → `SendJsonGetAsync` 同一個成功組裝點）補一行等效斷言，證明 `odata-function`／`odata-route`／`fetchxml` 三種 TemplateKind 共用的成功組裝邏輯一次到位修正，而不是只修了 WhoAmI 這條路徑：

```csharp
JsonSerializer.Serialize(result.Data).Should().NotContain("approvedWebApiRoot");
```

這兩個斷言在目前程式碼下必定 RED（`approvedWebApiRoot` 確實存在），修正後轉 GREEN。

### B. HTTP serialization regression（`GatewayWorkloadBoundaryTests.cs`，視 fixture 可重用性決定，屬選配）

**發現**：`GatewayWorkloadBoundaryTests.cs` 目前的 fixture 用 `RecordingExecutor`（`GatewayWorkloadBoundaryTests.cs:572-591`）整個取代 `IDynamicsOperationExecutor`，回傳固定的 `OperationExecutionResult.Success(new { value = Array.Empty<object>() })`。這代表：
- 這個測試檔案的請求鏈**完全不經過** `DynamicsWebApiClient`，因此**沒有**足夠 fixture 可以直接重現「Client 組裝含 `approvedWebApiRoot` 的 payload → Gateway HTTP 序列化」這條路徑。
- 若要在此檔案做 HTTP 層 regression，只能驗證「Gateway middleware/端點是否會對 `OperationExecutionResult.Data` 做任何欄位過濾或轉換」，而不是驗證 Client 本身的修正。

**建議做法**（滿足指示第 3 點「如已有足夠可重用 fixture，再補 HTTP serialization regression」的條件判斷）：
1. 讓 `RecordingExecutor` 支援可設定回傳 payload（例如建構子或 setter 注入一個含 `approvedWebApiRoot` 的匿名物件），模擬 Client 尚未修正前的洩露形狀。
2. 新增一條斷言：已授權請求（沿用 `Mapped_server_principal_is_the_only_workload_authority` 的授權設定）取得 HTTP 200 後，**回應 body 字串**不包含 `approvedWebApiRoot`、不包含 `https://` CRM 網域樣式字串。
3. 這條測試證明的是「Gateway 端點對 executor 回傳的 Data 是透明轉發，沒有二次過濾」——因此 Client 層修正即足以堵住整條路徑；不必也不應該在 Gateway 層另做欄位白名單（會產生雙重維護點）。

若時間有限，A 已足以構成完整 RED→GREEN 週期並涵蓋 Critical 缺口；B 屬於強化 regression coverage，非必要阻擋項。

## 三、可能的相容性或序列化風險

| 風險 | 評估 | 結論 |
| --- | --- | --- |
| 下游 C# 讀取 `approvedWebApiRoot` JSON 欄位 | 全 repo 搜尋僅 `DynamicsWebApiClient.cs:373`（產生端）；`CapacityKeys.cs` 的同名字串是**方法參數識別字**、屬 `Uri` 型別直接傳入，不是讀取 JSON 欄位，語意上與此次修正無關 | 無風險 |
| `ProductClient` 反序列化失敗 | `GatewayDynamicsOperationExecutor.cs:289` 把 `Data` 存成 `JsonElement?`，`:295` 直接整包取用，不會對特定屬性做 `GetProperty("approvedWebApiRoot")` 之類的必要性檢查 | 少一個屬性不會拋例外，無風險 |
| 既有測試斷言 JSON 屬性順序或做黃金字串比對 | 搜尋 `SpeechMessage.Dynamics.Tests` 未發現對 `result.Data` 做完整字串/順序比對的測試（僅 `ControlledOperationExecutorTests.cs:73` 的 `result.Data.Should().NotBeNull()`，非結構比對） | 無風險 |
| 既有測試斷言外送 HTTP 請求 URI 含 CRM 位址 | `DynamicsWebApiClientTests.cs:39/148` 斷言的是**要求送往 CRM 的 URI**（`seen.RequestUri`），也就是 `DynamicsHttpTransport` 送出的 outbound request，與**回應** `result.Data` 完全是兩件事，不受本次修正影響 | 無風險，勿混淆 |
| `ApprovedWebApiRoot` 內部安全用途被連帶弱化 | `approvedRoot` 仍持續用於：`ExecuteODataGetAsync` 的 `IsUnderApprovedRoot` 檢查（:164）、`ExecuteFetchXmlAsync` 的 scheme/host/port/path 逐項比對（:200-208）、`SendJsonGetAsync` 內部組 URI（`target`）。本次修正**只移除輸出到 Data 的字串化值**，不動這些內部檢查與 `ApprovedWebApiRootFactory` 建立/驗證邏輯 | 只要修正範圍精準限定在 `SendJsonGetAsync` 匿名物件建構那一行，即可保證 outbound URI allowlist 不受影響；驗證方式是確認 `ApprovedWebApiRootFactory`、`IsUnderApprovedRoot` 呼叫點行數維持不變 |
| Gateway HTTP 層是否有額外序列化/快取邏輯把舊 payload 形狀當作契約鎖定 | 此份分析未讀取 Gateway 端點 Controller/Minimal API 程式碼（不在授權範圍內），僅由型別簽章（`object? Data`）與規格文件推論其為透明轉發 | 建議在實作階段（非本分析階段）用一次性 `grep` 確認 Gateway 端點沒有對 `Data` 做 shape-locking 的序列化設定（例如 `JsonSerializerOptions` 的 contract resolver 白名單），若有則需同步調整測試 fixture B |

## 四、分級發現

**Critical**
- `DynamicsWebApiClient.cs:373`：成功回應把 `approvedWebApiRoot = approvedRoot.Value.ToString()` 放入回傳給產品呼叫端的 `OperationExecutionResult.Data`，直接違反 `.trellis/spec/.../dynamics-gateway-hosting-version-routing.md:115`「Product JSON must not contain ... raw CRM Web API URL」與 Validation Matrix 第 195 列的契約要求，並外洩 CRM hostname 與 `/api/data/v8.2|v9.1/` 版本路徑，構成內部拓撲/信任邊界資訊洩露，可能被用於後續 SSRF 或針對性攻擊之偵察階段。

**Warning**
- `DynamicsWebApiClientTests.cs` 與 `GatewayWorkloadBoundaryTests.cs` 目前均**沒有任何測試**斷言成功回應的 `Data`／HTTP body 不含 `approvedWebApiRoot` 或 CRM 網域字串 —— 這代表這個 Critical 缺口在既有測試套件下完全是「靜默通過」，本身即是一個測試涵蓋率缺口，應與生產碼修正同批補上（見上方 RED assertions）。
- `GatewayWorkloadBoundaryTests.cs` 的 `RecordingExecutor` 完全繞過 `DynamicsWebApiClient`，導致該檔案目前沒有可重用 fixture 能做「Client 到 HTTP 序列化」的端對端 regression；若未來要驗證 Gateway 層本身沒有引入新的洩露欄位，需要擴充 fixture（見上方 B 項建議），否則這類迴歸只能靠 Client 層單元測試守住。

**Info**
- `ApprovedWebApiRoot` 目前唯一合法用途集中在 `DynamicsWebApiClient` 內部的 URI 組裝與逐項邊界檢查（scheme/host/port/path），修正時只需刪除 `SendJsonGetAsync` 成功回應匿名物件裡的那一行，不需要也不應該觸碰 `ApprovedWebApiRootFactory.TryCreate`、`IsUnderApprovedRoot` 或 `ExecuteFetchXmlAsync` 的邊界比對邏輯，維持「outbound URI allowlist 不被修正過程弱化」的既有保證。
- 規格文件（`dynamics-gateway-hosting-version-routing.md:181-187`）要求新增/實質修改型別與方法均需完整繁體中文 XML 文件及非顯而易見分支的行內註解，且需 UTF-8 without BOM、CRLF；`SendJsonGetAsync` 屬於既有信任邊界關鍵路徑（authorization/timeout/取消/釋放皆已有完整中文說明），修正時應延續現有註解風格，在移除該欄位處補一行說明「為何不可回傳」的意圖性註解（例如引用契約邊界），而非只做語法翻譯式註解。

---
SESSION_ID: 47fa169a-3378-4eda-a068-2f2e410f97c5
