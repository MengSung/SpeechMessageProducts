# Error Handling

> How errors are handled in this project.

---

## Overview

<!--
Document your project's error handling conventions here.

Questions to answer:
- What error types do you define?
- How are errors propagated?
- How are errors logged?
- How are errors returned to clients?
-->

(To be filled by the team)

## LINE 管理者例外告警契約（跨產品永久規則）

### 1. Scope / Trigger

所有產品的「真正失敗」都必須能讓值班人員收到 LINE 告警：未處理例外、讓請求／背景工作失敗的例外，以及 catch 後採 fail-closed 或回傳失敗結果的例外。正常取消、用戶端斷線造成的 `OperationCanceledException`、成功恢復的重試與預期業務拒絕不發送告警。

### 2. Signatures

- ChurchReport 現有入口：`BaseChurchController.HandleError(Exception exception, string methodName)`。
- ChurchReport 現有告警：`ChurchReportLineAdminNotificationService.NotifyDefaultError(string source, string errorMessage)`。
- 新增產品或共用模組時，應提供可注入的 `IExceptionLineNotifier`（或等價介面），讓 HTTP、worker、queue callback 與 service catch 共用同一入口；禁止各處自行建立 LINE client。

### 3. Contracts

- 告警必須是 best-effort：LINE 失敗不可覆蓋原始例外、不可造成遞迴告警。
- 訊息至少包含產品／模組來源、例外型別、穩定 incident id、UTC 時間與可定位的 operation／route 名稱；不得包含 access token、cookie、Session 值、密碼、完整 request body、付款資料或未驗證的個資。
- 通知佇列、背景 task、timer 與 cancellation registration 必須有明確上限、逾時、停止 drain 與 Dispose；不得無界保留例外或 `HttpContext`。
- 未處理 HTTP 例外須在標準 `UseExceptionHandler`／錯誤頁處理器的內側攔截，記錄後重新拋出，保留既有 HTTP 回應契約。
- `ILogger.LogError`／`LogCritical`、背景服務的 fault 與 catch 後回傳失敗結果都必須接到告警入口；單靠 middleware 不算全覆蓋。
- 每個部署組態（Debug 與 Release）都必須啟用 `Exception.log` 錯誤 writer；檔案由 Host／DI 擁有並在停止時釋放，路徑固定在應用程式 `Logs` 目錄（或受信任的絕對路徑），不得由 request、Session 或使用者輸入決定。
- **強制順序：Exception.log 寫入成功並 flush → LINE 入列 → LINE 發送。** 不得同時啟動兩個非同步工作假裝滿足先後順序。落檔失敗只走独立 stderr／主機監控且不得發送該筆 LINE；送出成功不是落檔成功證據。

### 4. Validation & Error Matrix

| 條件 | 必須結果 |
|---|---|
| 未處理例外離開 HTTP pipeline | 發送一次 LINE，保留原例外傳播與錯誤頁。 |
| catch 後功能明確失敗 | 發送一次 LINE，回傳原定失敗結果。 |
| 正常取消、client disconnect、成功重試 | 不發送 LINE。 |
| LINE token/API/序列化失敗 | 記錄本地診斷；不得取代原例外或再次通知。 |
| Debug 或 Release 發生 Error/Critical | 寫入 `Exception.log`，並依可行動性規則送 LINE。 |
| 通知佇列達上限 | 丟棄最新告警並寫入受限計數器；不得配置無限佇列。 |

### 5. Good / Base / Bad Cases

- Good：Dataverse timeout 使 action 回傳錯誤，告警包含 `Dataverse`、operation 與 incident id，沒有 Session 或 token。
- Base：LINE API timeout；原始 CRM 例外仍照既有流程回傳，通知失敗只留 Trace／ILogger。
- Bad：在 `FirstChanceException`、每個正常 retry 或 `OperationCanceledException` 都推播，造成噪音與告警風暴。
- Bad：把 `exception.ToString()`、request body 或 cookie 原文直接送到 LINE。

### 6. Tests Required

- 未處理 HTTP 例外會通知一次且仍由錯誤處理器產生原回應。
- 已處理／catch 後失敗結果會通知一次；正常取消與成功 retry 不通知。
- 通知失敗不改變原例外，且不產生遞迴通知。
- A/B 並行請求的告警不交叉 Session、tenant、credential 或 request body。
- 佇列滿載、逾時、停止與 Dispose 後，背景 task、timer、registration 與 retained exception 數量回到基準。

### 7. Wrong vs Correct

```csharp
// Wrong：每個 catch 自行 new LINE client，並把完整例外與 request 資料送出。
catch (Exception ex) { new LineMessagingProcessorClass(token).SendMessage(id, ex.ToString()); }
```

```csharp
// Correct：使用共用、受限且不保存 request 的通知入口；保留原始失敗語意。
catch (Exception ex)
{
    exceptionLineNotifier.TryNotify("PaymentService.Create", ex);
    throw;
}
```

---

## Error Types

<!-- Custom error classes/types -->

(To be filled by the team)

---

## Error Handling Patterns

<!-- Try-catch patterns, error propagation -->

(To be filled by the team)

---

## API Error Responses

<!-- Standard error response format -->

(To be filled by the team)

---

## Common Mistakes

<!-- Error handling mistakes your team has made -->

(To be filled by the team)
