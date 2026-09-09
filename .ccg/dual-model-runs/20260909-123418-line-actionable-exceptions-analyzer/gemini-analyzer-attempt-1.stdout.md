# LINE 可行動例外告警系統 (LINE Actionable Exceptions Alert System) 架構與涵蓋率分析報告

## 1. 分析總覽 (Executive Summary)

本分析針對 ChurchReport 系統中 **「所有可行動/未處理/影響功能之例外狀況 (Actionable/Unhandled/Feature-impacting exceptions) 自動傳送至既有 LINE 管理者接收者」** 之需求進行深入評估。分析著重於在**不破壞現有業務例外處理行為、不洩漏 PII/敏感資訊、不產生 Request/Memory Leak、不造成同步執行緒阻塞 (Sync-Over-Async Blocking)、不引發無效 FirstChanceException 雜訊**的前提下，建立一套高效、安全且具備佇列與遞迴保護的非同步告警派發機制。

現行實作中存在數個影響穩定性與告警涵蓋率的重大問題（如 HTTP Middleware 順序錯位、`GetAwaiter().GetResult()` 同步阻塞、靜態 Log 遺漏 bridge、過度吞掉 catch 導致功能失效未告警等）。本報告提出了完整的架構最佳化與補強路徑，並以 **Critical / Warning / Info** 級別標示風險與具體建議。

---

## 2. UX 與系統維運影響評估 (UX & System Impact Assessment)

### 2.1 對使用者體驗 (UX) 的影響
- **Zero Latency Impact (零延遲感受)**：現行 `BaseChurchController.HandleError` 與 Middleware 均會在 HTTP 請求執行緒內同步等待 LINE API 回應 (`GetAwaiter().GetResult()`)。若 LINE API 發生網路延遲，前端使用者會感受明顯卡頓。轉為**共享有界非同步派發器 (Bounded Asynchronous Alert Dispatcher)** 後，例外記錄與告警派發將瞬時脫鉤，使用者可立即獲得流暢的錯誤提示頁面或 AJAX JSON 回應。
- **一致的錯誤回應 (Consistent Error View)**：保持既有的 AJAX JSON (`{ status: "error", message: ... }`) 與一般 Request 重導向至 `DisplayErrorView` 的 UI 行為，使用者不會遭遇斷崖式連線中斷。

### 2.2 對系統維運 (DevOps/SRE) 的影響
- **告警精準度 (Noise Reduction)**：明確過濾正常取消 (`OperationCanceledException` 伴隨 client disconnect) 及已成功重試/降級復原的例外，降低維運團隊的告警疲勞 (Alert Fatigue)。
- **隱私與資安防護 (Data Privacy & Compliance)**：告警訊息僅包含「安全元資料 (Safe Metadata)」，如 Incident ID、時間戳記、例外類別全名與 Stack Symbol 點。完全排除原始 Message、使用者姓名、 Session 狀態、HTTP Body/Header、Cookie、驗證碼與 CRM 憑證。

---

## 3. 現狀重大涵蓋缺口與風險評定 (Major Coverage Gaps & Findings)

### 🔴 Critical Findings (重大風險)

#### 1. HTTP Middleware 擺放順序錯位導致未處理例外漏報
- **具體位置**：`SpeechMessageProducts.ChurchReport/Startup.cs:849`
- **現狀分析**：
  ```csharp
  app.UseMiddleware<ChurchReport.Middleware.UnhandledExceptionLineNotificationMiddleware>();
  if (env.IsDevelopment()) { app.UseDeveloperExceptionPage(); }
  else { app.UseExceptionHandler("/Home/Error"); }
  ```
  在 ASP.NET Core 管線中，`UseExceptionHandler` 會捕獲後續控制器拋出的所有未處理 Exception 並進行內部重導向處理。由於 `UnhandledExceptionLineNotificationMiddleware` 被放置在 `UseExceptionHandler` **之前 (Outer)**，由 Controller 冒泡出來的例外會先被 `UseExceptionHandler` 截獲，導致 Middleware **完全無法收到 Controller 的未處理例外**！
- **影響**：所有非繼承自 `BaseChurchController` 或未在 Controller 主動 catch 的未處理例外，均無法觸發 LINE 告警。

#### 2. 同步阻塞呼叫 (Sync-Over-Async Thread Pool Starvation)
- **具體位置**：`SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:110`
- **現狀分析**：
  `_lineNotificationWorkflow.SendAsync(...).GetAwaiter().GetResult();` 在 HTTP Request 主執行緒上同步等待 LINE HTTP 請求。當高併發或 LINE 服務波動時，會導致 ThreadPool 執行緒耗盡 (Thread Starvation)，造成全站 502/504 響應超時。

#### 3. ILogger Provider Bridge 的無窮遞迴 (Infinite Recursive Notification Loop)
- **具體位置**：`SpeechMessageProducts.ChurchReport/Logging/TraceLoggerProvider.cs` 與未來新增之 LINE ILogger Provider Bridge。
- **現狀分析**：若將 `ILogger.LogError` / `LogCritical` 轉接至 LINE 告警派發器，而 LINE API 傳送過程失敗時若再呼叫 `ILogger.LogError` 記錄失敗，將引發無窮遞迴 (Infinite Loop)，最終導致 StackOverflowException 崩潰。

---

### 🟡 Warning Findings (警告事項)

#### 1. 被 Swallow (吞掉) 的功能失敗未被報告 (Feature Failure Swallowed)
- **具體位置**：
  - `MemberInfoController.cs` (多處 `catch { }` 或僅做 `TraceByLevel`)
  - `PersonalController.cs` (`catch (Exception memberEx) { }`)
  - `DonationKeyInDedicationService.cs` (`catch (Exception e) { Trace }`)
- **現狀分析**：多處業務邏輯（如會員資料同步、照片處理、奉獻開票、認證綁定）在 catch 後僅記錄 Debug Trace 或吞掉例外，雖然避免了頁面崩潰，但未告知管理者該關鍵功能已失效。

#### 2. 無界物件引用洩漏風險 (Object Graph Retention)
- **具體位置**：`UnhandledExceptionLineNotificationMiddleware.cs:30`
- **現狀分析**：若將 `Exception` 實例或 `HttpContext` 直接封裝丟入背景 Queue，會導致 Exception 物件圖 (含 Session、TargetSite、Request Headers) 長期停留在 Gen 2 GC 記憶體中，引發 Session / Memory Leakage。

#### 3. 缺乏佇列上界與 Drop 策略 (Unbounded Queue Risk)
- **現狀分析**：若系統發生爆發性異常（例如 CRM 連線中斷導致每秒數百個 Error），若告警佇列無上限，記憶體將快速暴漲。

---

### 🔵 Info Findings (一般資訊)

#### 1. 既有靜態門面相容性需求
- `ChurchReportLineAdminNotificationService.NotifyDefaultError` 被專案中 6+ 個地方廣泛呼叫（包含 `BaseChurchController`、`FeeManagementController`、`DonationPaymentManager`、`PollManager`）。重構時應保留此靜態介面作為 Adapter，底層轉接至共享非同步派發器。

---

## 4. 架構替代方案比較 (Options & Trade-offs)

| 評估維度 | 方案 A：直接在 Middleware/Controller 異步 Task.Run | 方案 B： AppDomain.FirstChanceException 攔截 | 方案 C (推薦)：共享有界非同步派發器 + Safe Metadata + ILogger Bridge |
| :--- | :--- | :--- | :--- |
| **執行緒與效能** | 產生大量 Fire-and-Forget Task，無法控制併發度與併發數 | 嚴重降低 CLR 效能，充斥大量正常控制流例外雜訊 | **高**（非同步 Bounded Channel、單一 Background Worker 消費） |
| **記憶體與 Leak 防護** | 易閉包捕獲 HttpContext / Session 活物件 | 會保留已拋出與已捕獲例外的完整物件圖 | **極佳**（當場萃取 Safe Metadata 純 Value 物件，無 Request 引用） |
| **告警涵蓋率** | 僅涵蓋有加 Task.Run 的程式碼點 | 涵蓋所有例外（包含內設 try/catch 恢復的非故障） | **精準涵蓋**（Unhandled + ILogger Error/Critical + 關鍵 Catch 手動通報） |
| **遞迴防護** | 無 | 難以防護 | **完善**（使用 `AsyncLocal<bool>` / `[ThreadStatic]` 抑制重入） |

---

## 5. 建議設計方案 (Recommended Solution Architecture)

整體架構設計應採用 **「單一責任派發器 (Single-Responsibility Alert Dispatcher)」**，實現完全脫鉤的例外告警流水線：

```
[ HTTP Exception Middleware ] ──┐
[ ILogger Error/Critical Bridge ] ├──> (Extract Safe Metadata) ──> [ Bounded Alert Channel ] ──> [ Background Worker ] ──> [ LINE Admin API ]
[ Explicit Catch Feature Helper ] ──┘                                 (Capacity=100, Safe Struct)        (Timeout, Reentrancy Guard)
```

### 5.1 核心元件設計規畫

#### 1. Safe Metadata 實體 (`AdminAlertIncident`)
僅保留純文字與數值欄位，避免捕獲 Request/Exception 實例：
```csharp
public sealed record AdminAlertIncident(
    string IncidentId,         // 例如 "INC-20260909-X7F" 或 Guid Short ID
    DateTimeOffset Timestamp,  // UTC 時間
    string ProductSource,      // "ChurchReport"
    string Category,           // "UnhandledException", "FeatureFailure", "SystemError"
    string ExceptionType,      // "System.NullReferenceException"
    string SourceSymbol        // "MemberInfoController.UpdateMember" (類別.方法名)
);
```

#### 2. 共享有界非同步派發服務 (`ILineAdminAlertDispatcher`)
- 底層使用 `System.Threading.Channels.Channel<AdminAlertIncident>`。
- 設定 `BoundedChannelOptions`: `Capacity = 100`, `FullMode = BoundedChannelFullMode.DropOldest`, `SingleReader = true`。
- 提供 `EnqueueAlert(string source, string category, Exception ex)` 方法：
  - 第一時間過濾 `OperationCanceledException`（當 `CancellationToken` 已發起取消時直接跳過）。
  - 當場反射提取 `ex.GetType().FullName` 與 Stack Top Frame Symbol，立即建立 `AdminAlertIncident` 入隊。

#### 3. 重入與遞迴防禦 (Reentrancy Guard)
在 Alert Dispatcher 發送 LINE API 或使用 ILogger 時，設定 `AsyncLocal<bool> s_isSendingNotification`：
```csharp
if (s_isSendingNotification.Value) return; // 防範無窮遞迴告警
try {
    s_isSendingNotification.Value = true;
    // 呼叫 LINE API ...
} finally {
    s_isSendingNotification.Value = false;
}
```

#### 4. ILogger Provider Bridge (`LineAdminLoggerProvider`)
- 實作 `ILoggerProvider` 與 `ILogger`。
- 僅在 `LogLevel.Error` 及 `LogLevel.Critical` 時觸發。
- 自動過濾 logger category 包含 `"LineMessagingProcessor"` 或 `"ChurchReport.Services.ChurchReportLineAdminNotificationService"` 的記錄，防止自循環告警。

#### 5. HTTP Middleware 修正與位置調整
- 將 `UnhandledExceptionLineNotificationMiddleware` 移至 `UseExceptionHandler` **內部** 或搭配 `IExceptionHandlerFeature`。
- 在 ASP.NET Core 標準管線中，最佳實作為在 `UseExceptionHandler` 所指向的錯誤 Action (`/Home/Error`) 或特定的 Diagnostic Listener 擷取 `IExceptionHandlerPathFeature.Error`。
- 保留 `BaseChurchController.HandleError`，但內部改呼叫 `ILineAdminAlertDispatcher.EnqueueAlert`，完全移除阻塞呼叫 `GetAwaiter().GetResult()`。

#### 6. 被吞例外與關鍵功能失敗手動通報 (Swallowed Feature Failures Helper)
針對如 `DonationPaymentManager` 或 `MemberInfoController` 中「catch 後回傳 fallback 但代表功能失敗」的路徑，提供統一擴充方法：
```csharp
ChurchReportLineAdminNotificationService.ReportFeatureFailure(
    source: "DonationPaymentManager", 
    featureName: "PaymentRegistration", 
    exception: ex);
```

---

## 6. 具體修復與實作路徑建議 (Concrete Implementation Steps)

主 Task 執行Session 進行代碼實作時，建議依循以下順序：

1. **建立 Alert 核心模型與介面 (Core Abstractions)**
   - 建立 `AdminAlertIncident.cs` (純 Safe Metadata)
   - 建立 `ILineAdminAlertDispatcher.cs` 與實作 `LineAdminAlertDispatcher.cs` (包含 Channel 與 Background Processing Service)

2. **升級 `ChurchReportLineAdminNotificationService` (Facade Refactoring)**
   - 保持既有靜態 API (`NotifyDefaultError`)，內部導向注入的 `ILineAdminAlertDispatcher` 或單例 Channel。
   - 移除 `GetAwaiter().GetResult()` 同步阻塞。

3. **修正 Middleware 順序與注入 (Startup.cs & Pipeline)**
   - 調整 `Startup.cs` 中 `UnhandledExceptionLineNotificationMiddleware` 的放置順序，確保能正確攔截未處理 Exception。
   - 註冊 `LineAdminLoggerProvider` 至 ASP.NET Core ILogging 系統。

4. **補強被吞下的關鍵 Feature Failure (Call-site Remediation)**
   - 檢視 `MemberInfoController.cs`、`PersonalController.cs` 與 `AuthenticationController.cs` 中僅印 Trace 的關鍵 catch 區塊，加入 `ReportFeatureFailure` 告警。

5. **安全與生命週期整合 (Shutdown & Cancellation)**
   - 確保 Dispatcher 在 `IHostedService.StopAsync` 時能於指定 Timeout (如 3 秒) 內完成 Graceful Flush，不拖延應用程式關閉。

---

## 7. 測試策略與驗證路徑 (Testing Strategy)

建議在 `ChurchReport.MemberInfo.Tests` 新增及執行以下測試：

### 7.1 單元與整合測試路徑 (Test Paths)
- **路徑**：`ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineAdminAlertDispatcherTests.cs`
  - 驗證 `OperationCanceledException` 被正確排除不告警。
  - 驗證敏感資安資料 (Message/PII/Session) 未出現在 `AdminAlertIncident` 之中。
  - 驗證 Channel 超出 Bounded Capacity (100) 時採 DropOldest 且不阻塞呼叫端。
  - 驗證 `AsyncLocal` 重入防禦可有效阻斷無限遞迴。
- **路徑**：`ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineAdminLoggerProviderTests.cs`
  - 驗證 `ILogger.LogError` 與 `ILogger.LogCritical` 會觸發 Alert Dispatcher。
  - 驗證 `ILogger.LogInformation` 與 `ILogger.LogWarning` 不會發送 LINE 告警。

### 7.2 測試執行指令 (Verification Commands)
```bash
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter "ChurchReportLineAdminNotificationServiceTests|LineAdminAlertDispatcherTests|LineAdminLoggerProviderTests"
```

---

## 8. 結論 (Decision Rationale)

1. **現有實現存在 3 大 Critical 問題**（Middleware 攔截無效、`GetAwaiter().GetResult()` 同步阻塞請求執行緒、潛在 ILogger 無限遞迴風險）。
2. **採用「共享有界非同步派發器 + Safe Metadata」架構** 能徹底解決記憶體洩漏、資安 PII 外洩與效能阻塞問題。
3. **保留既有 API 與錯誤呈現 UI**，可在零破壞現有業務 logic 的前提下達到 100% 可行動缺口的安全覆蓋。
