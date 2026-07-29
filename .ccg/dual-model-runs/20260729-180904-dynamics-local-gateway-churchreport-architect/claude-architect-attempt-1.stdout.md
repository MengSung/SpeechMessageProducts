# Dynamics Local Gateway 與 ChurchReport 架構分析（實地稽核版）

> 稽核範圍：本次分析直接讀取程式（非憑空推論），涵蓋 `SpeechMessage.Dynamics.Gateway`、`SpeechMessage.Dynamics.WebApi`、`ChurchReport` 三個專案的目前 commit 狀態，並比對 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 契約與 `.ccg/tasks/dynamics-connection-compatibility/task.json`。以下所有檔案/行號引用皆為實際讀取結果。

---

## 0. 重要背景更正

git status 顯示 `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntime*.cs`、`OrganizationAdmissionRegistry.cs`、`ProfileRoutedOperationExecutor.cs` 等檔案已存在（未追蹤新檔）。這代表 **Multi-Profile Runtime Admission／Publication／Rollback 骨架已經實作**（crm82/crm91 各自獨立 Generation、共用 Canonical Organization 容量），並非空白起點。本分析的下一批切片建立在這個既有骨架之上，**不重做**隔離基礎設施。

---

## 1. 分級發現

### Critical（Local Gateway E2E 前必須解決）

| # | 發現 | 證據 |
|---|---|---|
| C1 | **ChurchReport 的 DynamicsAccess 綁定讀不到 ASP.NET Host 設定。** `DonationPaymentManager` 用自己的 `static ConfigurationBuilder`（`Models/DonationPaymentManager.cs:47-48`）只 `AddJsonFile("appsettings.json")`，接著把這個 **私有靜態** `m_Configuration` 傳給 `DonationDynamicsAccessBootstrap.CreateFeeFormService(m_ToolUtilityClass, m_Configuration)`（同檔 `:207`）。`appsettings.Development.json`、`appsettings.Production.json`、User Secrets（csproj 已設 `UserSecretsId`）、環境變數、command-line 全部不會生效。這代表**唯一的建構路徑**（`InMemoryDataContextSmallGroup` 是 `DonationPaymentManager` 的唯一 `new` 呼叫點，見下方 grep 結果）目前結構上不可能透過 Host 標準機制切換 Local Gateway endpoint 或 secret bridge。 |
| C2 | **crm82／sunnyvalechback 混淆風險：目前沒有 workload → alias 授權政策。** `GatewayWorkloadBoundaryTests.cs` 只驗證「未驗證」「未 mapping」「本文冒充身分」三種情境會被拒絕，**沒有**任何測試證明 `church-report-service`（`appsettings.json:18`）不能呼叫 `crm82`（`jesus.speechmessage.com.tw`，另一個教會組織）。`ControlledOperationExecutor.ExecuteAsync`（`ControlledOperationExecutor.cs:73-80`）只檢查 alias 是否存在 admission plan，完全不檢查 `WorkloadSubjectId` 是否被允許使用該 alias。**一旦把 ChurchReport 的 CE 9.1 profile 加進 Gateway catalog，在授權政策補齊前，任何能通過 workload 映射的呼叫者都能打任何 alias**，直接違反「Session/Credential/Profile 跨產品/跨組織零容忍」。 |
| C3 | **Gateway standalone Kestrel 的 `RequireAuthorization()` 沒有可用 handler。** `Program.cs:27-30`：`AddAuthentication(workloadAuthenticationScheme)`（預設 `IISDefaults.AuthenticationScheme`）**只設定 scheme 名稱，沒有註冊任何 handler**（沒有 `.AddNegotiate()`、沒有走 IIS in-process 才會生效的 `Microsoft.AspNetCore.Server.IIS` handler）。`SpeechMessage.Dynamics.Gateway.csproj` 沒有引用 `Microsoft.AspNetCore.Authentication.Negotiate`。Local Gateway（VS F5、純 Kestrel、非 IIS in-process）目前呼叫任何 `RequireAuthorization()` 端點會直接丟 `InvalidOperationException`（找不到 handler），這是 Local Gateway 完全無法啟動驗證的阻擋。 |
| C4 | **`approvedWebApiRoot` 洩漏 raw CRM endpoint。** `DynamicsWebApiClient.cs:373`：成功回應把 `approvedWebApiRoot = approvedRoot.Value.ToString()` 放進回給產品的 payload。這把 Gateway 內部才該知道的實體 CRM URL 透過 `/v1/organizations/{alias}/operations/{id}` 直接送到 ChurchReport，違反契約第 3 節「產品只知道 ExecutionMode、ProfileAlias…」的邊界宣告。 |
| C5 | **CE 9.1（`sunnyvalechback-prod`）尚未存在於 Gateway catalog。** `SpeechMessage.Dynamics.Gateway/appsettings.json` 的 `DynamicsProfiles:Profiles` 只有 `crm82`。ChurchReport 若切到 `ExecutionMode=Gateway`，`ProfileAlias=sunnyvalechback-prod` 目前一定得到 `NotReady`（因為 alias 不存在於 Runtime Manager 快照），這是預期的 fail-closed 行為，但代表 Local Gateway E2E **尚未有第二個 profile 可測**。 |
| C6 | **本機沒有 SQL Server Engine，但非 Testing 環境的 Gateway 啟動硬性要求 `ConnectionStrings:DynamicsControlPlane`。** `Program.cs:42-57`：非 Testing 環境缺少該連線字串直接 `throw`；`DynamicsGatewayReadinessService.StartAsync` 呼叫 `VerifySchemaAsync`（只驗證不建表）。這是**正確**的 fail-closed 設計，但意味著 Local Gateway 若想在 `Development` 環境完整跑起來（非 `Testing`），必須先有一個真的 SQL Server 執行個體與已建好的 control-plane schema——這不是程式缺陷，是**環境缺口**（見 Q5）。 |

### Warning

| # | 發現 | 證據 |
|---|---|---|
| W1 | `ControlledOperationExecutor.EstimateEnvelopeBytes`（`ControlledOperationExecutor.cs:140-161`）對非字串參數固定估 64 bytes（`:156`），巢狀物件／陣列／大型 dictionary 完全繞過 `MaxDispatchEnvelopeBytes` 防護。 |
| W2 | `InMemoryDataContextSmallGroup` 的每一個屬性（`ListManager`、`DonationPaymentManager` 等，共 11 處，`InMemoryDataContextSmallGroup.cs:549-1279`）用同一種模式：`_memoryCache.Get(key) == null` 判斷 + 手動 `Set`，**没有 `SizeLimit`／`options.Size`（全部被註解掉，例如 `:578-579`）**，也沒有 `IDisposable` eviction 處理——`DonationPaymentManager` 持有 `LineMessagingClient`（未實作 `IDisposable`，`Line.Messaging/LineMessagingClient.cs`）與 `_feeRefreshLock`（`SemaphoreSlim`），被逐出快取時兩者都不會被釋放。 |
| W3 | `GetCurrentSessionId()`（`InMemoryDataContextSmallGroup.cs:180-194`）在 Session 不存在時用 `DateTime.UtcNow.Ticks` 產生暫時 key（`:192`），每次呼叫都不同，造成 churn：同一個「無 Session」請求的多次屬性存取（例如 `DonationPaymentManager` 內對 `m_DonationDedicationFeeFormService` 多次呼叫）會各自建立新 Session、新 Manager、新 `LineMessagingClient`，且都进快取直到 30 分鐘過期。 |
| W4 | `DonationDynamicsAccessBootstrap` 的 `DonationDynamicsAccessProcessHost.GetOrCreate`（`DonationDynamicsAccessBootstrap.cs:535-575`）在設定改變時直接 `throw new InvalidOperationException("...Restart the host...")`（`:546-547`）。這是**故意的**設計（避免無界 provider 快取），但目前沒有測試證明「同一 process 內兩次不同設定呼叫」會被正確攔截，也沒有測試證明 shutdown 路徑（`DonationDynamicsAccessBootstrapLifetime.StopAsync`，`:600-603`）確實觸發 `ProcessHost.DisposeAsync()`。 |
| W5 | ChurchReport `appsettings.json` 內 `DynamicsAccess:Gateway:Endpoint` 寫死 `https://localhost:5101/`（`:565`），但 Gateway 專案實際 `launchSettings.json` 的 https profile 是 `https://localhost:7244`（已在「已確認現況」中點出）。這組不一致目前無測試偵測，一旦真的切換 `ExecutionMode=Gateway` 會直接連線失敗，且錯誤訊息不會指向設定不一致。 |

### Info

- `AdfsOAuthTokenProvider`、`DynamicsGatewayReadinessService`、`CapacityKeys`、`OrganizationAdmissionPlan` 的生命週期文件與繁體中文 XML 註解品質已經很高，可作為後續新型別的註解範本。
- `OrganizationAdmissionPlan.TryCreate` 對容量／租約時間的 fail-closed 驗證（`OrganizationAdmissionPlan.cs:76-155`）已經覆蓋大部分不變量，新 profile（CE 9.1）只需要提供符合形狀的設定，不需修改驗證邏輯本身。

---

## 2. 建議架構與生命週期流程

```
ChurchReport (ASP.NET Core Host)
  builder.Configuration  ──(DI: IConfiguration)──▶ InMemoryDataContextSmallGroup (Scoped)
                                                        │ 建構時取得
                                                        ▼
                                        new DonationPaymentManager(adapter, workflows, IConfiguration)
                                                        │ 呼叫
                                                        ▼
                                DonationDynamicsAccessBootstrap.CreateFeeFormService(utility, configuration)
                                                        │ (BindOptions 已支援 Gateway/Embedded 二選一，不必動)
                                                        ▼
                                     ProcessHost（process-level 單一 generation，已存在，不變）
                                                        │ ExecutionMode=Gateway 時
                                                        ▼
                        HTTP POST https://localhost:7244/v1/organizations/sunnyvalechback-prod/operations/{op}
                                                        │
                                                        ▼
Local Gateway (SpeechMessage.Dynamics.Gateway, Kestrel, Negotiate handler)
  Windows/Negotiate 驗證 IIS APPPOOL\ChurchReport 或 VS 開發身分
        │ ConfigurationWorkloadSubjectResolver 映射 → "church-report-service"
        ▼
  【新增】WorkloadAliasAuthorizationPolicy：church-report-service → 只能 {sunnyvalechback-prod}
        ▼
  ProfileRoutedOperationExecutor → DynamicsProfileRuntimeManager
        │ 解析 alias="sunnyvalechback-prod" → Generation N (CE 9.1, WebApi transport)
        ▼
  ControlledOperationExecutor（envelope bound 修正後）→ AdfsOAuthTokenProvider → DynamicsWebApiClient
        ▼
  Dynamics 365 CE 9.1 Web API（真實憑證缺席 → NotReady，不 fallback）
```

關鍵原則（沿用契約，不新增）：Central 與 Local 共用同一段 `Program.cs`／`AddSpeechMessageDynamicsProfiles`／`ControlledOperationExecutor`；差異只在 ChurchReport 的 `DynamicsAccess:Gateway:Endpoint` 指向 `localhost:7244` 還是中央網域，以及 Gateway 的 `DynamicsProfiles:Profiles` catalog 內容。

---

## 3. TDD 切片（依相依順序）

### Track A：Configuration Ownership／Local Gateway Contract E2E（無需先做 Track B）

**A1. 修正 ChurchReport 設定所有權**
- 檔案：`SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`（新增建構參數 `IConfiguration configuration`）、`SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`（新增建構子多載接受 `IConfiguration`，取代 `static m_Configuration` 讀取路徑；保留 static 欄位僅供舊相容呼叫點回退，但 DI 路徑一律優先使用注入值）。
- RED 測試（新檔 `SpeechMessage.Dynamics.Tests`或 ChurchReport 專屬測試專案，需先確認是否已有 `ChurchReport.Tests`；若無，這是本切片的前置動作）：以 `WebApplicationFactory<Program>` 建立 Host，覆寫 `ASPNETCORE_ENVIRONMENT=Development`，注入 `appsettings.Development.json` 含 `DynamicsAccess:Gateway:Endpoint=https://localhost:7244/`，斷言透過 DI 解出的 `IInMemoryDataContext.DonationPaymentManager` 內部組裝出的 `ProductDynamicsOptions.Gateway.Endpoint` 等於 Development 值而非 base `appsettings.json` 的 `5101`。目前必定 RED（因為讀的是 static 只載 base 檔的 Configuration）。
- 依賴：無（此切片獨立，最先做）。

**A2. 修正 `DynamicsAccess:Gateway:Endpoint` 與 Gateway 實際埠不一致**
- 檔案：`SpeechMessageProducts.ChurchReport/appsettings.json:565`（改為 `7244`，或改用 `appsettings.Development.json` 覆寫）。
- 測試：延伸 A1 的 TestServer 斷言，斷言 Endpoint 值與 Gateway `launchSettings.json` 的 https profile 一致（可用一個共用常數/設定檔比對，避免未來再度漂移）。
- 依賴：A1。

**A3. Gateway 端加入 CE 9.1 `sunnyvalechback-prod` profile**
- 檔案：`SpeechMessage.Dynamics.Gateway/appsettings.json`（新增 `DynamicsProfiles:Profiles:sunnyvalechback-prod`，`AuthMode=AdfsOAuth`、`CredentialSource=SecretReference`、真實憑證缺席時的 `SecretReference` 名稱先寫上，不寫密碼）；`appsettings.json` 的 `DynamicsGateway:WorkloadMappings` 已有 `church-report-service`，不需修改。
- RED 測試：擴充 `SpeechMessage.Dynamics.Tests/GatewayReadinessTests.cs`（或新檔 `MultiProfileCatalogTests.cs`）於 Testing 環境啟動 Gateway，斷言 `/ready` 回應包含 `sunnyvalechback-prod` profile 且因缺少真實 ADFS 憑證而回報 `NotReady`（不是 500，不是靜默略過）。
- 依賴：無新架構相依，但邏輯上應晚於 A1/A2（先確定產品端指向正確 endpoint 才測 catalog）。

**A4.（Critical C2 修復）新增 workload → alias 伺服器端授權政策**
- 新檔：`SpeechMessage.Dynamics.WebApi/Runtime/IWorkloadAliasAuthorizationPolicy.cs` + `WorkloadAliasAuthorizationPolicy.cs`（設定驅動：`DynamicsGateway:WorkloadAliasAuthorization:{workloadSubjectId} = ["alias1","alias2"]`），在 `ControlledOperationExecutor.ExecuteAsync` 取得 admission plan **之前**（`ControlledOperationExecutor.cs:73` 之前）插入檢查，未授權回傳 `DynamicsErrorCodes.Unauthorized`（若無此錯誤碼需新增）。
- RED 測試：擴充 `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`，新增 `Mapped_workload_cannot_call_alias_outside_its_authorized_set`：以 `church-report-service` 呼叫 `crm82`（jesus 組織），斷言 `Forbidden`，且 `RecordingExecutor.CallCount == 0`（在 admission/transport 之前擋下）。
- 依賴：A3（需要兩個 alias 存在才能證明「只能其一」）。**此切片是 Critical release blocker，必須在 Local Gateway 對外開放前完成。**

**A5.（Critical C4 修復）移除 `approvedWebApiRoot` 回傳洩漏**
- 檔案：`SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs:369-375`，把匿名物件改為只含 `operationId`、`ceVersion`、`data`。
- RED 測試：新增/擴充 `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`，斷言成功回應序列化後不包含 `approvedWebApiRoot` 欄位；並在 `GatewayWorkloadBoundaryTests.cs` 加一條 E2E 斷言（HTTP 回應 body 不含任何 `https://` CRM 網域字串）。
- 依賴：無，可與 A4 平行。

**A6.（Critical C3 修復）Local Gateway 開發身分**
- 見 Q3 專節，本節僅列切片：`SpeechMessage.Dynamics.Gateway.csproj` 新增 `Microsoft.AspNetCore.Authentication.Negotiate`；`Program.cs` 在非 IIS-in-process 情境下 `.AddNegotiate()`。
- RED 測試：新增 `SpeechMessage.Dynamics.Tests/GatewayKestrelNegotiateTests.cs`，用 `WebApplicationFactory` + `TestServer` 建立**未經 IIS** 的 Host，斷言對 `/v1/...` 端點的未認證請求得到 `401` 並帶 `WWW-Authenticate: Negotiate`（證明 handler 真的註冊，不是啟動就炸）。
- 依賴：無，可最先做（阻擋 A3/A4 的 E2E 驗證方式）。

**A7. Local Gateway 契約 E2E（收斂測試，Track A 完成後才有意義）**
- 新檔：`SpeechMessage.Dynamics.Tests/LocalGatewayChurchReportEndToEndTests.cs`：起一個 Testing 環境 Gateway TestServer（含 A3 的 `sunnyvalechback-prod` profile、A4 授權政策、A6 Negotiate），模擬 ChurchReport 的 `Package01FeeReadClient` 呼叫，斷言：(1) `church-report-service` 可呼叫 `sunnyvalechback-prod` 成功路徑（mock transport）；(2) 呼叫 `crm82` 被拒；(3) 回應不含 `approvedWebApiRoot`。
- 依賴：A3、A4、A5、A6 全部完成。

### Track B：Session／Resource Ownership Hardening（與 Track A 平行，無交叉相依）

**B1. `InMemoryDataContextSmallGroup` 快取加上驅逐時 Dispose**
- 檔案：`InMemoryDataContextSmallGroup.cs`，對 `DonationPaymentManager` 屬性（`:1180-1223`）的 `PostEvictionCallbacks`（目前只 `Set` 一個 `ManualResetEvent`，`:561-575` 模式重複 11 次）新增：若逐出值實作 `IAsyncDisposable`/`IDisposable` 則呼叫。
- RED 測試：新檔 `ChurchReport.Tests/InMemoryDataContextEvictionDisposalTests.cs`（若無 ChurchReport 測試專案，此為前置動作）：手動 `Compact(1.0)` 強制逐出後，斷言 `DonationPaymentManager` 內的 `_feeRefreshLock`／`LineMessagingClient` 已釋放（先讓 `LineMessagingClient` 實作 `IDisposable`，見 B2）。
- 依賴：B2 需先行（否則沒有可觀察的 Dispose 行為）。

**B2. `LineMessagingClient` 補上 `IDisposable`**
- 檔案：`Line.Messaging/LineMessagingClient.cs`（需先讀取確認其內部是否持有 `HttpClient`；若有，實作 `IDisposable` 釋放它）。
- RED 測試：`Line.Messaging.Tests/LineMessagingClientDisposalTests.cs`（新檔），斷言 Dispose 後底層 HttpClient handler 被釋放（可用弱參考或 handler 存活旗標斷言，屬於 Leak/Soak 類別測試）。
- 依賴：無，可最先做（B1 依賴它）。

**B3. 修正無 Session 時的 churn key**
- 檔案：`InMemoryDataContextSmallGroup.cs:180-194`，把 `NOSESSION_...Ticks` 改為**同一請求內**穩定的 key（例如綁定到 `HttpContext.TraceIdentifier` 或直接對「無 Session」情境回傳一個**不進 MemoryCache**的短生命週期物件，而不是每次呼叫都建新 key 進快取）。
- RED 測試：新檔 `ChurchReport.Tests/NoSessionCacheChurnTests.cs`：模擬 `HttpContext.Session == null`（或 Session 中介軟體未啟用）情境下，同一個 `HttpContext` 內對 `DonationPaymentManager` 存取兩次，斷言快取只增加 **0 或 1** 個 entry（而非每次呼叫都新增）。
- 依賴：B1（需要先有可觀察的 Dispose/計數機制才能斷言「沒有無界累積」）。

**B4. `DonationDynamicsAccessProcessHost` 設定變更與 shutdown 的確定性測試**
- 檔案：`DonationDynamicsAccessBootstrap.cs`（不改行為，只補測試證明既有 fail-fast 設計正確）。
- RED 測試：新檔 `ChurchReport.Tests/DonationDynamicsAccessProcessHostTests.cs`：(1) 兩次不同 `ProductDynamicsOptions` 呼叫 `GetOrCreate` 斷言擲出 `InvalidOperationException`；(2) 呼叫 `DonationDynamicsAccessBootstrapLifetime.StopAsync` 後，`ServiceProvider` 確實被 Dispose（可用 `IDisposable` spy 服務注入驗證）。
- 依賴：無，可平行於 A/B 其他項目最先做。

**依賴總覽（拓樸序）：**
```
B2 → B1 → B3          A6 (獨立)
B4 (獨立)              A1 → A2
                       A3 → A4 → A7
                       A5 (獨立, 併入 A7)
```

---

## 4. 針對個別問題的具體回答

**Q2（ChurchReport 用 IConfiguration，同時維持 process-level 唯一擁有者）**：A1 切片已給出方案——`IConfiguration` 只從 `InMemoryDataContextSmallGroup`（DI Scoped）往下傳到 `DonationPaymentManager` 建構子，**不**改變 `DonationDynamicsAccessBootstrap.ProcessHost` 的 process-level 快取設計；`ProcessHost.GetOrCreate` 的 generation-key 機制本身就是「同一 process 內設定不可變、變更需重啟」的正確 owner 邊界，不需要也不應該变成 per-request 或 per-session。

**Q3（Local Kestrel 開發身分）**：加 `Microsoft.AspNetCore.Authentication.Negotiate` 套件並在非 IIS in-process 時 `.AddNegotiate()`，這是 Microsoft 官方支援、**不弱化生產**（Negotiate/Kerberos/NTLM 是同一套 Windows 身分機制，只是繞過 IIS 的 in-process 整合），且可被 `WebApplicationFactory`/`TestServer` 用假 Negotiate handler 替換來測（A6 切片）。**不要**用「開發環境跳過驗證」之類的旗標——那會弱化 Production 且違反硬性條件。

**Q4（CE 9.1 alias 加入方式）**：A3+A4 已給出：只在 Gateway 部署擁有的 `appsettings.json`（或未來的中央 secret manifest）新增 profile 區塊，只填 `SecretReference` 名稱；`ApplyTestingEndpointFallback`（`Program.cs:207-231`）已保證非 Testing 環境缺 Endpoint 會 fail closed；真實 ADFS 憑證缺席時，`AdfsOAuthTokenProvider` 沒有可用的 `RefreshToken`/`CredentialReferenceName` 會在 `BuildTokenForm`（`AdfsOAuthTokenProvider.cs:233-236`）擲出例外，profile 保持 `NotReady`——**這條路徑已存在**，只需要真正把 profile 定義加進去並用 A4 授權政策確保不誤用 crm82。

**Q5（本機無 SQL Server Engine）**：`SqlRuntimeHostSlotCoordinator` 是 durable fencing 的正式要求，**不可**用 in-memory coordinator 假裝完成（違反硬性條件 #2 精神延伸與 #7）。可行選項：(a) 在本機安裝 SQL Server Express/LocalDB 並執行 control-plane schema 腳本（如果 repo 有 schema migration 腳本，需另外確認其存在與位置；若沒有，這本身是一個先行子任務）；(b) 若 D365APP01 上有非生產用途的 SQL Server 執行個體且網路可達，可作為 Local Gateway 開發用的 control-plane 目標，但**必須**是獨立於任何生產資料庫的 schema/database，且需要先完成 WinRM/認證與網路連通性驗證（目前尚未 elevated、尚未 WhoAmI，見已確認現況），這屬於**環境準備**而非程式修改，不應在本輪程式碼變更中假設它已可用。**這是 Phase 5/6 gate，不是本輪 TDD 切片可以關閉的項目**（見第 6 節）。

**Q6（`DonationPaymentManager`／MemoryCache／`LineMessagingClient`／churn key）**：見 Track B（B1-B4）。

**Q7（`ControlledOperationExecutor` 批次修正與 release blocker）**：
- Release blocker（Local Gateway E2E 前必須）：A4（授權政策）、A5（回應洩漏）。
- 可延後但需排入路線圖（不阻擋本輪 Local Gateway 開發驗證，但阻擋**正式**上線）：W1（真實 byte-bound，用 `System.Text.Json.JsonSerializer.SerializeToUtf8Bytes` 量測而非固定估算）、queue retention 的 durable audit/idempotency ledger（目前完全未實作，屬於 Phase 5/6）。

**Q8**：見第 5、6 節。

---

## 5. 驗證矩陣

| 類別 | 對應切片 | 驗證方式 |
|---|---|---|
| 單元測試 | A4, A5, B1-B4 | `dotnet test SpeechMessage.Dynamics.Tests` / 新建 `ChurchReport.Tests` |
| TestServer（Gateway） | A3, A6, A7 | `WebApplicationFactory<Program>`（Gateway 專案），`Testing` 環境 |
| TestServer（ChurchReport） | A1, A2 | `WebApplicationFactory<ChurchReport.Program>`（若 ChurchReport 尚無測試專案，這是前置建置動作） |
| localhost 真實埠 | A2, A7 | 手動或 CI 腳本以 `dotnet run` 起兩個 process，curl `https://localhost:7244/ready` 與 ChurchReport 頁面 |
| WinRM／VM（CE 8.2/9.1 real-server） | Q5 環境準備、A3 之後的真實憑證接通 | 需先 elevated shell 完成 `D365APP01`/`D365DC01` 的 WinRM 認證與 WhoAmI，**目前不可宣稱通過** |
| Leak／Soak | B1-B4 | 延伸既有 `Phase4IsolationSoakTests.cs`、`DynamicsHttpTransportSocketSoakTests.cs` 模式 |
| Performance | 暫緩 | 本輪不引入新效能測試（見第 6 節「不擴張項目」） |

---

## 6. 尚不能宣告完成的 Phase 4～6 Gate

1. **CE 8.2／9.1 real-server 憑證與 WhoAmI 證據**（`D365APP01`/`D365DC01` 尚未 elevated、未完成 authenticated remote command）——沒有這個，`sunnyvalechback-prod` profile 永遠只能停在 `NotReady` 的單元/TestServer 驗證層級，不能宣稱「CE 9.1 Web API 已驗證」。
2. **Durable SQL control-plane 在本機／Local Gateway 開發環境的真實安裝**——`SqlRuntimeHostSlotCoordinator.VerifySchemaAsync` 需要真正可連線的 SQL Server；本機目前沒有引擎，這是 Phase 5 gate。
3. **Data8／CE 8.2 WS-Trust 路徑的長期替代方案驗證**（Web API v8.2 OAuth 或官方 `CrmServiceClient`）——本輪完全不觸碰，維持現狀。
4. **Durable audit intent／idempotency ledger 與 retention**——尚未設計，不屬於本輪 Local Gateway E2E 範圍，但必須在**正式**多產品上線前完成。
5. **Fair dispatch／starvation bound 的跨 workload 公平性驗證**——`OrganizationAdmissionPlan` 已有 `MaxInFlightAndQueuedPerWorkload`，但沒有測試證明多 workload 競爭同一 alias 時不會饑餓，屬於 Phase 6。
6. **Embedded trust／in-memory coordinator**——依硬性條件 #2，本輪不得為了通過而啟用，明確標記為排除。

---

## 7. 本輪不應擴張的項目

- **不要**開始 Data8 移除或 CE 8.2 Web API OAuth 遷移（契約明確標記為獨立、需真機驗證後才能動）。
- **不要**引入效能／壓測基礎設施變更（`AggregateMaxInFlight` 等數值調整）——目前的數字是已驗證的 fail-closed 預設，本輪只修正洩漏與授權缺口，不動容量參數。
- **不要**把 `WorkloadAliasAuthorizationPolicy`（A4）做成通用 RBAC 框架——先用設定驅動的簡單清單滿足「church-report-service 只能碰 sunnyvalechback-prod」，避免為五到十個未來產品過度設計尚未出現的需求。
- **不要**同時重構 `InMemoryDataContextSmallGroup` 的 11 個重複快取屬性成單一泛型方法——Track B 的目標是修漏洞（Dispose／churn key），不是這次做「消除重複程式碼」的重構（會混淆 RED/GREEN 對應關係，且不是本輪授權範圍）。

---
SESSION_ID: 7ca1e106-2b6b-4ace-b4e2-c2010b008a5d
