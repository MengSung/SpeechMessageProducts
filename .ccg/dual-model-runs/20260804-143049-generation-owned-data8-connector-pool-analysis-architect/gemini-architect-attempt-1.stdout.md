### 1. Analysis (架構評估與風險分析)

#### Critical Findings

##### 1. SDK 依賴洩漏至 Abstraction 邊界
- **檔案路徑**: `SpeechMessage.Dynamics.Abstractions/Configuration/ResolvedProfile.cs` (或新增的 `IConnectorLease` 介面)
- **說明**: 根據 PRD 規範，`SpeechMessage.Dynamics.Abstractions` 專案必須保持 **SDK-free**（不依賴 `Microsoft.Xrm.Sdk`）。然而，租約介面 `IConnectorLease` 需要暴露 Dynamics 的服務實例。若直接將其宣告為 `IOrganizationService`，會迫使 `Abstractions` 專案引入 CRM SDK 依賴。
- **後果**: 破壞架構的隔離性，導致所有引用 `Abstractions` 的輕量化端點都必須載入重型的 CRM SDK 程式集。
- **建議修正**: 在 `IConnectorLease` 中將 `Service` 屬性宣告為 `object`，並在具體的 `Data8` 實作專案中轉型為 `IOrganizationService`；或者定義一個無 SDK 依賴的包裝介面。

##### 2. WCF 通訊物件處置異常導致連線池 Slot 永久洩漏
- **檔案路徑**: `PowerPlatform.Dataverse.Client/OnPremiseClient.cs` 與 `ToolUtility/ConnectionOperations/CrmConnectionPool.cs`
- **說明**: `OnPremiseClient` 在處置 WCF Channel 與 ChannelFactory 時，若 `Close()` 失敗會拋出異常並嘗試 `Abort()`，最終可能拋出 `AggregateException`。若連線池在回收 Faulted 連線或進行 Dispose 時，未能妥善包覆 `try-finally` 釋放 `SemaphoreSlim` 或遞減 `_currentSize`，將導致該連線池的可用 Slot 永久被佔用。
- **後果**: 引發 `pool.acquire-timeout` (503) 錯誤，系統可用連線數逐漸歸零。
- **建議修正**: 確保在處置 `OnPremiseClient` 時，不論是否拋出異常，都必須在 `finally` 區塊中釋放容量計數與信號量。

---

#### Warning Findings

##### 3. 跨世代 (Multi-Generation) 租約歸還與生命週期混亂
- **檔案路徑**: 新增的 `IConnectorPool` 與 `IConnectorLease` 實作。
- **說明**: 當 Profile 更新時會產生新的 `GenerationId`。舊世代的 Pool 進入 Draining 狀態。若租約（Lease）歸還時沒有明確綁定其所屬的 `GenerationId` 或 Pool 實例，可能會被錯誤地歸還給新世代的 Pool。
- **後果**: 導致新世代使用了舊的憑證或設定，或者導致舊世代的 Pool 無法偵測到租約歸零而無法進行 deterministic 釋放。
- **建議修正**: `IConnectorLease` 必須持有其來源 `IConnectorPool` 的弱引用（WeakReference）或明確的 `GenerationId`，歸還時必須歸還至原世代的 Pool。

##### 4. Faulted/Cancelled/Expired 租約未被即時逐出
- **檔案路徑**: `IConnectorLease.MarkFaulted` 實作與租約釋放邏輯。
- **說明**: 當操作被取消（Cancelled）、逾時（Expired）或發生傳輸錯誤（Faulted）時，該連線可能已處於損壞狀態。若直接放回 Pool 中重用，會導致下一次 Acquire 拿到失效連線。
- **後果**: 造成後續請求連續失敗，降低系統可用性。
- **建議修正**: 在 `IConnectorLease` 處置時，檢查是否被標記為 `Faulted`。若是，則將 underlying `OnPremiseClient` 進行 `Dispose` 銷毀，不放回 `ConcurrentBag`，並釋放 Slot。

---

#### Info Findings

##### 5. 路由僅限 ResolvedProfile.ConnectorKind
- **檔案路徑**: `SpeechMessage.Dynamics.ControlPlane/Runtime/ProfileRoutedOperationExecutor.cs`
- **說明**: 路由邏輯必須嚴格依據 `ResolvedProfile.ConnectorKind` 進行分流，不可包含任何 Request-time 參數，亦不可在 Data8 失敗時自動 fallback 到 Official Worker。
- **後果**: 違反 fail-closed 原則，可能導致非預期的跨版本調用。
- **建議修正**: 確保 `IConnectorRouter` 實作中僅讀取 `ResolvedProfile.ConnectorKind`，若不匹配則直接拋出 `profile.connector-incompatible` 錯誤。

---

### 2. Architecture Decision (架構決策)

- **決策 1: SDK-Free 抽象邊界**
  - **方案**: 在 `SpeechMessage.Dynamics.Abstractions` 中定義 `IConnectorPool` 與 `IConnectorLease`，其中 `IConnectorLease.Service` 宣告為 `object`。
  - **理由**: 避免 `Abstractions` 專案引入 `Microsoft.Xrm.Sdk` 依賴，保持合約的純粹性。
  - **替代方案**: 引入一個包裝介面 `IOrganizationServiceWrapper`，但這會增加不必要的轉接開銷與複雜度。
  - **潛在副作用**: 呼叫端在使用 `Service` 時需要進行類型轉換（Cast to `IOrganizationService`），但此轉換僅發生在具體的執行器內部，風險可控。

- **決策 2: 租約與世代綁定 (Generation-Bound Leases)**
  - **方案**: 租約實作類別必須持有建立它的 `IConnectorPool` 實例引用，並在 `DisposeAsync` 時將自身歸還給該特定實例，而非全域的 Pool 管理器。
  - **理由**: 確保舊世代的連線在釋放後能正確回到舊 Pool 進行銷毀，達成 deterministic drain。

---

### 3. Implementation Plan (實作計畫與虛擬程式碼)

#### 步驟 1: 在 Abstractions 專案中定義 SDK-Free 合約
新增 `IConnectorPool`、`IConnectorLease` 與 `IConnectorRouter` 介面。

#### 步驟 2: 實作 Data8ConnectorPool 與 Data8ConnectorLease
在 `SpeechMessage.Dynamics.Connectors.Data8` 專案中實作上述介面，並包裝 `OnPremiseClient`。

#### 步驟 3: 修改 ProfileRoutedOperationExecutor
根據 `ResolvedProfile.ConnectorKind` 進行路由分流。

#### 虛擬程式碼與 Diff 提案：

```diff
--- /dev/null
+++ b/SpeechMessage.Dynamics.Abstractions/Execution/IConnectorPool.cs
@@ -0,0 +1,21 @@
+using System;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace SpeechMessage.Dynamics.Abstractions.Execution;
+
+public interface IConnectorPool : IAsyncDisposable, IDisposable
+{
+    Task<IConnectorLease> AcquireAsync(CancellationToken ct);
+}
+
+public interface IConnectorLease : IAsyncDisposable
+{
+    object Service { get; }
+    long GenerationId { get; }
+    void MarkFaulted(Exception? cause);
+}
+
+public interface IConnectorRouter
+{
+    IConnectorPool Resolve(ResolvedProfile profile);
+}
```

```diff
--- a/SpeechMessage.Dynamics.ControlPlane/Runtime/ProfileRoutedOperationExecutor.cs
+++ b/SpeechMessage.Dynamics.ControlPlane/Runtime/ProfileRoutedOperationExecutor.cs
@@ -49,6 +49,7 @@ public sealed class ProfileRoutedOperationExecutor : IDynamicsOperationExecutor
     private readonly IOrganizationAdmissionManager _admissionManager;
+    private readonly IConnectorRouter _connectorRouter;
 
     public ProfileRoutedOperationExecutor(
         IOrganizationAdmissionManager admissionManager,
+        IConnectorRouter connectorRouter)
     {
         _admissionManager = admissionManager;
+        _connectorRouter = connectorRouter;
     }
```

---

### 4. Considerations (考量事項)

- **效能 (Performance)**:
  - 租約的獲取與歸還應使用無鎖或低鎖結構（如 `ConcurrentBag` 與 `SemaphoreSlim`），避免在高併發下成為瓶頸。
  - 舊世代 Pool 在 Draining 時，應在租約數歸零時立即觸發處置，釋放 WCF 連線與記憶體。

- **可維護性 (Maintainability)**:
  - 透過 `IConnectorRouter` 進行解耦，未來若新增其他 ConnectorKind（如 `OfficialCrm82Worker`），只需擴充 Router 註冊，不需修改執行器核心邏輯。

- **無障礙與合規性 (Accessibility & Compliance)**:
  - 確保日誌與 Metrics 記錄中絕不洩漏敏感資訊（如 Token、憑證或 CRM 實體 GUID），符合資安合規要求。
