## P7.4 認獻單讀取 disabled boundary — Full-Stack Architect 獨立分析

（本分析已直接讀取現有原始碼驗證，未僅採信 Gemini 產出的推論；差異處另行標註。ZERO 檔案寫入權限，僅提供唯讀分析與示意 diff。）

---

### 1. Holistic Analysis（跨層總覽）

計畫的核心正確：以 `IPackage01DedicationBookingReadClient`（P7.1 已完成、已審過的封閉唯讀邊界）取代 `DonationBookingService.FillBookingList` 的 N+1 FetchXML 路徑，並用 sub-gate 級聯 + fail-closed 驗證來隔離風險。但从「表單模型如何被填入」這條路徑往上追，會發現一個**尚未被任一模型分析涵蓋的斷點**：`DedicationBookingList` 目前的唯一寫入路徑全部是**同步、void** 方法，而新 client 是 **Task-based**。這是本次審查最大的落地風險，Gemini 的報告完全沒有處理它。

呼叫鏈（已用 grep 驗證，非臆測）：

```
DonationPaymentModelAssembler.FillDedicationBookings(Entity, DonationPaymentFormModel)   // private, void
  → DonationPaymentModelAssembler.Build(Entity, DonationPaymentFormModel)                // public, sync, 回傳 model
    → DonationPaymentManager.SetDonationPaymentModel(Entity)                             // public, sync
      → 至少 6 個呼叫點：AuthenticationController.Private.cs:345、
        DedicationController.cs:146/158/190/716、DonationPaymentLoginController.cs:116
```

以及另外兩個獨立 sync 入口：`DonationPaymentManager.ProcessDedicationBooking()`、`GetDedicationBookingList(Entity)`（`DonationPaymentManager.cs:609,623`），皆直接呼叫 `FillBookingList`。

任務約束明文禁止 `.Result` / `.GetAwaiter().GetResult()`。若新 adapter 只在 `DonationBookingReadService.FillBookingListAsync` 停筆，而 `Build()`／`SetDonationPaymentModel()`／`ProcessDedicationBooking()`／`GetDedicationBookingList()` 不同步改為 async 並一路傳播到最上層的 6 個 controller 呼叫點（其中包含 login 流程），要嘛：
(a) 有人在某一層偷偷用 `.Result` 打平（違反明文約束），要嘛
(b) 只完成 Client/Service 層，controller 層仍呼叫舊同步 `FillBookingList`（gate 形同虛設，等於沒有 cutover）。

Gemini 報告的 Warning #1（新舊雙軌並存風險）只講到「呼叫端若沒有 if-else 分流會雙軌執行」，但沒有指出**分流本身需要把 async 一路往上傳播穿過 6 個 controller 呼叫點與一個 login 流程**，這是規模和複雜度都遠大於單純「加 if-else」的架構決策，應在 P7.4 設計文件中明確列出要修改的檔案清單與呼叫鏈深度，否則此 sub-gate 很可能陷入「gate=true 時只能靠同步阻塞才能通得過」的死局。

---

### 2. Interface Design / 修正後的邊界

沿用現有已驗證安全的 **Package02/Package03 sub-gate 模式**（`TryCreatePackage02ContactProfileClient`、`TryCreatePackage03SpecialResourceClient`：先 `BindOptions` + `EnsureNonEmptyProductProfile`，再判斷 `injectedClient`），而不是沿用有漏洞先例的 `CreateFeeFormService`（見下方 Critical #1 更正）。

```diff
--- a/SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs
+++ b/SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs
@@
+        /// <summary>
+        /// 讀取 P7.4 認獻單讀取的獨立 consumer sub-gate。必須同時依賴 Package01FeeReadsEnabled 主閘門；
+        /// 任一缺失或 false 都在 options bind、host resolution、client/pool/handler 建立或 outbound I/O 前
+        /// 回傳 false，維持與 FeeEditorRead／UngroupedCommitmentRead 一致的 fail-closed 邊界。
+        /// </summary>
+        public static bool IsPackage01DedicationBookingReadEnabled(IConfiguration configuration)
+        {
+            ArgumentNullException.ThrowIfNull(configuration);
+            if (!IsPackage01Enabled(configuration))
+            {
+                return false;
+            }
+
+            var raw = configuration["DynamicsAccess:Package01DedicationBookingReadEnabled"];
+            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
+                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
+        }
+
+        /// <summary>
+        /// 嘗試建立認獻單讀取 client。sub-gate=false 時在 options bind／host resolution 前回傳 null；
+        /// sub-gate=true 時先驗證非空 deployment ProfileAlias，injected facade 不能繞過此驗證。
+        /// 目前僅支援 Gateway（與 P7.1 fee read 對齊）；若未來要支援 Embedded，
+        /// 必須同時新增 RequestGuard 允許清單項目（見 Info #2）。
+        /// </summary>
+        public static IPackage01DedicationBookingReadClient? TryCreatePackage01DedicationBookingReadClient(
+            IConfiguration configuration,
+            IPackage01DedicationBookingReadClient? injectedClient = null)
+        {
+            ArgumentNullException.ThrowIfNull(configuration);
+            if (!IsPackage01DedicationBookingReadEnabled(configuration))
+            {
+                return null;
+            }
+
+            var productOptions = BindOptions(configuration);
+            EnsureNonEmptyProductProfile(productOptions, "Package01 dedication booking read");
+            EnsureGatewayOnly(productOptions);
+            if (injectedClient is not null)
+            {
+                return injectedClient;
+            }
+
+            var processHost = GetStartedProcessHost();
+            var executor = CreateGatewayExecutor(productOptions, processHost);
+            return new Package01DedicationBookingReadClient(
+                executor,
+                NullLogger<Package01DedicationBookingReadClient>.Instance);
+        }
```

`EnsureGatewayOnly` 呼叫是刻意補上的：目前 `CreateGatewayExecutor` 內部本來就會在 Embedded 選項（無 `Gateway.Endpoint`）時丟例外，能達到相同 fail-closed 結果，但屬於「意外落地」而非「設計宣告」。顯式呼叫 `EnsureGatewayOnly`（與 P7.1 `CreateFeeFormService` 一致）可讓錯誤訊息與意圖更明確，也讓未來要開放 Embedded 時，這行會被迫改掉而不會被忽略。

---

### 3. Findings

#### Critical

**C1｜Gemini 報告引用的「注入客戶端繞過 ProfileAlias 驗證」漏洞範例方法名有誤，且真正有漏洞的是尚未被提到的 `CreateFeeFormService`**
- 檔案：`SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs:56-104`（`CreateFeeFormService`），對照 `:240-263`（`TryCreatePackage02ContactProfileClient`）
- 原理說明：Gemini 報告聲稱 `TryCreatePackage02ContactProfileClient` 等工廠方法「若 injectedClient 不為 null 會直接回傳，未驗證 ProfileAlias」。經逐行核對，`TryCreatePackage02ContactProfileClient`（:253-258）與 `TryCreatePackage03SpecialResourceClient`（:330-335）都是先 `BindOptions` + `EnsureNonEmptyProductProfile`，才判斷 `injectedClient`——這個模式是**安全的**，也正是 Gemini 自己在步驟 1 程式碼中實際採用的模式。真正略過 profile 驗證的是 `CreateFeeFormService`（P7.1 費用表單）：當 `injectedFeeReadClient is not null` 時（:77-87），只做 `injectedOptions ?? Options.Create(BindOptions(configuration))`，**從未呼叫 `EnsureNonEmptyProductProfile` 或任何非空檢查**；`TryCreatePackage01Client`（:110-126）也不接受 `injectedClient` 參數，因此該類別完全沒有「注入繞過驗證」的既有先例可供 P7.4 抄錯。
- 修復建議：(1) 新 `TryCreatePackage01DedicationBookingReadClient` 必須依照 Package02/03 模式撰寫（如上方 diff），不要參照 `CreateFeeFormService`；(2) 建議另開一張技術債票，回頭修正 `CreateFeeFormService` 的 `injectedFeeReadClient` 分支缺少 profile 驗證的既有問題，避免下次有人以它為範本複製漏洞。

**C2｜Async adapter 若不能一路傳播到全部 6 個同步呼叫點，將被迫違反「禁止 `.Result`/`GetAwaiter().GetResult()`」的硬性約束**
- 檔案：`SpeechMessageProducts.ChurchReport/Services/DonationPaymentModelAssembler.cs:138-153`（`FillDedicationBookings`、`Build`）、`SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs:350-359,609-626`（`SetDonationPaymentModel`、`ProcessDedicationBooking`、`GetDedicationBookingList`），以及其上游 6 個呼叫點（`AuthenticationController.Private.cs:345`、`DedicationController.cs:146/158/190/716`、`DonationPaymentLoginController.cs:116`）
- 原理說明：這些方法全部是 `void`/同步回傳，且沒有一個目前是 `async`。新 client 是 `Task<IReadOnlyList<DedicationBookingRecordDto>>`。計畫文件與 Gemini 的程式碼都只到 `DonationBookingReadService.FillBookingListAsync` 為止，完全沒有交代 gate=true 分支要如何從 controller 一路以 async 呼叫下來。若實作者為了「不動 controller」而抄捷徑加 `.GetAwaiter().GetResult()`，就直接違反任務的硬性約束，且在 IIS/ASP.NET 同步 context 下有 deadlock 風險（尤其 login 流程 `DonationPaymentLoginController.cs:116` 是高風險位置）。
- 修復建議：在正式編碼前，設計文件必須明列要改為 `async Task` 的完整檔案/方法清單與呼叫鏈深度（至少上述 3 個 service 層方法 + 6 個 controller action），並確認 ASP.NET 版本（Core 或 Framework）決定 controller action 改 async 的可行性；若某些呼叫點（如 `AuthenticationController.Private.cs:345` 的 login 建置流程）暫時無法承受 async 化，應明確聲明該路徑在 P7.4 範圍內維持舊同步路徑（gate 對它保持關閉），而不是含糊帶過。

---

#### Warning

**W1｜Legacy 雙軌並存的分流粒度未定義到 method 層級**（延伸 Gemini Warning #1，補充具體檔案座標）
- 檔案：同 C2 清單
- 原理說明：Gemini 只抽象地說「controller 需嚴格 if-else 分流」，但 `FillDedicationBookings`（assembler 私有方法）、`ProcessDedicationBooking`、`GetDedicationBookingList` 是三個**互相獨立**的同步入口，且都直接呼叫 `_bookingService.FillBookingList`。若只改掉 `FillDedicationBookings` 而漏了 `ProcessDedicationBooking`/`GetDedicationBookingList`，會造成同一個 contact 在不同使用者操作路徑上出現新舊資料不一致（例如登入時走新路徑，但認獻管理頁面重新整理時仍走舊 N+1 路徑）。
- 修復建議：三個入口必須同時、一致地套用同一個 gate 判斷，或在設計文件明確排除某些入口不在 P7.4 範圍內並說明理由。

**W2｜`DedicationBookingRecordDto.EndDate`/`StartDate` 為 `null` 時的預設值與 legacy 語意需要明確核對，而非直接沿用 Gemini 範例中的 `DateTimeOffset.MinValue`**
- 檔案：`SpeechMessage.Dynamics.ProductClient/Models/DedicationBookingRecordDto.cs:66-74`（DTO 註解明確指出「空值表示上游未提供日期，並不表示無限期保留或重試讀取」）
- 原理說明：Gemini 的 `MapToModel` 範例用 `(dto.EndDate ?? DateTimeOffset.MinValue).LocalDateTime.ToShortDateString()`。對於仍在進行中、尚無結束日期的認獻單（常見情境），這會顯示「0001/1/1」。這**可能**與舊 `DonationBookingService.MapBooking`（`_utility.GetEntityDateTimeAttribute(...).ToLocalTime().ToShortDateString()`，未做 null 特殊處理）行為一致，但也可能不一致（取決於 `GetEntityDateTimeAttribute` 對缺欄位的既有語意）——這是行為對等性（parity）問題，不是本次新增的安全風險，但若沒有在 P7.4 測試中明確比對 legacy 與新路徑在「進行中認獻單」情境下的顯示輸出，很容易在 cutover 後才被使用者回報為畫面異常。
- 修復建議：新增一組針對「`EndDate=null`／`StartDate=null`（進行中認獻單）」的固定樣本測試，同時跑 legacy `MapBooking` 與新 `MapToModel`，比對輸出字串是否一致；不一致則需要產品端明確決定要維持哪種顯示語意。

---

#### Info

**I1｜資源生命週期與無狀態約束（審查通過）**
- 檔案：`SpeechMessage.Dynamics.ProductClient/FeeReads/Package01DedicationBookingReadClient.cs`
- 說明：已核對此類別為 stateless singleton，`_executor`/`_logger` 皆為 DI 擁有的無狀態參考；每次呼叫只在方法範圍建立 `Dictionary`/DTO/`ReadOnlyCollection`，取消權杖以 `ConfigureAwait(false)` 原樣往下傳遞；`operation.Succeeded`、`OperationId`、`ResponseKind`、`DedicationBookingRecords is null` 四重檢查未通過即 fail closed（不發佈部分資料），符合任務對 lifecycle ownership／partial publication 的要求。此部分計畫與現有實作已經一致，不需修改。

**I2｜Embedded 模式的 `RequestGuard` 允許清單尚未包含此 operation，與 Gateway-only 硬編碼形成雙重但脆弱的一致性**
- 檔案：`SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs:823-832`（`GetOrCreateEmbeddedExecutor` 內的 `RequestGuard` 陣列，僅含 `RuntimeHealthWhoAmI`、`MemberInfoContactUpdateBasicInfo`、`MemberInfoContactUpdateLineProfile`、`MemberInfoContactCountUngroupedCommitment`）
- 說明：`OperationIds.PaymentsDedicationRetrieveByContact` 已在 `Package01OperationRegistry`／`Data8ProfileOperationExecutor`／`Package01Data8ReadOperations` 完成 Embedded 端執行邏輯（已核對存在），但**未列入** Embedded 的 `RequestGuard` 允許清單。目前計畫用 `CreateGatewayExecutor` 強制走 Gateway，所以不會觸發此缺口；但若未來（P7.5+ 或另一次重構）改用 `CreatePackage02Executor` 式的 dual-dispatch 讓此 capability 也支援 Embedded，忘記同步更新 `RequestGuard` 清單會導致執行期被攔截（fail-closed，非安全漏洞，但屬於容易漏改的耦合點）。建議在新增的 XML 註解中明確寫出「若日後支援 Embedded，必須同時更新此處」，作為給未來實作者的路標。

**I3｜P7.5/P8 邊界未被觸碰（審查通過）**
- 說明：本計畫範圍內未出現 CE mutation、feature enablement 變更、traffic 變更或任何 P7.5/P8 相關程式碼／設定鍵；`Package01DedicationBookingReadEnabled` 是全新獨立鍵，未與既有已上線的 `Package01FeeEditorReadEnabled`／`Package02UngroupedCommitmentReadEnabled` 共用旗標，符合「可獨立 rollback」原則。

---

### 4. Integration Notes

- **與 Gemini 分析的差異僅兩處需要更正**：(1) Critical 漏洞範例的方法名指錯（C1，應指向 `CreateFeeFormService` 而非 `TryCreatePackage02ContactProfileClient`）；(2) Gemini 完全未觸及的 async 呼叫鏈落地問題（C2），這是本次審查新增的最高風險項，建議列為**進入實作前的阻斷項**（blocker），因為它直接決定「gate=true 分支是否可能被迫違反 `.Result`/`GetAwaiter().GetResult()` 禁令」這個任務的硬性紅線。
- 建議下一步：在 Trellis 設計文件中新增一節「async 傳播範圍」，明列需要改為 `async Task` 的檔案／方法／呼叫點清單（本報告已提供完整清單），經確認可行後才進入 TDD 實作階段。

---
SESSION_ID: 1d339ccd-ce97-4df5-9c62-95782e34d3e2
