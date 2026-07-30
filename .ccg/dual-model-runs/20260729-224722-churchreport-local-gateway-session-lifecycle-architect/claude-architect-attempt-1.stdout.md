# ChurchReport Local Gateway 與 Session 資源生命週期 — 實作前架構分析

（唯讀分析，未修改任何程式。已讀取全部 9 個指定檔案 + `Line.Messaging/LineMessagingClient.cs`、`Services/Caching/SmallGroupCacheManager.cs`、Gateway 端 `GatewayWorkloadBinding.cs`/`GatewayWorkloadBoundaryTests.cs` 作為既有模式佐證。）

## 0. 關鍵程式證據重述（已驗證，非假設）

| 位置 | 事實 |
|---|---|
| `DonationPaymentManager.cs:172-173` | `new LineMessagingClient(channelAccessToken)` 呼叫的是 `LineMessagingClient` 的 **Obsolete 建構子**（`Line.Messaging/LineMessagingClient.cs:124-132`），該建構子內部 `new HttpClient()` 且 `_disposeClient=true`，即每個 `DonationPaymentManager` 都私有持有一條全新 HttpClient/連線池。 |
| `DonationPaymentManager.cs:120` | `_feeRefreshLock = new(1,1)`（`SemaphoreSlim`），類別**不是** `IDisposable`。 |
| `InMemoryDataContextSmallGroup.cs:1180-1223` | `DonationPaymentManager` 屬性以 `GetCurrentSessionId()+"_DonationPaymentManager"` 為鍵存進 `IMemoryCache`，30 分鐘 absolute + 30 分鐘 sliding；`PostEvictionCallbacks` 的 lambda 只在 `state != null` 時 `Set()` 一個 `ManualResetEvent`（目前呼叫端從未傳 `state`，等同 no-op），**完全沒有 Dispose 被逐出的 value**。此樣式在檔案內對 12 個屬性重複貼上，全部相同缺口。 |
| `AuthenticationController.Session.cs:33-72`（Logout） | 只 `HttpContext.Session.Clear()` + `CommitAsync()` + `SignOutAsync` + 刪 Cookie，**完全未觸碰 `IMemoryCache`** 中以舊 Session 複合鍵存放的物件圖。 |
| `AuthenticationController.Private.cs:171-205`（`InitializeUserSessionAsync`，Session Fixation Step 1-2） | 登入前 `Session.Clear()` + `CommitAsync()` 觸發**新 Session ID**，但同樣不觸碰舊 Session 鍵下的快取物件——舊 `DonationPaymentManager`（含未釋放 HttpClient/Semaphore）成為孤兒，直到 30 分鐘 TTL。 |
| `DonationFeePaymentProcessor.cs:47,105-165,191-220` | 這是**第二個相同形狀的洩漏**：類別已宣告 `IDisposable` 且有 finalizer，但兩個建構子都用 Obsolete 單參數 `new LineMessagingClient(token)`（一樣內部 new HttpClient），而 `Dispose(bool disposing)`（194-205 行）**只有註解，完全沒有釋放 `m_LineMessagingClient`**。這證明「有 IDisposable 骨架」不代表「有真正釋放」——是本次修復必須避免重蹈覆轍的活教材。 |
| `LineUtilityClass.cs:130-163` | **已經是正確範本**：`Dispose(bool)` 依序釋放 `m_ToolUtilityClass?.Dispose()`（實際上 Factory 管理，這行可能是誤植——見下方 Warning）、`m_LineMessagingClient?.Dispose()`、`(m_ReplyUtility as IDisposable)?.Dispose()`，並有標準 finalizer。**新設計應複製此樣式，而非重新發明。** |
| `DonationDynamicsAccessBootstrap.cs:447-593` | 已有一個非常貼近本次需求的既有範本：`DonationDynamicsAccessProcessHost`——單一 gate（`SemaphoreSlim _gate`）保護 `_provider`/`_generationKey`，`GetOrCreate` 對「設定改變」採 fail-closed（丟例外要求重啟），`DisposeAsync` 冪等（Dispose 後 `_provider=null`）。**Layer 1A 的 session-owned 資源 coordinator 應仿造這個「單一世代、gate 保護、冪等 Dispose」骨架**，但要額外處理「多個 session 世代並存、需要 ref-count 而非整體重啟」的差異（見 Q3）。 |
| `SpeechMessage.Dynamics.Gateway/Security/GatewayWorkloadBinding.cs` + `GatewayWorkloadBoundaryTests.cs:617` | Gateway 端 `WorkloadSubjectId`/`ProfileAliases` 完全由**已驗證 Windows principal（SID 優先，principal name fallback）**在伺服器端設定推導，測試明確斷言「刻意不讀取 X-Principal、X-Workload 或其他 caller-controlled header」，未帶合法身分時回 `NoResult` 觸發正式 401/403 challenge。**這個 anti-spoof 特性在 Gateway 端已經存在且有測試**，ChurchReport 端只需確保 preflight 呼叫走同一條 pipeline，不需要也不應該自建繞過路徑。 |
| `OperationIds.cs:20-21` | `RuntimeHealthWhoAmI = "runtime.health.whoami"` 已存在（ORG-CALL-00003），可直接重用，不需新增 operation。 |

---

## 1. 建議架構與資源生命週期流程

### 1.1 唯一 Owner 設計（回答 Q1）

新增一個 **Singleton** 服務（暫名 `SessionScopedResourceDisposalCoordinator`），是 session-owned cache graph 的唯一 owner。它知道 `InMemoryDataContextSmallGroup` 目前內嵌在每個屬性 getter 裡的複合鍵演算法（`GetCurrentSessionId()+"_XXX"`），並提供：

```
string AcquireLease(string cacheKeyPrefix, string suffix, Func<T> factory) → T   // 取得/建立值＋登記 lease
void ReleaseLease(string cacheKeyPrefix, string suffix)                          // request 結束時釋放
void EvictAndDrain(string cacheKeyPrefix)                                        // logout / 登入前 clear / host shutdown 呼叫
```

四個觸發點（登入前 clear、Logout、`IMemoryCache` PostEvictionCallback、host shutdown）**全部收斂呼叫同一個 `EvictAndDrain`／內部的冪等 `TryDispose` 函式**，不各自實作一套釋放邏輯——這是避免「四條路徑各寫各的、行為分歧」的關鍵設計原則，也是 `DonationDynamicsAccessProcessHost` 已驗證過的模式延伸。

`InMemoryDataContextSmallGroup` 本身是 **Scoped**（Startup.cs:643 已註冊），天然對應「一個 HTTP request 的生命週期」；讓它實作 `IDisposable`，在自己被 DI 容器 Dispose 時釋放這次 request 取得的所有 lease——這樣「in-flight request」與「outstanding lease」自然一致，不需要在每個 Controller action 手動 Release。

### 1.2 `DonationPaymentManager`／`DonationFeePaymentProcessor` 的 Dispose 範圍（回答 Q2）

兩者都應採用 `IDisposable`（同步即可，底層資源 `HttpClient`/`SemaphoreSlim`/`LineMessagingClient` 皆同步可釋放，不需要 `IAsyncDisposable`，避免過度設計）。**只釋放自己 `new` 出來的資源**，複製 `LineUtilityClass.cs:132-149` 的樣式：

- ✅ 釋放：`_feeRefreshLock`（自己 new 的 `SemaphoreSlim`）、`m_LineMessagingClient`（自己 new 的 `LineMessagingClient`，其內部 HttpClient 依 `_disposeClient` 旗標決定是否真的關閉 socket）。
- ❌ **不可**釋放：透過建構子注入的 `ILineNotificationWorkflow?`/`ILineReplyWorkflow?`（DI 擁有，可能是 Scoped/Singleton，跨多個 Manager 共用）；`ToolUtilityClass`（Factory 統一管理，兩個檔案的既有註解都已說明這點，維持現狀）。
- ⚠️ **待 Layer 1A 開工前先驗證**：`PushUtility`/`ReplyUtility` 是否自己也會 Dispose 傳入的 `LineMessagingClient`（`LineUtilityClass.cs:145` 對 `ReplyUtility` 做了 `as IDisposable` 條件轉型，暗示它可能是 IDisposable）。若是，`DonationPaymentManager.Dispose()` 與 `PushUtility`/`ReplyUtility` 的 Dispose 會**雙重擁有同一個 `LineMessagingClient` 的釋放權**——必須明確指定「誰是唯一 disposer」，另一方只能持有引用不釋放，否則會產生雙重 Dispose 競爭（`LineMessagingClient.Dispose()` 本身需具冪等性，但仍應避免依賴這點）。

冪等保護：以 `Interlocked.Exchange` 對 `_disposed` 旗標做 CAS，讓「顯式 drain」與「PostEvictionCallback 安全網」同時觸發時，只有第一個呼叫真正執行釋放。

### 1.3 避免 logout Dispose 與 in-flight request 競爭（回答 Q3 — 全流程最高風險項）

**問題場景**：Request A 正在 `SaveDonationPaymentDedicationAsync()` 內使用 `m_LineMessagingClient` 推播 LINE 通知；同時另一分頁觸發 Logout → cache eviction → Dispose。若 Dispose 在 A 的 HTTP 呼叫進行中關閉底層 HttpClient，A 會拋出 `ObjectDisposedException`，導致奉獻通知遺失且無法重試。

**設計：per-entry 參照計數（ref-count），而非全域鎖**：

1. Coordinator 對**每一個複合鍵**（而非全域）維護一個小型狀態機：`Live → Evicting → Disposed`，搭配一個 `int` 計數器（`Interlocked` 操作，不用 `SemaphoreSlim`——**若用單一全域 `SemaphoreSlim` 保護所有 session，會讓不相關使用者的請求互相序列化，是明顯的效能/死鎖陷阱**，必須避免，這點應列為 Critical 級設計禁區）。
2. `InMemoryDataContextSmallGroup` 首次存取 `.DonationPaymentManager` 時呼叫 `AcquireLease`：若狀態為 `Live`，計數 `+1` 並回傳實例；若狀態已是 `Evicting`/`Disposed`（代表舊世代正在被 logout/re-login drain），**透明地建立新世代**（新的複合鍵，因為 `GetCurrentSessionId()` 在登入後本就會產生新的 Session ID/指紋，兩者天然對齊，不需要額外協調）。
3. Logout／登入前 clear 呼叫 `EvictAndDrain`：立即 `_memoryCache.Remove(key)`（新請求再也拿不到這個實例）+ 狀態轉為 `Evicting` + 計數 `-1`（代表「快取本身持有的那一份參照」釋放）。**若計數已為 0（沒有 in-flight request），立刻同步 Dispose；若還 >0，直接返回，不阻塞 Logout 的 HTTP thread**——真正的 Dispose 由**最後一個** `ReleaseLease`（即最後一個結束的 in-flight request，經由 `InMemoryDataContextSmallGroup.Dispose()` 觸發）把計數降到 0 時執行。
4. 這是標準的「最後釋放者觸發清理」慣用法（類似 COM/SafeHandle 參照計數），不需要任何阻塞式等待，Logout 回應延遲不受影響，同時保證 Dispose 絕不會發生在真正還有 in-flight 使用者的期間。

### 1.4 IMemoryCache Eviction Callback 的 State/Value Owner（回答 Q4）

現況的 lambda（`InMemoryDataContextSmallGroup.cs` 12 處重複）技術上**沒有**捕捉 `this`／Session／HttpContext（`state` 恆為 `null`），這點是安全的，但因此也完全沒用。修正方向：

- 改用 **`static` lambda**（C# 編譯器會在編譯期強制禁止捕捉任何外部變數，包含 `this`），簽章為 `static (key, value, reason, state) => ((SessionScopedResourceDisposalCoordinator)state!).OnEvicted(key, value, reason)`，把 **Coordinator 的 Singleton 參照**透過 `PostEvictionCallbackRegistration.State` 顯式傳入——這是唯一允許存在於 closure 的「狀態」，且它本身是長壽命、無 per-request 資料的服務物件，不會洩漏 Session/Controller/HttpContext/Credential/DI graph。
- `OnEvicted` 內部：`if (value is IDisposable d) TryDisposeOnce(key, d)` — 走**與 Q3 explicit drain 相同的冪等 Dispose 入口**，讓「TTL/容量觸發的被動逐出」與「Logout/登入的主動 drain」共用同一份釋放邏輯，不允許出現第二套行為。
- 既有的 `ManualResetEvent` 測試掛鉤（目前是 dead code，因為 `state` 從未真的傳入）可保留給 Layer 1A 測試使用，但需改由 Coordinator 統一管理，不再散落在 12 個屬性各自的 lambda 裡。

### 1.5 ChurchReport 主 DI 擁有 ProductClient／Gateway 啟動 Fail-Closed（回答 Q5、Q6）

現況 `DonationDynamicsAccessBootstrap` 是 **static class**，`ProcessHost` 是 process-level static 欄位，**完全在 ASP.NET Core DI 容器之外**，由 `DonationPaymentManager` 建構子中以靜態呼叫方式取用（`DonationPaymentManager.cs:207`）。已確認的正確行為：`Package01FeeReadsEnabled=false` 時 `IsPackage01Enabled` 短路，完全不建立/解析任何 Dynamics client（`DonationDynamicsAccessBootstrap.cs:64-68`）——這點**已符合**「flag=false 時不得解析／建立 client」要求，屬 Info 級確認，非缺陷。

缺口在 flag=true 分支：
- `ProcessHost` 脫離 DI，無法在測試中替換、無法保證與其他 `IHostedService` 的啟動順序關係（目前只有一個 `DonationDynamicsAccessBootstrapLifetime` 負責 `StopAsync` 時 Dispose，`StartAsync` 是空的）。
- **設定錯誤只在第一次真正建立 `DonationPaymentManager` 時（即第一個使用者請求）才會 `throw InvalidOperationException`**，不是在應用程式啟動時。這與 `Startup.cs:320-329` 對 `CrmConnection:Password` 缺漏採用的「啟動期立即 throw」慣例不一致——後者是本專案已驗證可行的 fail-closed 慣用法，應該套用到 `DynamicsAccess:Gateway/Embedded` 設定上。

**建議**：
1. 把 `DonationDynamicsAccessProcessHost` 抽成 `IDynamicsProductClientProcessHost` 介面，以 `services.AddSingleton<...>()` 真正註冊進 DI，`DonationDynamicsAccessBootstrap.CreateFeeFormService` 從 static 方法改為注入該服務的實例方法（`BindOptions`/`AlignFromCrmConnection`/`IsPackage01Enabled` 維持純函式，不需要 DI）。
2. 新增一個 `IHostedService`（`DynamicsGatewayPreflightHostedService`），在 `StartAsync` 中：若 `Package01FeeReadsEnabled=false` 直接 no-op 返回；若 `true`，依 `ExecutionMode` 建立 executor（沿用既有的 `CreateGatewayExecutor`/`CreateEmbeddedExecutor`，設定不全會如現況一樣 throw），**並額外對 Gateway 模式執行一次 `runtime.health.whoami` 呼叫**，任何例外都讓 `StartAsync` 往外拋——.NET Generic Host 會讓 `IHost.RunAsync` 因 `StartAsync` 例外而終止啟動、process 非零退出，達成「設定錯誤時直接拒絕啟動」。
3. Whoami preflight **必須**透過正式的 `GatewayDynamicsOperationExecutor`／`IDynamicsOperationExecutor` 管線發出，不可另建一支專用 preflight HttpClient——這樣才能保證它走的認證路徑（Negotiate/Windows/AdfsOAuth，依 `Embedded.AuthMode` 或 Kestrel Negotiate binding）與正式流量完全一致，也自動繼承 Gateway 端「不接受 `X-Principal`/`X-Workload`」的既有保證（`GatewayWorkloadBoundaryTests.cs:617` 已驗證伺服器端行為；用戶端只需證明「從不主動送出這類 header」，兩者互補，不是重工）。

---

## 2. 精確 RED Test Matrix

以下每一項在目前程式碼下**應該失敗**（因為對應的行為缺口確實存在），列出目標檔案、驗證的缺口類別、與斷言重點。

| # | 類別 | 測試名稱（建議） | 目標 | 現況為何會 RED | 斷言重點 |
|---|---|---|---|---|---|
| 1 | Baseline | `DonationPaymentManager_Dispose_ReleasesOwnedSemaphoreAndLineClient` | `DonationPaymentManager` | 類別非 `IDisposable`，編譯即失敗（或反射檢查失敗） | Dispose 後對 `_feeRefreshLock` 呼叫任何操作應拋 `ObjectDisposedException`；`m_LineMessagingClient` 底層 HttpClient 的 socket handle 計數不再增加 |
| 2 | Baseline | `DonationFeePaymentProcessor_Dispose_ActuallyDisposesLineMessagingClient` | `DonationFeePaymentProcessor.cs:194-205` | `Dispose(bool)` 目前是空實作，不釋放 `m_LineMessagingClient` | 建立→Dispose→反射/包裝驗證內部 `HttpClient` 已被要求關閉 |
| 3 | Dup-Dispose | `DonationPaymentManager_Dispose_CalledTwice_IsIdempotentNoThrow` | `DonationPaymentManager` | 尚無 Dispose 實作可測 | 連續呼叫兩次 `Dispose()` 不拋例外，且第二次不重複釋放 |
| 4 | Dup-Dispose | `SessionResourceCoordinator_EvictionCallbackAndExplicitDrain_RaceToDisposeOnce` | 新 Coordinator | Coordinator 不存在 | 同時觸發「TTL 逐出回呼」與「Logout 顯式 drain」，斷言底層值的 `Dispose` 只被呼叫一次（用 mock 計數） |
| 5 | Race | `DonationPaymentManager_InFlightRequestSurvives_ConcurrentLogoutEviction` | Coordinator + `DonationPaymentManager` | 目前 Logout 完全不觸碰快取，且 Manager 無 ref-count | 模擬：取得 lease → 觸發 `EvictAndDrain` → 確認取得的實例在 lease 釋放前仍可用（不拋 `ObjectDisposedException`）→ 釋放 lease 後才真正 Dispose |
| 6 | Race | `SessionResourceCoordinator_AcquireDuringEvicting_TransparentlyGetsNewGeneration` | Coordinator | 尚無此邏輯 | 舊世代進入 `Evicting` 後，新請求呼叫 `AcquireLease` 應拿到全新實例，而非拋例外或拿到即將被 Dispose 的舊實例 |
| 7 | Race | `SessionResourceCoordinator_NoGlobalLock_ConcurrentDifferentSessionsDoNotBlockEachOther` | Coordinator | 尚無實作，也是防止「naive 全域鎖」誤植的守門測試 | 對兩個不同 session key 並行呼叫 `AcquireLease`，量測不應有超過個別 entry 鎖粒度的序列化等待 |
| 8 | Logout/Re-login | `Logout_EvictsCachedDonationPaymentManagerAndDisposesIt` | `AuthenticationController.Session.cs` | Logout 目前不呼叫任何快取清除 | 登入建立 Manager → 取得其資源控制代碼 → Logout → 斷言快取已無該鍵、資源已釋放 |
| 9 | Logout/Re-login | `Login_PreClearsPriorSessionCache_NoOrphanedManagerAcrossFixationReset` | `AuthenticationController.Private.cs:180-188` | Session Fixation 只 clear ASP.NET Session，不清 `IMemoryCache` 舊鍵 | 建立舊 Session 下的 Manager → 觸發 `InitializeUserSessionAsync`（新 Session ID）→ 斷言舊複合鍵已被 evict 且資源已釋放 |
| 10 | Logout/Re-login | `RepeatedLoginLogoutCycles_DoNotAccumulateLiveHttpClientsOrSemaphores` | 整合（AuthenticationController + Coordinator） | 目前每輪 Login→Logout 都會孤兒化一組 HttpClient+Semaphore | 迴圈 N 次 Login/Logout，斷言追蹤中的「存活資源數」不隨迭代次數線性成長（保持有界） |
| 11 | Host Shutdown | `HostShutdown_DrainsAllOutstandingSessionScopedResources` | Coordinator + `IHostedService` | 目前無任何與 host shutdown 掛鉤的 session 資源清理（`DonationDynamicsAccessBootstrapLifetime` 只管 Dynamics ProcessHost，不管 session cache graph） | 建立多個 session 世代的 Manager → 觸發 `IHostApplicationLifetime.StopApplication()`/`StopAsync` → 斷言全部已 Dispose，無殘留 |
| 12 | Eviction Callback 安全性 | `EvictionCallback_UsesStaticLambda_NoClosureOverSessionOrHttpContext` | `InMemoryDataContextSmallGroup.cs` 12 處屬性 | 現況雖未捕捉，但也是「巧合安全」而非「設計保證」，缺少防退化測試 | Roslyn/反射方式驗證 `PostEvictionCallbackRegistration.EvictionCallback` 委派的 `Target` 為 `null`（`static` lambda 特徵），防止未來有人不小心改成捕捉 `this` |
| 13 | Startup Fail-Closed | `Startup_Package01Enabled_GatewayEndpointMissing_HostFailsToStart` | `DonationDynamicsAccessBootstrap.cs` + 新 `DynamicsGatewayPreflightHostedService` | 目前只在**第一個使用者請求**時才 throw，不是啟動期 | 以 `Package01FeeReadsEnabled=true` + 缺 `Gateway:Endpoint` 建置 `IHost`，斷言 `host.StartAsync()` 拋出且不進入可接受流量狀態 |
| 14 | Startup Fail-Closed | `Startup_Package01Disabled_PreflightIsNoOp_NoClientCreated` | 同上 | 目前分支雖已正確 no-op，但缺少**防退化**測試守住這個安全預設 | `Package01FeeReadsEnabled=false` 時啟動 Host，斷言 `IDynamicsOperationExecutor` 從未被 `GetRequiredService`／從未發出任何 HTTP 呼叫 |
| 15 | Whoami/Spoof | `WhoAmIPreflight_NeverSetsPrincipalOrWorkloadHeaderOnOutgoingRequest` | `GatewayDynamicsOperationExecutor` 呼叫路徑 | 目前無此 preflight，也無守門測試 | 用 fake `HttpMessageHandler` 攔截 whoami 呼叫的 `HttpRequestMessage`，斷言 `X-Principal`/`X-Workload` header 不存在 |
| 16 | Whoami/Spoof | `WhoAmIPreflight_Failure_IsFailClosed_HostDoesNotStart` | 新 `DynamicsGatewayPreflightHostedService` | 尚無實作 | fake executor 讓 whoami 回傳非預期 principal／逾時／非 2xx，斷言 `StartAsync` 拋出 |

---

## 3. 建議檔案變更與平行 Ownership

### Layer 1A — Session resource owner／Manager disposal／cache eviction（可與 1B 平行開工）

- `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`：新增 `IDisposable`，釋放 `_feeRefreshLock` + 自建 `m_LineMessagingClient`（先確認 `PushUtility`/`ReplyUtility` 是否已各自持有釋放權，避免雙重 Dispose）。
- `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`：修正既有 `Dispose(bool)`，補上 `m_LineMessagingClient` 釋放（這是修 bug，不是新增能力）。
- 新檔：`SpeechMessageProducts.ChurchReport/Services/Caching/SessionScopedResourceDisposalCoordinator.cs`（含 per-entry 狀態機、ref-count、`static`-lambda-safe eviction 入口、與 `DonationDynamicsAccessProcessHost` 相同精神的冪等 Dispose）。
- `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`：`DonationPaymentManager` 屬性改走 Coordinator 的 `AcquireLease`；類別本身實作 `IDisposable`，在 Scoped 生命週期結束時釋放本次 request 取得的 lease。**Layer 1A 範圍刻意只涵蓋 `DonationPaymentManager` 屬性**，其餘 11 個屬性（`ListManager`/`FeeList`…）維持現狀，留待後續切片，避免這一輪變更面過大。
- 測試：新檔 `ChurchReport.MemberInfo.Tests/SessionLifecycle/` 下對應第 2 節 #1–#7、#12 的測試。

### Layer 1B — ChurchReport 主 DI／Gateway preflight／configuration contract（可與 1A 平行開工）

- `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`：把 `DonationDynamicsAccessProcessHost` 抽成可注入的 `IDynamicsProductClientProcessHost`；`BindOptions`/`AlignFromCrmConnection`/`IsPackage01Enabled` 維持 static 純函式不動。
- 新檔：`SpeechMessageProducts.ChurchReport/Services/DynamicsGatewayPreflightHostedService.cs`。
- `SpeechMessageProducts.ChurchReport/Startup.cs`：於 `ConfigureServices` 追加 `IDynamicsProductClientProcessHost` 的 singleton 註冊、追加 preflight `IHostedService`（註冊順序放在既有 `DonationDynamicsAccessBootstrapLifetime`（Startup.cs:277）**之前**，確保 `StartAsync` 先跑 preflight）。
- 測試：對應第 2 節 #13–#16。

**Startup.cs 的共用邊界（唯一交集）**：Layer 1A 只在 ~643 行（`IInMemoryDataContext` 註冊）附近追加一行 `services.AddSingleton<SessionScopedResourceDisposalCoordinator>()`；Layer 1B 只在 ~277 行（`DonationDynamicsAccessBootstrapLifetime` 註冊）附近追加。兩者是同一檔案的不同區塊，衝突機率低但非零——**列為 Warning 級協作風險**，建議兩個切片各自的 PR 都用小範圍 diff，避免大範圍重排 `ConfigureServices`。

### Layer 2 — 登入／登出／re-login integration wiring（必須等 Layer 1A + 1B 完成）

- `AuthenticationController.Session.cs`（Logout）：在 `HttpContext.Session.Clear()` **之前**取得當前複合鍵，呼叫 Coordinator 的 `EvictAndDrain`。
- `AuthenticationController.Private.cs`（`InitializeUserSessionAsync`）：Session Fixation Step 1（`Session.Clear()`）之前，同樣呼叫 `EvictAndDrain`（對舊 Session 鍵）。
- 對應第 2 節 #8–#11 的整合測試。

---

## 4. 相容性／Rollback 風險

- **API 相容性**：`DonationPaymentManager`/`DonationFeePaymentProcessor` 新增 `IDisposable` **不改變**任一現有公開方法簽章，四個既有建構子（含 `DonationPaymentManagerNamingTests.cs` 涵蓋的路徑）維持不變——低風險、可加性變更。
- **HttpClient 來源選擇**：本分析**不建議**在這一輪把 `LineMessagingClient` 的建立方式從 Obsolete 單參數建構子換成 `IHttpClientFactory` 共用 HttpClient（那需要改 `DonationPaymentManager` 建構子簽章，牽動更大面）。Layer 1A 的 MVP 是「正確釋放目前已擁有的 HttpClient」，把「改用共用連線池」列為**後續 fast-follow**、非本切片必要項，降低這一輪的 rollback 面。
- **Coordinator 為 Singleton 新依賴**：`InMemoryDataContextSmallGroup` 建構子若新增 Coordinator 參數，需為既有測試（若有直接 `new InMemoryDataContextSmallGroup(...)` 的測試）補上假物件；建議提供一個安全預設（找不到 Coordinator 時 fallback 回目前的直接 `_memoryCache.Get/Set` 行為並記警告），確保沒有走過新程式碼路徑的既有測試/呼叫端不會直接爆炸。
- **`Package01FeeReadsEnabled` 保持 `false`**：Layer 1B 的 preflight `IHostedService` 在 flag=false 時必須是嚴格 no-op（見 RED test #14），這是本次修復**不得破壞**的既有安全預設，也是任何 rollback 的最後防線——即使 Layer 1B 全部回退，只要這個旗標維持 false，production 行為不受影響。
- **Session Cache Key 演算法不可變**：Coordinator 必須沿用 `InMemoryDataContextSmallGroup.GetCurrentSessionId()` 現有的複合鍵組成方式（SessionId+BoundUserId+指紋+時間戳），不可重新設計鍵演算法本身——否則會與其餘 11 個尚未納入本次範圍的快取屬性產生鍵不一致，增加不必要的耦合面。
- **Rollback 手段**：Layer 1A/1B/2 三層若各自獨立成 PR，任一層都可單獨 revert 而不影響另兩層編譯（Layer 2 依賴 1A+1B 的公開介面，但 1A/1B 之間互不依賴）——建議照此順序合併與（必要時）回退。

---

## 5. Critical / Warning / Info 分級發現

**Critical**

1. `DonationFeePaymentProcessor.Dispose(bool)`（`Tools/DonationFeePaymentProcessor.cs:194-205`）已宣告 `IDisposable` + finalizer，卻完全不釋放 `m_LineMessagingClient`（內部持有獨立 HttpClient）——這是「看起來已修好、實際上沒修好」的高風險假象，任何以此檔案為範本的後續開發都會複製這個假安全感。
2. `DonationPaymentManager` 無 `IDisposable`，且被 `InMemoryDataContextSmallGroup` 以 Session 複合鍵快取 30 分鐘（sliding）——多使用者同時在線時，每個獨立 Session/裝置指紋組合都會累積一組未釋放的 HttpClient + SemaphoreSlim，是 production 下的漸進式 socket/handle 洩漏，符合任務描述的 release-blocking 等級。
3. Logout（`AuthenticationController.Session.cs`）與登入前 Session Fixation reset（`AuthenticationController.Private.cs`）均**未觸碰** `IMemoryCache` 中的 session-owned 物件圖，兩者都會製造孤兒物件，且目前唯一的回收機制（30 分鐘 TTL）本身也不會 Dispose（見下一項）——換言之目前完全沒有任何路徑會釋放這些資源，只能靠應用程式重啟。
4. 若未來實作 Q3 的 ref-count 機制時誤用「單一全域 `SemaphoreSlim`」而非 per-entry 狀態機，會讓不相關使用者的請求互相阻塞，屬於設計期就該排除的陷阱，已在 RED test #7 明確設防。

**Warning**

5. `DonationPaymentManager`/`LineUtilityClass` 對 `PushUtility`/`ReplyUtility` 與其內部 `LineMessagingClient` 的釋放權歸屬未明確——若 Layer 1A 為 Manager 加上 Dispose 卻未先確認這點，可能出現雙重 Dispose 或釋放權遺漏。
6. `DonationDynamicsAccessBootstrap` 目前是 static class + process-level static `ProcessHost`，設定錯誤只在**第一個使用者請求**才會 throw，不是啟動期 fail-closed，與本專案在 `Startup.cs:320-329`（`CrmConnection:Password`）已建立的「啟動期立即失敗」慣例不一致。
7. Layer 1A 與 Layer 1B 在 `Startup.cs` 的 `ConfigureServices` 有共同接觸面（不同行號區塊），雖非真正重疊，但平行開發時建議各自維持最小 diff，降低合併衝突。
8. `LineUtilityClass.cs:139` 的 `Dispose(bool)` 對 `m_ToolUtilityClass?.Dispose()`——但 `DonationPaymentManager`/`DonationFeePaymentProcessor` 兩處註解都明確表示「`ToolUtilityClass` 由 Factory 統一管理生命週期，不應手動 Dispose」；`LineUtilityClass` 這行是否為既有的另一個潛在錯誤（提前釋放 Factory 共用的單例），建議 Layer 1A 順手釐清並在測試中鎖定正確行為，避免把這個疑似 bug 一併當「正確範本」複製。

**Info**

9. `Package01FeeReadsEnabled=false` 時，`DonationDynamicsAccessBootstrap.CreateFeeFormService` 已正確短路、不建立任何 Dynamics client——這是既有的正確安全預設，Layer 1B 只需替它補上防退化測試（RED #14），不需要改邏輯本身。
10. Gateway 端 `GatewayWorkloadBinding`/`ConfigurationGatewayOperationAuthorizer` 已經是「不接受 caller-controlled header、身分完全來自已驗證 Windows principal」的正確實作，且有 `GatewayWorkloadBoundaryTests.cs` 覆蓋——ChurchReport 端的 whoami preflight 只需確保「走同一條正式 executor 管線」即可自動繼承這個保證，不需要另建一套用戶端驗證邏輯。
11. `OperationIds.RuntimeHealthWhoAmI`（`runtime.health.whoami`）已存在，Layer 1B 可直接重用，不需要新增 operation ID 或修改 registry。
12. `LineUtilityClass.cs` 的 `Dispose(bool)` 整體結構（扣除上述第 8 項的疑點）是本次「Manager 級 Dispose」設計的最佳既有範本，`DonationDynamicsAccessBootstrap.DonationDynamicsAccessProcessHost` 是本次「Coordinator 級 gate + 冪等 Dispose + fail-closed on config change」設計的最佳既有範本——兩者都應被直接引用為實作依據，而非重新設計。

---
SESSION_ID: e922ab84-07a1-443e-8462-6665f786ea54
