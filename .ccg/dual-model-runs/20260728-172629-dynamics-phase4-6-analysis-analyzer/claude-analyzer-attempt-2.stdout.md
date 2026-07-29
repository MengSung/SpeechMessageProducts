I have enough verified evidence. Producing the final analysis report now.

# Dynamics Phase 4-6 實作分析報告（Attempt 2 — 覆核工作目錄最新變更）

**分析範圍**：`SpeechMessage.Dynamics.*` + `SpeechMessageProducts.ChurchReport` against `prd.md`/`design.md`/`implement.md`/`phase0-runtime-capacity-adr.md`/`phase4-isolation-hardening-verification.md`
**方法**：直接讀原始碼 + 現場執行 `dotnet test`（7/7 綠燈）驗證新變更 + `git diff`/`grep` 交叉核對未變更部分
**重要前提**：本次分析與同一 run 目錄下的 `claude-analyzer-attempt-1.stdout.md`（17:26-17:37）之間，工作目錄已被**實際修改**（`Program.cs` +83 行、`GatewayHttpClientFactory.cs` 已刪除、`ProductClientServiceCollectionExtensions.cs`/`GatewayDynamicsOperationExecutor.cs`/`GatewayProductClientTests.cs` 均有改動）。以下結論以**當前程式碼**為準，並明確標註哪些是「attempt-1 後新完成」、哪些「原地未動」。

---

## 一、狀態變更摘要（相對 attempt-1）

| Attempt-1 發現 | 當前狀態 | 證據 |
|---|---|---|
| C1 Gateway 無任何身分驗證，信任 body `WorkloadSubjectId` | **已修復** | `Program.cs:24-37,127-167,196-236` 新增 Negotiate 認證中介軟體 + `ConfigurationWorkloadSubjectResolver`；`OperationHttpRequest` 已移除 `WorkloadSubjectId` 欄位，`UnmappedMemberHandling=Disallow` 使 body 帶該欄位直接 400；`GatewayWorkloadBoundaryTests.cs` 4/4 **綠燈**（現場執行確認） |
| C4（Product→Gateway 段） `GatewayHttpClientFactory` 靜態無界字典 | **已修復** | 該檔案已刪除（`git status: D`）；`ProductClientServiceCollectionExtensions.cs:54-78` 改為標準 `IHttpClientFactory` + `SetHandlerLifetime(10min)` + `SocketsHttpHandler` 有界連線池設定 |
| C2 無 durable coordinator，`RequireDurableHostCoordinator` 死開關 | **未修復，且行為已改變（見下方新發現 C1'）** | `Program.cs:115-116` |
| C3 Idempotency Ledger / Audit Intent 不存在 | **未變更，仍不存在** | 全 repo 搜尋零命中 |
| C4（Embedded 段）`EmbeddedProviders` 靜態快取無 dispose | **未變更** | `DonationDynamicsAccessBootstrap.cs:42,382` 行號與內容與 attempt-1 描述一致 |
| C5 `Verify-NoDynamicsSdk.ps1` report-only、CI 未強制 | **未變更** | 未在本次 diff 範圍內 |

---

## 二、Critical 發現（更新後）

### C1'（新發現，取代原 C2 的「死開關」描述）：`RequireDurableHostCoordinator` 現在對非 Testing 環境一律為 `true`，但系統中仍只有 in-memory coordinator — Gateway 在 Testing 以外的任何環境，第一個真實操作請求就會拋出未捕捉例外

- `SpeechMessage.Dynamics.Gateway\Program.cs:115-116`：
  ```csharp
  options.Admission.RequireDurableHostCoordinator =
      !builder.Environment.IsEnvironment("Testing");
  ```
  這是從「硬編 `false`」改為「非 Testing 一律 `true`」的**方向正確**的 fail-closed 收斂。
- 但 `SpeechMessage.Dynamics.WebApi\DependencyInjection\WebApiServiceCollectionExtensions.cs:91` 唯一登錄的仍是 `TryAddSingleton<IRuntimeHostSlotCoordinator, InMemoryRuntimeHostSlotCoordinator>()`，`IsDurable => false`（`InMemoryRuntimeHostSlotCoordinator.cs:25`）。
- `OrganizationAdmissionManager.cs:74-81`（`EnsureHostSlotCoreAsync`）在**每次**真實操作請求時（非啟動時）檢查 `_plan.RequireDurableHostCoordinator && !_slotCoordinator.IsDurable`，成立即 `throw new InvalidOperationException`。
- 我追蹤了呼叫鏈（`EnsureHostSlotAsync` → executor → `Program.cs` 的 `MapPost` handler）：**沒有任何 catch 把這個例外轉成結構化 `OperationExecutionResult.Failure`**，會直接變成 ASP.NET Core 預設的未處理例外 → 裸 500，不符合 design.md 對 `LeaseFailure` 應該回傳結構化、fail-closed 錯誤語意的要求。
- **後果**：Gateway 現在**在任何非 Testing 環境下（Development/Staging/Production）完全無法執行任何真實操作**，包含 attempt-1 I2 提到的 `jesus` 組織 WhoAmI smoke。這代表：(a) 這是誠實的方向——不會再讓人誤以為單進程隔離等於多主機安全；(b) 但目前**沒有任何文件更新**告知團隊「Phase 4 P4-B（durable coordinator）完成前，Gateway 對任何真實環境都是不可用的」，若不補一份「已知阻斷」說明，容易被誤判為「Gateway 又壞了」而不是「刻意收緊」。
- **建議**：(1) 在 `EnsureHostSlotCoreAsync` 的呼叫端加上 try/catch，把此例外轉成結構化 `503 LeaseFailure` 回應；(2) 在 README/runbook 明確記錄此為預期行為並附上 P4-B 完成前的臨時運維指引（例如：僅 `Testing` 環境可跑,其餘環境暫停部署）。

### C2. 完全沒有 durable cross-host coordinator（維持，細節同 attempt-1，未變更）
- 唯一實作仍是 `InMemoryRuntimeHostSlotCoordinator.cs`；`AdmissionEpoch`/`Quarantine`/`Fencing`（跨行程意義下）/`SpeechMessageDynamicsControlPlane` 在程式碼中仍為零命中。
- 部署 ≥2 個 Gateway/Embedded replica（PRD 要求）時，`MaximumRuntimeHosts`/`AggregateMaxInFlight` 仍完全無法跨進程強制執行。

### C3. Idempotency Ledger（ADR-003）與 Audit Intent Reservation（ADR-004）仍未實作（維持）
- `IdempotencyLedger`/`OutcomeUnknown`/`AuditIntent`/`OperationDefinitionRevision` 全 repo `.cs` 搜尋零命中；`ControlledOperationExecutor.cs` 無 ledger 相依性。

### C4-Embedded. `EmbeddedProviders` 靜態 `IServiceProvider` 快取仍無 reload/drain/dispose（維持）
- `DonationDynamicsAccessBootstrap.cs:42`（`ConcurrentDictionary<string, IServiceProvider> EmbeddedProviders`）、`:382`（`GetOrAdd`）：process 生命週期，設定變更（profile 輪替）時舊 provider（含其 `HttpClient`/handler）永不 dispose，違反 design.md §7.3 replace-and-drain。
- 注意：Product→Gateway 段的靜態字典（原 C4）已修復，但 **Embedded 段的同型態問題完全獨立、仍在**，不要把兩者混為一談視為已解決。

### C5. Phase 6 no-SDK 閘門仍是 report-only、未被任何 CI 擋下（維持，未在 diff 範圍內）
- `eng\Verify-NoDynamicsSdk.ps1` 預設無 `-FailOnFindings`；`.github\workflows\toolutility-tests.yml` 用 `continue-on-error: true`。
- 現存真實違規維持：`SpeechMessageProducts.ChurchReport.csproj:112-113`（`Microsoft.Crm.Sdk.Proxy` HintPath）、`SpeechMessageProducts.sln`/`ToolUtility.csproj:53` 的 `PowerPlatform.Dataverse.Client`、`ChurchReport\Startup.cs:302-328` 硬編 `SPEECHMESSAGE\Administrator` 帳號與正式 SOAP URL 後備值、`.ccg\diagnostics\LegacySoapProbe` 掃描覆蓋率缺口。

### C6（新發現）：`Microsoft.AspNetCore.Authentication.Negotiate` 10.0.7 被 NuGet audit 標記為高嚴重性弱點，且正是剛加入、負責 Gateway 身分驗證邊界的套件
- `SpeechMessage.Dynamics.Gateway\SpeechMessage.Dynamics.Gateway.csproj` 新增 `<PackageReference Include="Microsoft.AspNetCore.Authentication.Negotiate" Version="10.0.7" />`。
- `dotnet test` 還原時輸出：
  ```
  warning NU1903: 套件 'Microsoft.AspNetCore.Authentication.Negotiate' 10.0.7 具有已知的 高 嚴重性弱點
    https://github.com/advisories/GHSA-2p3q-h3hg-jcqq
    https://github.com/advisories/GHSA-8prm-248r-h957
  ```
- **後果**：這個套件正是本次修復 C1 所依賴的驗證機制核心；把身分驗證邊界建立在一個已知高嚴重性弱點的套件版本上，等於用一個新風險去補另一個風險。**在宣稱 C1 已修復之前，必須先確認/升級到已修補版本**，並將此檢查納入 CI（`dotnet list package --vulnerable` 或等效機制），否則 Phase 4 no-SDK 閘門的「乾淨」也會被這類供應鏈弱點掩蓋掉。

---

## 三、Warning 發現（沿用 attempt-1，逐項覆核仍成立）

- **W1**：`AllowLocalDevPasswordGrant` 隱性自動啟用路徑（`DonationDynamicsAccessBootstrap.cs:210-224`）未變更，仍只靠字串守門，建議改用明確環境旗標。
- **W2**：Embedded manifest/registry 信任驗證仍未實作（`EmbeddedServiceCollectionExtensions.cs` 無簽章驗證程式碼）。
- **W3**：Phase 5 讀取流程（`Package01FeeReadsEnabled`）已上線且切換/rollback runbook 紮實，但仍無自動化 legacy-vs-Package01 parity 測試。
- **W4**：Phase 4 測試矩陣覆蓋率已因本次修復略有改善（Gateway workload boundary 4 案例、product client handler 隔離 3 案例皆轉綠），但 implement.md 要求的多主機隔離/soak/fault matrix/idempotency ledger 狀態機等大部分項目仍缺（原因：這些本質上依賴 C2/C3 尚未存在的能力）。

---

## 四、Info 發現

- **I1（值得肯定）**：本次覆核期間完成的 C1/C4(Product 段) 修復是誠實且有紅綠證據的——`GatewayWorkloadBoundaryTests.cs` 現場執行 7/7 通過（含 `GatewayProductClientTests` 新增/擴充的 3 案例），且身分推導確實從「client body」搬到「server-side principal→workload 對映表」，符合 design §6 要求的方向。
- **I2**：`RequireDurableHostCoordinator` 這次的收緊（C1'）代表團隊選擇了「fail closed 而非 fail open」，這是正確的安全預設；但也意味著**當前工作目錄狀態下，Phase 4/5/6 的任何真實環境 smoke 測試都會立即失敗**，直到 P4-B（durable coordinator）就緒。這比 attempt-1 當時的狀態（假裝可以跑但實際上沒有隔離）更誠實，但需要立刻同步給團隊，避免被誤讀為迴歸。
- **I3**：attempt-1 提到的 CE 8.2/9.1 real-server smoke 缺口（I2/I4，ADFS IFD 卡關）在本次未變更範圍內，結論不變：目前沒有任何組織通過完整 real-server smoke。

---

## 五、SQL Server Lease Schema / 認證設計 / HttpClient 所有權設計 / SDK 遷移盤點 / TDD 排序

以下五節內容（SQL lease schema、Gateway 認證設計、HttpClient/Handler 所有權設計、SDK/WCF/SOAP 消費者盤點、Phase 4→6 TDD 相依排序）與 attempt-1 報告中對應章節**實質內容一致且未被本次程式碼變更推翻**，僅有以下三處需要疊加修正，其餘請以 attempt-1 報告原文為準（該文件已存放於同一 run 目錄 `claude-analyzer-attempt-1.stdout.md` 第 91-251 行）：

1. **P4-A（Gateway workload authentication）已完成**，可從 TDD 排序中標記為 done；下一個關鍵路徑是 **P4-B（durable coordinator）**，且現在有更強的急迫性——因為 C1' 顯示系統已經在「等待 P4-B」上做了 fail-closed 收緊，非 Testing 環境已被阻斷。
2. **HttpClient/Handler 所有權設計**：Product→Gateway 段的收斂方案（改用 `IHttpClientFactory` + `SetHandlerLifetime`）已經是**目前程式碼的實作**，不再是建議；Embedded 段（`EmbeddedProviders`）的收斂方案仍是待辦建議，原文不變。
3. **驗證缺口清單**新增一項：**Phase 4 的「Gateway 已具備 workload 身分隔離」宣稱現在可以誠實地成立**（原 attempt-1 第 2 點缺口已解決）；但新增缺口——**沒有任何自動化檢查防止把已知弱點套件版本鎖進去（C6）**，以及**沒有文件記錄「Gateway 目前在非 Testing 環境不可用」這個新的操作性事實（C1'）**。

---

**一句話總結（更新版）**：相對 attempt-1，這次覆核期間完成了 Phase 4 最關鍵的一塊拼圖——Gateway workload 身分驗證與 Product 端 HttpClient 生命週期治理，且有現場執行的 7/7 綠燈佐證，是誠實且紮實的進展。但同時暴露了一個新的誠實問題：`RequireDurableHostCoordinator` 收緊為預設 `true` 後，Gateway 在等到 durable coordinator（P4-B）落地之前，對任何非 Testing 環境都是不可用的（未捕捉例外、無文件記錄），而且新引入的 Negotiate 套件本身帶著已知高嚴重性弱點。C2（durable coordinator）、C3（idempotency/audit ledger）、C4-Embedded（EmbeddedProviders 生命週期）、C5（Phase 6 CI 閘門與現存 SDK 違規）四根柱子仍是設計文件而非程式碼，Phase 4/5/6 完成的宣稱目前仍不成立。

---
SESSION_ID: 67b83ee5-6a0d-464c-b78c-fc9286cf6158
