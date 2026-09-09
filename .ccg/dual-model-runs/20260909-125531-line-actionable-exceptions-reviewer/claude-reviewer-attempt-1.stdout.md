# 程式碼審查報告：line-actionable-exceptions（Claude Reviewer Pass）

> 審查對象：commit `ed1ddb86`（原工作區變更已由主線 session 提交）。本次為唯讀審查，未做任何修改。已讀取 `ExceptionDiagnostics.cs`、`ExceptionReporting.cs`、`ExceptionLoggerProvider.cs`、`LineExceptionSender.cs`、`UnhandledExceptionLineNotificationMiddleware.cs`、`Program.cs`／`Startup.cs`／`BaseChurchController.cs`／`ChurchReportLineAdminNotificationService.cs` 全部 diff、對應測試，以及 4 個實際呼叫舊 facade 的產品程式碼（`FeeManagementController.cs`、`PollManager.cs`、`DonationPaymentManager.cs`）。已交叉核對 Gemini 於 `.ccg/dual-model-runs/20260909-125531-.../gemini-reviewer-attempt-1.stdout.md` 的既有結論，以下標示「(沿用 Gemini)」的項目為驗證後確認同意，其餘為本次新增發現。

---

## 🔴 Critical

### 1.（新發現）`OperationCanceledException` 排除邏輯會誤吞真正的逾時失敗，違反「逾時仍須記錄」契約
- **位置**：`ToolUtility/Diagnostics/ExceptionDiagnostics.cs:76-82`
```csharp
if (exception is OperationCanceledException canceled &&
    (cancellationToken.IsCancellationRequested || canceled.CancellationToken.IsCancellationRequested))
{
    _reported.Add(exception, new object());
    return false;
}
```
- **問題**：這裡用 `||` 把「呼叫端傳入的 `cancellationToken`」與「例外物件自帶的 `canceled.CancellationToken`」OR 在一起。但只要是**任何正常途徑丟出**的 `OperationCanceledException`／`TaskCanceledException`（例如 `CancellationTokenSource.CancelAfter` 逾時、`HttpClient.Timeout` 逾時、`token.ThrowIfCancellationRequested()`），其**自帶**的 `CancellationToken.IsCancellationRequested` 幾乎必定為 `true`——這是 .NET 建構這類例外時的固有行為，與呼叫端關不關心這次取消完全無關。也就是說，第二個 OR 條件幾乎永遠成立，使第一個條件（呼叫端傳入的 `cancellationToken`）形同虛設。
  - `BaseChurchController.HandleError(exception, methodName)`（`Controllers/BaseChurchController.cs:384`）呼叫 `ExceptionReporting.Report(exception, methodName)` 時**沒有傳任何 cancellationToken**（用預設值 `default`）。程式碼註解自己也承認「系統的多數操作牽涉外部 CRM 與金流」，這類呼叫常見以內部 `CancellationTokenSource(timeout)` 包一層取消保護；一旦逾時，該內部 token 被取消並丟出 `OperationCanceledException`，流進 `HandleError` → `Report`，就會被本條件誤判為「用戶端正常取消」而**完全不落檔、不通知 LINE**。
  - `ExceptionLoggerProvider.ExceptionLogger.Log`（`Logging/ExceptionLoggerProvider.cs:48`）與 `ExceptionReporting.Attach` 內的 `OnUnhandled`／`OnUnobserved`（`Diagnostics/ExceptionReporting.cs:60,66`）同樣都以預設 `cancellationToken` 呼叫 `Report`，有同樣風險。
  - 這是唯一真正會傳有效 `cancellationToken` 的呼叫端只有 `UnhandledExceptionLineNotificationMiddleware.InvokeAsync`（傳 `context.RequestAborted`），其餘幾乎所有路徑都只依賴例外自帶的 token，等於**幾乎所有 `OperationCanceledException` 都會被靜默丟棄**，不論來源是真的用戶端斷線還是內部逾時。
- **具體失敗情境**：Controller 呼叫 CRM／金流 API 時用 `using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await client.SendAsync(req, cts.Token);` 逾時 → 拋出 `TaskCanceledException`（`cts.Token` 已取消）→ 被 catch 後呼叫 `HandleError(ex, "ChargePayment")` → `Report` 判定為「正常取消」→ **Exception.log 沒有任何紀錄，管理者也收不到 LINE**，但這正是需求文件（AGENTS.md 新增段落、requirements.md）明確要求「逾時」必須落檔通知的情境。
- **測試盲點**：`ExceptionDiagnosticsTests.Notification_failure_is_logged_without_recursion_and_timeout_is_actionable`（`ChurchReport.MemberInfo.Tests/LineSharedWorkflow/ExceptionDiagnosticsTests.cs:95-121`）用來驗證「逾時仍可行動」的案例故意用完全不同型別 `TimeoutException`，而不是帶有已取消 token 的 `OperationCanceledException`；因此測試**永遠不會**觸發上述邏輯缺陷，屬於測試覆蓋率缺口。
- **修復建議**：判斷「是否為預期取消」應**只看呼叫端傳入的 `cancellationToken`**（即呼叫端明確告知「這是我認可的取消來源」，例如 `context.RequestAborted`），不要再用例外自帶的 token 做 OR：
  ```csharp
  if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
  ```
  若要同時支援「例外的 token 就是呼叫端傳入的那個 token」場景，可改用 `ReferenceEquals(canceled.CancellationToken, cancellationToken)` 或明確比對，而不是各自獨立判斷 `IsCancellationRequested`。並補一個測試：以「呼叫端未傳 cancellationToken，但例外自帶已取消的內部逾時 token」建構 `OperationCanceledException`，斷言仍會落檔＋通知。

---

### 2.（沿用 Gemini，已驗證）檔案輪替遇鎖定會讓 Log／LINE 永久（直到鎖釋放）癱瘓
- **位置**：`ToolUtility/Diagnostics/ExceptionDiagnostics.cs:163-171`，外層 `catch { Emergency(...); return false; }` 在 `:177`。
- 已重讀程式碼確認 Gemini 分析正確：`File.Move` 若因外部程序持有 handle（稽核腳本、AV、文字編輯器）而丟出 `IOException`，會被最外層 `catch` 吞掉、`Write` 回傳 `false`，檔案大小仍 `> _maximumFileBytes`，導致**下一次呼叫又重新觸發同一輪替、同一失敗**，形成迴圈式阻斷，直到外部鎖被釋放為止（不一定是「永久」，但可能是不可預期的長時間窗口，且期間所有例外都無聲丟失）。
- **補充風險**：輪替迴圈是 5 次 `File.Move` 依序執行，若中途某一步失敗（例如 `Exception.3.log → Exception.4.log` 失敗），先前已成功的搬移（如 `Exception.4.log → Exception.5.log`）不會回滾，會留下**部分輪替**的檔案編號不一致狀態。
- **修復建議**：如 Gemini 建議，把輪替迴圈包成獨立 `try/catch`，失敗時記一次 `Emergency`／`WriteStatus` 但**不要讓輪替失敗阻斷本次 Write**（退而求其次繼續 append，即使超過 `_maximumFileBytes`），避免單次外部鎖定造成整條落檔管線停擺。

---

### 3.（新發現）舊版 Facade 把真例外丟棄成 `null`，造成診斷內容全失＋重複通知
- **位置**：`SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:18-27`
```csharp
public static void NotifyDefaultError(string source, string errorMessage)
{
    ExceptionReporting.Report(null, source + ".LegacyAdminError");
}
public static void NotifyDefaultError(string source, string category, string errorMessage)
{
    ExceptionReporting.Report(null, source + ".LegacyAdminError");
}
```
- **實際呼叫端（都是真的例外處理路徑，不是理論案例）**：
  - `Controllers/FeeManagementController.cs:588-597`：`catch (Exception e) { ...ErrorString = ...+e.ToString(); NotifyDefaultError("新莊靈糧堂", ErrorString); ...; throw e; }`
  - `Models/PollManager.cs:417-425`：同樣模式，`throw e;` 收尾。
  - `Models/DonationPaymentManager.cs:479-485`（經 `NotifyDonationPaymentError`）、`:284-286`（經 `NotifyDonationRegistrationError`）：同樣先呼叫舊 facade，再往外拋。
  - `Controllers/BaseChurchController.cs:452`（`SendLineErrorNotification`）也是同一模式，雖然目前 `HandleError` 已不再呼叫它（見下方 Info），但它仍是 `protected` 可被子類別呼叫。
- **問題**：
  1. **診斷內容全失**：由於 `exception` 傳 `null`，`ExceptionDiagnostics.Report` 產生的紀錄會是 `ExceptionType="ReportedError"`、`Location="unknown.unknown"`、`Stack=""`（見 `ExceptionDiagnostics.cs:91-95`, `StackSymbols` 對 `null` 回傳空字串）。這些呼叫點原本的 `ErrorString` 含有完整 `e.ToString()`（型別、訊息、堆疊），現在等於完全遺失，即使成功落檔也對排查毫無幫助。
  2. **重複通知**：因為傳的是 `null`，`_reported`（`ConditionalWeakTable<Exception, object>`）無法用來去重；上述 4 處呼叫在通知完後又 `throw e;`／往外拋同一個例外實例，該例外之後極可能再流到 `BaseChurchController.HandleError`（見 `catch-audit.json` 中 `FeeManagementController.cs` 同類 catch 呼叫 `HandleError(e, ...)` 的既有模式）或 `UnhandledExceptionLineNotificationMiddleware`／`ExceptionLoggerProvider`，屆時會用**真正的例外實例**再報一次（這次才有完整型別／堆疊）。結果是**同一次真實失敗產生兩筆 incident、最多兩則 LINE 推播**：一則空洞、一則詳細——直接牴觸 AGENTS.md 新增規則與 requirements.md 強調的「落檔與 LINE 使用相同事件 ID」單一事件設計精神，也會造成管理者收到重複、甚至誤導性的告警。
  3 `.ccg/tasks/line-actionable-exceptions/catch-audit.json` 目前**沒有**任何條目對應 `NotifyDefaultError`／`LegacyAdminError`（已用 grep 確認 catch-audit.json 中無此字串），代表這個既存的重複上報缺口未被目前的稽核工具捕捉到，屬於任務要求檢查的「terminal catch coverage gap」。
- **測試盲點**：`ChurchReportLineAdminNotificationServiceTests.Legacy_calls_use_shared_log_before_line_without_raw_error_text` 只驗證 `NotifyDefaultError` 單獨呼叫時內容不外洩，未驗證「舊 facade 呼叫後例外又被上層 `HandleError`／middleware 重新上報」時是否重複，因此無法暴露此問題。
- **修復建議**：這 4～5 個呼叫點都已經在 `catch (Exception e)` 範圍內拿得到真正的 `Exception` 物件，應直接改成呼叫 `ExceptionReporting.Report(e, "<方法名稱>")`（保留型別與堆疊），而不是透過只吃字串的舊 facade；`NotifyDefaultError` 這個字串簽章對「例外」場景已不再適用，應標記為僅供「沒有 Exception 物件、純文字告警」的極少數情境使用，或直接淘汰。

---

## 🟡 Warning

### 1.（沿用 Gemini，已驗證）具名 Mutex 未加 `Global\` 前綴
- **位置**：`ToolUtility/Diagnostics/ExceptionDiagnostics.cs:44-45`。確認未加前綴會落在 Session-local 命名空間；若 ChurchReport 以 IIS worker process（Session 0）與另一支互動式工具（Session 1）同時寫入同一目錄，兩者的具名 Mutex 不會互斥。建議依部署情境決定是否改為 `Global\ExceptionLog-...`（需搭配 `MutexSecurity`），或在 XML 文件註明僅保證同一 Session 內互斥。

### 2.（沿用 Gemini，已驗證）`LineExceptionSender` 的 Channel Token 僅建構時讀取一次
- **位置**：`Services/LineExceptionSender.cs:27-37, 63`。組態熱重載（`ReloadOnChange`）時不會反映新 Token；若日後金鑰輪替需要重啟服務才能生效。建議改為每次發送時透過 `IConfiguration`／`IOptionsMonitor` 重新解析，或至少在文件中明確標註此限制。

### 3.（新發現）`_gate` 鎖在持有期間執行同步磁碟 I/O 與跨程序 Mutex 等待，可能在錯誤爆量時序列化整個系統
- **位置**：`ExceptionDiagnostics.cs:70-109`（`Report`）與 `:181-185`（`ConsumeAsync` 內 `WriteStatus`，同樣在 `lock (_gate)` 下呼叫）。
- `Report` 整個方法體（含 `_fileMutex.WaitOne(1s)`、`Directory.CreateDirectory`、`FileStream` 開檔／寫入／`Flush(flushToDisk:true)` 強制實體落盤）都包在單一 in-process `lock (_gate)` 內。正常情境（單筆錯誤）沒問題，但若短時間內大量執行緒同時呼叫 `Report`（例如某個下游服務全面故障，多個請求同時炸掉），所有執行緒會在這個鎖上排隊，且每個排隊者都可能等到前一個持鎖者做完「跨程序 Mutex 等待 + 實體 flush」（可達秒級），對 ASP.NET Core 執行緒集區造成不必要的阻塞式壓力。`ConsumeAsync` 的背景 Task 也共用同一把鎖，等於錯誤通報與 LINE 失敗狀態寫入會互相排隊。
- 這是設計上「單一 owner 保序寫入」的必然代價，不算功能錯誤，但建議至少確認錯誤爆量情境下的執行緒池影響是可接受的（例如評估是否需要把檔案 I/O 移到專屬執行緒，或把鎖粒度縮小到只保護共享狀態、I/O 本身用檔案層級鎖即可）。

---

## 🔵 Info

1. **去重機制設計正確且經管線驗證**：`ConditionalWeakTable<Exception, object>` 搭配「middleware 先 `Report` 再 `throw;`」的模式，在 `Startup.cs:849-861` 的管線順序下（`UseDeveloperExceptionPage`／`UseExceptionHandler` 都在 `UnhandledExceptionLineNotificationMiddleware` 外側）能正確避免同一例外被 ASP.NET Core 內建 hosting／exception-handler 的 `ILogger` 紀錄二次上報，這部分設計嚴謹，值得保留。
2. **Program.cs 生命週期順序正確**：`registration.Dispose() → diagnostics.DisposeAsync() → sender.Dispose()`（`Program.cs:80-82`）符合「先解除全域事件、再 drain 佇列、最後釋放 HttpClient」的要求，`FinishAsync` 對 consumer 的 5 秒 drain + 逾時後才 cancel 的邏輯也正確（`ExceptionDiagnostics.cs:234-241`）。
3. **PII／敏感資料隔離測試扎實**：`ExceptionDiagnosticsTests` 五個案例涵蓋落檔失敗、佇列滿載、去重、LINE 失敗不遞迴、併發輪替，且都斷言不含注入的機密字串，覆蓋面良好。
4. **死碼提醒**：`BaseChurchController.SendLineErrorNotification`（`Controllers/BaseChurchController.cs:448-452`）在 `HandleError` 移除呼叫後，目前專案內已無任何呼叫點（僅剩定義本身），若確認未被子類別使用，建議連同其 try/catch 一併移除，避免日後有人誤用回舊的「傳原始字串」路徑。
5. `ExceptionLogger.Log`（`Logging/ExceptionLoggerProvider.cs:42-48`）內部又做了一次 `IsEnabled` 檢查，與 `Program.cs:213` 的 `AddFilter<ExceptionLoggerProvider>(null, LogLevel.Error)` 重複，屬防禦性冗餘、無害，可視需要保留。

---

## 總結與建議處置順序

1. **必須先修（Critical #1）**：`OperationCanceledException` 排除邏輯目前會誤吞真正逾時失敗，影響面最廣（幾乎所有非 middleware 路徑），且直接違反 AGENTS.md 剛新增的「逾時仍須記錄」規則——這比檔案輪替鎖定問題更容易在正式環境每天發生。
2. **必須修（Critical #2，沿用 Gemini）**：檔案輪替遇鎖定的降級保護。
3. **必須修（Critical #3）**：把 4 個舊 facade 呼叫點改成直接傳遞真實 `Exception`，消除內容遺失與重複通知。
4. **建議修（Warning 1-2，沿用 Gemini）**：Mutex `Global\` 前綴視部署拓樸決定；Token 動態刷新視金鑰輪替需求排優先級。
5. 修復後建議至少補三個測試：內部逾時型 `OperationCanceledException`（token 不同於呼叫端）仍應落檔通知；舊 facade 呼叫後例外被上層重新拋出時不應產生第二則 LINE；檔案輪替遇 `IOException` 時後續 `Report` 仍可成功（用可注入的鎖定模擬）。

**其餘（Program/Startup/BaseChurchController 生命週期、去重與 PII 隔離設計）審查結果為 PASS**，未發現新問題。

---
SESSION_ID: 445a7bc3-2753-4561-9b5c-599d470363d1
