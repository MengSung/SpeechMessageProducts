# 技術設計

## 權威來源

本任務的完整技術設計位於：

**`docs/dynamics-connection-management-spec.md`**（13 章，含型別契約、錯誤碼、測試要求、驗收條件）

本檔不重複該規格內容，僅記錄設計決策摘要與對應關係，避免兩處失同步。

## 架構圖

- `docs/dynamics-design-merged.drawio` — 綜合版設計（2 頁：架構 + 取捨紀錄與設定範例）
- `docs/dynamics-design-comparison.drawio` — 與前一版設計的差異對照
- `docs/speechmessageproducts-architecture-20260804.drawio` — 全方案現況架構

## 設計決策摘要

### D-1　兩個獨立維度

`ConnectionMode`（共用核心跑在哪個進程）與 `ConnectorKind`（傳輸實作）是**正交**的，任意組合皆合法，除非 §5.2 相容性矩陣否決。

```
ConnectionMode  ∈ { Embedded, DedicatedGateway, CentralGateway }
ConnectorKind   ∈ { Data8, OfficialCrm82Worker, OfficialCrm91Worker }
```

### D-2　ConnectionMode 決定「治理層跑在哪」，不決定「要不要治理」

三種模式在 `ControlPlane` 之後共用**完全相同的程式碼路徑**。`Embedded` 省略的只有 HTTP 傳輸；`DynamicsOperationContract` 與 `RequestGuard` 一個都不能少（規格規則 3.1、C1）。

### D-3　Data8 為永久 Connector

`ConnectorKind.Data8` 是 P1～P4 唯一有實作且進 CI 的傳輸。`OfficialCrm82Worker`／`OfficialCrm91Worker` 保留既有 63 檔資產作為**編譯期擴充點**：不進 CI、不寫平行行為測試、不承諾兩種 Connector 行為一致。

### D-4　Pool 與 Capacity 是不同的切分維度

| 維度 | 鍵 | 意義 |
| --- | --- | --- |
| Organization Capacity | `OrganizationId` | 同一實體 Organization 的聚合總預算；不同 ProfileAlias 指向同一 Org 時共用 |
| 實體 Pool | `(ProfileAlias, GenerationId)` | 實際連線物件容器；按世代隔離，防止不同 Connector／憑證／設定混用 |

### D-5　SQL 不是必要條件

`InMemoryCapacityCoordinator` 是預設值，適用 `Embedded`、`DedicatedGateway` 與單節點 `CentralGateway`。`IDistributedCapacityCoordinator`（既有 SQL 實作可直接用）僅在多節點 Central 才需要（規格規則 6.9、C2）。

### D-6　ProfileAlias 是唯一產品選擇鍵

產品只提供 `ProfileAlias`。Profile 解析出 `OrganizationAlias` → `OrganizationCatalog` 取得 `OrganizationId`、`CeVersion`、`ConnectorKind`、`CredentialReference`。

同一 Organization 若要同時啟用不同 Connector，必須建立不同的 ProfileAlias（例如 `sunnyvalechback` 與 `sunnyvalechback-official`）。禁止 Request-time 切換（規格規則 4.3、5.1）。

### D-7　借出必須在取得 Permit 之後才解析世代

排隊期間不得持有任何連線物件、Runtime 或 Token Provider —— 否則舊世代無法收斂（規格規則 7.1）。

## 現有資產對應

完整對應表見規格 §12。摘要：

| 動作 | 元件 |
| --- | --- |
| **保留不動** | `Abstractions/Operations`、`ProductClient`、`Gateway` HTTP 主機、`ControlPlane/Capacity`（Admission、Coordinator）、`ControlPlane/Runtime`（世代管理）、63 檔 Worker 資產 |
| **重構** | `DynamicsExecutionMode` → `ConnectionMode`（三值）、`ProfileRoutedOperationExecutor` → `IConnectorRouter`、`Gateway/Security/*Authorizer` → 抽出 `IRequestGuard` |
| **新增** | `ConnectorKind`／`CeVersion` 列舉、`OrganizationCatalog`、`IProfileResolver`／`ResolvedProfile`、`SpeechMessage.Dynamics.Connectors.Data8` 專案 |
| **移植改造** | `ToolUtility/ConnectionOperations/CrmConnectionPool` → `Data8ConnectorPool`（複製後改造，原檔不動） |
| **重寫** | `Embedded/DependencyInjection/EmbeddedServiceCollectionExtensions`（目前無條件拋例外） |
| **修正** | `PowerPlatform.Dataverse.Client/OnPremiseClient.cs`（`IDisposable` ＋ 視 A1 結果的 `_sdkMajorVersion`） |

## 已知技術風險

見規格 §11。兩項必須處理：

1. **`OnPremiseClient` 未實作 `IDisposable`** —— WCF channel 與 ChannelFactory 從未關閉。這是「無 Memory Leakage」底線目前唯一不成立之處，屬必修。
2. **`_sdkMajorVersion` 為 `static readonly`** —— 全進程共用，影響 8.2／9.1 同進程共存的 WSDL 探索。是否必修取決於前置項 A1 的測試結果。
