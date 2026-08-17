# 技術設計：ToolUtilityClass 改為 Scoped 並抽離追蹤資源

對應 `prd.md`。執行順序見 `implement.md`。

## 1. 核心決策：改「取得方式」而非「置換參照」

被否決的作法：逐一把 `ToolUtility.m_Crm2011OrganizationService` 換成注入的連線。

理由：那只處理 61 個欄位參照，但真正共用連線的是 **3126 次方法呼叫**
（它們經由 `ToolUtilityClass._facade`，而 `_facade` 以同一條連線建構）。
逐一置換無法解決併發問題，卻要付出 61 處改動的風險。

採用的作法：把 `ToolUtilityClass` 的生命週期從程序級改為 request 級。

```
現在  35 個檔案各自 ToolUtilityFactory.GetInstance()  →  一個程序級單例  →  一條共用連線
目標  35 個檔案由建構式接收 ToolUtilityClass(Scoped) →  每 request 一個  →  該 request 的連線
```

**那 3126 次呼叫一行都不必改**，因為它們的形式仍是 `_toolUtility.Foo()`；
改變的只是 `_toolUtility` 從哪裡來。

## 2. 阻礙與其成因

`ToolUtilityClass` 目前混合了兩種生命週期的職責：

| 職責 | 正確的生命週期 | 現況 |
|---|---|---|
| Dataverse 資料存取 | request 範圍 | 綁在單例上 |
| 追蹤／診斷日誌 | **程序範圍** | 也綁在同一個單例上 |

`InitializeTracing()`（`ToolUtilityClass.Core.cs:114`）建立 `FileStream`、
`StreamWriter`、`TextWriterTraceListener`，並執行：

```csharp
Trace.Listeners.Add(listener);   // :133 —— System.Diagnostics.Trace 的行程級靜態集合
```

若直接把 `ToolUtilityClass` 改成 Scoped，每個 request 都會再加一個 listener：

- `Trace.Listeners` 無界成長（記憶體洩漏）
- 每行日誌被寫 N 次
- N 個 `FileStream` 指向同一檔案

**所以必須先把追蹤職責抽出，改由一個真正的 Singleton 擁有。**

## 3. 目標架構

```
┌─────────────────────────────────────────────────────────┐
│ Singleton（程序範圍，整個 Worker Process 一份）           │
│                                                          │
│   IToolUtilityTracer                                     │
│     · 擁有 FileStream / StreamWriter / TextWriterTraceListener │
│     · Trace.Listeners.Add 只在建構時執行一次              │
│     · 由 DI 於應用程式關閉時 Dispose                      │
│                                                          │
│   IBoundedClientPool（前置任務已建立）                    │
└─────────────────────────────────────────────────────────┘
                          ▲ 注入
┌─────────────────────────────────────────────────────────┐
│ Scoped（request 範圍，每個請求一份）                      │
│                                                          │
│   IOrganizationService  ← 已存在（PooledOrganizationService）│
│   ToolUtilityClass      ← 本任務改為 Scoped               │
│     ctor(IOrganizationService, IToolUtilityTracer, IConfiguration) │
│     · 不再自行建立連線                                    │
│     · 不再持有任何程序級資源                              │
└─────────────────────────────────────────────────────────┘
```

## 4. 型別契約

```csharp
/// 程序級追蹤資源擁有者。整個 Worker Process 只有一個實例。
public interface IToolUtilityTracer
{
    void TraceByLevel(int totalLevel, int qualifiedLevel, string message);
}

internal sealed class FileToolUtilityTracer : IToolUtilityTracer, IDisposable
{
    // 擁有 FileStream / StreamWriter / TextWriterTraceListener
    // 建構時 Trace.Listeners.Add(...) 恰好一次
    // Dispose 時 Trace.Listeners.Remove(...) 並釋放串流
}
```

`ToolUtilityClass` 的 `TraceByLevel` **簽章不變**，改為委派：

```csharp
public void TraceByLevel(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
    => _tracer.TraceByLevel(TotalLevel, QualifiedLevel, StringToProcess);
```

→ 160 個呼叫點零改動（R1、R6 滿足）。

### `TraceByLevelStatic` 的處理

該方法目前是 `static`，無法注入。兩個選項：

- **選項 A（建議）**：保留 static，內部改為讀取一個 `internal static` 的 tracer 參照，
  由 DI 於啟動時設定一次。這是 service locator，但範圍極小且只用於診斷輸出。
- 選項 B：改為執行個體方法 —— 需修改其所有呼叫點，違反 R1 的精神。

選 A，並在註解中明確記載此為刻意的例外與其理由。

## 5. DI 註冊

```csharp
// Startup.cs
services.AddSingleton<IToolUtilityTracer, FileToolUtilityTracer>();
services.AddScoped<ToolUtilityClass>();          // 由 DI 建構，注入連線與 tracer
services.AddScoped<IToolUtilityProvider, ToolUtilityProvider>();  // 由 Singleton 改為 Scoped
```

`ToolUtilityProvider.GetToolUtility()` 改為回傳注入的 Scoped 實例，
不再呼叫 `ToolUtilityFactory.GetInstance()`。

`ToolUtilityFactory` 的靜態單例邏輯**整個刪除**（A1 要求 `GetInstance` 呼叫點為 0）。

## 6. 35 個呼叫點的遷移模式

三種既有形態，對應三種改法：

| 形態 | 範例 | 改法 |
|---|---|---|
| 欄位初始化式取得 | `private readonly ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance(...)` （`QrCodeUtility.cs:50`、`GalleryViewModel.cs:47`） | 改為建構式參數，由呼叫端傳入 |
| 建構式內取得 | `m_ToolUtilityClass = ToolUtilityFactory.GetInstance(...)` （`RecurringDonationPaymentProcessor.cs:91`） | 同上 |
| Controller 基底 | `BaseChurchController` 的 `IToolUtilityProvider` | Provider 改 Scoped 即可，呼叫端不動 |

前兩種形態的類別多由 Controller 手動 `new`，因此 Controller 需把自己注入的
`ToolUtilityClass` 往下傳。Controller 已由 DI 管理，取得來源無虞。

## 7. 相容性

- `TraceByLevel` / `TraceByLevelStatic` 簽章不變 → 160 個呼叫點不動
- `ToolUtilityClass` 的公開 API 不變 → 3126 次呼叫不動
- `m_Crm2011OrganizationService` 欄位**保留**，但改為建構式注入的值；
  61 個殘留參照因此自動變成 request 範圍，不需逐一置換（A2 滿足）

## 8. 風險

| 風險 | 影響 | 緩解 |
|---|---|---|
| 背景批次無 DI scope | 取不到 Scoped 的 ToolUtilityClass | 第 1 階段先查清（PRD Q1）；必要時以 `IServiceScopeFactory` 建立短 scope |
| `Trace.Listeners` 重複加入 | 記憶體洩漏＋重複日誌 | A3／A4 測試守住 |
| 35 個呼叫點分佈廣 | 一次改完難以回退 | 分批，每批一 commit，每批後跑 67 個測試 |
| `ToolUtilityFactory` 刪除影響未知呼叫端 | 編譯失敗 | 以編譯器輸出取得完整清單，不靠 grep 估算 |

## 9. Rollout 與 Rollback

三階段，每階段可獨立驗證與回退：

1. **抽離追蹤** —— 新增 Singleton tracer，`ToolUtilityClass` 委派。此時
   `ToolUtilityClass` 仍是單例，行為完全不變，純粹是職責搬移。**風險最低。**
2. **改變生命週期** —— `ToolUtilityClass` 與 `IToolUtilityProvider` 改 Scoped，
   連線改為注入。`BaseChurchController` 的呼叫端不動。
3. **遷移 35 個 GetInstance 呼叫點** —— 分批；完成後刪除 `ToolUtilityFactory`
   的靜態單例邏輯。

**不可回退點**：第 1 階段改變 `Trace.Listeners` 的加入時機。需先於測試環境確認
日誌檔仍正常寫入且無重複行。
