# Error Handling

## Exception.log → LINE 管理者例外告警契約（跨產品永久規則）

### 1. Scope / Trigger

所有現有與未來產品、MVC、API、worker、queue callback、共用服務與工具，都必須記錄真正失敗、未處理或影響功能的例外與 Error/Critical。**Debug 與 Release 都依獨立輸出設定執行；雙開時先寫入並 flush `Exception.log`，再排入／發送 LINE。** 正常取消、client disconnect、成功恢復的 retry、預期業務拒絕不通知；不得使用 FirstChanceException 對每個曾拋出的例外通知。

此規則是每次新增／修改程式都必須落實的契約。全域 middleware 無法看到被 catch 吞掉的例外；每個 catch 後回傳失敗、空結果或錯誤頁的邊界，仍須明確上報。新增產品必須在其 Host 接入共用 owner，不能只引用本文件就視為已啟用。

### 2. Signatures

- 共用 owner：`ExceptionDiagnostics.Report(Exception exception, string operation, CancellationToken cancellationToken = default, bool notify = true)`。回傳 true 表示已落檔，或 LINE-only 已入列；不保證 LINE 送達。
- 非 DI 相容轉接：`ExceptionReporting.Report(Exception exception, string operation, CancellationToken cancellationToken = default)`；Host 以 `ExceptionReporting.Attach(diagnostics)` 註冊唯一 owner。
- ChurchReport catch：`ChurchReportLineAdminNotificationService.ReportException(string source, Exception exception)` 或 `BaseChurchController.HandleError(Exception exception, string methodName)`。
- `NotifyDefaultError(string, string)`／三字串版本僅為沒有 Exception 物件的舊式錯誤事件保留；真正 catch 必須傳原始 Exception，不可先 ToString 再丟棄型別與堆疊。相容字串內容不寫入／傳送。
- DI 呼叫端直接注入共用診斷 owner 或等價介面；不得在各處 new LINE client。

### 3. Contracts

設定位置：`SpeechMessageProducts.ChurchReport/appsettings.json`；只在 Host 啟動時讀取，修改後重啟。無效布林拒絕啟動；讀取設定／初始化 owner 之前失敗只用固定 stderr，不猜測目的地。其他 appsettings 環境檔／環境變數依 ASP.NET 標準順序覆寫。

```json
"ExceptionNotifications": {
  "WriteExceptionLog": true,
  "SendLine": true
}
```

| WriteExceptionLog | SendLine | 實際輸出 |
|---|---|---|
| true | true | 先落檔 flush，成功後 LINE |
| true | false | 只寫 Exception.log，不建立 LINE client／consumer |
| false | true | 只發 LINE；失敗狀態只 stderr，無檔案 I/O |
| false | false | 兩者皆停用 |

缺少設定時兩者預設 true；此明確授權的四種組合優先於原雙開的預設描述。其他 ILogger console／debug provider 與業務 LINE 訊息不受這兩個例外專用開關影響。


- 兩者都啟用時強制順序：JSONL 寫入 `Logs/Exception.log` → `Flush(flushToDisk: true)` 成功 → 有界 LINE channel 入列 → 發送。同一筆日誌與 LINE 共用 IncidentId，不得以兩個平行非同步工作假裝保序。
- 同一 Exception 實例以 weak key 去重，不延長例外／request 生命；重新拋出使用 `throw;` 保留堆疊。不得將 terminal failure 包裝為 null 使上層再次通知。
- 紀錄欄位為 IncidentId、Utc、Operation、ExceptionType、Location、HResult 與最多五層程式符號／PDB 行號 Stack。Operation／source 只能使用開發者擁有的固定產品／類別／方法名稱，不能傳 route 實值、姓名或 request 值。
- 不記錄／傳送 Exception.Message、Data、ToString、原始 StackTrace 路徑、request body、cookie、Session、token、密碼、金流資料與個資。Release 無 PDB 時行號可為 0，但型別與程式符號仍保留。
- 正常取消只由呼叫端明確傳入且已取消的 token 判定。例外自帶的已取消 token 可能代表 CRM／HttpClient 內部逾時，不能據此跳過紀錄；未取消的 caller 發生逾時仍須依所選目的地上報。
- 每筆上限 4 KiB；一般檔案上限 5 MiB，保留目前檔與五份備份。輪替被拒絕 Delete share 時，保留證據並允許目前檔有限附加至正常上限兩倍；每次事件重試輪替，解除鎖定後恢復。禁止截斷證據或無界 append。
- 磁碟滿、拒絕寫入、鎖等待逾時、降級硬上限均走固定 stderr／主機監控，該筆不排入 LINE，亦不假稱落檔成功。檔案讀取工具使用 `FileShare.ReadWrite | FileShare.Delete`，診斷檔不得置於 wwwroot；受信任部署目錄須授權服務帳戶寫入，不得由 request／Session 決定路徑。
- 寫檔關閉時不建立檔案／mutex，以下 LINE 狀態只送固定 stderr；不得繞過開關寫檔。
- LINE channel 容量 64，滿載以同事件 ID 寫入 `LineQueueFull`；發送失敗只追加本地 `LineDeliveryFailed`，不得遞迴通知或取代原錯誤。每次發送逾時 5 秒；程序強制終止時無法保證已入列訊息送達。
- Program 是 owner：解除全域事件與 static 綁定 → 停止／drain channel → Dispose writer／mutex → Dispose sender／HttpClient。所有 queue、task、timer、registration、stream、connection 都有上限與確定性清理，不保存 HttpContext、Session 或使用者狀態。
- ChurchReport LINE token 與管理者設定只在啟動讀取；變更部署憑證或收件人後須重新啟動 Host。錯誤落檔不受 DEBUG 或 `DiagnosticsTrace:Enabled` 控制；原三個開發 Trace 檔的 Release 禁寫規則另行保留。
- 同步 flush 是「落檔後才通知」的成本，僅在錯誤路徑執行；錯誤爆量須監控磁碟延遲、LINE queue 與 stderr。不能以無界背景工作或隱藏錯誤交換吞吐量。

### 4. Validation & Error Matrix

| 條件 | 必須結果 |
|---|---|
| 未處理 HTTP 例外 | 標準 handler 內側 middleware 按設定輸出（雙開先落檔再通知），重新拋出同例外，保留原回應。 |
| catch 後功能失敗 | 透過共用入口傳真實 Exception，保留既有失敗結果。 |
| 同例外再經外層 logger／handler | 一筆 incident，一次 LINE 入列。 |
| caller 正常取消／成功恢復 retry | 不通知；正常取消標記避免 framework 再次上報。 |
| 內部已取消 timeout token、caller 未取消 | 按設定輸出。 |
| Debug／Release 的 Error/Critical | 按所選目的地輸出，分類規則相同。 |
| 輪替遭讀取器鎖定 | 有界附加至兩倍上限；解鎖後下一事件恢復輪替。 |
| 落檔失敗／降級硬上限 | 固定 stderr、無 LINE；修復後新事件可重試。 |
| LINE 失敗／queue 滿載 | 寫檔啟用則記本地狀態，停用則固定 stderr；不遞迴通知。 |

### 5. Good / Base / Bad Cases

- Good：CRM timeout 經 catch 回傳錯誤，型別與方法位置先落檔，再以同 incident 通知。
- Base：LINE API timeout，原錯誤仍保留於 Exception.log，通知失敗狀態也只寫本地。
- Bad：用 `#if DEBUG` 包住 Exception.log，或雙開時未確認 flush 就啟動 LINE。
- Bad：只加入 middleware 卻宣稱所有被吞掉的 catch 都已涵蓋；以字串丟棄原例外後再 rethrow。

### 6. Tests Required

- `ExceptionOutputOptionsTests`：JSON 設定四種組合、缺省／無效值、LINE-only 故障／滿載／關機不建立日誌。

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

## 雙目標程式庫與 net48 Plugin

依使用者 2026-11-03 核定，出處設計 v3.3 10.5／10.9、計畫 04 §5.4：
- LineMessagingProcessor.Core 不是最終失敗邊界，不參考 ToolUtility 或 notifier，不新增未接入的 callback／no-op。無 HTTP 回應的內部逾時／HttpRequestException 以 Retryable 結果保留原始 TransportException；caller 取消只在收到回應前生效，收到回應後一律回傳分類結果；非 caller 造成的取消一律視為傳輸失敗（Retryable，保留原始例外）。依使用者 2026-11-03 核定補修。其他非預期例外往外拋。有 HTTP 回應不製造 TransportException。
- 結果與例外只屬於這次操作，不存 static／快取，不持有 request／response／credential；回應先 Dispose。Core 自產訊息與 ToString 不含 Token、內文或 userId，禁止把原始 Exception 直接序列化或寫進診斷輸出。
- net48 Plugin 沙箱不寫 Exception.log、不呼叫 notifier、不另外發管理者 LINE。Retryable 先以 ITracingService 追蹤安全欄位，再拋 InvalidPluginExecutionException；有原始傳輸例外時保留為 InnerException。Permanent 只追蹤；非預期例外追蹤型別後 `throw;`。
- D365 端最終通知由階段 5 LineWebhook 每五分鐘彙總、依門檻交給共用入口；階段 0～4 只能從系統作業與 Plugin 追蹤查看，屬排定的過渡期。
- net10 主機呼叫 Core，重試用盡後才經 ExceptionReporting 上報一次，屬階段 2 以後；正常 caller 取消與成功恢復的重試不上報，通知管道自身失敗不遞迴通知。
- 日後遇到階段段落省略責任時，先查設計 Word 第十章及計畫 02～04；已有答案就按出處執行並在 WP 回報註明，不能只因本段未重複敘述而判定缺少架構。

## P2-01 Hosting sender 與相容入口

### 1. 適用範圍

例外傳輸的真正實作位於 `SpeechMessage.Hosting/LineExceptionSender.cs`；
產品 facade 不可保留第二套 HTTP／token／Development 安全政策。
三個 diagnostics 型別已搬入 Hosting，保留舊 namespace 與 ToolUtility 型別轉送。
此抽取不表示 Program、語系完成度生成或整包 P2-01 已驗收。

### 2. 可執行介面

- `new SpeechMessage.Hosting.LineExceptionSender(IConfiguration configuration,
  string? fallbackRecipient = null, HttpMessageHandler? transport = null)`。
- `Task SendAsync(string message, CancellationToken cancellationToken)`、
  `void Dispose()`。
- 舊 `ChurchReport.Services.LineExceptionSender(IConfiguration)` 只提供原產品後備收件人，
  並委派上述實作；共用層不可硬編碼產品管理者 ID。
- 舊 Development 三個入口委派 `StartupGuards.ValidateDevelopmentConfiguration`、
  `AddDevelopmentConfigurationOverrides`、`AddDeploymentLineEnvironmentOverrides`。

### 3. 所有權與資料契約

設定先驗證，再接管自有 transport；建構失敗時 caller 仍擁有注入的 handler。
成功後由 sender 的 HttpClient 唯一負責釋放。只保留啟動 token／收件人快照，
不保留 IConfiguration、request、Session 或 provider；Authorization 只存在單次 request。
預設 handler 無 cookie、禁止 redirect、每站一連線、pool lifetime 五分鐘，
client timeout 五秒。每筆 request／response 均 Dispose，不讀供應商本文、不重試或通知自身失敗。
Host 先停止並 drain diagnostics，再 Dispose sender；關閉旗標必須先發布，
即使 transport 清理失敗，finally 仍清空部署字串且不再次清理已失敗 owner。

### 4. 驗證與錯誤矩陣

| 條件 | 必須結果 |
| --- | --- |
| 缺 token／收件人 | 固定錯誤、零 HTTP 操作 |
| 非 2xx | 固定訊息，保留 StatusCode，response 釋放、本文零讀取 |
| 無回應的傳輸故障 | 原例外身分向上傳遞，不重送／遞迴通知 |
| caller 取消／內部逾時 | 工作失敗並排空，由 diagnostics 依原 caller token 分類 |
| 關閉後新操作 | ObjectDisposedException，不以缺設定誤報 |
| transport Dispose 失敗 | 保留失敗且清空部署參照，重複 Dispose 不重入 |

### 5. 正常、缺省與禁止案例

正常：同時存活的 A／B sender 各使用自身 request token 與收件人。
缺省：只有產品組合根明確提供的後備收件人可使用，不推定其他產品管理者。
禁止：把 token 放 DefaultRequestHeaders、讓 request 選 handler，或保留已關閉 sender 的收件人。

### 6. 必跑驗證

`LineExceptionSenderTests` 目前有 17 個離線案例，涵蓋選擇順序、快照、
兩 owner 各六十四筆以 barrier 證明同時存活、request／response 釋放、失敗及關閉故障。
`StartupGuardsTests` 保護實際委派且無重複政策；原 DevelopmentSafetyTests／
ExceptionOutputOptionsTests 必須 Debug、Release 皆綠，不能只靠新共用 helper。
型別搬移、相依圖、file documentation、編碼、duplicate 與 I18n 仍獨立檢查。

### 7. 錯誤與正確作法

錯誤：在產品 facade 再 new HttpClient、重新解析 token 或複製 ValidateTestHost。
正確：產品只傳設定與自己的後備常數給共用 sender，Development 入口只委派 StartupGuards。
共用 sender 不自行發通知，最終失敗仍由唯一 diagnostics owner 分類與去重。
