我已完成程式碼層級的證據蒐集（`PowerPlatform.Dataverse.Client`、`CrmConnectionService`、`CrmConnectionPool`、`CrmClientFactory`、`Startup.cs`、專案檔與 `appsettings.json`），以下為完整分析報告。

---

## Analysis: Dynamics 365 8.2 / 9.1 連線相容性與 Web API 遷移評估

## Conclusion

1. **目前程式碼並非純 Web API 連接器，而是雙軌 SOAP/WS-Trust 架構**：`OnPremiseClient`（自製 WS-Trust 客戶端）與 `CrmConnectionService`（`Microsoft.Xrm.Sdk.Client` 的 `OrganizationServiceProxy`/`DiscoveryServiceProxy`）並存，兩者都走 `/XRMServices/2011/Organization.svc` SOAP 端點（非已淘汰的 `OrganizationData.svc` OData v2，符合限制條件）。
2. **`OnPremiseClient` 本身對 8.2/9.1 有較好的相容性設計**（動態抓 WSDL 並帶 `sdkversion` 查詢參數協商），但 `CrmConnectionService` 的 SOAP 方法所依賴的組件鏈結（`Microsoft.Crm.Sdk.Proxy.dll` v9.0.2.52）是透過**跨repo外部絕對路徑的 net462 DLL** 被拉進 net10.0 專案，這是比 SDK 版本相容性更急迫的風險——它會影響**建置可重現性**，且尚未驗證 net462 二進位在 net10 執行期下是否可正常運作。
3. **目前架構完全不支援單一 process 內同時維護 8.2 與 9.1 兩個具名 CRM Profile**。`ICrmConnectionPool` 在 `Startup.cs` 中以 **Singleton** 註冊，且 `CrmConnectionPool` 建構時就把 `serverUrl/username/password` 寫死為單一組——沒有 profile key、沒有 keyed DI、沒有隔離機制。這直接觸及使用者定義的 release blocker（跨 profile 憑證/連線洩漏）。
4. **Web API-first 策略方向正確，但不能完全取代 SOAP**：8.2 on-prem 的 Web API 版本（v8.2）功能集少於 9.1，且 8.2/9.1 on-prem 的驗證機制主要仍是 Windows 整合驗證（AD）或 WS-Federation（Claims/IFD），而非現代 OAuth Bearer——這代表新連接器仍需保留類似 `OnPremiseClient.ConnectFederated` 的 WS-Trust STS token 取得邏輯，只是把它接到 `HttpClient` 而非 WCF channel。

---

## Source evidence

| # | 檔案:行號 | 證據 |
|---|---|---|
| E1 | `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:129,144-166` | 建構時載入 `url + "?wsdl&sdkversion=" + _sdkMajorVersion`，依 WSDL 中的 `AuthenticationPolicy` 分派 AD 或 Federation 流程 |
| E2 | `OnPremiseClient.cs:69-88` | `_sdkVersion`/`_sdkMajorVersion` 從 `typeof(IOrganizationService).Assembly` 讀出，失敗時硬編碼回退 `"9.1.2.3"` / major `9` |
| E3 | `OnPremiseClient.cs:48-58` | 每次呼叫都透過 `OperationContextScope` 帶 `SdkClientVersion`/`UserType`/`CallerId` header（僅 WCF channel 情境） |
| E4 | `ToolUtility/ConnectionOperations/CrmConnectionService.cs:94-277` | 三種舊式方法（CRM2011 HTTP、Claims-Based HTTPS、Federated Discovery+Proxy）皆用 `OrganizationServiceProxy`/`DiscoveryServiceProxy`/`ServiceConfigurationFactory`，來自 `Microsoft.Xrm.Sdk.Client` 舊版 WCF SDK，而非 `Microsoft.PowerPlatform.Dataverse.Client` |
| E5 | `CrmConnectionService.cs:430-441` | `CreateOnPremiseClient` 是唯一使用自製 `OnPremiseClient` 的方法，標註「推薦用於新專案」 |
| E6 | `ToolUtility/ConnectionOperations/CrmConnectionPool.cs:52-91` | 建構子固定 `_serverUrl/_username/_password` 三個 `readonly string`，`CreateConnection()`（L293-322）永遠呼叫同一組參數的 `CreateOnPremiseClient` |
| E7 | `SpeechMessageProducts.ChurchReport/Startup.cs:302-349` | `ICrmConnectionPool` 註冊為 **單一 Singleton**，從 `CrmConnection` 設定段落讀取**唯一一組** `ServerUrl/Username/Password` |
| E8 | `SpeechMessageProducts.ChurchReport.csproj:104-111` | `Microsoft.Crm.Sdk.Proxy.dll` 以 HintPath 引用外部路徑 `..\..\..\..\DevExpressDevExtreme-23.1.5版本\...\Microsoft.CrmSdk.CoreAssemblies.9.0.2.52\lib\net462\...`，位於**倉庫之外**、**特定開發機路徑**，且是 **net462** 組件，被引入 **net10.0** 專案 |
| E9 | `ToolUtility.Tests.csproj:44` | 測試專案改用正式 NuGet 套件 `Microsoft.CrmSdk.CoreAssemblies` 9.0.2.56（與 E8 的 9.0.2.52 版本不一致） |
| E10 | `SpeechMessageProducts.ChurchReport/appsettings.json:242-251` | `CrmConnection` 區段內**明文密碼已提交至版本控制**（`Password` 欄位有實際值），與 `Startup.cs:316-324` 「避免硬編碼密碼」的註解與 fallback-to-env 邏輯矛盾 |
| E11 | `PowerPlatform.Dataverse.Client.csproj:50` | `Microsoft.PowerPlatform.Dataverse.Client` 版本 `1.1.32`（新版 Dataverse SDK，非舊版 `Microsoft.Xrm.Tooling.Connector`） |
| E12 | `ToolUtility/Factories/CrmClientFactory.cs:58-155` | 已存在 `ICrmClient` 抽象與 `DataverseServiceClientAdapter`/`LegacyOrganizationServiceAdapter` 雙 Adapter 模式，是導入 Web API Adapter 的現成擴充點 |
| E13 | 全倉庫搜尋 `Profile\|Tenant\|MultiOrg` (ConnectionOperations 目錄) | **無任何** profile/tenant 隔離相關程式碼——證實目前無多 profile 機制 |

---

## Compatibility matrix

| 項目 | Dynamics 365 8.2 On-Prem (AD) | Dynamics 365 8.2 On-Prem (IFD/Claims) | Dynamics 365 9.1 On-Prem (AD) | Dynamics 365 9.1 On-Prem (IFD/Claims) | Dynamics 365 Online (OAuth) |
|---|---|---|---|---|---|
| `OnPremiseClient` (WS-Trust) | 可行（`ConnectAD`），**需對真機驗證** WSDL 中 `AuthenticationPolicy` 結構是否一致 | 可行（`ConnectFederated`），**需驗證** STS WS-Trust 1.3 policy 探索邏輯在 8.2 ADFS 上是否吻合 | 可行，理論上相容性優於 8.2（`sdkversion` 協商機制） | 可行，同左 | **不支援**（此類別未實作 OAuth 分支） |
| `CrmConnectionService` SOAP 方法（`OrganizationServiceProxy`） | 可行但依賴 E8 風險路徑組件 | 可行但同上，且需驗證 `AuthenticationProviderType.Federation` 分支在 8.2 上行為 | 可行，`Microsoft.Crm.Sdk.Proxy.dll` 版本標示 9.0.2.52，**理論上針對 9.x 設計**，用於 8.2 屬於「向下相容」需實測 | 同上 | 不支援（無 OAuth 分支） |
| `DataverseServiceClientAdapter`（`Microsoft.PowerPlatform.Dataverse.Client` 1.1.32） | **未知**——1.1.x 版 Dataverse Client 主要針對 9.x/Online 測試，官方未正式承諾 8.2 SOAP 端點相容 | 未知，同左 | 較可能相容（此套件設計目標即 9.x+） | 較可能相容 | 原生支援（`AuthType=OAuth`） |
| Web API（OData v4） | **有已知功能落差**（見 Risks W1） | 有落差，且 8.2 on-prem 預設**無**現代 OAuth Bearer 支援，仍需 WS-Federation token | 功能較完整，多數 9.x Web API 特性可用 | 需 ADFS 支援 OAuth（9.x on-prem 才官方支援 hybrid OAuth） | 原生、功能最完整 |
| CRM 2011 `OrganizationData.svc` (OData v2) | 已淘汰，**不建議** | 已淘汰 | 已淘汰 | 已淘汰 | N/A |

---

## Risks

**[Critical] R1 — appsettings.json 內明文密碼已提交版本控制**
`SpeechMessageProducts.ChurchReport/appsettings.json:242-251` 的 `CrmConnection:Password` 欄位含有實際明文密碼，已進入 Git 歷史。這與 `Startup.cs` 中「避免硬編碼密碼、改用環境變數」的防護邏輯完全矛盾，形同該防護從未真正生效。**這是憑證管理面向最直接的洩漏風險**，且會被本次多 profile 擴充直接放大（若照抄同一模式新增 9.1 profile，會產生第二組明文密碼）。

**[Critical] R2 — 無 Profile/租戶隔離機制，單一 Singleton 綁死一組憑證**
`Startup.cs:302-349` 將 `ICrmConnectionPool` 註冊為唯一 Singleton，`CrmConnectionPool` 建構子（E6）把 `serverUrl/username/password` 存為 `readonly` 欄位，`CreateConnection()` 永遠用同一組參數建立連線。目前**沒有**任何 keyed DI、profile 參數、或依 entity/route 選擇 profile 的機制。若要同時服務 8.2 與 9.1，唯一路徑是新增第二個 `CrmConnectionPool` 實例——但目前 DI 註冊方式（單一 lambda 產生單一 `ICrmConnectionPool`）**不支援**同時解析兩個具名池。若日後有人為求快速交付而讓同一 pool/service 動態切換 `serverUrl`，會導致連線池內混雜不同來源的連線物件，`ConcurrentDictionary<IOrganizationService, PooledConnection>`（E6）以 `IOrganizationService` 實例為 key，本身不記錄「這個連線屬於哪個 profile」，一旦誤用即為**跨租戶資料/憑證洩漏**。

**[Critical] R3 — net462 CRM SDK DLL 以倉庫外部路徑掛入 net10.0 專案**
`SpeechMessageProducts.ChurchReport.csproj:104-111`（E8）的 `HintPath` 指向 `..\..\..\..\DevExpressDevExtreme-23.1.5版本\...\net462\Microsoft.Crm.Sdk.Proxy.dll`，四層跳出目前 git 倉庫範圍，指向特定開發機的資料夾（且版本 9.0.2.52 與測試專案 E9 的 9.0.2.56 不一致）。這代表：(a) 在其他機器 / CI / 新開發者環境上**建置很可能直接失敗**；(b) 即使建置成功，net462 組件在 net10.0 執行期下的相容性從未經過正式驗證，`OrganizationServiceProxy`/`DiscoveryServiceProxy` 等 WCF client 類別本身歷史上就是為 .NET Framework 設計（`LegacyOrganizationServiceAdapter.cs:42-44` 自己的註解也寫著「OrganizationServiceProxy 在 .NET Core/.NET 10 不支援」，但 `CrmConnectionService.cs` 卻直接在 net10.0 專案中使用它）——**這是自相矛盾且需要立即釐清的技術債**。

**[Warning] W1 — Dynamics 365 8.2 Web API 已知功能落差（需對真機驗證）**
以下項目為文件記載或社群普遍認定的 8.2 → 9.x Web API 差距，**必須對實機 8.2/9.1 伺服器驗證**才能定案：
- 8.2 on-prem 的 Web API（v8.2 端點）**沒有官方 hybrid OAuth 支援**——一般仍需 Windows 整合驗證或透過 ADFS 的 WS-Federation claims token，而非標準 OAuth2 Bearer；9.x on-prem 才開始有 ADFS OAuth（hybrid connectivity）的官方指引。
- Multi-select Option Set（choice 欄位多選）在 Web API 是 9.0+ 才引入，8.2 不支援。
- Elastic tables / Virtual entities 等 9.x+ 專屬能力在 8.2 不存在（若目前系統未使用則風險低，需盤點實際 entity schema）。
- 部分 `$batch`/`$expand`/alternate key 語法細節在 8.2 與 9.1 之間可能有邊界差異，建議實測而非假設一致。

**[Warning] W2 — WS-Trust/SDK 版本協商需對兩台真機各自驗證**
`OnPremiseClient` 的 `_sdkVersion`/`sdkversion` 查詢參數（E1、E2）機制假設伺服器會根據該參數回傳對應版本的 WSDL/驗證原則。Dynamics 365 伺服器端對 `SdkClientVersion` header 通常有**最低/最高相容版本窗**限制，若 `OnPremiseClient` 對兩個版本的伺服器都送出同一組（從組件讀出的）SDK 版本號，其中一台可能拒絕連線或行為異常。需要對 8.2 與 9.1 兩台真機分別驗證。

**[Warning] W3 — Proxy/Channel 生命週期與 Dispose 責任不透明**
`CrmConnectionService.SetFederatedOrganizationProxy`/`SetClaimsBasedAuthenticationOrganizationService`（E4）回傳 `OrganizationServiceProxy`（`IDisposable`），但呼叫端是否統一透過 `CrmConnectionPool`/`using` 正確釋放未在此檢視範圍內完全確認；`CrmConnectionPool.DisposeConnection`（E6 L406-425）僅處理它自己建立的 `OnPremiseClient` 連線（`PoolOwned=true`），對外部注入的連線（`ReleaseConnection` 中 `PoolOwned=false` 分支，L189-201）則從不主動 Dispose，長期執行下可能造成 WCF channel 洩漏。

**[Info] I1 — SOAP 端點選型正確**
兩條連線路徑皆使用 `/XRMServices/2011/Organization.svc`（SOAP，非 REST），並未使用已淘汰的 `OrganizationData.svc`（OData v2），符合使用者的限制條件。

**[Info] I2 — 既有 Adapter 模式是良好的遷移地基**
`ICrmClient` + `DataverseServiceClientAdapter` + `LegacyOrganizationServiceAdapter`（E12）已經是標準 Adapter Pattern，`CrmClientFactory` 也已依 `IConfiguration` 做條件式建立。新增一個 `WebApiCrmClientAdapter` 可以直接掛入同一個 factory，不需重構既有呼叫端。

---

## Recommended architecture

**設定驅動的具名 Profile 模型（優先解決 R2）**

```
CrmConnection:Profiles:Jesus82   { Version: "8.2", AuthMode: "AD",     BaseUrl, Username, PasswordRef }
CrmConnection:Profiles:Jesus91   { Version: "9.1", AuthMode: "Claims", BaseUrl, Username, PasswordRef }
```

- 以 `ICrmConnectionPoolFactory.GetPool(profileName)` 取代目前單一 `ICrmConnectionPool` Singleton；每個 profile 各自持有獨立的 `CrmConnectionPool` 實例（獨立 `_connectionLookup`/`_connections`），從根本上避免連線混用。
- 密碼一律從 Secret Manager / Key Vault / 環境變數解析（`PasswordRef` 只存參照名），移除 `appsettings.json` 內任何明文值（對應 R1）。
- 每個 profile 內部仍可用 `OnPremiseClient`（AD/Claims 皆支援）作為 SOAP fallback，但由 profile 的 `Version` 決定要不要走 Web API Adapter。

**中期：Web API-first Adapter（`HttpClient` + OData v4）**

- 掛入既有 `ICrmClient`/`CrmClientFactory` 架構，新增 `WebApiCrmClientAdapter`。
- 驗證層需同時支援：AD（`HttpClientHandler` + `Negotiate`/Windows 整合驗證）、Claims/IFD（沿用 `OnPremiseClient.ConnectFederated` 的 WS-Trust STS 邏輯取得 token，再以 `Authorization` header 附加到 Web API 請求）、Online（標準 MSAL OAuth，`Microsoft.Identity.Client` 已在 `ToolUtility.csproj` 內，可直接沿用）。
- 對 8.2 profile：Web API 僅覆蓋已驗證可用的 CRUD/查詢功能；W1 所列落差項目保留走 SOAP（`OnPremiseClient`/`CrmConnectionService`）作為 fallback，不強行全面 Web API 化。

**優先順序（不含程式修改，此處僅為建議路線）**

1. 修 R1（移除明文密碼、統一走 Secret 來源）與 R3（把 CRM SDK DLL 改為正式可還原的 NuGet 套件參考，或至少把路徑收進倉庫範圍並鎖定版本）——這兩項是任何後續工作的地基，不修就無法安全地做其他驗證。
2. 修 R2：導入具名 Profile + Keyed Pool，先用「單一 profile 但改走設定驅動」驗證架構不破壞現有行為，再加入第二個 profile。
3. 針對 8.2、9.1 各自真機執行 Validation tests（見下），確認 W1/W2 的具體差異清單。
4. 依驗證結果決定 Web API Adapter 的涵蓋範圍，SOAP 僅保留給落差項目。

---

## Validation tests

1. **建置可重現性測試**：在乾淨環境（無 `DevExpressDevExtreme-23.1.5版本` 資料夾）還原並建置 `SpeechMessageProducts.ChurchReport.csproj`，預期目前會失敗（驗證 R3）。
2. **雙 Profile 並行連線測試**：同一 process 內，對 8.2 與 9.1 兩台真機各建立一個 `OnPremiseClient`/`CrmConnectionPool`，並行執行 `WhoAmI`，斷言回傳的 `OrganizationId`/`UserId` 各自正確、且互不污染（驗證 R2 修復後的隔離性）。
3. **SdkClientVersion 邊界測試**：分別對 8.2、9.1 伺服器送出 `OnPremiseClient` 建構請求，記錄伺服器回傳的 `AuthenticationPolicy`/WSDL 差異，確認 `_sdkVersion` 協商是否被伺服器接受（驗證 W2）。
4. **Web API 功能落差清單**：對 8.2、9.1 各自的 Web API 端點逐項測試——multi-select choice 欄位、`$batch`、alternate key `$filter`、OAuth vs Windows 整合驗證是否可用，產出實測版差異表以取代本報告中的文件推測（驗證 W1）。
5. **Dispose/洩漏測試**：長時間跑 `CrmConnectionPool.AcquireConnection`/`ReleaseConnection` 循環，監控 WCF channel/handle 數量是否隨時間增長（驗證 W3）。
6. **憑證掃描**：對倉庫執行 secret-scanning（如 `git log` + `trufflehog`/`gitleaks`），確認 R1 的密碼是否已存在於歷史提交中，決定是否需要輪替該密碼並清理歷史。

---

如需要，我可以針對「R1 明文密碼清理」「R3 SDK DLL 參考修正」「R2 具名 Profile 架構」個別展開具體的程式碼變更計畫（本次僅做分析，未修改任何檔案）。

---
SESSION_ID: 96d9ddd9-351b-4dbe-85b7-367c72262df0
