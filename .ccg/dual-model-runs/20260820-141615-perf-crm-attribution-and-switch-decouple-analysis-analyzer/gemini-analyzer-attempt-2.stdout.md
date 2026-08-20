# 系統架構與 ASP.NET Core DI／資源生命週期審查報告

本報告針對 **Perf CRM 歸因與 Session 診斷開關解耦** 任務的設計方案進行實作前完整性審查。本審查遵循 `AGENTS.md` 的資源生命週期、跨使用者隔離（Cross-User Isolation）與效能優化規範，不修改任何檔案。

---

## 一、 關鍵審查發現 (Review Findings)

### 1. Critical (會阻止依設計安全實作或驗收的致命問題)

*   **C-1: DI 裝飾器生命週期必須嚴格繼承 Scoped，嚴禁 Singleton 化**
    *   **具體檔案/型別**：`SpeechMessageProducts.ChurchReport/Startup.cs` 內置換 `IOrganizationService` 的 ServiceDescriptor 區塊。
    *   **判定原因**：`IOrganizationService` 承載當前請求的 Dataverse 連線與上下文。在 `Startup.cs` 中置換 `ServiceDescriptor` 時，必須確保新註冊的描述符生命週期與原描述符完全一致（即 `ServiceLifetime.Scoped`）。若因誤用或複製貼上導致其被註冊為 `Singleton`，將導致 `TimedOrganizationService` 跨請求共享，造成嚴重的跨使用者、跨租戶連線與狀態洩漏，直接違反 `AGENTS.md` 的隔離契約。
*   **C-2: 必須保留 `TimedOrganizationService.Inner` 屬性以防連線池洩漏**
    *   **具體檔案/型別**：`SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/TimedOrganizationService.cs` 的 `Inner` 屬性。
    *   **判定原因**：系統的連線池回收機制（如 `ReleaseConnection`）在釋放連線時，會偵測連線是否為 `TimedOrganizationService`，若是，則必須透過 `.Inner` 屬性解包出真實的底層連線以歸還給連線池。若在重構中遺失此屬性或未正確解包，將導致連線無法歸還，造成連線池迅速耗盡與資源洩漏。

### 2. Warning (應在實作或測試時特別核對的潛在問題)

*   **W-1: DI 替換順序必須位於 `AddToolUtility()` 之後**
    *   **具體檔案/型別**：`SpeechMessageProducts.ChurchReport/Startup.cs` 中的 `services.AddToolUtility()` 與 `#if DEBUG` 裝飾器置換區塊。
    *   **判定原因**：`AddToolUtility()` 內部使用 `TryAddScoped` 註冊 `IOrganizationService`。若裝飾器置換邏輯被錯誤移至 `services.AddToolUtility()` 之前執行，`services.LastOrDefault(d => d.ServiceType == typeof(IOrganizationService))` 將回傳 `null`，導致裝飾器未被掛載，CRM 效能歸因依然為零。
*   **W-2: `SessionVerbose` 必須在 Release 組態下編譯期防閉**
    *   **具體檔案/型別**：`ToolUtility/Diagnostics/DiagnosticTraceOptions.cs` 與 `SessionDiagnosticsSwitch`。
    *   **判定原因**：`SessionVerbose` 產生的日誌量極大（佔原日誌 88%）。除了在 `DiagnosticTraceOptions.CreateDisabled` 中強制設為 `false` 外，所有寫入 Session 診斷日誌的方法必須維持 `[Conditional("DEBUG")]` 標記，確保 Release 編譯時編譯器會完全移除調用端程式碼（包括字串插值產生的記憶體分配），避免影響生產環境效能。

### 3. Info (一般性建議與優化資訊)

*   **I-1: 徹底移除死碼 `TimedToolUtilityProvider`**
    *   **具體檔案/型別**：`SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/TimedToolUtilityProvider.cs`。
    *   **判定原因**：當 `IOrganizationService` 改在 DI 容器層級直接裝飾後，`ToolUtilityClass` 及其內部的 `_facade` 在建構時即已取得已裝飾的 `TimedOrganizationService`。原先用於事後欄位替換的 `TimedToolUtilityProvider` 已無任何存在價值，應整檔刪除並清理 `Startup.cs` 中的註冊代碼。

---

## 二、 UX 與設計系統評估 (UX & Design Evaluation)

1.  **使用者影響評估 (UX Analysis)**：
    *   **診斷精確性**：修正後，`[Perf] crm{n,ms}` 將能精確反映真實的 CRM 呼叫次數與耗時，消除 `[Perf-Gap]` 中虛假的未歸因時間（Gap），使維運人員能精確定位效能瓶頸。
    *   **日誌減量**：預設關閉 `SessionVerbose` 可減少約 88% 的無效日誌雜訊，避免磁碟 I/O 瓶頸，提升系統整體穩定度。
2.  **設計系統一致性 (Design Evaluation)**：
    *   將裝飾器模式（Decorator Pattern）從「事後反射/欄位替換」移至「DI 組合根（Composition Root）」，符合 ASP.NET Core 標準的依賴注入設計模式，提升了架構的可維護性與一致性。

---

## 三、 建議的最小可驗證測試形狀 (Verifiable Test Shape)

為確保 DI 裝飾與生命週期正確，建議於 `ToolUtility.Dataverse.Tests` 專案中新增以下單元測試：

```csharp
[Fact]
public void IOrganizationService_ShouldBeDecoratedAsTimedOrganizationService_InDebug()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddHttpContextAccessor();
    
    // 模擬 Startup.cs 中的註冊行為
    services.AddToolUtility();
    
    // 模擬 #if DEBUG 中的置換邏輯
    var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IOrganizationService));
    Assert.NotNull(descriptor);
    Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime); // 驗證必須為 Scoped
    
    services.Remove(descriptor);
    services.Add(new ServiceDescriptor(
        typeof(IOrganizationService),
        sp => {
            // 模擬建立 inner 服務
            var inner = new Mock<IOrganizationService>().Object; 
            var http = sp.GetRequiredService<IHttpContextAccessor>();
            return new TimedOrganizationService(inner, http);
        },
        descriptor.Lifetime));

    var provider = services.BuildServiceProvider();

    // Act & Assert
    using (var scope = provider.CreateScope())
    {
        var resolvedService = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
        
        // 1. 驗證解析出來的是 TimedOrganizationService 裝飾器
        var timedService = Assert.IsType<TimedOrganizationService>(resolvedService);
        
        // 2. 驗證 Inner 屬性不為 null 且可正確解包
        Assert.NotNull(timedService.Inner);
        
        // 3. 驗證 ToolUtilityClass 注入的也是同一個已裝飾的實例
        var toolUtility = scope.ServiceProvider.GetRequiredService<ToolUtilityClass>();
        // 透過反射或公開欄位驗證 toolUtility 內部的 service 為 TimedOrganizationService
    }
}
```

---

## 四、 具體檢查清單 (Implementation Checklist)

### 1. DI Descriptor 置換與 Scope 檢查
*   [ ] 檢查 `Startup.cs` 中置換 `IOrganizationService` 的程式碼，確認其 `ServiceDescriptor` 的 `Lifetime` 屬性嚴格設為 `ServiceLifetime.Scoped`。
*   [ ] 確認置換邏輯位於 `services.AddToolUtility()` 之後。
*   [ ] 確認已完全移除 `TimedToolUtilityProvider` 的註冊與檔案。

### 2. 釋放鏈與連線池檢查
*   [ ] 檢查 `TimedOrganizationService.cs`，確認 `Inner` 屬性公開且正確回傳建構式傳入的 `IOrganizationService`。
*   [ ] 檢查連線池回收代碼，確認無任何阻礙 `timed.Inner` 解包的邏輯。

### 3. Session 診斷開關與日誌檢查
*   [ ] 檢查 `DiagnosticTraceOptions.cs`，確認 `SessionVerbose` 預設為 `false`，且在 `CreateDisabled` 中強制為 `false`。
*   [ ] 檢查 `appsettings.json`，確認 `DiagnosticsTrace:SessionVerbose` 設為 `false`。
*   [ ] 檢查 `SessionDiagnosticsSwitch` 相關的日誌輸出方法，確認皆標記有 `[Conditional("DEBUG")]`。

### 4. 實跑 Trace 交叉比對驗證 (AC-1 / AC-2)
*   [ ] 啟動應用程式並正常關閉，確認 `Trace.log` 中 `[GetCurrentSessionId]` 等日誌行數為 **0**（當 `SessionVerbose` 為 `false` 時）。
*   [ ] 觸發 CRM 呼叫，確認 `Trace.log` 中的 `[Perf] crm{n=X,ms=Y}` 其 `n` 大於 0，且與 `dataverse-trace.jsonl` 中的 `crmCount` 數值完全一致。
*   [ ] 確認 `[Perf-Gap]` 未再出現因未扣除 CRM 時間而產生的虛假大 Gap。
