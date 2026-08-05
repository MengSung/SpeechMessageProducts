# P5 Dedicated Gateway 對齊分析報告

## 審查範圍
提交範圍 `18f273b0..7d984981`（"新增 Dedicated Gateway 模式與 Data8 專用初始化" + "重構 ChurchReport Data8 Runtime 資源管理"），涵蓋 `Data8ProfileRuntime`、`DedicatedGatewayOptions`、`DedicatedGatewayData8Configuration`、`DedicatedData8RuntimeHostedService`、Gateway `Program.cs`、ChurchReport `EmbeddedData8Runtime`／launchSettings／appsettings，以及對應測試。工作目錄本身無未提交變更（僅有 `.ccg/` 分析暫存檔），故以此提交範圍作為「P5 變更」。`.trellis/tasks/08-05-dedicated-gateway-alignment/task.json` 狀態為 `in_progress`，PRD 驗收清單全部未勾選，符合尚在開發中的現況。

---

## Critical

### C1. Gateway 以 `DedicatedGateway` 啟動設定啟動時，會在啟動期直接擲例外中止（WorkloadBindingSet 與 configuredProfileAliases 不匹配）

- `SpeechMessage.Dynamics.Gateway/Program.cs:140-170`：Dedicated 分支僅將 `configuredProfileAliases` 設為 `[dedicatedOptions.ProfileAlias]`（即 `"sunnyvalechback"`），並以此建立 `ConfigurationGatewayOperationAuthorizer`。
- `SpeechMessage.Dynamics.Gateway/appsettings.Development.json:20-34`：`DynamicsGateway:ActiveWorkloadBindingSet = "Local"`，`WorkloadBindingSets:Local` 唯一 binding 的 `ProfileAliases` 是 `["crm82"]`。此檔案未被新的 `DedicatedGateway` launchSettings profile 覆寫（該 profile 只設定 `DynamicsGateway__DeploymentMode`、`Dedicated__ProfileAlias`、`DedicatedGateway__Data8__*`、`DynamicsConnectionManagement__*`，未觸及 `ActiveWorkloadBindingSet` 或 `WorkloadBindingSets`）。
- `SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs:52-58`（`_canonicalProfileAliases` 只含 constructor 傳入的 `configuredProfileAliases`）與 `:96-99`／`:310-346`（`ReadCanonicalList` 對每個 binding 的 `ProfileAliases` 做 `canonicalValues.TryGetValue` 查找，找不到即 `throw new InvalidOperationException($"unknown {valueKind} '{requestedValue}' is not allowed.")`）：因為 `"crm82"` 不在 Dedicated 模式的 `{"sunnyvalechback"}` 集合中，建構子必定拋出。
- `GatewayOperationAuthorizationStartupValidator`（同檔 `:444-463`）以 constructor injection 強制在 Host 啟動、開始接流量前 materialize `IGatewayOperationAuthorizer`；因此此例外會在 Generic Host `StartAsync` 階段中止整個進程。

**失敗情境**：依 VS「Multiple Startup Projects」把兩個專案都指到各自的 `DedicatedGateway` launch profile 並按 F5，`SpeechMessage.Dynamics.Gateway` 會立刻丟出 `InvalidOperationException: unknown profile alias 'crm82' is not allowed.` 而無法監聽 `https://localhost:7244/`。P5 的核心 F5 情境目前無法成立。

### C2. 即使修好 C1，ChurchReport 的 `DedicatedGateway` launch profile 也不會實際呼叫 Gateway（`Package01FeeReadsEnabled` 未一併開啟）

- `SpeechMessageProducts.ChurchReport/Properties/launchSettings.json:22-28`：`DedicatedGateway` profile 只設定 `DynamicsAccess__ConnectionMode`、`ProfileAlias`、`Gateway__Endpoint`，未設定 `DynamicsAccess__Package01FeeReadsEnabled`。
- `SpeechMessageProducts.ChurchReport/appsettings.Development.json:9`：`Package01FeeReadsEnabled: false`，未被上述 profile 覆寫。
- `SpeechMessageProducts.ChurchReport/Services/DynamicsGatewayPreflightHostedService.cs:87-111`：`ConnectionMode != Embedded` 時走 else 分支，`if (!DonationDynamicsAccessBootstrap.IsPackage01Enabled(_configuration)) { return; }` —— 直接 no-op，不建立 executor、不呼叫 Gateway。此行為與既有測試 `DynamicsGatewayPreflightHostedServiceTests.Flag_false_is_a_strict_no_op_before_executor_or_http_creation`（`:30-40`）一致，證明是既有（非 P5 新增）設計，但 P5 新增的 `DedicatedGateway` profile 沒有一併打開這個旗標。
- `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs:63-94`、`:100-116`：`CreateFeeFormService`／`TryCreatePackage01Client` 同樣以 `IsPackage01Enabled` 短路，false 時回退既有 `ToolUtility`／`Startup.cs:336-387` 註冊的 `ICrmConnectionPool`（與 `ConnectionMode` 完全無關）。

**失敗情境**：用 `DedicatedGateway` profile 啟動 ChurchReport，其啟動與執行期行為與預設 `ChurchReport` profile **完全相同**——不會對 7244 發出任何 HTTP 呼叫，也不會執行受控 WhoAmI 驗證。這與 `design.md`（"Gateway 不可用時，ChurchReport preflight 會在其有界 timeout 內 fail closed"）及 PRD FR1 的預期直接矛盾，屬於設計文件與程式碼不一致。

---

## Warning

### W1. `/ready` 在 Dedicated 模式下的 `profile` 欄位恆為 `"active"`，無法反映真正的就緒狀態
`SpeechMessage.Dynamics.Gateway/Program.cs:233-245`：`profile = runtime.Executor is not null ? "active" : "not-ready"`。`Data8ProfileRuntime.Executor` 是建構子中必定賦值的非空屬性（`Data8ProfileRuntime.cs:82`），而 `Data8ProfileRuntime` 本身已透過 `DedicatedData8RuntimeHostedService` 的 constructor injection 在 Host 啟動時強制建立（`Program.cs:157`、`DedicatedData8RuntimeHostedService.cs:16-17`）。因此只要進程存活，這個欄位永遠是 `"active"`，不像非 Dedicated 分支（`Program.cs:246-283`）會回報真實的 `Admission.InFlight/Queued/ActivePermits/HostSlotReady` 並可回 503。屬於可觀測性弱化，不是資源或安全缺陷。

### W2. P5 未依 PRD/實作計畫更新 Visual Studio 多啟動專案文件
`prd.md` FR7 明確要求「Visual Studio 開發文件必須說明一次性設定『Multiple startup projects』」，`implement.md` 第 5 步也列出要修改 `docs/dynamics-connection-management-plan.md`、`docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`。實際 `git diff --name-status 18f273b0..7d984981` 中沒有任何 `docs/*.md` 變更。缺少文件會放大 C1／C2 這類「兩份設定檔語意需同步」但無編譯期防呆的風險。

---

## Info

### I1. Embedded／Dedicated 的 runtime、pool、admission、client、permit 隔離設計正確
- `Data8ProfileRuntime.cs:20-73`：每個 host（Embedded 經 `ChurchReport/Services/EmbeddedData8Runtime.cs:31-41`；Dedicated 經 `Program.cs:147-152`）各自 `new Data8ProfileRuntime(...)`，各自擁有 `Data8ConnectorPoolRegistry`、`OrganizationAdmissionManager`；沒有 static/shared 欄位可跨 Profile、Organization 或模式外洩。
- `DisposeCoreAsync`（`Data8ProfileRuntime.cs:98-108`）先 drain/dispose pool、再 dispose admission，失敗聚合但不中斷後續 cleanup，且以 `_disposeGate` + `_disposeTask` 保證 idempotent（可安全被 Host StopAsync 與 DI ServiceProvider 兩次呼叫）。
- Credential 只存在於 host-owned `Data8OnPremiseConnectionSettings`／`OnPremiseData8ConnectorClientFactory`（`DedicatedGatewayData8Configuration.cs:64-68`、`CrmConnectionEmbeddedProfileMapper.cs:134-177`），未見寫入 request、pool key、log 或 static 欄位。

### I2. Dedicated 正確排除 Official Worker 與 SQL coordinator，改用 In-Memory host slot coordinator
`Program.cs:42-47`（`OfficialWorkerDeploymentConfiguration.TryAddAdjacentOverlay` 只在 `!isDedicatedGateway` 執行）、`Program.cs:159-166`（`AddSpeechMessageDynamicsOfficialWorkers` 只在 else 分支）、`Program.cs:178-193`（`AddSqlRuntimeHostSlotCoordinator`／`DynamicsGatewayReadinessService` 只在 `!isDedicatedGateway` 註冊）、`Data8ProfileRuntime.cs:56-59`（固定 `new InMemoryRuntimeHostSlotCoordinator()`，`MaximumRuntimeHosts=1`）均與 P5 設計文件相符。

### I3. Dedicated HTTP pipeline 的既有保護維持完整
Development-only HTTPS loopback 檢查（`Program.cs:207-223`）、Development 固定註冊真正的 Negotiate handler（`Program.cs:95-102`）、`RequestGuard` singleton 於任何 executor/admission 前執行（`Program.cs:174-176,351-357`）、no-store header（`Program.cs:197-205,417-420`）、POST handler 依 `isDedicatedGateway` 傳入 `RequestOrigin.DedicatedGateway`（`Program.cs:351-353`）均已到位，未見退化。

### I4. 測試缺口：目前沒有任何測試會捕捉 C1／C2 這類「跨檔案設定語意不一致」的整合缺陷
既有新增測試（`Data8ProfileRuntimeTests`、`DedicatedGatewayOptionsTests`、`CrmConnectionEmbeddedProfileMapperTests` 新增案例）都只驗證單一元件的組態綁定與隔離邏輯（例如 `CrmConnectionEmbeddedProfileMapperTests.cs:200-226` 只斷言 launchSettings JSON 的字面值，不會真的啟動 Gateway 或 ChurchReport 進程）。`implement.md` 第 3、5 步驟原規劃「Dedicated host 設定测試」「gateway unavailable bounded failure」等案例，但實際 diff 未包含任何會建構 `ConfigurationGatewayOperationAuthorizer` 或跑過 `Program.cs` top-level 組裝路徑的測試，因此 C1（WorkloadBindingSet × configuredProfileAliases 交集）與 C2（Package01 旗標與 ConnectionMode 耦合）都不會被 CI 攔截。

---

## 結論與建議行動
1. **先修 C1**：讓 `SpeechMessage.Dynamics.Gateway` 的 Dedicated 啟動設定擁有自己的 `WorkloadBindingSets`（`ProfileAliases` 含 `"sunnyvalechback"`），或改由 `DedicatedGateway` launchSettings profile 覆寫 `ActiveWorkloadBindingSet`/`WorkloadBindingSets`，並補一個會實際建構 `ConfigurationGatewayOperationAuthorizer`（或跑 `WebApplicationFactory` 以 Dedicated 設定啟動）的測試，鎖死這條路徑。
2. **再修 C2**：決定 ChurchReport 的 `DedicatedGateway` launch profile 是否也要開 `DynamicsAccess__Package01FeeReadsEnabled=true`，或另立一個不依賴 Package01 旗標、專門驗證 Dedicated 連通性的 F5 preflight；並更新 `design.md`／`prd.md` 使文件與程式碼一致。
3. 補齊 PRD FR7 要求的 Visual Studio 多啟動專案文件（W2），內容應明確標註 C1／C2 的相依設定，避免下一位開發者重複踩坑。
4. W1 可視優先度延後：若要在 Dedicated `/ready` 提供有意義訊號，需讓 `Data8ProfileRuntime` 或 `Data8ConnectorPoolRegistry` 暴露輕量診斷（不影響本次任務範圍）。

未發現 deterministic disposal、ServiceProvider、pool/permit/CTS/timer/task 或 cookie/credential/session 保留方面的 Critical/Warning 問題（見 I1）。

---
SESSION_ID: 054bb3b7-b053-426f-ae09-f2f25397a4f7
