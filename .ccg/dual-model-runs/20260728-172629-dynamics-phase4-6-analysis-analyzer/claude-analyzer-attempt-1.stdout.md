# Dynamics Phase 4-6 實作分析報告

**分析範圍**：no-SDK Dynamics 365 Gateway/Embedded 專案群（`SpeechMessage.Dynamics.*`）against `prd.md` / `design.md` / `implement.md` / `phase0-runtime-capacity-adr.md` / `phase4-isolation-hardening-verification.md`
**方法**：直接讀取原始碼 + 4 個平行只讀探索 + 現場執行 `dotnet test` 驗證一項關鍵宣稱
**結論（先講重點）**：Phase 4 的隔離/生命週期硬化（本機、單行程）確實完成且有紅綠證據；但 Phase 4 的**跨主機（cross-host）**要求、Gateway 認證、Phase 6 的強制閘門**全部尚未實作**。目前若宣稱「Phase 4/5/6 已完成」是不誠實的。

---

## 一、Critical 發現

### C1. Gateway 完全沒有身分驗證中介軟體，仍信任 caller 傳入的 `WorkloadSubjectId`
- `SpeechMessage.Dynamics.Gateway\Program.cs:117-133`：`OperationHttpRequest.WorkloadSubjectId` 直接來自 JSON body，僅在空白時退回 `"anonymous-scaffold"`；程式碼自己的註解承認「scaffolding 先允許 body 傳入…不可當成安全模型」。
- 全專案 `grep ClaimsPrincipal|WindowsIdentity|HttpContext.User` 在 Gateway/WebApi/Embedded 中零命中；`Program.cs` 沒有任何 `AddAuthentication`/`AddNegotiate`/`UseAuthentication`/`UseAuthorization`。
- `SpeechMessage.Dynamics.WebApi\Capacity\OrganizationAdmissionManager.cs`（`NormalizeWorkload`, `_workloadCounts`）直接把這個 caller 控制的字串當成容量計費/佇列公平性的 key。
- **已有紅測試證明此缺口**：`SpeechMessage.Dynamics.Tests\GatewayWorkloadBoundaryTests.cs`（未提交、新檔案）明確寫出目標行為（未驗證拒絕、未對映拒絕、body 覆寫拒絕、僅信任伺服器對映身分）。我現場執行：
  ```
  dotnet test --filter FullyQualifiedName~GatewayWorkloadBoundaryTests
  失敗: 4，通過: 0，總計: 4
  ```
  失敗原因是 `OptionsValidationException`（缺少 `DynamicsGateway:AuthenticationScheme`/`WorkloadMappings` 相關設定與中介軟體），證實這是**尚未實作**而非測試誤判。
- **後果**：任何能連到 Gateway port 的呼叫者可任意挑選 `WorkloadSubjectId`，直接進入 admission 與 CRM dispatch — 這正是 PRD「Unauthenticated/unmapped callers must fail before admission/CRM」的直接違反，也是 zero-tolerance session/tenant leakage 條款的違反（一個 workload 可冒充另一個 workload 的佇列配額/稽核歸屬）。

### C2. 完全沒有 durable cross-host coordinator；`RequireDurableHostCoordinator` 是死開關
- 唯一實作 `IRuntimeHostSlotCoordinator` 的類別是 `SpeechMessage.Dynamics.WebApi\Capacity\InMemoryRuntimeHostSlotCoordinator.cs`，`IsDurable => false`（第 25 行），檔頭註解自承「只能保證同一個進程內」。
- `Gateway\Program.cs:96` 與 `Embedded\DependencyInjection\EmbeddedServiceCollectionExtensions.cs:142` 都硬編 `RequireDurableHostCoordinator = false`，且**不是**從 config 讀取（無法透過 appsettings 切換）。
- `OrganizationAdmissionManager.cs:77-81` 雖然有 `if (_plan.RequireDurableHostCoordinator && !_slotCoordinator.IsDurable) throw` 的保護，但因為系統中不存在任何 `IsDurable => true` 的實作，把旗標打開只會讓服務永遠啟動失敗 — 這不是一個可切換的能力，是一個從未被建置的能力。
- **完全沒有 `AdmissionEpoch`、fencing、quarantine 概念的程式碼**（`grep Quarantine`/`AdmissionEpoch` 在 `.cs` 中零命中，僅存在於設計文件）。Fencing token 存在但只是進程內 `Interlocked.Increment`，跨行程/跨機器毫無意義。
- `SpeechMessageDynamicsControlPlane`（使用者提出的 SQL Server 控制平面資料庫）**在程式碼中完全不存在** — 純粹是提案階段，尚無任何 SqlConnection/DbContext 程式碼。
- **後果**：只要部署 ≥2 個 Gateway/Embedded replica（PRD 明文要求正式環境至少 2 個 Gateway replica），`MaximumRuntimeHosts` 與 `AggregateMaxInFlight` 的組織級預算完全無法跨進程強制執行 — 這是 Phase 2/3/4 的核心不變量，目前只在單一進程內成立。

### C3. Idempotency Ledger（ADR-003）與 Audit Intent Reservation（ADR-004）完全未實作
- 全 repo 對 `IdempotencyLedger`、`OutcomeUnknown`、`AuditIntent`、`OperationDefinitionRevision` 的 `.cs` 搜尋**零命中**（僅設計文件與 `.ccg` 審查記錄提及）。
- `ControlledOperationExecutor.cs`（寫入路徑執行器）沒有任何 ledger 相依性注入。
- **後果**：目前任何「write」能力若進入 operation registry，將沒有任何機制防止重試造成重複寫入、也沒有 crash-safe 稽核保留。目前系統事實上只安全地支援唯讀（Package 1 Fee Reads），這與現況吻合，但也表示 ADR-003/004 不是「已完成待整合」，而是「尚未開始」。

### C4. `GatewayHttpClientFactory` 靜態字典：全域生命週期，Reload 時無法置換/處置
- `SpeechMessage.Dynamics.ProductClient\Gateway\GatewayHttpClientFactory.cs:22-70`：`static readonly ConcurrentDictionary<string, HttpClient> Clients`，以 Gateway endpoint authority 為 key，`GetOrAdd` 後永不移除/處置/加上限。
- ChurchReport 手動 bootstrap（`DonationDynamicsAccessBootstrap.cs:332`）直接呼叫此靜態工廠；同時 `AdfsOAuthTokenProvider.cs` 走標準 `IHttpClientFactory` DI — **repo 內同時存在兩套不同的 HttpClient 生命週期治理模型**，違反 design.md §7.2「ProfileRuntime 是唯一 owner、no factory/caller shares handler」的單一所有權原則（雖然此靜態工廠不是 CRM profile runtime 本身，是 Product→Gateway 這一段，但同樣的所有權/處置紀律應該一致套用）。
- 同檔案旁的 `DonationDynamicsAccessBootstrap.cs:42-43` 的 `EmbeddedProviders`（`ConcurrentDictionary<string, IServiceProvider>`）也是同樣模式：process-level 靜態快取、無 reload/drain、無處置路徑。若 Embedded 設定變更（例如 profile 輪替），舊的 `IServiceProvider`（含其 `HttpClient`/handler）永遠不會被 dispose，直接違反 design.md §7.3 replace-and-drain 的「retired generation 必須歸零」硬性要求。
- **後果**：不是立即記憶體洩漏（endpoint/cache-key 數量有限），但這是「無法在不重啟行程的情況下安全輪替憑證/端點」的設計缺陷，且與 Phase 4 soak/disposal 測試矩陣的精神直接衝突。

### C5. Phase 6 的 no-SDK 閘門仍是 report-only，且未被任何 CI 擋下
- `eng\Verify-NoDynamicsSdk.ps1` 預設（未帶 `-FailOnFindings`）回傳 exit 0；`eng\no-sdk-source-roots.json` 本身標記 `"mode": "report-only"`。
- 唯一的 CI workflow `.github\workflows\toolutility-tests.yml:27-30` 執行此腳本時用 `continue-on-error: true` + `-SummaryOnly`，**無法讓 build 失敗**，且此 workflow 只建置/測試 `ToolUtility.Tests`，完全不建置/測試任何 `SpeechMessage.Dynamics.*` 專案。
- 目前真實違規（非計畫中的臨時清單項目，而是**現存於 production 路徑**的）：
  - `SpeechMessageProducts.ChurchReport.csproj:112-113`：`Microsoft.Crm.Sdk.Proxy` 直接 HintPath 指到 repo 外的 `Dynamics 365 SDK DLL` 資料夾。
  - `SpeechMessageProducts.sln` 仍包含 `PowerPlatform.Dataverse.Client` 專案；`ToolUtility.csproj:53` 仍有 `ProjectReference` 指向它。
  - `SpeechMessageProducts.ChurchReport\Startup.cs:302-328`：硬編預設帳號 `SPEECHMESSAGE\Administrator` 與正式 SOAP endpoint URL 作為 `ICrmConnectionPool`/`CrmConnectionPool` 的後備值（fallback），這正是使用者點名的「raw CRM fallback credentials」。
  - 掃描清單漏了 `.ccg\diagnostics\LegacySoapProbe`（`excludedRelativePaths` 排除 `.ccg`），但該工具專案確實引用 SDK proxy DLL — manifest 覆蓋率有缺口。
- **後果**：即使今天把旗標打開變成 mandatory gate，也會立即因上述真實違規而炸開，證明目前完全不具備宣稱 Phase 6 完成的條件；同時因為未接進 CI，任何人都可能無聲無息地重新引入 SDK 相依。

---

## 二、Warning 發現

### W1. `AllowLocalDevPasswordGrant` 有隱性自動啟用路徑，貼近 ROPC 禁令邊界
`DonationDynamicsAccessBootstrap.cs:210-224`：當 `AuthMode=AdfsOAuth` 且 `ManifestOrRegistrySource=local-dev-manifest` 且沒有 `CredentialReferenceName` 時，**自動**把 `AllowLocalDevPasswordGrant` 設為 `true`，橋接 `CrmConnection:Password` 進 password grant。這是刻意設計的本機開發後備（`phase3-tier-a-ifd-auth-blocker.md` 證實 ADFS authorization_code 尚未在此環境完成註冊），但設計文件明確寫「Do not fall back to ROPC」「no password... by default」。目前用 `local-dev-manifest` 字串守門，但這個守門值本身可被 config 任意設定；建議在程式碼層面加上環境/組態強制檢查（例如 `IsDevelopment()` 或明確的 non-production 旗標），而非只靠字串比對，避免正式環境誤用同一個 `ManifestOrRegistrySource` 值。

### W2. Embedded 的 manifest/registry 信任驗證完全未實作
探索確認 `SpeechMessage.Dynamics.Embedded` 目前只有 `EmbeddedServiceCollectionExtensions.cs`，沒有簽章 manifest 驗證或 registry 查核程式碼。這代表 design.md §4.1「Embedded 必須對簽章 manifest/中央 registry 驗證後才能解析 secret/admission slot，否則 fail closed remains NotReady」目前**沒有任何強制**；`ManifestOrRegistrySource` 只是一個字串旗標，決定要不要橋接本機秘密，並非信任錨點驗證。

### W3. Phase 5 讀取流程已上線但缺 parity/rollback 自動化證據
`Package01FeeReadsEnabled`（`ChurchReport\appsettings.json` 現為 `false`）已正確接到 `DonationFeeQueryService` 並可在 Gateway/Embedded 間切換，且 `phase3-enablement-rollback.md` 提供了完整的人工 rollback runbook — 這部分做得紮實。但搜尋 `Package01FeeRead|Parity|Rollback` 於所有測試檔案，**找不到任何自動化 legacy-vs-Package01 parity 測試**，只有 client DTO 解析的單元測試。design.md §11.1 明確要求「shadow read/compare」；目前 parity 證明完全依賴 `phase3-tier-a-enablement-checklist.md` 的手動比對步驟。

### W4. Phase 4 測試矩陣覆蓋率遠低於 implement.md 要求
`SpeechMessage.Dynamics.Tests`（15 檔）+ `SmokeTests`（2 檔，皆預設關閉）對照 implement.md Phase 4 第 1-7 項要求：
| 類別 | 狀態 |
|---|---|
| 多 workload/多 generation fake-server 隔離/cross-talk | 缺 |
| reload/drain generation-count 測試 | 缺（無 generation 概念） |
| soak / GC / handle / socket 測試 | 缺 |
| fault injection 401/429/503/DNS/reset | 部分（僅 timeout/queue 層） |
| FetchXML/OData injection 編碼矩陣 | 僅 1 個案例（GUID + `&`） |
| nextLink 驗證 | 缺 |
| idempotency ledger 狀態機 | 缺（ledger 不存在） |
| Windows auth tagged-union 全矩陣 | 部分 |
| Embedded manifest/registry 信任測試 | 缺（功能不存在） |
| CE 8.2/9.1 real-server smoke | 僅骨架、預設關閉、9.1 用 API v8.2 路由（見 I2） |

---

## 三、Info 發現

- **I1（值得肯定）**：`phase4-isolation-hardening-verification.md` 描述的本機/單行程隔離工作是誠實且有紅綠證據的（atomic admission、host-slot 序列化、ADFS handler 隔離、exactly-once disposal、sync-context 安全）。這是紮實的地基，只是範圍被文件自己正確標註為「local hardening」，並非 Phase 4 完成。
- **I2**：`phase3-tier-a-ifd-auth-blocker.md` 顯示 Windows/NTLM 直接連線目前得到 302 導到 IFD 頁面，代表 `jesus` 這個正式組織實際上是 IFD-only（不是純 AD），而 ADFS authorization_code 因 RP/ClientId 未在 ADFS 註冊而卡住 — 這代表 design.md §6.3 要求的「target-specific cold-start proof of a non-password service/workload flow」目前**尚未在唯一已知正式組織上完成**，等於目前沒有一個組織真正通過 IFD feasibility gate。
- **I3**：`SpeechMessage.Dynamics.WebApi/WebApi/Embedded/Gateway/Abstractions` 五個新專案本身的 SDK 掃描是乾淨的（agent C 確認零命中），證明新程式碼從一開始就沒有引入 SDK 相依，遷移邊界設計是被遵守的。
- **I4**：目前唯一有實質資料的正式組織是 `jesus`（CE 9.1，IFD）。PRD 要求 v8.2 與 v9.1 都要有 real-server smoke 證據；目前沒有已知 CE 8.2 環境的探測紀錄可核實，Phase0 ADR 也承認「尚未選定 production storage vendor」。

---

## 四、安全的 TDD 順序與 Phase 4→6 相依排序

Phase 4/5/6 不能線性推進；下列排序把「先讓地基變 durable，再讓上層測試有意義」當成硬相依：

1. **P4-A｜Gateway workload authentication（先做，其他 Phase 4 測試都依賴它）**
   - Red：`GatewayWorkloadBoundaryTests.cs`（已存在，4/4 red）。
   - Green：加入 Windows/Negotiate 認證中介軟體 + `principal → WorkloadSubjectId` 對映表（來自簽章設定或 registry），`Program.cs` 改為在 `MapPost` 前執行 `[Authorize]` 並從 `HttpContext.User` 取得已對映的 `WorkloadSubjectId`，body 中的欄位一律拒收（unknown field 即 400）。
   - 這一步必須先做，因為往後所有「跨 workload 隔離」「fairness」「audit 歸屬」測試都假設 `WorkloadSubjectId` 是伺服器可信值。

2. **P4-B｜Durable `IRuntimeHostSlotCoordinator`（ADR-001 技術選型 + 實作）**
   - 先寫 ADR 選型（SQL Server 已有 D365APP01 現成執行個體可用，符合「不可修改 MSCRM_CONFIG/組織庫」的邊界，可用獨立 `SpeechMessageDynamicsControlPlane`）。
   - Red：以現有 `InMemoryRuntimeHostSlotCoordinator` 測試套件為範本，新增「兩個獨立 `SqlConnection` 模擬兩台主機」的 fault test（見下節 SQL 設計），先寫會失敗的併發 acquire 測試。
   - Green：實作 `SqlRuntimeHostSlotCoordinator`，`IsDurable => true`。
   - 這一步必須在 P4-C 之前，因為 quarantine/fencing/epoch 測試沒有 durable store 就無法做跨行程驗證。

3. **P4-C｜AdmissionEpoch + Quarantine + Fencing（在 durable coordinator 之上補齊語意）**
   - 目前完全不存在，需新增型別與狀態機，並在 `OrganizationAdmissionManager` 接上 epoch 檢查。

4. **P4-D｜Idempotency Ledger（ADR-003）+ Audit Intent（ADR-004）**
   - 可與 P4-B/C 平行進行（不同 schema，但同一個 SQL Server 執行個體），因為它們不相依於 coordinator，只相依於「有 durable store」這個事實。
   - 這是 Phase 5 若要納入任何 **write** 能力的硬前提；若 Phase 5 候選維持唯讀，此項可延後但不可跳過（PRD 已把它列為 zero-tolerance）。

5. **P4-E｜HttpClient/Provider 生命週期收斂**
   - 修正 `GatewayHttpClientFactory` 與 `EmbeddedProviders` 的靜態快取，改為可控 disposal（見下方設計），並補上 reload/drain soak 測試。

6. **P4-F｜完整測試矩陣補齊**（fake-server cross-talk、soak、fault injection、FetchXML/OData injection 矩陣、nextLink、Windows tagged-union、Embedded manifest/registry）— 這些多數不相依於 P4-B/C/D，可與之平行寫，但「跨主機」相關案例（老/新 generation overlap、lease exhaustion）必須等 P4-B 完成才有意義去斷言。

7. **P4-G｜CE 8.2/9.1 real-server smoke，含 Discovery-service release 證據**（IFD service-flow 需先在 ADFS 註冊 client，見 I2 blocker）。

8. **Phase 5**：僅在 P4-A 完成後才可把 Gateway 模式的 Package 1 讀取流程視為「production-safe candidate」（目前 Embedded 模式因無 manifest 驗證，仍不可視為與 Gateway 等價，design.md §12.2 明文要求 Embedded 需與 Gateway 證據對齊才能允許）。補齊自動化 parity/rollback 測試（W3）。

9. **Phase 6**：只有在（a）所有消費者遷移完成、（b）C5 所列現存違規全部清除、（c）`Verify-NoDynamicsSdk.ps1` 改為 CI 強制閘門且覆蓋率含 `.ccg/diagnostics` 之後，才可宣稱完成。

---

## 五、SQL Server Lease Schema / 事務 / 鎖定 / Fencing / Quarantine 設計

目標：`SpeechMessageDynamicsControlPlane`（獨立資料庫，絕不觸碰 `MSCRM_CONFIG`/組織庫），供 `IRuntimeHostSlotCoordinator` 使用。全程使用 `SYSUTCDATETIME()`（伺服器 UTC），避免客戶端時鐘漂移影響 TTL 判定。

```sql
CREATE TABLE dbo.RuntimeHostSlotLease (
    LeaseNamespaceId     NVARCHAR(200)   NOT NULL,   -- RuntimeHostSlotLeaseNamespace（CanonicalKeyV1 字串）
    SlotOrdinal          INT             NOT NULL,   -- 0..MaximumRuntimeHosts-1，固定槽位而非自由競爭
    HostInstanceId        NVARCHAR(200)   NULL,       -- 目前持有者（NULL = 空槽）
    FencingToken          BIGINT          NOT NULL DEFAULT 0,
    AdmissionEpoch         BIGINT          NOT NULL DEFAULT 0,
    LeaseExpiresAtUtc      DATETIME2(3)    NULL,
    QuarantineUntilUtc     DATETIME2(3)    NULL,       -- 過期/撤銷後的隔離期限
    RowVersion             ROWVERSION      NOT NULL,
    CONSTRAINT PK_RuntimeHostSlotLease PRIMARY KEY (LeaseNamespaceId, SlotOrdinal)
);

CREATE TABLE dbo.AdmissionEpochLog (
    LeaseNamespaceId  NVARCHAR(200) NOT NULL,
    AdmissionEpoch     BIGINT        NOT NULL,
    AggregateMaxInFlight INT NOT NULL,
    MaximumRuntimeHosts  INT NOT NULL,
    PublishedAtUtc     DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    ConfigDigest        VARBINARY(32) NOT NULL,
    CONSTRAINT PK_AdmissionEpochLog PRIMARY KEY (LeaseNamespaceId, AdmissionEpoch)
);
```

**設計要點**：
- **固定槽位而非自由列**：`SlotOrdinal` 在建立 `AdmissionEpoch` 時依 `MaximumRuntimeHosts` 預先插入固定筆數的列（狀態全為空槽），避免「INSERT 競爭建立新 row」造成的幽靈併發問題；acquire 永遠是對既有列做 `UPDATE`。
- **Acquire（conditional create-or-take）**：單一交易、`READ COMMITTED SNAPSHOT`（避免鎖等待造成 head-of-line blocking）+ 明確 row lock：
  ```sql
  BEGIN TRAN;
    UPDATE TOP (1) dbo.RuntimeHostSlotLease WITH (ROWLOCK, UPDLOCK, READPAST)
    SET HostInstanceId = @hostInstanceId,
        FencingToken = FencingToken + 1,
        AdmissionEpoch = @currentEpoch,
        LeaseExpiresAtUtc = DATEADD(SECOND, @ttlSeconds, SYSUTCDATETIME())
    OUTPUT INSERTED.SlotOrdinal, INSERTED.FencingToken
    WHERE LeaseNamespaceId = @ns
      AND (HostInstanceId IS NULL AND (QuarantineUntilUtc IS NULL OR QuarantineUntilUtc <= SYSUTCDATETIME()))
       OR (LeaseExpiresAtUtc < SYSUTCDATETIME());  -- 已過期、尚未被人 release 的槽也可回收，但需先進 quarantine（見下）
  COMMIT;
  ```
  實務上應拆成兩段：過期槽先被一個獨立的「reaper」交易轉入 quarantine（設定 `QuarantineUntilUtc`），acquire 只挑「HostInstanceId IS NULL AND (QuarantineUntilUtc IS NULL OR 已過)」的槽，避免 acquire 交易本身兼職做「偵測過期」的雙重責任，簡化推理。
- **Renew（fencing 保護）**：
  ```sql
  UPDATE dbo.RuntimeHostSlotLease WITH (ROWLOCK, UPDLOCK)
  SET LeaseExpiresAtUtc = DATEADD(SECOND, @ttlSeconds, SYSUTCDATETIME())
  WHERE LeaseNamespaceId = @ns AND SlotOrdinal = @slot
    AND HostInstanceId = @hostInstanceId
    AND FencingToken = @expectedFencingToken   -- 呼叫端必須帶著 acquire 時拿到的 token
    AND LeaseExpiresAtUtc >= SYSUTCDATETIME(); -- 已過期則拒絕 renew（fail closed）
  -- @@ROWCOUNT = 0 → LeaseFailure，呼叫端必須立刻停止新 admission
  ```
- **Fenced release（優雅終止）**：
  ```sql
  UPDATE dbo.RuntimeHostSlotLease
  SET HostInstanceId = NULL, LeaseExpiresAtUtc = NULL
  WHERE LeaseNamespaceId = @ns AND SlotOrdinal = @slot
    AND HostInstanceId = @hostInstanceId AND FencingToken = @expectedFencingToken;
  ```
  只有在「所有 outbound-work lease 已歸零」之後才呼叫（design.md §7.2.2 的 graceful drain 規則）。
- **Quarantine 轉換**（由背景 reaper 定時執行，獨立交易，UTC 比較）：
  ```sql
  UPDATE dbo.RuntimeHostSlotLease WITH (ROWLOCK, UPDLOCK)
  SET HostInstanceId = NULL,
      QuarantineUntilUtc = DATEADD(SECOND, @quarantineSeconds, SYSUTCDATETIME())
  WHERE LeaseNamespaceId = @ns AND LeaseExpiresAtUtc < SYSUTCDATETIME()
    AND HostInstanceId IS NOT NULL;
  ```
  `@quarantineSeconds >= maxOutboundWorkLifetime + settlementMargin`，符合 design.md 的「舊/新主機的聚合預算不可疊加」不變量。
- **失效關閉語意**：Coordinator 連線失敗、逾時、或回傳比目前 `FencingToken` 小的值 → 呼叫端一律視為 `LeaseFailure`，立即停止新 admission、標記 NotReady，並取消超過 fence 時限的既有工作；**絕不本機延長租約**。連線層使用**有界**逾時（例如 2 秒 command timeout）與**有界**重試次數（例如 3 次、指數退避），逾時預算必須落在租約 TTL 之內，不可無限重試。
- **連線/命令邊界**：每次 acquire/renew/release 都是單一 short-lived `SqlConnection`（走連線池），單一交易、單一 round-trip 內完成（用上面的 `UPDATE ... OUTPUT` 樣式，不要「先 SELECT 再 UPDATE」的兩段式，避免 TOCTOU）。所有命令都要有 `CommandTimeout`。

---

## 六、Gateway 認證/授權設計（伺服器端推導 workload identity）

1. **傳輸層**：Gateway 部署在僅限內網存取（design.md §9.1 已要求 mTLS/JWT），ASP.NET Core 加入 `AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate()`（Windows 網域環境，內網服務對服務），或依部署平台改用驗證過的 client certificate/SPIFFE。**絕不接受** `X-Product`/`X-Workload` 之類的自訂 header 作為身分來源。
2. **身分對映**：新增伺服器端唯讀對映表（設定或 registry 來源，不可由 caller 影響），`Dictionary<string principalName, string workloadSubjectId>`。中介軟體在 `[Authorize]` 通過後，從 `HttpContext.User.Identity.Name` 查表得到 `WorkloadSubjectId`；查無對映 → `403 Forbidden`，且**不進入** `IDynamicsOperationExecutor`（`GatewayWorkloadBoundaryTests.cs` 已經寫好這個 contract，只差實作）。
3. **契約層防呆**：`OperationHttpRequest` 移除 `WorkloadSubjectId` 欄位（或保留欄位但用 duplicate-aware/unknown-field 嚴格解析器直接拒絕它出現在 body 中，回 `400`，如同 `Hostile_body_identity_cannot_override_server_mapped_workload` 測試所驗證）。`OperationExecutionRequest.WorkloadSubjectId` 只能由伺服器端中介軟體填入，Minimal API handler 完全不讀 body 裡的這個欄位。
4. **Embedded 模式**：`WorkloadSubjectId` 來自啟動時已驗證的簽章 manifest/registry（W2 所述功能需先補齊），不是來自任何執行期輸入。
5. **絕不外洩**：`WorkloadSubjectId`、對映結果、`FencingToken`、任何 principal 名稱都不可進入 telemetry/exception/correlation ID 的原始形式；只用 `CanonicalKeyV1` 編碼後的值。目前程式碼因為根本沒有驗證,所以還沒有真的洩漏 — 但一旦加入 Negotiate，這個規則必須從第一天就落地（例如把 `NormalizeWorkload` 的輸入來源從「body 字串」換成「對映後的 workloadSubjectId」，並確保 log 只記錄後者）。

---

## 七、HttpClient / Handler / Profile-Generation 所有權與處置設計

**現況問題**：兩套治理模型並存（C4）。收斂方案：

1. **Product → Gateway 段**（`GatewayHttpClientFactory`）：改為非 static、透過標準 `IHttpClientFactory` + named client（`services.AddHttpClient("dynamics-gateway", ...)`），把 `UseCookies=false`/`AllowAutoRedirect=false`/`MaxConnectionsPerServer` 設定搬進 `ConfigurePrimaryHttpMessageHandler`。這樣可以吃到 .NET 內建的 handler 輪替（`SetHandlerLifetime`）而不需要自己管理靜態字典，且與 `AdfsOAuthTokenProvider` 已經在用的 `IHttpClientFactory` 模式一致，消除「兩套模型」的架構不一致。
2. **Embedded 段**（`EmbeddedProviders` 靜態 `IServiceProvider` 快取）：改為由呼叫端（ChurchReport DI 容器)持有單一 `IServiceProvider` 生命週期，並在設定變更時明確呼叫 `DisposeAsync()` 舊 provider、建立新的（對齊 design.md §7.3 replace-and-drain）。至少要加上一個「舊 provider 在 N 秒後若還沒被下一次請求引用，記錄一次性 dispose」的最低限度保護，而不是永久累積。
3. **Profile Runtime（WebApi 內部）**：目前設計文件的 `ProfileRuntimeKey`/`disposeHandler:true`/exactly-once disposal 規則已經在本機隔離驗證中證明過（phase4-isolation-hardening-verification.md），這部分不需要重做，只需要在跨主機 coordinator（P4-B）就緒後，把「drain 完成」與「fenced release」串起來（目前 `RuntimeHostSlotLease` 是本機物件，drain 完成後呼叫 in-memory coordinator release；一旦換成 SQL coordinator，這個呼叫路徑要原封不動地指到新的 durable 實作，介面已經抽象化，這是好的設計決策）。
4. **殘留風險清單**（明確列出，供 Phase 4 soak 測試斷言）：
   - `GatewayHttpClientFactory.Clients`：process 生命週期，永不清空 → 改後應變成受 handler lifetime 管理。
   - `EmbeddedProviders`：process 生命週期，永不清空 → 需要顯式 dispose 路徑。
   - `InMemoryRuntimeHostSlotCoordinator._slots`：`PurgeExpired` 已存在，本機沒問題，但這是**要被取代**而非保留的元件（design 明講「不可把它當最終方案」）。

---

## 八、SDK/WCF/SOAP 消費者盤點與遷移風險

| 專案/檔案 | 違規符號 | 生產/測試 | Phase 6 移除順序 |
|---|---|---|---|
| `SpeechMessageProducts.ChurchReport.csproj:112-113` | `Microsoft.Crm.Sdk.Proxy` 直接 HintPath（指向 repo 外部 SDK DLL 資料夾） | 生產 | 1（連同 Startup.cs 的 `ICrmConnectionPool` 註冊一起處理，見下） |
| `ToolUtility.csproj:53` → `PowerPlatform.Dataverse.Client.csproj` | ProjectReference | 生產 | 2（待 ToolUtility 所有 CRM call site 遷移完成後移除） |
| `SpeechMessageProducts.sln` | 包含 `PowerPlatform.Dataverse.Client` 專案節點 | 建置圖 | 3（移除 ToolUtility 參照之後） |
| `PowerPlatform.Dataverse.Client\OnPremiseClient.cs` 等 6 檔 | `IOrganizationService` WS-Trust 實作 | 生產（借用專案） | 4（從可建置原始碼移出/刪除） |
| `ToolUtility`（76 檔：`CrmConnectionPool.cs`/`CrmConnectionService.cs`/`ICrmConnectionPool.cs`/`ICrmClient.cs`/`EntityOperations`等） | `IOrganizationService`/`ICrmConnectionPool` 等 SDK 形狀介面 | 生產 | 5（依 Organization-call coverage matrix 逐項遷移，非整批） |
| `SpeechMessageProducts.ChurchReport`（110 檔：`Startup.cs`/`WebServiceConnector\*`/`Controllers\*`/`TimedOrganizationService.cs`） | 同上 + 硬編帳號/URL fallback | 生產 | 6（含 Startup.cs 的 `SPEECHMESSAGE\Administrator` 預設值與正式 SOAP URL 移除，這是獨立於 Phase 5 遷移進度的**立即可修**安全衛生問題） |
| `ToolUtility.Tests`（16 檔）、`ChurchReport.MemberInfo.Tests`（5）、`ChurchReport.Tests`（1） | `Microsoft.CrmSdk.CoreAssemblies` 套件參照等 | 測試 | 7（隨對應生產程式碼一起換成 Gateway contract/fake-server 測試） |
| `.ccg\diagnostics\LegacySoapProbe` | SDK proxy DLL HintPath | 診斷工具 | 8（目前不在 `no-sdk-source-roots.json` 掃描範圍內，屬 manifest 覆蓋率缺口，應決定是刪除此工具或明確列入臨時清單並附 owner/deadline） |

**Phase 5 有界候選確認**：現有的 `Package01FeeReadsEnabled` 讀取流程（`DonationFeeQueryService.cs` → `Package01FeeReadClient` → Gateway/Embedded executor）完全符合 PRD「one bounded, read-heavy ChurchReport workflow」的定義，且已具備 feature flag + 人工 rollback runbook。**這不需要重新選擇**，只需要（a）先完成 P4-A 認證閘門、（b）補上自動化 parity 測試（W3）、（c）在 `jesus`（唯一已知正式組織）完成 IFD service-flow 驗證（I2 blocker）之後才可視為 production-ready。

**Phase 6 精確移除順序**（沿用 implement.md §Phase 6，但依上表具體化）：
1. 移除 ChurchReport 的 `Microsoft.Crm.Sdk.Proxy` HintPath 與 `Startup.cs` 硬編帳號/URL fallback。
2. 待 ToolUtility 所有 65+ call site 依 Organization-call coverage matrix 遷移完成後，移除 `ToolUtility.csproj` 對 `PowerPlatform.Dataverse.Client` 的 ProjectReference。
3. 從 `SpeechMessageProducts.sln` 移除 `PowerPlatform.Dataverse.Client` 專案節點。
4. 刪除/移出 `PowerPlatform.Dataverse.Client` 原始碼目錄。
5. 移除所有 `Microsoft.Xrm*`/`Microsoft.CrmSdk*`/`Microsoft.PowerPlatform.Dataverse*` 套件參照（含測試專案）。
6. 移除 WCF CRM adapter/SOAP pool/SDK 形狀介面與其測試，換成 Gateway contract/fake-server 測試。
7. 輪替所有現存 CRM 憑證，確認 secret provider 是唯一來源。
8. 把 `Verify-NoDynamicsSdk.ps1` 從 `continue-on-error`/`report-only` 改為 CI 強制閘門（`-FailOnFindings`），並把 manifest 的 `excludedRelativePaths` 收斂到不再意外放過 `.ccg/diagnostics` 這類仍含 SDK 參照的工具目錄。
9. 執行獨立 dual-model 架構/程式碼審查與最終安全/效能驗證。

---

## 九、會讓「Phase 4/5/6 已完成」宣稱不誠實的驗證缺口清單

1. **Phase 4**：沒有任何跨主機/跨行程測試（因為沒有 durable coordinator 可測）— 目前所有「isolation/soak/fault」證據都只涵蓋單一進程。implement.md 明文要求的「Gateway plus Embedded aggregate-permit test」「same-organization old/new generation overlap」在物理上還做不到。
2. **Phase 4**：Gateway workload authentication 是 0/4 red — 任何說「Gateway 已具備 workload 身分隔離」的宣稱都可以用 `GatewayWorkloadBoundaryTests.cs` 現場反證。
3. **Phase 4**：idempotency ledger 與 audit intent 完全不存在，design.md §7.5 zero-tolerance release gates 的「telemetry/queue 不含身分資料」等條款目前是因為**功能未啟用**才成立，不是因為**已被驗證安全**。
4. **Phase 4**：CE 8.2 real-server smoke 證據不存在（只有 `jesus`＝CE 9.1 的部分探測，且被 IFD 認證卡住，見 I2）；design.md 明文「An API v8.2 route on a CE 9.1 product is not exact CE 8.2 product proof」— 目前甚至連 v9.1 的完整 smoke（WhoAmI 成功）都尚未達成。
5. **Phase 5**：無自動化 parity/rollback 測試，目前的「等價性」證明完全依賴人工 checklist，不符合 design.md §11.1 對 shadow-compare 的要求。
6. **Phase 6**：`Verify-NoDynamicsSdk.ps1` 是 report-only 且未接 CI；即使接了 CI，現存的 ChurchReport HintPath、`.sln` 中的 `PowerPlatform.Dataverse.Client`、`ToolUtility` ProjectReference、`Startup.cs` 硬編帳號都會讓它立刻失敗 — 目前完全不具備宣稱 Phase 6 完成的條件，連「已升級為強制閘門」這個前置動作都還沒做。

---

**一句話總結**：Phase 4 在「單一 Gateway/Embedded 進程內的資源與生命週期隔離」這個子範圍已經有可信的紅綠證據；但 PRD/design 定義的 Phase 4 本質是「多主機、多組織、多 workload 的安全共存證明」，而支撐這個證明的三根柱子——durable coordinator（C2）、Gateway 認證（C1）、idempotency/audit ledger（C3）——目前都是設計文件而非程式碼。Phase 5 的候選與 rollback 機制是本次分析中最接近完成的一塊，但仍缺自動化 parity 證據。Phase 6 的強制閘門尚未啟用，且啟用後會立即被至少 4 處現存違規擋下。

---
SESSION_ID: 34f9ae42-3971-4179-879f-b1b68384feae
