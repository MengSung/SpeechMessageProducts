# Dynamics Gateway Central／Local 與 D365 8.2／9.1 設計解釋說明書

> 文件日期：2026-07-29  
> 文件性質：架構理念、討論紀錄、設計決策與後續驗證說明  
> 對應正式 SPEC：`.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`

可開啟的彩色互動圖：`docs/dynamics-gateway-central-local-82-91-architecture.html`

## 1. 先講最後結論

目前建議採用以下方向：

1. 正式環境以 **Central Gateway** 為預設。
2. Visual Studio 開發與個別隔離環境先使用 **Local Gateway**。
3. Central Gateway 與 Local Gateway 使用相同的 ProductClient、REST Contract、Operation Registry 與 Adapter 介面。
4. D365 8.2 與 D365 9.1 對產品提供相同呼叫方式，但 Gateway 內部必須使用不同 Profile Runtime、驗證狀態與 Transport Adapter。
5. D365 9.1 優先使用 Microsoft 官方 Web API，或在 OAuth 適用時使用官方 `ServiceClient`。
6. D365 8.2 目前先保留 Data8 `OnPremiseClient`，因為現場可工作的 IFD 路徑仍是 WS-Trust／SOAP。
7. Data8 只能作為暫時 Legacy 相容橋接，不能成為 Central／Local Gateway 的永久核心。
8. D365 8.2 的正式替代目標是：
   - ADFS OAuth 打通後使用 Web API v8.2；或
   - 使用獨立 .NET Framework 4.8 Worker 搭配 Microsoft 官方 `CrmServiceClient`。
9. Embedded 不立即刪除，也不繼續擴大；先保留，等 Local Gateway 與 8.2／9.1 驗證完成後再決定。
10. `PowerPlatform.Dataverse.Client` 專案現在不能刪，未來符合移除 Gate 後可以刪除。

最重要的核心觀念是：

> 我們要追求的是「所有產品共用相同的 Dynamics 呼叫契約」，而不是強迫 D365 8.2 與 9.1 共用同一個 SDK、同一條連線或同一個 Session。

## 2. 完整設計總覽

```mermaid
flowchart TB
    subgraph Products["產品 A～10（.NET 10）"]
        A["產品 A"]
        B["產品 B"]
        C["產品 C"]
        X["產品 4～10"]
    end

    A --> PC["共用 Dynamics ProductClient／相同 REST Contract"]
    B --> PC
    C --> PC
    X --> PC

    PC --> M{"Gateway Endpoint 指向哪裡？"}

    M -->|"正式環境：中央 Endpoint"| CG["Central Gateway"]
    M -->|"VS／隔離環境：localhost"| LG["Local Gateway\n產品專屬獨立 Process"]

    CG --> CPR["中央 Profile Runtime Manager"]
    LG --> LPR["Process-local Profile Runtime Manager"]

    CPR --> AC["Organization Admission Coordinator\n同一實體 CRM 的總併發上限"]
    LPR --> AC

    AC --> R{"Configured Profile Router"}

    R -->|"crm91"| P91["D365 9.1 Adapter"]
    P91 --> W91["Direct Web API v9.1\n或 Microsoft ServiceClient"]
    W91 --> CRM91["D365 CE 9.1"]

    R -->|"crm82"| P82["D365 8.2 Adapter"]
    P82 --> W82A["目標 A：Web API v8.2\nADFS OAuth 驗證通過後"]
    P82 --> W82B["目標 B：官方 net48 Worker\nMicrosoft CrmServiceClient"]
    P82 --> W82T["暫時：Data8 Legacy Worker\nWS-Trust／SOAP"]

    W82A --> CRM82["D365 CE 8.2 IFD"]
    W82B --> CRM82
    W82T --> CRM82
```

## 3. 我們的討論是怎麼演進到現在這個方案

### 3.1 最初的方向：完全不能使用舊 CRM SDK

最初的限制是「不得參考舊版 CRM SDK 套件、組件或命名空間」。因此第一版目標偏向完全使用 Dynamics Web API、`HttpClient` 與 OData，並逐步移除：

- `Microsoft.Xrm.*`
- `Microsoft.CrmSdk.*`
- `Microsoft.Crm.Sdk.*`
- `IOrganizationService`
- WCF／SOAP／WS-Trust
- Repository 內從 GitHub 取得的 Data8 `PowerPlatform.Dataverse.Client`

這個方向的優點是 Framework 相依較少、HTTP 行為透明、比較容易支援 .NET 10，也比較不會把舊 SDK DLL 分散到每一個產品。

### 3.2 對「新的一定比舊的好」產生疑問

接著討論到：舊 CRM SDK 的語法已經很熟悉，新 Web API 需要重新學習，而且把既有 CRUD、QueryExpression、FetchXML、OrganizationRequest 全部改寫，確實有學習與遷移成本。

因此設計不再用「新的一定比較好」作為理由，而改成看以下實際條件：

- 是否為 Microsoft 官方支援管道。
- 是否能在 .NET 10 產品環境可靠運作。
- 是否能集中管理多產品連線、驗證、Pool 與錯誤處理。
- 是否能避免每一個產品各自保存密碼與 SDK 相依。
- 是否能同時支援實際存在的 D365 8.2 與 9.1。

### 3.3 對「網路下載的 PowerPlatform.Dataverse.Client」的疑慮

Repository 內的：

```text
PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj
```

並不是 Microsoft 官方 `Microsoft.PowerPlatform.Dataverse.Client` 的原始碼，而是 Data8 的第三方 WS-Trust 相容實作。它：

- 以 .NET 10 建置。
- 提供 `OnPremiseClient`。
- 實作 `IOrganizationService`。
- 內部使用 WCF、SOAP 與 WS-Trust。
- 參考 Microsoft 官方 Dataverse Client 套件。
- README 明確表示不受 Microsoft 或 Data8 正式支援，只提供 best-effort 支援。

問題不在於「從網路下載就一定不好」。Microsoft 官方 NuGet 套件也是透過網路下載。真正需要考慮的是：

- 維護者是誰。
- 有沒有正式支援承諾。
- 資安更新與 Framework 升級由誰負責。
- 發生 WCF、ADFS、Socket 或 Authentication 問題時由誰排除。
- 是否存在可被 Microsoft 官方元件取代的路徑。

所以 Data8 可以暫時使用，但不適合成為十個產品共同依賴的永久核心。

### 3.4 設計目標改成「官方優先」，不再要求「完全不能用 SDK」

後續決定不再把「有沒有 SDK」當成唯一標準，而改成：

1. 優先使用 Microsoft 官方介面或官方 NuGet 套件。
2. 產品不直接依賴 SDK。
3. SDK 即使需要存在，也只能藏在 Gateway 或隔離 Worker 後面。
4. 多產品共用統一的 ProductClient 與 REST Contract。
5. 未來替換內部 Transport 時，不需要修改 ChurchReport 或其他產品的業務程式。

這是目前設計最重要的轉折。

### 3.5 Central Gateway 成為終極目標

由於現有產品可能從四、五個增加到十個以上，最終希望做到：

- 統一 Profile 設定。
- 統一 Secret Reference。
- 統一 Authentication。
- 統一 Connection Runtime／Pool。
- 統一 Retry、Timeout、Circuit Breaker、Health、Audit 與 Telemetry。
- 統一控制對同一個 D365 Organization 的總併發量。

因此 Central Gateway 適合正式環境。產品不直接連 CRM，而是呼叫中央 Gateway。

### 3.6 產品 JSON 與執行方式的討論

每一個產品仍保有自己的 `appsettings.json`，但產品 JSON 的責任要很小：

- 選擇 `ExecutionMode`。
- 指定 `ProfileAlias`。
- 指定 Gateway Endpoint。

產品 JSON 不應保存：

- CRM 密碼。
- Access Token／Refresh Token。
- Client Secret。
- Certificate Private Key。
- 原始 CRM Organization Service URL。
- CRM SDK DLL 位置。
- 任意 Transport 種類。

Central 與 Local 目前不是兩個新的 `DynamicsExecutionMode` enum 值。現有程式契約仍是：

```text
Gateway
Embedded
```

Central／Local 都屬於 `Gateway` 模式，差異在 Endpoint：

- Central：`https://dynamics-gateway.internal/`
- Local：`https://localhost:7443/`（範例埠）

這樣可以避免為部署位置增加不必要的程式分支。

### 3.7 Embedded 與 Local Gateway 的討論

Embedded 是把 Dynamics Runtime 直接放入 ChurchReport 等產品 Process。

Local Gateway 則是：

- ChurchReport 是一個 Process。
- Dynamics Gateway 是另一個 Process。
- ChurchReport 透過 localhost HTTP 呼叫 Gateway。

Local Gateway 對目前需求比較適合，因為在 Visual Studio 2026 可以設定 Multiple Startup Projects，同時啟動 ChurchReport 與 Gateway，又能保留：

- 獨立 Console。
- 獨立 Health／Ready Endpoint。
- 獨立 Connection Pool。
- 獨立 SDK／WCF 相依。
- 獨立 Crash 與 Process Recycling 邊界。

因此目前決策是 Local Gateway 優先；Embedded 保留但暫緩。

### 3.8 D365 8.2 讓設計不能只看 Web API 理論

D365 CE 8.2 有 `/api/data/v8.2/` Web API，理論上 .NET 10 可直接使用 `HttpClient` 呼叫，不需要 Data8 或特定 SDK Assembly。

但是目前實際 IFD 環境的結果是：

| 測試路徑 | 結果 |
| --- | --- |
| SOAP／WS-Trust | 可以工作 |
| Web API＋Windows NTLM | 被 IFD 導向，不能直接使用 |
| OAuth Password Grant | ADFS 回覆 `unsupported_grant_type` |
| OAuth Authorization Code | OAuth Client／Redirect URI 尚未註冊 |
| Refresh Token | 尚無可用 Token 路徑 |

所以「8.2 Web API 存在」不等於「目前 8.2 Web API 已可供正式程式使用」。

目前 Data8 暫時必要的原因，是現場 Authentication 條件，而不是 D365 8.2 天生一定依賴 Data8。

### 3.9 加回 Central Gateway 後的完整設計

現在的完整設計不是只選 Local，也不是只選 Central：

- **Central Gateway**：正式環境的共用部署。
- **Local Gateway**：VS 開發、整合測試或個別隔離部署。
- **Embedded**：保留但暫緩。

三者不是三套不同的業務 API。Central 與 Local 共用同一套 Gateway REST Contract；未來如果 Embedded 繼續發展，也必須實作相同 `IDynamicsOperationExecutor` 行為。

## 4. Central Gateway 到底集中什麼

Central Gateway 集中的不是「一個萬用 CRM Connection」。它集中的是管理責任：

- Product Workload Authentication。
- Product／Profile／Operation Authorization。
- Profile Registry。
- Secret Reference Resolution。
- Operation Registry。
- Retry／Timeout／Backpressure。
- Audit／Telemetry／Health。
- Profile Runtime Generation。
- Aggregate Organization Admission。

在 Central Gateway 內，仍然必須有不同 Runtime：

```text
crm82 generation N
crm91 generation M
```

兩者不能共用：

- 可變的 `IOrganizationService`。
- WCF Channel。
- Access Token／Refresh Token Cache。
- Credential Object。
- Metadata Cache（除非有明確版本與 Organization Key）。
- SDK DLL Loading Context。
- Session 或使用者狀態。

## 5. Local Gateway 到底是不是「個別式」

是。Local Gateway 的實體 Process 與實體 Pool 是個別式的。

例如：

```text
ChurchReport -> ChurchReport Local Gateway -> Local Pool
產品 B       -> 產品 B Local Gateway       -> Local Pool
產品 C       -> 產品 C Local Gateway       -> Local Pool
```

但是 Local Gateway 不能把「個別 Pool」理解成「各自可以無限制連線」。如果三個 Local Gateway 都連到同一個實體 D365 Organization，它們仍然要共用同一個 Organization Admission Budget。

因此設計上區分兩件事：

| 項目 | 是否共用 |
| --- | --- |
| HttpClient／Socket Pool | 不共用，屬於 Process |
| WCF／SDK Client Pool | 不共用，屬於 Process／Worker |
| Token／Credential State | 不跨 Profile／Process 共用 |
| 同一個 CRM 的總併發上限 | 必須共用或協調 |
| Operation Registry／REST Contract | 必須一致 |

## 6. D365 9.1 的設計

### 6.1 建議路徑

```text
產品
  -> Central 或 Local Gateway
  -> crm91 Profile Runtime
  -> Direct Web API v9.1 或 Microsoft ServiceClient
  -> D365 CE 9.1
```

優先順序：

1. Direct Web API v9.1。
2. Microsoft 官方 `Microsoft.PowerPlatform.Dataverse.Client.ServiceClient`，前提是目標的 OAuth／Authentication 模式通過實機驗證。

D365 9.1 不需要 Data8 作為必要基礎。

### 6.2 為什麼仍要透過 Gateway

即使使用官方 `ServiceClient`，也不建議每一個產品直接參考，因為 Gateway 還負責：

- Secret 不進入產品。
- 統一 Token Cache。
- 統一 Pool 與 Dispose。
- 統一 Operation Allowlist。
- 統一 Audit 與錯誤格式。
- 未來替換 ServiceClient／Web API 時產品不變。

## 7. D365 8.2 的設計

### 7.1 目前狀態

目前工作路徑：

```text
ChurchReport
  -> ToolUtility
  -> Data8 OnPremiseClient
  -> WS-Trust／SOAP
  -> D365 CE 8.2 IFD
```

現在直接刪除 Data8 會造成：

- `ToolUtility.csproj` ProjectReference 失效。
- `CrmConnectionService.CreateOnPremiseClient` 無法建置。
- 現有 8.2 工作路徑中斷。

### 7.2 第一階段目標

先把產品與 Data8 解耦：

```text
ChurchReport
  -> Gateway REST Contract
  -> crm82 Legacy Adapter
  -> Data8 Legacy Worker
  -> D365 8.2
```

Data8 最好放入可回收的獨立 Worker Process，不應直接成為 Central Gateway 的永久長生命週期 Pool Client。

原因是目前 `OnPremiseClient` 沒有實作 `IDisposable`，既有 Pool 的：

```csharp
(connection?.Service as IDisposable)?.Dispose();
```

無法證明 Data8 內部 WCF Channel／ChannelFactory 已被 `Close` 或 `Abort`。這是 Socket、Handle 與長時間資源保留風險，必須視為正式發布阻擋條件。

### 7.3 最終替代方案 A：Web API v8.2

必須先完成：

- ADFS OAuth Client Registration。
- Redirect URI。
- 適合服務工作負載的 Token Flow。
- Token Renewal／Restart。
- 8.2 Web API Capability Matrix。
- 實際 CRUD、FetchXML、Actions、Functions、Paging 驗證。

不能因為 `/api/data/v8.2/` 可以回應，就認定所有舊 SDK 呼叫都可以搬過去。D365 8.2 Web API 對部分 Action／Return Type 有已知限制。

### 7.4 最終替代方案 B：Microsoft 官方 Legacy Worker

建立獨立 .NET Framework 4.8 Process：

```text
Gateway .NET 10
  -> IPC／localhost HTTP
  -> Legacy Worker .NET Framework 4.8
  -> Microsoft CrmServiceClient
  -> D365 8.2 IFD
```

好處是：

- 產品仍然是 .NET 10。
- Gateway 仍然是 .NET 10。
- 舊 Framework SDK 被隔離在另一個 Process。
- 使用 Microsoft 官方 XrmTooling／CrmServiceClient。
- Worker 可以獨立鎖 SDK 版本與重新啟動。

## 8. 8.2 與 9.1 要不要共用同一個 Worker

初期不要。

建議：

```text
LegacyWorker82 -> 鎖定經 8.2 實機驗證的 SDK／Authentication
LegacyWorker91 -> 只有真的需要 Legacy SDK 時才建立，鎖定 9.1 版本
```

原因：

- `Microsoft.Xrm.Sdk.dll`／`Microsoft.Crm.Sdk.Proxy.dll` 版本可能不同。
- 同一個 .NET Framework Process 可能遇到 Assembly Binding Redirect。
- 8.2 IFD WS-Trust 與 9.1 OAuth／IFD 狀態不同。
- Channel、Token、Credential 與 Metadata 不應混用。
- 最新 9.1 SDK 能否完整支援實際 8.2 Server，必須用實機測試證明，不能只靠套件描述推論。

實機驗證證明可以共用後，才考慮合併，不要一開始就把合併當成前提。

## 9. Product JSON 應該怎麼寫

### 9.1 Central Gateway

```json
{
  "DynamicsAccess": {
    "ExecutionMode": "Gateway",
    "ProfileAlias": "crm82",
    "Gateway": {
      "Endpoint": "https://dynamics-gateway.internal/",
      "ApiPrefix": "/v1"
    }
  }
}
```

### 9.2 Local Gateway

```json
{
  "DynamicsAccess": {
    "ExecutionMode": "Gateway",
    "ProfileAlias": "crm91",
    "Gateway": {
      "Endpoint": "https://localhost:7443/",
      "ApiPrefix": "/v1"
    }
  }
}
```

### 9.3 為什麼不是 `CentralGateway`／`LocalGateway`

因為目前程式中的 `DynamicsExecutionMode` 是：

```csharp
Gateway = 0,
Embedded = 1
```

Central 與 Local 是 Gateway 的部署拓撲，不需要改變產品業務程式。Endpoint 已經可以表達呼叫中央服務或 localhost。

如果未來真的要加入新的 enum，必須另外修改：

- Strongly Typed Options。
- Validation。
- DI Registration。
- JSON Schema。
- 測試與 Migration。

目前沒有必要增加這個複雜度。

## 10. Embedded 的最後決定是什麼

Embedded 現在：

- 不刪除。
- 不作為目前開發預設。
- 不繼續擴大產品使用範圍。
- 不視為正式 8.2 相容方案。
- 保留程式與研究成果。

等以下條件完成後再決定：

1. Local Gateway 在 ChurchReport 開發流程可正常使用。
2. Central／Local REST Contract 一致。
3. 8.2 與 9.1 實機驗證完成。
4. Aggregate Admission 能涵蓋所有 Runtime Host。
5. Secret／Token／Credential 隔離通過。
6. Runtime Drain／Dispose／Socket／Handle Soak 通過。

之後可以選擇：

- 繼續發展 Embedded，作為非常特殊的零 HTTP Hop 部署。
- 保留但不提供正式支援。
- 確認沒有用途後移除。

## 11. Phase 4、Phase 5 是否會受到影響

目前改變方向仍不算太晚，因為已建立的重要基礎仍可以沿用：

- `IDynamicsOperationExecutor` 抽象。
- Operation Registry。
- ProductClient。
- Gateway HTTP Contract。
- Profile Runtime／Generation 概念。
- Organization Admission／Host Lease。
- Isolation／Lifecycle／Soak Tests。
- 8.2／9.1 Capability Matrix。

需要調整的是 Transport 與 Hosting 決策，不是把 Phase 4、Phase 5 全部推翻。

應避免的作法是讓既有 Phase 4／5 直接把 Data8 或 SDK Client 滲透進產品業務碼。只要產品仍依賴 ProductClient／Gateway Contract，內部 Adapter 可以更換。

## 12. Connection Pool 在這個架構中的真正意思

### 12.1 Central Gateway Pool

中央 Gateway 內可能有：

```text
crm82 generation 7 runtime
crm91 generation 3 runtime
```

每一個 Runtime 擁有自己的：

- Http Handler／HttpClient；或
- Worker Proxy；或
- SDK Client；
- Token／Authentication State；
- Metadata Cache；
- Retry／Health State；
- Cancellation／Timer；
- Dispose／Drain 邊界。

### 12.2 Local Gateway Pool

每一個 Local Gateway 有自己的 Process-local Runtime。它不與 Central Gateway 共用記憶體中的 Pool，也不與另一個 Local Gateway 共用物件。

### 12.3 什麼必須跨 Host 協調

同一實體 D365 Organization 的：

- 最大同時 Outbound Requests。
- 最大 Runtime Host 數。
- Queue Capacity。
- Retry Budget。
- Rollout／Blue-Green Capacity。

必須透過 Organization Admission Plan 協調。否則五個 Local Gateway 各認為自己可以開十條連線，實際上可能同時送出五十個工作，反而失去集中管理的意義。

## 13. Data8 專案什麼時候才能刪

以下條件全部完成前不能刪除：

1. `ToolUtility` 不再 `ProjectReference` Data8 專案。
2. 所有產品不再直接呼叫 `CreateOnPremiseClient`。
3. 程式碼中不再建立 `OnPremiseClient`。
4. ChurchReport 與其他產品全部改走 ProductClient／Gateway。
5. D365 8.2 有通過實機測試的替代 Adapter。
6. D365 9.1 路徑不依賴 Data8。
7. 8.2 替代路徑完成：
   - WhoAmI／Identity Probe。
   - CRUD。
   - Query／FetchXML。
   - Paging。
   - 實際 Actions／OrganizationRequest。
   - Authentication Renewal／Reconnect。
   - Gateway／Worker Restart。
   - Long-running Socket／Handle／Memory Soak。
8. 有可驗證的 Rollback／Feature Flag。
9. Solution、Project、Package、Source 掃描沒有剩餘 Data8 相依。
10. 刪除後完整 Build、Tests、8.2／9.1 Smoke Test 全部通過。

## 14. 失敗時應該怎麼處理

### 14.1 Central Gateway 失效

- 正式產品應回報 Gateway unavailable／NotReady。
- 不可在產品內偷偷改成直接連 CRM。
- 不可自動切換 Data8 或另一個 Profile。
- 由部署層做 Gateway HA、Restart 或 Rollback。

### 14.2 Local Gateway 沒有啟動

- ChurchReport 開發環境顯示明確的 localhost Gateway 連線錯誤。
- Visual Studio Multiple Startup Projects 應同時啟動兩個 Process。
- 不可自動改走正式 Central Gateway，以免開發機誤用正式 CRM。

### 14.3 8.2 Web API OAuth 失敗

- `crm82-webapi` Profile 保持 NotReady。
- 不在同一個請求中偷偷改走 WS-Trust。
- 由部署設定明確選擇暫時 Legacy Profile／Transport。

### 14.4 Worker Crash

- Gateway 偵測 Worker 不健康。
- 停止新請求。
- 正在執行的工作依 Deadline 失敗或取消。
- Worker 可以依政策重新啟動。
- 不得無限重試非冪等寫入。

## 15. 建議實施順序

### 第一階段：固定產品邊界

- ChurchReport 只依賴 ProductClient／Gateway Contract。
- 不再新增任何直接 CRM SDK／Data8 呼叫。
- `ExecutionMode=Gateway` 可透過 Endpoint 選 Central 或 Local。

### 第二階段：Local Gateway 優先

- VS 2026 同時啟動 ChurchReport 與 Local Gateway。
- 驗證 Health、Ready、Authentication、Profile、Pool 與 Error Handling。
- 先跑 9.1 正式路徑。
- 8.2 暫時透過隔離 Legacy Worker。

### 第三階段：Central Gateway

- 多產品改指向中央 Endpoint。
- 集中 Profile、Secret、Policy、Audit、Telemetry。
- 驗證不同產品公平排程與同一 CRM 的總併發限制。

### 第四階段：8.2 官方替代

- 完成 ADFS OAuth PoC；或
- 完成 Microsoft `CrmServiceClient` net48 Worker。
- 與 Data8 路徑做結果、效能與穩定性比對。

### 第五階段：移除 Data8

- 所有移除 Gate 通過。
- 先停用與觀察，再刪除 ProjectReference／Solution Entry／Source。

### 第六階段：重新評估 Embedded

- 有明確效益才繼續。
- 如果 Local Gateway 已滿足 VS 開發與部署需求，Embedded 可以保持停用或移除。

## 16. 常見問題

### Q1：Central Gateway 和 Local Gateway 是不是兩套程式？

建議是同一個 Gateway Host 程式與同一套 Adapter Contract，用不同部署設定啟動。Central 是共用部署；Local 是產品旁邊的獨立 Process。

### Q2：Local Gateway 能不能使用 Central 的設定？

可以使用相同的 Profile Definition／Manifest 模型，但 Local Process 仍要建立自己的實體 Pool。Secret 必須由核准的 Secret Provider 解析，不能直接複製到產品 JSON。

### Q3：產品可以執行中切換 8.2／9.1 嗎？

一般使用者請求不能任意切換。產品與部署設定指定允許的 `ProfileAlias`；Gateway 由授權政策決定該 Workload 能否使用該 Profile。設定變更需要 replace-and-drain。

### Q4：8.2 與 9.1 可以有相同的業務呼叫語法嗎？

可以。產品呼叫相同的 Operation ID 與 Typed Parameters。Gateway 內部不同 Adapter 負責轉換。但只有在 Capability Matrix 證明兩個版本都支援時，該 Operation 才能同時啟用。

### Q5：為什麼不直接讓 ChurchReport 參考 Microsoft 官方 ServiceClient？

因為這仍會把 Authentication、Token、Pool、Retry、SDK 更新與 Secret 放回產品。Gateway 邊界的價值是把這些責任集中，而不是單純把第三方 SDK 換成官方 SDK。

### Q6：Data8 現在是不是不安全？

不能直接下結論說它一定不安全；目前它是可以工作的第三方相容實作。但它缺乏正式支援，且現有 `OnPremiseClient` 的 WCF 生命週期沒有被既有 Pool 的 `IDisposable` 路徑完整覆蓋。因此不適合在沒有額外生命週期保護與 Soak Test 的情況下成為永久長生命週期 Pool。

### Q7：現在改設計會不會讓前面工作浪費？

不會。只要保留 ProductClient、Gateway Contract、Operation Registry、Profile Runtime 與 Admission 等邊界，前面的工作仍是基礎。改變的是內部 Transport 與 Hosting 優先順序。

## 17. 最終決策表

| 項目 | 現在決策 | 未來條件 |
| --- | --- | --- |
| Central Gateway | 正式環境預設 | 完成多產品部署與 HA 驗證 |
| Local Gateway | 目前優先實作／驗證 | 作為 VS 與隔離部署正式選項 |
| Embedded | 保留、暫緩 | Local 與實機 Gate 後再決定 |
| D365 9.1 | Web API／官方 ServiceClient | 依實際 Authentication 選定 |
| D365 8.2 | Data8 暫留 | 改 Web API OAuth 或官方 net48 Worker |
| Data8 專案 | 現在不能刪 | 全部移除 Gate 通過後刪除 |
| 8.2／9.1 Worker | 初期分開鎖版本 | 實機證明相容後才能合併 |
| 產品 JSON | Gateway＋Alias＋Endpoint | 不允許 Secret／CRM URL／Transport |
| Connection Pool | Central 或 Local Process 內分 Profile | 同一 CRM 總容量跨 Host 協調 |

## 18. 一句話總結

> 產品 A～10 永遠只學一種 Dynamics 呼叫方式；Central Gateway 與 Local Gateway 負責部署差異，`crm82` 與 `crm91` 負責版本差異，Web API／官方 SDK／暫時 Data8 Worker 負責 Transport 差異，而這些差異全部不能滲透回產品業務程式。
