我已完整核對 issue.md 及四份 evidence 檔案中列出的所有 `file:line` 證據，逐一對照原始碼確認。以下為審查報告。

---

## F08 Payment Provider Core 診斷草稿審查報告

### 總結
草稿中列出的 4 項 SEC 問題與 3 項 PERF 問題，其 `file:line` 證據經逐一比對原始碼後**全數準確**，無明顯捏造或行號錯位。範圍界定（scope）正確排除了 MVC/CRM/workflow/LINE 通知等非 F08 職責範圍，`runtime-validation-plan.md` 也未做任何違反 DIAGNOSIS_ONLY 限制的建置/測試宣稱。**無 Critical 等級問題，無阻擋核准（blocker）項目。**

---

### 🔴 Critical
無。

---

### 🟡 Warning

**1. PERF-001 對「provider 是否為長生命週期實例」的敘述過度保留，證據其實可以更確定**
- 位置：`docs/project-modular-diagnostics/F08-payment-provider-core/evidence/performance-analysis.md` PERF-001 段落
- 草稿原文：「`SinopacPaymentProvider` 註冊為 typed HttpClient，因此在 request pipeline 中『可能』被共用，視 DI lifetime 與 typed client 用法而定」——用了推測性措辭。
- 我核對了 `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs:40`（`services.AddSingleton<IPaymentGateway, PaymentGateway>();`）與 `SpeechMessage.Payments/Gateway/PaymentGateway.cs:30-36`（建構子接收 `IEnumerable<IPaymentProvider> providers` 並直接存入 instance 欄位 `_providers`）。這是 **確定（confirmed）而非推論**的 captive dependency：`PaymentGateway` 是 Singleton，其建構子只會在應用程式啟動時執行一次，所以透過 `AddHttpClient<SinopacPaymentProvider>()` 註冊的（原本應為 transient 的）provider 實例，實際上被鎖進 Singleton 生命週期，終身重複使用同一個 `SinopacPaymentProvider`、同一個 `_sendLock`、同一個底層 `HttpClient`。
- 這代表 PERF-001 的鎖爭用（lock contention）風險是**確定發生**，不是「視情況而定」。草稿應將此證據補強為 confirmed，而非用「may be used... depending on」這種保留語氣稀釋結論。
- 附帶可一併記錄的延伸風險（同一組證據可支撐）：因為 provider 被 captive 進 Singleton，`AddHttpClient<T>()` 原本用來輪替底層 `SocketsHttpHandler`（避免 DNS 過期）的機制也會失效，因為同一個 `HttpClient` 實例終身不變。這屬於同一 extraction seam（transport 層）可一併處理的 finding，不需要新開一條，但建議在 PERF-001 證據中補上 `PaymentGateway.cs:30-36` 與 `ServiceCollectionExtensions.cs:40` 的引用。
- 是否為阻擋核准項目：**否**，不阻擋核准，但建議在下一輪修正證據強度與嚴重度敘述（可考慮從 Medium 提升到 High，因為是確定而非「可能」發生）。

---

### 🔵 Info

**1. PERF-002 未提及 LinePayClient 舊建構子已標記 `[Obsolete]`**
- 位置：`LinePayCSharp/LinePayClient.cs:68`（`[Obsolete("建議使用接受 HttpClient 參數的建構函式，以避免 Socket 耗盡問題")]`），對應草稿引用的 `LinePayClient.cs:68` 到 `:75`。
- 草稿正確指出該建構子仍會 `new HttpClient()`，但未提及此建構子已有 Obsolete 警告主動勸阻呼叫端使用。這不影響 finding 的正確性，只是嚴重度敘述可以更精確一點（現有 code 已有部分緩解措施，非「毫無提示的陷阱」）。
- 是否為阻擋核准項目：**否**，屬於敘述完整性的小建議。

**2. SEC-002（callback replay/idempotency）的可利用性因 provider 而異，草稿未區分**
- 位置：對照 `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:54`（callback 只回傳 `PaymentStatus.Pending`，真正狀態仍需另外呼叫 `QueryPaymentAsync` 確認）與 `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs:37`、`SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:34-36`（這兩者的 callback 直接把 provider 狀態碼映射為 `PaymentStatus.Succeeded`）。
- 也就是說，重放攻擊真正能讓下游誤判為「已成功」的路徑主要是 Taishin 與 MyPay，Sinopac 的 callback 本身不會直接產出 Succeeded 狀態。草稿的 SEC-002 描述是**正確但未分級**，若補充此區分可讓修復優先序更清楚（此為我從程式碼比對得出的推論，非草稿原文明示）。
- 是否為阻擋核准項目：**否**。

**3. 遺漏高價值項目：未見明顯遺漏**
- 已比對 `MyPaySignatureVerifier.cs`、`MyPayCallbackParser.cs`、`MyPayStatusMapper.cs`、`TaishinHashVerifier.cs`、`TaishinCallbackParser.cs`、`SinopacCallbackParser.cs`、`SinopacPaymentProvider.cs`、`PaymentError.cs`、`PaymentDiagnosticsSanitizer.cs`、`IPaymentGateway.cs`、`PaymentCallbackRequest.cs`、`PaymentCallbackResult.cs`、`LinePayClient.cs`、`MyPayRequestMapper.cs`、`ServiceCollectionExtensions.cs`、`PaymentGateway.cs`。除上述 Warning 1 提到可補強的 captive-dependency 證據外，未發現草稿遺漏之其他高價值 F08 安全/效能/抽取類別問題。
- 抽取建議（extraction-analysis.md 的 5 項 seam）與現有重複程式碼證據吻合（MyPay/Sinopac/Taishin 三個 parser 的 `ReadFields`/`ReadJsonFields`/`ReadFormEncodedFields` 幾乎完全重複），合理且未過度設計。

**4. 嚴重度評級整體合理**
- SEC-001（High）、SEC-002（High）、SEC-003（Medium）、SEC-004（Medium）、PERF-001（Medium，建議見上方 Warning 1）、PERF-002（Medium）均與程式碼實際風險相符，未見誇大或低估到需要調整核准狀態的程度。

**5. 未違反 DIAGNOSIS_ONLY / runtime validation 限制**
- `runtime-validation-plan.md` 明確聲明未執行 `dotnet build/test/restore`，僅列出「未來允許修改程式碼後」可執行的驗證指令與測試案例，屬於規劃性質而非既成事實宣稱，符合任務限制。
- `issue.md`／`review-log.md` 皆標示 Nested agent count: 0，且未見任何跨模組（F09、B05/B07/F06、MVC/CRM）之實作或運行時驗證結論被誤植為 F08 範圍內的確定結論。

---

### 核准結論
**建議核准（Approve）**，無 Critical / 阻擋項目。建議在下一輪把 Warning 1（PERF-001 的 captive-dependency 證據）補強為 confirmed 等級證據，並視情況調整其嚴重度；其餘為 Info 等級的完整性建議，不影響本輪核准。

---
SESSION_ID: 4aa299b2-47de-4b7a-b429-795ecb6a4e80
