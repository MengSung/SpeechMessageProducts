# Error Handling

## Exception.log → LINE 管理者例外告警契約（跨產品永久規則）

### 1. Scope / Trigger

所有現有與未來產品、MVC、API、worker、queue callback、共用服務與工具，都必須記錄真正失敗、未處理或影響功能的例外與 Error/Critical。**Debug 與 Release 一律先寫入並 flush `Exception.log`，再排入／發送 LINE。** 正常取消、client disconnect、成功恢復的 retry、預期業務拒絕不通知；不得使用 FirstChanceException 對每個曾拋出的例外通知。

此規則是每次新增／修改程式都必須落實的契約。全域 middleware 無法看到被 catch 吞掉的例外；每個 catch 後回傳失敗、空結果或錯誤頁的邊界，仍須明確上報。新增產品必須在其 Host 接入共用 owner，不能只引用本文件就視為已啟用。

### 2. Signatures

- 共用 owner：`ExceptionDiagnostics.Report(Exception exception, string operation, CancellationToken cancellationToken = default, bool notify = true)`。回傳 true 只代表成功落檔，不保證 LINE 送達。
- 非 DI 相容轉接：`ExceptionReporting.Report(Exception exception, string operation, CancellationToken cancellationToken = default)`；Host 以 `ExceptionReporting.Attach(diagnostics)` 註冊唯一 owner。
- ChurchReport catch：`ChurchReportLineAdminNotificationService.ReportException(string source, Exception exception)` 或 `BaseChurchController.HandleError(Exception exception, string methodName)`。
- `NotifyDefaultError(string, string)`／三字串版本僅為沒有 Exception 物件的舊式錯誤事件保留；真正 catch 必須傳原始 Exception，不可先 ToString 再丟棄型別與堆疊。相容字串內容不寫入／傳送。
- DI 呼叫端直接注入共用診斷 owner 或等價介面；不得在各處 new LINE client。

### 3. Contracts

- 強制順序：JSONL 寫入 `Logs/Exception.log` → `Flush(flushToDisk: true)` 成功 → 有界 LINE channel 入列 → 發送。同一筆日誌與 LINE 共用 IncidentId，不得以兩個平行非同步工作假裝保序。
- 同一 Exception 實例以 weak key 去重，不延長例外／request 生命；重新拋出使用 `throw;` 保留堆疊。不得將 terminal failure 包裝為 null 使上層再次通知。
- 紀錄欄位為 IncidentId、Utc、Operation、ExceptionType、Location、HResult 與最多五層程式符號／PDB 行號 Stack。Operation／source 只能使用開發者擁有的固定產品／類別／方法名稱，不能傳 route 實值、姓名或 request 值。
- 不記錄／傳送 Exception.Message、Data、ToString、原始 StackTrace 路徑、request body、cookie、Session、token、密碼、金流資料與個資。Release 無 PDB 時行號可為 0，但型別與程式符號仍保留。
- 正常取消只由呼叫端明確傳入且已取消的 token 判定。例外自帶的已取消 token 可能代表 CRM／HttpClient 內部逾時，不能據此跳過紀錄；未取消的 caller 發生逾時仍須落檔／LINE。
- 每筆上限 4 KiB；一般檔案上限 5 MiB，保留目前檔與五份備份。輪替被拒絕 Delete share 時，保留證據並允許目前檔有限附加至正常上限兩倍；每次事件重試輪替，解除鎖定後恢復。禁止截斷證據或無界 append。
- 磁碟滿、拒絕寫入、鎖等待逾時、降級硬上限均走固定 stderr／主機監控，該筆不排入 LINE，亦不假稱落檔成功。檔案讀取工具使用 `FileShare.ReadWrite | FileShare.Delete`，診斷檔不得置於 wwwroot；受信任部署目錄須授權服務帳戶寫入，不得由 request／Session 決定路徑。
- LINE channel 容量 64，滿載以同事件 ID 寫入 `LineQueueFull`；發送失敗只追加本地 `LineDeliveryFailed`，不得遞迴通知或取代原錯誤。每次發送逾時 5 秒；程序強制終止時無法保證已入列訊息送達。
- Program 是 owner：解除全域事件與 static 綁定 → 停止／drain channel → Dispose writer／mutex → Dispose sender／HttpClient。所有 queue、task、timer、registration、stream、connection 都有上限與確定性清理，不保存 HttpContext、Session 或使用者狀態。
- ChurchReport LINE token 與管理者設定只在啟動讀取；變更部署憑證或收件人後須重新啟動 Host。錯誤落檔不受 DEBUG 或 `DiagnosticsTrace:Enabled` 控制；原三個開發 Trace 檔的 Release 禁寫規則另行保留。
- 同步 flush 是「落檔後才通知」的成本，僅在錯誤路徑執行；錯誤爆量須監控磁碟延遲、LINE queue 與 stderr。不能以無界背景工作或隱藏錯誤交換吞吐量。

### 4. Validation & Error Matrix

| 條件 | 必須結果 |
|---|---|
| 未處理 HTTP 例外 | 標準 handler 內側 middleware 先落檔再通知，重新拋出同例外，保留原回應。 |
| catch 後功能失敗 | 透過共用入口傳真實 Exception，保留既有失敗結果。 |
| 同例外再經外層 logger／handler | 一筆 incident，一次 LINE 入列。 |
| caller 正常取消／成功恢復 retry | 不通知；正常取消標記避免 framework 再次上報。 |
| 內部已取消 timeout token、caller 未取消 | 落檔成功後通知。 |
| Debug／Release 的 Error/Critical | 都寫入 Exception.log，按可行動性規則通知。 |
| 輪替遭讀取器鎖定 | 有界附加至兩倍上限；解鎖後下一事件恢復輪替。 |
| 落檔失敗／降級硬上限 | 固定 stderr、無 LINE；修復後新事件可重試。 |
| LINE 失敗／queue 滿載 | 原事件已落檔，另記本地固定狀態，不遞迴通知。 |

### 5. Good / Base / Bad Cases

- Good：CRM timeout 經 catch 回傳錯誤，型別與方法位置先落檔，再以同 incident 通知。
- Base：LINE API timeout，原錯誤仍保留於 Exception.log，通知失敗狀態也只寫本地。
- Bad：用 `#if DEBUG` 包住 Exception.log，或未確認 flush 就啟動 LINE。
- Bad：只加入 middleware 卻宣稱所有被吞掉的 catch 都已涵蓋；以字串丟棄原例外後再 rethrow。

### 6. Tests Required

- `ExceptionPipelineTests`：真實 ASP.NET handler 保留同一例外及 500，logger 不重複；Error/Critical 不呼叫敏感 formatter。
- `ExceptionDiagnosticsTests`：真實檔案先於假 sender、併發輪替、去重、滿載、故障不遞迴、正常取消、內部 timeout、讀取鎖定／硬上限／恢復、拒絕寫入後重試與 Dispose 清理。
- `ChurchReportLineAdminNotificationServiceTests`：legacy 不洩露敏感字串，真實例外經 facade 與外層重新上報後只有一筆，型別／位置／Stack 存在。
- Debug 與 Release 都執行上述測試；新產品／新增 terminal catch 必須驗證實際失敗邊界有接入，正常恢復路徑沒有誤報。

### 7. Wrong vs Correct

```csharp
// Wrong：丟失真實例外身分，且完整文字可能含憑證／個資。
catch (Exception ex) { NotifyDefaultError("Payment", ex.ToString()); throw ex; }
```

```csharp
// Correct：共用 owner 先落檔 flush 再排入 LINE；同一實例在外層再次遇到時去重。
catch (Exception ex)
{
    ExceptionReporting.Report(ex, "PaymentService.Create");
    throw;
}
```
