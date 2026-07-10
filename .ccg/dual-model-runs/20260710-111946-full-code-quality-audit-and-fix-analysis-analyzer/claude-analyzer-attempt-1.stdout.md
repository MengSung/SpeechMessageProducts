# Analysis: 全專案 Session Leak / Memory Leak / 效能缺陷稽核

## Context

本次為唯讀靜態分析，範圍涵蓋 ChurchReport 主站、Line.Messaging、ToolUtility、SpeechMessage.Payments 等 18 個專案。專案內已存在大量歷史稽核文件（記憶體優化、效能優化計畫、2026-07-06 安全稽核報告），部分修復是真的（如 QPayToolkit.cs 的 HttpClient 修復、Session Bleeding 快取隔離），但**多數文件僅涵蓋當時審查的局部範圍**，並未涵蓋本次發現的新群集。本報告以直接讀碼驗證為準，不採信文件本身的「已修復」宣稱。

---

## Top 10 高風險缺陷群集

### 1.〔Critical〕HttpClient 逐請求 / 逐 Session 建立且從未 Dispose — 最高信心度的 Socket 耗盡與記憶體洩漏源
- **證據鏈**：
  - `Controllers/QrCodeController.cs:95`、`Controllers/PhoneBindingController.cs:129` → `new QrCodeUtility()`（每次 HTTP 請求都 new，**未實作 IDisposable、呼叫端也沒有 `using`**）
  - `Tools/QrCodeUtility.cs:91` → `m_LineMessagingClient = new LineMessagingClient(channelAccessToken)`（呼叫 **Obsolete** 建構子）
  - `Line.Messaging/LineMessagingClient.cs:126` → `_client = new HttpClient(); _disposeClient = true;`（**永遠沒有任何呼叫端呼叫 Dispose**）
  - 相同模式亦出現在 `LineNotifyUtility.cs:65`、`DonationPaymentProcessor.Core.cs:130`、`SundayQrCodeUtility.cs:79`、`SmallGroupQrCodeUtility.cs:89`、`RecurringDonationPaymentProcessor.cs:80`、`PersonalQrCodeUtility.cs:79`、`LineUtilityClass.cs:190,331`、`DonationFeePaymentProcessor.cs:110,154`、`Models/DonationPaymentManager.cs:171`
- **狀態**：**已驗證**（非假設）
- **影響**：QrCode/PhoneBinding 為逐請求建立、`DonationPaymentManager` 為逐 Session 建立（見 #3），兩者皆會不斷產生未釋放的 `HttpClient`/`SocketsHttpHandler`，是典型「速度變慢＋記憶體緩慢上升＋高流量下 Socket 耗盡」成因。
- **假警報陷阱**：`文件/記憶體優化/HttpClient-修復報告.md` 宣稱 HttpClient 洩漏已修復，但**只涵蓋 QPayToolkit.cs（Phase 1）**，本群集完全在其範圍外，容易被誤判為「已處理」。

### 2.〔Critical〕`DonationPaymentManager : Controller` — 業務物件繼承 MVC Controller 且被手動 new 出來快取 30 分鐘
- **證據**：`Models/DonationPaymentManager.cs:43`
- **狀態**：class 宣告**已驗證**；是否有程式路徑意外對其設定 `ControllerContext`/`HttpContext`/`TempData` 尚未逐一追蹤（**Hypothesis**，需 Phase C 執行期確認）
- **影響**：未經 MVC Activator 初始化的 `Controller` 基底成員多為 null；若任何程式碼路徑曾經（或未來）對其寫入 `HttpContext`，該實例會被 `InMemoryDataContextSmallGroup` 快取 30 分鐘，等同把單一請求的 HttpContext 物件圖延長生命週期 30 分鐘 — 嚴重的跨請求物件洩漏風險。即使目前未觸發，這是架構層級的地雷。

### 3.〔Critical〕IMemoryCache 無 SizeLimit ＋ 逐 Session 快取的非原子 double-check ＋ Eviction callback 恆為 no-op
- **證據**：
  - `Startup.cs:170-177`：`services.AddMemoryCache(...)`「不設定 SizeLimit，讓系統根據記憶體壓力自動管理」、`CompactionPercentage = 0.10`
  - `Models/InMemoryDataContextSmallGroup.cs`：14 個屬性（ListManager、SmallGroupDataList、WeeklyReportData…DonationPaymentManager、ToolUtilityClass 等）皆用 `if (_memoryCache.Get(key)==null) { 建立...Set } return Get()` — **非原子**，同一 session 併發請求可能建立兩份實例，多餘那份直到過期都佔用記憶體
  - 所有 `PostEvictionCallbacks` 只設定 `EvictionCallback`，**從未設定 `State`**，因此 callback 內 `if (state != null)` 永遠不成立 → **eviction 時完全不做任何清理**，即使快取值持有 `LineMessagingClient`/`HttpClient` 等 IDisposable 資源，也不會在此處被釋放
- **狀態**：**已驗證**
- **影響**：高併發／大量並行 Session 時，快取物件數隨 Session churn 線性成長，且沒有真正的資源釋放路徑，只能靠 GC 記憶體壓力事後補償；與群集 #1 疊加後放大記憶體與延遲問題。
- **假警報陷阱**：程式碼與文件多處標註「✅ Session Bleeding 修復」，只解決了「跨使用者資料混淆」，**不涵蓋**本群集談的「資源釋放/併發原子性」問題，兩者不衝突但也互不涵蓋，易被合併誤判為同一件事已解決。

### 4.〔Warning〕Sync-over-async：付款／通知／Session 路徑多處 `.GetAwaiter().GetResult()`
- **證據**：`Middleware/SessionValidationMiddleware.cs:247`、`Controllers/BaseChurchController.cs:986`（`RegenerateSessionId`）、`Services/PaymentNotificationService.cs:128`（`SendLineMessage`）、`Services/ChurchReportLineAdminNotificationService.cs:110`、`Tools/DonationFeePaymentProcessor.cs:337`、`ToolUtility/PushUtility.cs:537,546,555`
- **狀態**：**已驗證**
- **影響**：ASP.NET Core 無 SynchronizationContext，不會死鎖，但每次呼叫佔用一條 threadpool 執行緒直到外部 I/O（LINE API、Session commit）完成；奉獻/付款高峰期（例如主日禮拜後）併發量上升時會加劇 threadpool 飢餓、回應延遲。
- **假警報陷阱**：`記憶體洩漏審計與修復實施步驟.md` 稱已將 `.Result` 全面改為 `.GetAwaiter().GetResult()`「保持向下相容」——這只是把「可能死鎖」換成「保證阻塞」，並未解決 sync-over-async 本身佔用執行緒的問題，容易被誤讀為「已修復」。

### 5.〔Info，已知死碼〕`CheckSessionOutAttribute`（SessionAttribute.cs:26-71）
- **狀態**：**已驗證為死碼**——`async override void`、核心邏輯全數被註解、目前無任何有效路徑套用此 Attribute（既有 2026-07-06 安全稽核報告已列為 L-3）
- **陷阱說明**：ActionFilterAttribute 實例會在多個請求間共用，其 `SessionId` instance field 若被重新啟用將造成跨請求競態；目前安全純因未被使用，屬「看起來危險、實際上是死碼」的假警報，建議直接刪除以避免未來被誤用。

### 6.〔Critical，安全但高度相關〕appsettings.json 明文機密（CRM 網域管理員密碼、LINE/金流密鑰）已入 git 版控
- 見既有 `2026-07-06_系統安全稽核報告_SessionLeakage與授權.md` C-1，本報告不重複展開，但因與「找出所有缺點」高度相關且尚未修復，建議與效能修復並行處理（優先序：機密輪替 > 其他效能項）。

### 7.〔Warning〕CrmConnectionPool 健康檢查為同步阻塞呼叫
- **證據**：`ToolUtility/ConnectionOperations/CrmConnectionPool.cs:261-269, 340-349` — `AcquireConnection` 內同步執行 `service.Execute(WhoAmIRequest)`（CRM SDK 無非同步 API）
- **狀態**：程式碼**已驗證**；實際延遲影響為 **Hypothesis**，需在 CRM 回應變慢的情境下做負載測試才能確認嚴重度
- 屬 SDK 限制而非明顯 bug，建議列為觀察項，非立即修復項。

### 8.〔Info〕`InMemoryDataContextSmallGroup.cs` 熱路徑內大量 `Debug.WriteLine`
- **狀態**：**已驗證**；因 `[Conditional("DEBUG")]`，Release 建置會被編譯器整行移除，Production 無效能影響，但 Debug/Staging 環境下有明顯負擔，且降低可讀性。
- **假警報陷阱**：容易被誤判為 Production 效能熱點，其實只影響 Debug 建置。

### 9.〔Warning〕檔案編碼污染
- **證據**：`Controllers/BaseChurchController.cs:940-1005` 讀取後顯示中文注釋為亂碼（疑似 Big5/CP950 誤存或工具鏈編碼設定不一致），其餘多數檔案為正確 UTF-8。
- **狀態**：**已驗證**存在編碼不一致，屬工具鏈/CI 問題而非執行期缺陷，但若字串常數受影響會有訊息毀損風險。

### 10.〔Warning，待驗證〕`ApiControllers/*` 授權盤點未完
- 既有安全報告已標記 `AssignSmallGroupController`、`SchedulerDataController`、`ShepherdMethodLookupController`、`SpiritLeaderLookupController` 需逐一確認是否遺漏 Session/授權檢查（與 H-3 IDOR 同型態風險）。本次因時間限制**未逐一開啟驗證**，列為 **Hypothesis**，建議排入 Phase A 檢查清單。

---

## 已驗證 vs. 需執行期驗證 一覽

| 群集 | 狀態 |
|---|---|
| 1 HttpClient 未釋放 | 已驗證（讀碼確認建構鏈與缺乏 Dispose） |
| 2 DonationPaymentManager 繼承 Controller | 結構已驗證；HttpContext 是否被實際捕捉待 Phase C 確認 |
| 3 IMemoryCache 無上限＋非原子＋eviction no-op | 已驗證 |
| 4 Sync-over-async | 已驗證；execution-time 影響程度待壓測 |
| 5 死碼 Attribute | 已驗證為無效路徑 |
| 6 明文機密 | 已驗證（沿用既有安全報告） |
| 7 CRM 健康檢查同步阻塞 | 已驗證存在；嚴重度待壓測 |
| 8 Debug.WriteLine 熱路徑 | 已驗證僅影響 Debug 建置 |
| 9 編碼污染 | 已驗證 |
| 10 ApiControllers 授權盤點 | 未驗證，需補查 |

---

## 分階段修復策略

### Phase A（確定性靜態修復＋聚焦單元測試，風險低、可獨立提交）
1. 群集 1：把所有 `new LineMessagingClient(token)` 呼叫改為注入共用 `IHttpClientFactory`/長生命週期 `HttpClient`（比照 `LineMessagingClient` 已提供的 DI 建構子），並讓 `QrCodeUtility`/`LineUtilityClass`/`DonationFeePaymentProcessor` 等實作 `IDisposable` 並在呼叫端 `using`。
2. 群集 3：修正 `PostEvictionCallbacks` 忘記設定 `State` 的問題，並在 eviction callback 中對持有 IDisposable 資源的快取值呼叫 Dispose；評估是否要為 `AddMemoryCache` 設定合理 `SizeLimit`。
3. 群集 5：刪除已確認死碼的 `CheckSessionOutAttribute`。
4. 群集 9：掃描並統一 `.cs` 檔案編碼為 UTF-8 without BOM。
5. 群集 10：逐一開啟 4 個 `ApiControllers/*` 檔案確認授權檢查是否存在，缺漏者比照 `MemberInfoController.CanViewContact` 補上。

### Phase B（較大範圍重構，需整合測試）
6. 群集 2：評估是否讓 `DonationPaymentManager` 改為一般 POCO（不再繼承 `Controller`），並確認快取生命週期內是否曾被賦予 HttpContext 相關成員。
7. 群集 3：把 `InMemoryDataContextSmallGroup` 的 14 個屬性從「Get→null 判斷→Set」改為 `GetOrCreate` 或加鎖，確保同 Session 併發請求不會建立多份實例。
8. 群集 4：把 `PaymentNotificationService.SendLineMessage`、`ChurchReportLineAdminNotificationService`、`DonationFeePaymentProcessor` 的同步阻塞路徑改為全程 async（需往上追蹤呼叫鏈，可能牽動 Controller action 簽章）。
9. 群集 6：機密移出版控（環境變數/User-Secrets/KeyVault），需搭配金鑰輪替與部署流程變更。

### Phase C（需啟動應用程式的執行期驗證）
10. 對 QrCode/PhoneBinding 端點做併發壓力測試，觀察 `netstat`/`SocketsHttpHandler` 連線數與記憶體曲線，驗證群集 1、3 修復前後差異。
11. 對 `DonationPaymentManager` 做 memory profiler（dotnet-gcdump / dotnet-trace）追蹤，確認是否存在 HttpContext 物件圖被異常延長生命週期（群集 2）。
12. 模擬 CRM 延遲，觀察 `CrmConnectionPool.AcquireConnection` 對併發請求延遲的實際影響（群集 7）。

---

## 建議的第一批窄範圍修復（風險/效益最佳）

**目標**：群集 1（HttpClient 未釋放）＋群集 3 的 eviction callback 修正。

理由：
- 兩者都是**確定性、局部性修改**，不涉及業務邏輯變更，可用既有的 DI 建構子（`LineMessagingClient(HttpClient, ...)`）與 `IHttpClientFactory` 直接替換，Blast radius 侷限在建構子與 Dispose 邏輯。
- 這兩項疊加起來是最直接對應使用者原始症狀（Session Leak / 速度慢 / Memory Leak）的根因，且修復後可立即用 Phase C 的壓力測試量化改善幅度。
- 不牽動 `DonationPaymentManager : Controller` 的架構問題（群集 2 需要更謹慎的重構與更廣的回歸測試）。

---

## 驗證前需準備的命令與測項

```bash
# 建置與既有測試基準線
dotnet build SpeechMessageProducts.sln
dotnet test ChurchReport.Tests ChurchReport.MemberInfo.Tests ToolUtility.Tests \
  Line.Messaging.Tests LineMessagingProcessor.Tests LineMessagingProcessor.RichMenus.Tests

# 修復後：確認不再有裸 new HttpClient / Obsolete 建構子呼叫
grep -rn "new HttpClient()" --include=*.cs SpeechMessageProducts.ChurchReport ToolUtility Line.Messaging
grep -rn "new LineMessagingClient(" --include=*.cs SpeechMessageProducts.ChurchReport ToolUtility

# 執行期驗證（Phase C）：連續打 QrCode/PhoneBinding 端點觀察 socket 數與記憶體
# 建議工具：dotnet-counters monitor（System.Net.Http, System.Runtime）、dotnet-gcdump collect
```

測試案例建議：
- [ ] 對 `QrCodeController`/`PhoneBindingController` 連續發送 N 個並發請求，修復前後比較連線數（`netstat`/`dotnet-counters`）與 P95 延遲。
- [ ] 針對 `InMemoryDataContextSmallGroup` 的任一屬性，模擬同一 Session 兩個並發請求，確認不再建立雙實例（可加計數斷言）。
- [ ] Eviction callback 修正後，建立單元測試驗證 30 分鐘（或縮短測試用逾時）後快取值的 Dispose 有被呼叫。
- [ ] `dotnet test` 全綠，且既有 `PushUtilityWorkflowTests`、`DonationPaymentProcessorGatewayAdapterTests` 等現有測試不回歸。

---

## 假警報陷阱總結（避免重工既有「已修復」項目）

| 陷阱 | 實際狀況 |
|---|---|
| HttpClient 記憶體洩漏已修復（HttpClient-修復報告.md） | 只涵蓋 QPayToolkit.cs，群集 1 的 12+ 個呼叫點未涵蓋 |
| Session Bleeding 已修復（多處 ✅ 註解） | 解決的是跨使用者資料混淆，不涵蓋快取資源釋放與併發原子性（群集 3） |
| `.Result` 已全面改為 `.GetAwaiter().GetResult()` | 只是避免死鎖寫法，未解決執行緒佔用本身（群集 4） |
| `CheckSessionOutAttribute` 看起來像 Session 防護機制 | 已是死碼，未在任何路徑生效（群集 5），但仍應清除以防未來誤用 |
| CrmConnectionPool 的 Timer 已驗證正確處置 | 正確，此為既有稽核唯一「無需修復」的資源型態項目 |

---

## Action Items（去重後排序）

1. [ ] Phase A：替換所有 `new LineMessagingClient(token)`／`new HttpClient()` 為 DI 共用 HttpClient，補齊 IDisposable/using
2. [ ] Phase A：修正 `InMemoryDataContextSmallGroup` eviction callback 的 `State` 缺漏，加入資源釋放邏輯
3. [ ] Phase A：刪除死碼 `CheckSessionOutAttribute`
4. [ ] Phase A：盤點並補齊 4 個 `ApiControllers/*` 的授權檢查
5. [ ] Phase A：統一檔案編碼為 UTF-8 without BOM
6. [ ] Phase B：重新設計 `DonationPaymentManager`，移除對 `Controller` 的繼承
7. [ ] Phase B：`InMemoryDataContextSmallGroup` 快取存取改為原子化（`GetOrCreate`/鎖）
8. [ ] Phase B：清除付款/通知路徑的 sync-over-async
9. [ ] Phase B：機密移出版控並輪替（已知安全項，建議與效能修復並行排程）
10. [ ] Phase C：壓力測試量化群集 1、2、3、7 的實際影響並回填本報告的 Hypothesis 項目

---
SESSION_ID: 6f059ee2-5bb4-4491-8660-24b1eea3ab8a
