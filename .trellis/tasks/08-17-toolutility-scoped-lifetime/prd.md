# ToolUtilityClass 改為 Scoped 並抽離追蹤資源

## 目標

消除「一條非執行緒安全的 Dataverse 連線被整個 Worker Process 共用」的併發風險。

作法不是逐一置換 `m_Crm2011OrganizationService` 的參照，而是改變 `ToolUtilityClass`
本身的取得方式：由程序級單例改為 request 範圍（Scoped），並由 DI 注入該 request
的 `IOrganizationService`。

前置任務 `08-17-four-product-dataverse-connection-architecture` 已完成連線池、
Scoped 註冊、生命週期修正與測試修復；本任務接續處理最後一項殘留風險。

## 已確認的事實（皆有證據）

### 真實規模

| 量測 | 數值 |
|---|---|
| 直接呼叫 `ToolUtilityFactory.GetInstance()` 的檔案 | **35** |
| 透過該單例呼叫方法的總次數 | **3126** |
| `m_Crm2011OrganizationService` 殘留參照 | **61** |
| `TraceByLevel` 呼叫點 | **160** |

「61 個殘留參照」不是問題的規模。那 3126 次方法呼叫全部經由
`ToolUtilityClass._facade`，而 `_facade` 是以同一條 `m_Crm2011OrganizationService`
建構的（`ToolUtilityClass.Core.cs:87`、`:99`），因此**全部共用同一條連線**。

### 為什麼不能直接把 ToolUtilityClass 改成 Scoped

`ToolUtilityClass` 同時持有**程序級**的追蹤資源
（`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs`）：

```
:71  private String m_TraceLogFile
:72  private Lazy<FileStream> _lazyXmlFileStream
:73  private Lazy<StreamWriter> _lazyXmlFileStreamWriter
:74  private Lazy<TextWriterTraceListener> _lazyListener
:75  private const String TRACE_DIRECTOR = @"D:\除錯追蹤\CHURCH_REPORT_TRACE.TXT"
:114 private void InitializeTracing()
:133 Trace.Listeners.Add(listener)        ← 加入 .NET 的「全域」集合
```

`Trace.Listeners` 是 `System.Diagnostics.Trace` 的行程級靜態集合。若
`ToolUtilityClass` 變成 Scoped，**每個 request 都會再加入一個 listener**，
造成：

1. `Trace.Listeners` 無界成長 —— 真正的記憶體洩漏
2. 每一行日誌被重複寫入 N 次（N = 已建立的 scope 數）
3. 每個 scope 各開一個 `FileStream` 指向同一個檔案

因此**必須先抽離追蹤資源**，才能改變生命週期。順序不可顛倒。

### 連線建立

`InitializeCrmConnection()`（`:141`）在建構式中無條件建立自己的連線；改為 Scoped 後
應改由建構式接收 DI 提供的 `IOrganizationService`，不再自行建立。

### 前置任務已建立的基礎

- `IOrganizationService` 已註冊為 Scoped（`PooledOrganizationService`）
- `OnPremiseClient` 已實作確定性關閉
- 短命物件釋放長命單例的錯誤已全部修正
- `ToolUtility.Tests` 已修復（63 測試）＋ `ToolUtility.Dataverse.Tests`（4 測試）

## 需求

- R1 `TraceByLevel` 與 `TraceByLevelStatic` 的**簽章不得改變**（160 個呼叫點不動）。
- R2 追蹤資源在整個 Worker Process 中只能有一份：一個 `FileStream`、一個
  `StreamWriter`、一個 `TextWriterTraceListener`，且只加入 `Trace.Listeners` 一次。
- R3 `ToolUtilityClass` 改為 Scoped，其連線由建構式接收，不再自行建立。
- R4 `ToolUtilityClass` 不再持有任何需要確定性釋放的程序級資源。
- R5 35 個 `ToolUtilityFactory.GetInstance()` 呼叫點改為由 DI 取得。
- R6 那 3126 次方法呼叫**一行都不必修改**。
- R7 短命物件不得釋放長命物件（延續前置任務的鐵律）。
- R8 遷移過程中，任一階段結束時系統都必須可建置、可登入。

## 驗收標準

全部可用一道指令機械判定。

| # | 判定方式 |
|---|---|
| A1 | `ToolUtilityFactory.GetInstance` 僅存在於已文件化的 20 個 B 類殘留點；Tools 目錄不得再有執行命中。 |
| A2 | `m_Crm2011OrganizationService` 的殘留僅存在於同一份 20 個 B 類持有鏈清單；本 Run 不處理該清單。 |
| A3 | `grep -rn "Trace.Listeners.Add" --include=*.cs .` 只命中新的追蹤 Singleton，且該處在整個程序只執行一次（以測試斷言） |
| A4 | 測試：連續建立 100 個 `ToolUtilityClass` scope 後，`Trace.Listeners.Count` 不成長 |
| A5 | 測試：`ToolUtilityClass` 建構時不再建立任何 Dataverse 連線（以 mock 斷言 `CreateOnPremiseClient` 未被呼叫） |
| A6 | `dotnet build SpeechMessageProducts.sln -c Debug` 建置成功 0 錯誤 |
| A7 | `ToolUtility.Tests` 63 測試 ＋ `ToolUtility.Dataverse.Tests` 至少 13 測試全綠 |
| A8 | 人工回歸：登入、會友查詢／編輯、奉獻、影像上傳、LINE 綁定、批次下載、QR Code 產生 —— **不列為 agent 完成條件** |
| A9 | B 類殘留清單逐一列出精確 `檔案:行號` 與所屬 session-cache holder，且已建立後續票草稿。 |

## 不在範圍

- 明文密碼與憑證輪替（使用者已明確表示現況可接受）
- `appsettings.Production.json` 孤立的 `ConnectionPool` 區段
- `ToolUtilityClass` 的 API 介面重新設計（本任務只改生命週期與取得方式）
- 產品 B / C / D
- `InMemoryDataContextSmallGroup` 的 13 個 session-key `IMemoryCache` 項目重新設計或移除；
  其 20 個 B 類呼叫點保留至後續票處理。

## 仍開放的問題

- Q1 是否有背景批次／非 request 路徑會使用 `ToolUtilityClass`？那些沒有 DI scope，
  必須另行設計（例如以 `IServiceScopeFactory` 建立短生命週期 scope）。
  **會阻擋第 3 階段**，須於第 1 階段查清。
- Q2 `WeeklyReportProcessor`、`RecurringDonationPaymentProcessor` 等工具類別是否在
  背景執行緒上執行？若是，其 scope 生命週期需明確設計。

## 參考

- 前置任務：`.trellis/tasks/08-17-four-product-dataverse-connection-architecture/`
- 架構圖：`docs/architecture/dataverse-architecture-final-v2.png`
