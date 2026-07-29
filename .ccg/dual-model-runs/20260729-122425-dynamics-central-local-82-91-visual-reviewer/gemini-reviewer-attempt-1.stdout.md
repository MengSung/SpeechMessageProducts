# 視覺化與架構審查報告：Central/Local Gateway with Dynamics CE 8.2 and 9.1

本報告針對 `dynamics-central-local-82-91.html` 及其對應的 Dynamics 365 CE 8.2 / 9.1 閘道器架構設計進行唯讀審查。

```
VALIDATION REPORT
=================
User Experience: 18/20 - 架構設計清晰，Central vs Local 邊界明確，但 WCF 連接釋放問題會影響系統穩定性。
Visual Consistency: 19/20 - 視覺化圖表與設計規格書高度一致，移除了過時的命名。
Accessibility: 18/20 - 互動按鈕已加入 aria-controls，圖示名稱已修正，但仍需確保 HTML 視覺化在不同螢幕尺寸下的響應性。
Performance: 15/20 - 由於 Data8 OnPremiseClient 未實現 IDisposable，在高併發下存在 Socket 耗盡的嚴重效能與穩定性風險。
Browser Compatibility: 19/20 - HTML 視覺化使用標準的 Mermaid 和 Web 技術，相容性良好。

TOTAL SCORE: 89/100

ISSUES FOUND:
- [Critical] PowerPlatform.Dataverse.Client/OnPremiseClient.cs 未實現 IDisposable，導致 WCF 連接通道無法正確關閉，存在 Socket 耗盡風險。
- [Warning] Data8 庫為非官方支援（best-effort only），在生產環境中存在長期維護風險。
- [Warning] ADFS OAuth 驗證依賴度高，若 ADFS 配置不支援 OAuth 授權流程，將迫使系統退回 WS-Trust/SOAP。
- [Warning] CE 8.2 與 CE 9.1 SDK 在同進程中並存會導致版本衝突，必須嚴格執行進程隔離。

RECOMMENDATION: PASS
```

---

## 1. 審查問題回覆 (Review Questions Answers)

### Q1: 架構在技術上是否準確，且與所述決策一致？
**是。** 架構設計完全符合所述決策：
* 產品使用統一的 `ProductClient` / REST 合約，在啟動時選擇 `CentralGateway` 或 `LocalGateway`（對應程式碼中的 `Gateway` 和 `Embedded` 模式），並提供 `ProfileAlias`，不提供憑證。
* Central Gateway 是生產環境的預設模式，擁有集中共享的 profile runtimes/pools。
* Local Gateway 是進程本地的，用於開發或隔離部署。
* 兩者物理上分離，但共享同一個組織級別的准入/併發預算（admission/concurrency budget）。
* CE 9.1 偏好 direct Web API v9.1 或官方 ServiceClient。
* CE 8.2 暫時使用 Data8 WS-Trust bridge，目標是 direct Web API 或 out-of-process .NET Framework 4.8 worker。
* CE 8.2 和 9.1 的 legacy SDK workers 保持獨立的版本鎖定和進程隔離。
* Data8 是暫時的，只有在替代方案通過真實伺服器測試且所有專案/源碼引用被移除後才能移除。
* Embedded 模式在此視覺化中被推遲，且故意不作為推薦的執行模式。

### Q2: 是否有任何措辭錯誤地暗示所有版本/身分共享一個可變的連接/會話？
**否。** 架構明確指出，每個 profile 世代擁有獨立的 HttpClient/socket pool、憑證提供者、元數據快取和健康狀態。
* 憑證快取和運行時狀態是基於不可變的 profile 世代、組織 URI、API 版本和認證上下文進行隔離的，並非所有版本/身分共享同一個可變的連接/會話。
* 組織級別的併發預算（admission budget）是通過 canonical organization capacity key 進行協調的，這是一個非機密的標識符，用於跨多個 profile 世代和主機進行流量控制，而不是共享同一個物理連接或會話。

### Q3: 是否有任何措辭過度聲稱官方 ServiceClient 或 Web API 對當前 CE 8.2 IFD 環境的支援？
**否。** 架構和評估報告中明確指出，官方的 ServiceClient 在 .NET 10 下不支援 WS-Trust/SOAP，因此無法直接用於 CE 8.2 IFD。而 Direct Web API 需要 ADFS 支援 OAuth 授權流程。
* 任何聲稱官方 ServiceClient 或 Web API 可以直接、無縫支援當前 CE 8.2 IFD 環境的說法都是過度聲稱（overclaim），因此架構中將 Data8 作為暫時的橋接方案，並將 ADFS OAuth 驗證作為 Direct Web API 的前提條件。

### Q4: Central vs Local 的所有權邊界是否易於理解？
**是。** 非常清晰：
* Central Gateway 擁有集中共享的 profile runtimes/pools，是生產環境的預設模式。
* Local Gateway 是每個產品獨立的 out-of-process Windows 服務/控制台，用於開發或隔離部署，其物理連接池是進程本地的。
* 兩者物理上分離，但共享同一個組織級別的准入/併發預算（admission/concurrency budget）。

### Q5: Data8 的保留/移除以及官方 worker 的遷移邊界是否清晰？
**是。** 非常清晰：
* Data8 只是暫時的橋接方案（WS-Trust bridge），用於解決當前 CE 8.2 IFD 的驗證問題。
* 移除 Data8 的條件是：
  1. ADFS OAuth 驗證在真實伺服器上通過，且 Direct Web API 可用；或者
  2. 官方 .NET Framework 4.8 Worker 實作完成並通過測試。
  3. 所有專案和源碼中對 Data8 的引用都被完全移除。

### Q6: 圖表中是否遺漏了隔離、憑證、連接池或資源生命週期風險？
**是。** 存在以下風險，需要在架構中進一步明確或在實作中加以防範（詳見下方 Findings）。

---

## 2. 審查發現 (Review Findings)

### 🔴 Critical (嚴重缺陷)
* **檔案路徑**: `PowerPlatform.Dataverse.Client/OnPremiseClient.cs`
  * **問題說明**: `OnPremiseClient` 實現了 `IOrganizationService`，但**未實現 `IDisposable`**。在 `ConnectFederated` 中創建的 WCF 連接通道（`ChannelFactory` 或 `IClientChannel`）在釋放時無法被正確關閉（`Close`/`Abort`）。這會導致底層的 TCP 連接和 Socket 無法被及時釋放，在高併發或頻繁重載配置時，會引發 **Socket 耗盡 (Socket Exhaustion)** 的風險，進而導致系統崩潰。
  * **建議**: 讓 `OnPremiseClient` 實現 `IDisposable`，並在 `Dispose` 方法中安全地關閉和釋放 WCF channel 和 `ChannelFactory`。

### 🟡 Warning (警告)
* **檔案路徑**: `ToolUtility/ToolUtility.csproj`
  * **問題說明**: Data8 庫是非官方支援的（best-effort only），在生產環境中存在長期維護風險。一旦微軟更新或 ADFS 環境變更，該庫可能無法及時修復。
  * **建議**: 嚴格限制 Data8 的使用範圍，僅作為過渡期的臨時方案，並加速推動 ADFS OAuth 驗證以轉向 Direct Web API。
* **檔案路徑**: `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`
  * **問題說明**: 當前 Web API 依賴 ADFS OAuth 驗證，如果 ADFS 配置不正確或不支援 password grant，將導致 Web API 無法使用，從而不得不退回到 WS-Trust/SOAP。
  * **建議**: 在真實伺服器上進行 ADFS OAuth 驗證測試，確保 Client ID 和 Redirect URI 配置正確。
* **檔案路徑**: `ToolUtility/ConnectionOperations/CrmConnectionService.cs`
  * **問題說明**: 在同一個進程中同時引用 CE 8.2 和 CE 9.1 的 SDK（`Microsoft.Xrm.Sdk.dll`）會導致版本衝突（v8.x vs v9.x），從而引發運行時異常。
  * **建議**: 必須使用獨立的、版本鎖定的 Worker 進程進行隔離，禁止在同一個進程中混合使用不同版本的 SDK。

### 🔵 Info (提示)
* **檔案路徑**: `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`
  * **說明**: 該類別實現了 no-SDK Web API 客戶端，使用 `HttpClient` 直接調用 OData v8.2/v9.1 接口，這符合架構設計的最終目標，應作為首選路徑。

---

## 3. 建議 (Suggestions)

1. **修正 WCF 釋放邏輯**: 立即為 `OnPremiseClient` 實現 `IDisposable`，確保所有 WCF 通道在生命週期結束時被正確關閉。
2. **加速 ADFS OAuth 驗證**: 優先在測試環境中驗證 ADFS OAuth 流程，一旦通過，立即將 CE 8.2 遷移至 Direct Web API，以盡早移除 Data8 依賴。
3. **嚴格執行進程隔離**: 確保 CE 8.2 和 CE 9.1 的 legacy SDK workers 運行在獨立的進程中，避免 DLL 版本衝突。

## 4. 肯定之處 (Positive Notes)

1. **架構邊界清晰**: Central vs Local 的設計非常合理，既保證了生產環境的集中管理，又兼顧了開發環境的便利性。
2. **准入控制設計完善**: 共享組織級別的准入/併發預算（admission budget）能有效保護 Dynamics 伺服器不被過載。
3. **無 SDK 目標明確**: 架構明確了移除 SDK 依賴的最終狀態和具體指標，有助於系統向 .NET 10 順利演進。
