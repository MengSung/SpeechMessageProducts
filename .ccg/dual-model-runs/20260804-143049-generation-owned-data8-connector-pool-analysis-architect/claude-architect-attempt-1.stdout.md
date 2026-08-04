# P3 Data8 Connector Pool 分析報告

以唯讀模式檢視了 `.trellis/tasks/08-04-generation-owned-data8-connector-pool/prd.md`、父任務 `08-04-dynamics-connection-management-plan/{design,implement}.md`、權威規格 `docs/dynamics-connection-management-spec.md`，以及 `IOrganizationAdmissionManager`、`DispatchEnvelope`、`ResolvedProfile`、`OnPremiseClient`、`DynamicsProfileRuntimeManager`、`IOrganizationAdmissionRegistry`、既有 `OnPremiseClientDisposalTests`/`OrganizationAdmissionManagerTests` 與 legacy `CrmConnectionPool`。以下為實作前必須處理的風險。

---

## Critical

### C1　規格草案的 `IConnectorLease.Service` 型別直接違反 SDK-free 邊界
- `docs/dynamics-connection-management-spec.md:349` — `IConnectorLease` 定義為 `IOrganizationService Service { get; }`，`IOrganizationService` 屬於 `Microsoft.Xrm.Sdk`。
- `.trellis/tasks/08-04-generation-owned-data8-connector-pool/prd.md:10` 要求「在 Abstractions 定義不引用 CRM SDK 的 … `IConnectorLease` …」；`prd.md:23` 更明文禁止「Pool 契約公開 … `IOrganizationService` … 型別」；`prd.md:36` 驗收條件為「契約編譯期不含 CRM SDK 型別」。
- 若依規格草案原樣實作，`SpeechMessage.Dynamics.Abstractions` 必須參考 `Microsoft.Xrm.Sdk`，這會污染所有 Profile（包含未來 `OfficialCrm82Worker`/`OfficialCrm91Worker`，它們沒有 `IOrganizationService`），也直接讓 P3 驗收條件不成立。
- **修正建議**：`IConnectorLease` 的 Abstractions 層公開表面應比照既有 `IDynamicsProfileExecutionLease.Executor : IDynamicsOperationExecutor`（`SpeechMessage.Dynamics.ControlPlane/Runtime/IDynamicsProfileRuntime.cs:43`）的作法，只暴露 SDK-free 的操作介面；`IOrganizationService` 只能留在 `SpeechMessage.Dynamics.Connectors.Data8` 專案內部實作，不進 Abstractions 契約。實作前應先修正規格書 §7.1，避免規格與 PRD 互相矛盾。

### C2　Pool／世代所有權元件缺失，`IConnectorRouter.Resolve` 是無狀態查找而非生命週期擁有者
- 規格 §5.1（`docs/dynamics-connection-management-spec.md:270-276`）只定義 `IConnectorPool Resolve(ResolvedProfile profile)`，未定義「誰建立 Pool」「誰在新世代發布時建立新 Pool、淘汰舊 Pool」「誰呼叫 Pool 的 Drain」。
- `prd.md:14`「Pool Drain 必須先拒絕新 Lease，等待既有 Lease 歸還，最後釋放閒置與故障資源」與 §6.2-6.3（規則 6.2/6.3）的世代收斂機制，在既有程式庫中是由 `DynamicsProfileRuntimeManager`（`SpeechMessage.Dynamics.ControlPlane/Runtime/DynamicsProfileRuntimeManager.cs`）以 `ProfileSlot.Active/Draining` + `BeginDrain()`/`DrainAndDisposeAsync()` 實作（見 `IDynamicsProfileRuntime.cs:97-104`）。
- 父任務 `implement.md:55`「接上既有 `DynamicsProfileRuntimeManager` 世代機制」雖有提到要銜接，但 P3 子任務自己的 `prd.md`（唯一在此任務中被視為權威來源的文件）完全沒有提到 `DynamicsProfileRuntimeManager`、`IDynamicsProfileRuntime` 或任何等價的 Pool 生命週期擁有者，也沒有在 Scope（`prd.md:7-16`）列出建立此元件的工作項。
- **修正建議**：在動工前先補一個明確元件（例如 `IConnectorPoolManager`／`Data8ConnectorPoolRegistry`），比照 `DynamicsProfileRuntimeManager` 的 `ProfileSlot.Active/Draining` 模式管理 `(ProfileAlias, GenerationId)` → `IConnectorPool` 的建立、發布與 drain，並讓 `IConnectorRouter.Resolve` 只做查找，不做建立/淘汰。缺少此元件，§10.3 的 `Generation_old_pool_disposed_after_lease_zero`、`Generation_at_most_one_active_and_one_draining` 類測試無從編寫。

### C3　Permit 取得與 Generation 解析順序（規則 7.1）在 P3 契約中沒有對應保護
- 規則 7.1（`docs/dynamics-connection-management-spec.md:369`）：「③ 借出必須在 ② 取得 Permit 之後才解析 Active Generation」。
- 既有參考實作 `DynamicsProfileRuntimeManager.AcquireAsync`（`SpeechMessage.Dynamics.ControlPlane/Runtime/DynamicsProfileRuntimeManager.cs:186-273`）刻意「不持有 Runtime Execution Lease」直到 Permit 到手，並在拿到 Permit *之後* 於鎖內重新解析 Active Runtime，且用 `ReferenceEquals(currentActive.AdmissionManager, admissionManager)` 與 `ConfigurationDigest` 比對，偵測 Permit 排隊期間世代已被替換的情況（第 220-234 行）。
- P3 規格的 `IConnectorPool.AcquireAsync(CancellationToken ct)`（無 envelope 參數）意味著取得 Permit 與呼叫 `Pool.AcquireAsync` 是兩個獨立呼叫；若呼叫端在取得 Permit *前* 就用 `ResolvedProfile`（含 `GenerationId`）呼叫 `Router.Resolve(profile)` 鎖定 Pool，然後在 Permit 排隊完成後才對該 Pool 呼叫 `AcquireAsync`，就會重演 `DynamicsProfileRuntimeManager` 特意避免的「Queue 等待期間持有舊世代強引用」問題，違反規則 7.1，且 `Permit_acquired_before_generation_resolved`（§10.3）測試無法通過。
- **修正建議**：P3 必須明確規定「Router.Resolve 只能用 Permit 取得**之後**重新解析的 `ResolvedProfile`」，並比照 `DynamicsProfileRuntimeManager` 加上世代/Digest 一致性再檢查；這個時序保證必須寫進 P3 的契約文件，不能只靠實作者自行揣摩。

### C4　容量共用機制引用錯誤：`prd.md` 只提 `IOrganizationAdmissionManager`，遺漏 `IOrganizationAdmissionRegistry`
- `prd.md:12`：「Data8 Pool 透過既有 `IOrganizationAdmissionManager` 取得組織容量 Permit，不建立第二套容量預算」。
- 但程式庫中真正保證「同一 `OrganizationId` 的多個 Profile Generation 共用同一個 Admission Manager」的元件是 `IOrganizationAdmissionRegistry`（`SpeechMessage.Dynamics.ControlPlane/Capacity/IOrganizationAdmissionRegistry.cs:39-54`），其 `Acquire(OrganizationAdmissionPlan)` 回傳一個引用計數的 `IOrganizationAdmissionRegistration`，明確規定「最後一個 Registration 釋放後，Registry 才 Dispose Manager」（第 6-10 行生命週期契約）。`IOrganizationAdmissionManager` 本身不負責跨 Profile 共用。
- 若 Data8ConnectorPool 直接注入/建立 `IOrganizationAdmissionManager` 而不透過 `IOrganizationAdmissionRegistry.Acquire`，會違反規則 6.7（Organization Capacity 以 `OrganizationId` 聚合），且 `Capacity_shared_across_profiles_of_same_organization`（§10.3）測試會失敗。
- **修正建議**：P3 的 Pool 建構流程必須改為「取得 `IOrganizationAdmissionRegistration`（來自 Registry），持有到 Pool 完成 drain 後才釋放 Registration」，並在 `prd.md`/實作契約中明確引用 `IOrganizationAdmissionRegistry`，而不是只寫 `IOrganizationAdmissionManager`。

### C5　直接移植 `CrmConnectionPool.Dispose()` 會違反「先拒絕新借出、等待既有 Lease 歸還」的 deterministic drain 要求
- `ToolUtility/ConnectionOperations/CrmConnectionPool.cs:457-470`：`Dispose()` 是同步、立即執行——`_disposed = true` 後馬上 `_cleanupTimer?.Dispose()` 與 `_semaphore?.Dispose()`，只清空目前在 `_connections`（閒置佇列）裡的連線，**完全不等待已借出（in-use）的連線歸還**。
- `prd.md:14` 明確要求「Pool Drain 必須先拒絕新 Lease，等待既有 Lease 歸還，最後釋放閒置與故障資源」。若照 `implement.md:52`「複製後改造」直接搬移這段 Dispose 邏輯，會有兩個具體問題：
  1. Drain 不會等待 in-flight lease，直接違反 prd.md 的排空語意。
  2. `_semaphore.Dispose()` 在還有借出中連線的情況下執行後，稍後呼叫 `ReleaseConnection()`（第 178-217 行）中的 `_semaphore.Release()`（第 215 行）會拋出 `ObjectDisposedException`——而 `CrmConnectionPool` 沒有像 `OrganizationAdmissionManager.ReleasePermit`（`SpeechMessage.Dynamics.ControlPlane/Capacity/OrganizationAdmissionManager.cs:365-380`）那樣 `catch (ObjectDisposedException)` 的容忍處理。
- **修正建議**：新 Pool 的 Drain/Dispose 必須採用 `OrganizationAdmissionManager.ShutdownCoreAsync`/`WaitForReservationsToDrainAsync`（`OrganizationAdmissionManager.cs:493-570`）或 `DynamicsProfileRuntimeManager.DrainOwnedRuntimeAsync`（`DynamicsProfileRuntimeManager.cs:838-863`）已驗證過的「TaskCompletionSource 型 reservation-drained 訊號 + 對稱 Release 且吞下 ObjectDisposedException」模式，不能直接複製 legacy 的同步 Dispose 路徑。

---

## Warning

### W1　Legacy Pool 把明碼帳密存成整個 Pool 生命週期的欄位
- `ToolUtility/ConnectionOperations/CrmConnectionPool.cs:35-36`：`_username`、`_password` 是 `readonly` 欄位，存活整個 Pool 生命週期（可能數小時到數天）。
- 新架構下憑證應由 `CredentialReference` → `CredentialProvider` 按需解析（規格 §4.1，`docs/dynamics-connection-management-spec.md:178-188`），且 prd.md 第 31 行的生命週期不變量 5 明訂「credential、token … 不得進入共享 Pool key 或 Client」。「複製後改造」（`implement.md:52`）若原樣保留 `_username`/`_password` 欄位模式，會讓明碼憑證長期常駐於 Pool 物件記憶體中。
- **修正建議**：新 Pool 建構子不應保存明碼帳密欄位；應在每次建立連線時透過 `CredentialProvider` 依 `CredentialReference` 即時取得憑證，用畢即釋放參考。

### W2　`prd.md` 的 8 項測試清單遺漏規格中兩項具名測試
- `prd.md:38`「測試覆蓋：健康歸還、故障淘汰、取消／逾時釋放 Permit、Generation drain、跨 Profile 隔離、同 Organization 容量共用、Dispose idempotency、soak 無單調資源成長」。
- 對照規格 §10.3/§10.4（`docs/dynamics-connection-management-spec.md:451-474`），明顯缺少：
  - `Permit_acquired_before_generation_resolved`（對應本報告 C3）
  - `Ce82_and_Ce91_pools_coexist_in_one_process`（依賴 §11.1 `_sdkMajorVersion` instance 化，見 W3）
- **修正建議**：在 P3 測試清單中明確補上這兩項，或在 prd.md 中寫明排除理由與後續追蹤任務，避免實作者遺漏。

### W3　`OnPremiseClient._sdkMajorVersion` 仍是 `static readonly`，是 Ce82/Ce91 共存測試的前置阻塞
- `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:76-94`：`_sdkVersion`/`_sdkMajorVersion` 為 `static readonly`，由靜態建構子一次性設定，全進程共用。
- 規格 §11.1（`docs/dynamics-connection-management-spec.md:488-502`）與父任務 `design.md:75`「已知技術風險」都指出這是「視 A1 結果」才確定是否必修的項目，且 `implement.md:90`（G-0 關卡）要求「A1 結果已知；`_sdkMajorVersion` 是否必修已確定」才能開始 P2。
- 目前程式碼顯示此修正**尚未實作**（欄位仍是 static），若 A1 結果顯示 8.2/9.1 WSDL 探索有差異，`Ce82_and_Ce91_pools_coexist_in_one_process` 測試會直接失敗，且是 Pool 層無法繞過的底層限制。
- **修正建議**：P3 開始前應先確認 A1 決策狀態（`implement.md:27` 的使用者前置項），若尚未執行或結果未知，應在 G-1 關卡前補做，避免 P3 pool 測試因下層限制而卡住。

### W4　`MarkFaulted` 與 `DisposeAsync` 的並行語意未定義
- 規格 §7.1（`docs/dynamics-connection-management-spec.md:352`）：`void MarkFaulted(Exception? cause)`，同步、可能被多次呼叫。
- `prd.md:29` 生命週期不變量 3 要求「Lease 的 `DisposeAsync` 必須 exactly-once」，但沒有明確規範 `MarkFaulted` 與併發 `DisposeAsync` 之間的互斥保證。既有 `OrganizationAdmissionManager.AdmissionPermit.Dispose()`（`OrganizationAdmissionManager.cs:842-851`）用 `Interlocked.Exchange(ref _disposed, 1)` 達成 idempotent 且執行緒安全的釋放判定，是可直接沿用的模式。
- **修正建議**：`IConnectorLease` 實作應以 `Interlocked` 型旗標記錄「是否已 faulted」，並在 `DisposeAsync` 內以同一個原子讀取決定「健康歸還」或「淘汰」路徑，避免 `MarkFaulted` 與 `DisposeAsync` 競態下讀到不一致狀態；建議新增一項測試明確涵蓋此競態（目前規格 §10.4 未列出）。

---

## Info

### I1　建議提供 bounded snapshot API 取代反射式測試
- 既有 legacy 測試 `SpeechMessage.Dynamics.Tests/OnPremiseClientDisposalTests.cs:136-141`（`Pool_dispose_when_service_dispose_fails_releases_its_owned_capacity_slot`）用反射讀取 `CrmConnectionPool` 的 private `_currentSize` 欄位驗證容量回收。
- 程式庫已有更好的既定模式：`AdmissionMetricsSnapshot`（`IOrganizationAdmissionManager.cs:43-58`）與 `DynamicsProfileRuntimeManagerSnapshot`（`DynamicsProfileRuntimeManager.cs:279-302`）都是「非秘密、有界」的公開快照方法。
- **建議**：新 `IConnectorPool` 直接提供類似 `GetSnapshot()`（idle/active/faulted 計數）的公開 API，供 §10.4 的 soak 測試（`Soak_repeated_acquire_release_no_monotonic_growth`）與跨 Profile 隔離測試使用，不要延續反射讀取 private 欄位的測試手法。

### I2　測試風格建議延續現有慣例
- 現有 `OnPremiseClientDisposalTests.cs` 與 `OrganizationAdmissionManagerTests.cs` 一致採用 xunit `[Fact]` + FluentAssertions + 手刻 fake（`DispatchProxy`／自製 `IOrganizationService`/`ICrmConnectionService` 替身），未使用 Moq 等 mocking 框架。P3 新測試應延續此風格以維持一致性與可讀性。

### I3　`DispatchEnvelope` 與 `IConnectorPool.AcquireAsync(ct)` 參數不對稱本身合理，但邊界元件未定名
- `DispatchEnvelope`（`SpeechMessage.Dynamics.ControlPlane/Capacity/DispatchEnvelope.cs`）攜帶完整 Envelope 給 `IOrganizationAdmissionManager.AcquireAsync`，而規格 §7.1 的 `IConnectorPool.AcquireAsync(CancellationToken ct)` 只需要 `ct`——這個不對稱本身符合既有 `DynamicsProfileRuntimeManager.AcquireAsync` 的分工模式（Permit 用 Envelope，Runtime/Pool 借出只用 ct）。
- 但目前沒有任何文件為「在 Permit 拿到後、Pool.AcquireAsync 前，重新解析 Active Generation」這段邏輯的擁有者命名（呼應 C2/C3）。建議在 P3 的技術契約中明確定名此元件並寫入 Scope，避免不同實作者各自在 Router 或呼叫端插入不一致的邏輯。

---

## 總結

最需要在動工前解決的是 **C1**（規格與 PRD 對 `IConnectorLease.Service` 型別互相矛盾，屬阻塞性衝突，必須先修規格書再寫程式碼）與 **C2/C3/C4**（Pool／世代／容量共用的擁有者與時序保證，目前只存在於既有 Worker Runtime 的實作模式中，P3 尚未把這套已驗證過的模式明確銜接到 Data8 Pool）。**C5** 則是移植 legacy `CrmConnectionPool` 時最容易在無意間引入的具體 bug（drain 語意與 semaphore dispose 競態）。建議在撰寫 RED 測試之前，先以一份補充設計筆記把 C1-C4 的元件與時序定案，再進入 P3 §10.3/§10.4 的測試撰寫階段。

---
SESSION_ID: e13478a7-ad6b-44de-8df8-30403066c292
