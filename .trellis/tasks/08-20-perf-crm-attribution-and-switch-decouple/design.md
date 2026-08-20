# 技術設計

## 一、CRM 歸因修復

### 現況資料流（壞的）

```
DI: TryAddScoped<IOrganizationService, GatewayOrganizationService>()
        ↓  (ServiceCollectionExtensions.cs:91)
ToolUtilityClass(organizationService, ...)
        ├─ m_Crm2011OrganizationService = organizationService
        │        ↑ TimedToolUtilityProvider 事後把這個欄位換成裝飾器
        │          但只有 1 個呼叫點會用到
        └─ _facade = new ToolUtilityFacade(organizationService)
                 ↑ 捕獲「未裝飾」的原始參考，158 個呼叫點都走這裡
                        ↓
                 Lazy<IEntityQueryService> 等子服務再各自捕獲一次
```

裝飾器裝在支線上，主幹（`_facade`）完全沒被裝飾。

### 目標資料流

把裝飾動作往上游移到 **DI 解析點**，讓 `ToolUtilityClass` 的建構式參數本身就是裝飾過的：

```
DI: IOrganizationService → GatewayOrganizationService
        ↓  ChurchReport Startup 於 #if DEBUG 置換 descriptor
    TimedOrganizationService(GatewayOrganizationService, IHttpContextAccessor)
        ↓
ToolUtilityClass(decorated, ...)
        ├─ m_Crm2011OrganizationService = decorated   ✅
        └─ _facade = new ToolUtilityFacade(decorated) ✅
                 └─ 所有 Lazy 子服務捕獲的也是 decorated ✅
```

裝飾發生在唯一入口，結構上不可能被繞過。

### 為什麼不用其他方案

| 方案 | 否決理由 |
|---|---|
| 在 `ToolUtilityFacade` 內部再包一層 | 要改共用函式庫 `ToolUtility`，影響非 ChurchReport 的消費端；且子服務各自捕獲，包一層仍會漏 |
| 事後改寫 `_facade._organizationService` | `Lazy<>` 子服務可能已求值並捕獲舊參考，無法回溯 |
| 改在 `GatewayOrganizationService` 內部埋計時 | 把 ChurchReport 的診斷型別滲入共用函式庫，違反現有分層 |

### 實作位置

`SpeechMessageProducts.ChurchReport/Startup.cs`，`ConfigureServices` 內的 `#if DEBUG` 區塊。

**沿用該檔 425–451 行既有的 descriptor 置換模式**（目前用於 `IToolUtilityProvider`）：
找出 `IOrganizationService` 的 `ServiceDescriptor`，取出原本的實作
（`ImplementationFactory` / `ImplementationInstance` / `ImplementationType` 三種情形），
以相同 `Lifetime` 重新註冊為回傳 `TimedOrganizationService` 的工廠。

必須在 ToolUtility 的 DI 擴充方法**之後**執行，否則 `TryAddScoped` 尚未登記，找不到 descriptor。

### 生命週期不變量

- 原註冊為 `Scoped`；置換後必須維持 `Scoped`，不得升為 Singleton
  ——`TimedOrganizationService` 持有 `_inner`，若跨請求共用會讓 lease 洩漏到請求邊界外。
- `TimedOrganizationService.Profiler` 透過 `IHttpContextAccessor` 動態取得，
  本身不持有 request 狀態，因此 Scoped 是安全的。
- `TimedOrganizationService.Inner` 必須保留——連線池 `ReleaseConnection` 依賴它解包歸還真正連線。

### `TimedToolUtilityProvider` 的處置

改在 DI 層裝飾後，`TimedToolUtilityProvider` 的包裝條件
（`m_Crm2011OrganizationService is not TimedOrganizationService`）將永遠為 false，成為死碼。

**處置：移除該型別及其在 `Startup.cs` 的註冊區塊。**
保留它會造成兩處裝飾邏輯並存，未來維護者無法判斷哪一處才是有效的。

若移除範圍過大而有風險，允許保留檔案但必須在檔頭註解明確標記其已失效及原因——
不允許「留著不管」。

### 已知限制（必須寫入交接報告，不得隱瞞）

`TimedOrganizationService` 只實作 `IOrganizationService` 的 8 個方法。
任何**不經由 `IOrganizationService` 介面**的 CRM 存取路徑（若存在）仍不會被計入。
因此 AC-1 要求與 JSONL 的 `crmCount` 做**逐請求交叉比對**，而不是只確認「非零」。
若比對出現系統性落差，必須如實回報落差幅度與可能來源，不得調整門檻掩蓋。

## 二、Session 開關解耦

### 設計

在 `DiagnosticTraceOptions` 增加一個獨立屬性（建議名 `SessionVerbose`），
來源為設定鍵 `DiagnosticsTrace:SessionVerbose`，**預設 `false`**。

`Startup.cs:158` 改為：

```csharp
ChurchReport.Diagnostics.SessionDiagnosticsSwitch.Enabled =
    _diagnosticTraceOptions.SessionVerbose;
```

`ProfilingSwitch.Enabled`（157 行）**維持綁定 `Enabled` 不變**——
profiling 是本任務要修好的量測主體，不應預設關閉。

### 必須維持的既有不變量

- `SessionVerbose` 只能為 `allowEnabled && configuredSessionVerbose`，
  沿用 `FromConfiguration` 既有的 Release 防線寫法。
- `CreateDisabled` 產生的物件，`SessionVerbose` 必須為 `false`。
- `SessionDiagnosticsSwitch` 維持 `#if DEBUG`、維持 `volatile bool`、
  維持不持有任何 request / Session / 使用者 / 租戶狀態。
- `WriteSessionDiagnostic` 的 `[Conditional("DEBUG")]` 必須保留
  ——它確保 Release 連 interpolated string 的參數評估都被編譯期移除。

### 設定檔異動

`appsettings.json` 的 `DiagnosticsTrace` 區塊補上 `"SessionVerbose": false`。
`appsettings.Development.json` **不要**加這個鍵，讓它走預設 false——
這樣才能驗證 AC-2 的預設行為。

## 三、測試

- `DiagnosticTraceOptionsTests`：補測 `SessionVerbose` 的預設值、
  設定讀取、`allowEnabled: false` 時強制為 false、`CreateDisabled` 為 false。
- `SessionDiagnosticsSwitchTests`：既有測試不得刪除；若其斷言綁定舊耦合行為，
  更新為對應新語意並在測試註解說明變更原因。
- CRM 歸因的正確性**無法用單元測試證明**（需真實 DI 圖與請求上下文），
  因此以 AC-1 的實跑 trace 交叉比對作為驗收依據。
  可補一個 DI 組裝測試，驗證解析出的 `IOrganizationService` 為 `TimedOrganizationService`。
