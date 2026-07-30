# 最終安全審查報告：Development Workload Binding Set 隔離修正

## 1. 審查結論
**PASS**

本次審查針對具名 binding set 關閉 Development 繼承 Central 授權的修正進行了全面的安全評估。評估結果顯示，所有安全邊界、startup fail-closed 契約、防禦性路徑解析以及測試覆蓋皆已正確實作，未發現任何 release blocker。

---

## 2. 核心安全契約檢查

### 2.1 Development → Central 授權繼承 (Authorization Inheritance)
* **檢查結果**：**不存在**。
* **具體機制**：`ConfigurationGatewayOperationAuthorizer` 經由讀取 `DynamicsGateway:ActiveWorkloadBindingSet` 指定的具名集合（例如 `Local`），並僅對該集合進行實例化（materialize）。`appsettings.Development.json` 中已將 selector 切換為 `Local`，因此 Local authorizer 僅會載入 `WorkloadBindingSets:Local`，完全不會讀取或聯集 `Central` 集合。
* **測試驗證**：`GatewayWorkloadBoundaryTests.cs` 中的 `Development_configuration_does_not_inherit_central_workload_binding` 測試已確實載入 base + Development JSON，並驗證 Central 的 principal 在 Local 環境下會被判定為 `unmapped-principal`（Succeeded=false）。

### 2.2 Selector Fallback 與 Path Injection
* **檢查結果**：**不存在**。
* **具體機制**：
  * **防範 Path Injection**：在 `ReadActiveBindingSections` 中，程式碼先列舉 `DynamicsGateway:WorkloadBindingSets` 的直接子節點（`GetChildren()`），再於記憶體中進行不區分大小寫的精確比對（`string.Equals`），而非直接將 selector 變數拼接進 configuration path。這有效防止了冒號（`:`）等路徑注入攻擊。
  * **防範 Fallback**：若 selector 為空白、wildcard、未定義、指向標量值（scalar-only）或空集合，`ReadActiveBindingSections` 將直接拋出 `InvalidOperationException`，使 Host 在啟動階段即 fail closed，絕不回退至 Central 或聯集所有集合。

### 2.3 Testing → Central 授權繼承
* **檢查結果**：**不存在**。
* **具體機制**：測試工廠（`CreateFactory`）在設定中明確將 `ActiveWorkloadBindingSet` 指定為 `Testing`，並動態建構 `DynamicsGateway:WorkloadBindingSets:Testing` 集合，實現了測試環境與 Central 授權的物理隔離。

### 2.4 生命週期與資源洩漏 (Lifecycle & Resource Leak)
* **檢查結果**：**不存在**。
* **具體機制**：`ConfigurationGatewayOperationAuthorizer` 註冊為 Singleton，其內部狀態在構造函數中即被編譯為唯讀的 `FrozenDictionary`。在 Request 熱路徑上僅進行 $O(1)$ 的唯讀查找，無任何 lock、reload subscription、背景 Task、Timer 或連線資源配置，因此在 Host 關閉時無需額外的 cleanup 處理。

### 2.5 繁體中文註解與 UTF-8 編碼契約
* **檢查結果**：**符合契約**。
* **具體機制**：
  * 所有新增與修改的程式碼（如 `ConfigurationGatewayOperationAuthorizer.cs`）皆包含詳盡的繁體中文 XML 註解，深入說明了信任邊界、唯一擁有者、fail-closed 行為與效能取捨。
  * 檔案編碼經確認皆為 UTF-8 without BOM，換行格式為 CRLF，且包含 final CRLF。部分工具讀取時產生的亂碼已證實為解碼器誤判，實際檔案位元組正確無誤。

---

## 3. 發現分級

### Critical
* **無**。未發現任何安全漏洞或 release blocker。

### Warning
* **無**。先前關於 .NET Configuration 陣列合併（array merge）的 Warning 已透過改用具名 binding set 結構徹底解決。

### Info

#### Info 1: 核心功能開關狀態確認
* **檔案**：`SpeechMessageProducts.ChurchReport/appsettings.json` 等
* **根因**：`Package01FeeReadsEnabled` 必須維持為 `false`，以確保 Phase 4/5/6 的安全邊界。
* **現狀**：經確認，該 flag 在所有環境設定檔中皆維持為 `false`，且測試 `Development_configuration_selects_local_gateway_while_package01_reads_remain_disabled` 已對此進行常態斷言。

#### Info 2: Data8 與舊版 SDK 專案保留
* **檔案**：`PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`
* **根因**：根據規劃，Data8 專案與 `PowerPlatform.Dataverse.Client` 必須保留至 Phase 6 Gate 前，不得提前移除。
* **現狀**：專案結構完整保留，未受本次變更影響。
