# ChurchReport Local Gateway 遷移與 Zero-Leak Hardening 研究

## 0. 文件定位與研究邊界

- 研究日期：2026-07-29（Asia/Taipei）。
- 研究快照：以 `b68e6a9a` 後的工作樹為準；研究期間工作樹另有其他代理正在處理的變更，本文件未修改任何 implementation、test、configuration 或 Gateway AuthN/AuthZ 檔案。
- 本文件是唯讀研究成果，不代表已啟用 `Package01FeeReadsEnabled`，也不代表 `sunnyvalechback-prod` 已取得 Local Gateway 授權。
- 本文件刻意不提出任何會與目前 Gateway AuthN/AuthZ 工作重疊的切片；以下建議不得修改：
  - `SpeechMessage.Dynamics.Gateway/Program.cs`
  - `SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs`
  - `SpeechMessage.Dynamics.Gateway/Security/IGatewayOperationAuthorizer.cs`
  - 現有 Gateway workload binding／Negotiate 測試檔案
- 研究目標只有兩個：
  1. 定義 ChurchReport 在 Development 使用 Local Gateway 的安全設定契約。
  2. 找出 ChurchReport 目前跨 request／session／user／process 的 authority、mutable state 與 resource ownership 缺口，提供可用 TDD 驗證的 zero-leak 修復順序。

## 1. 結論摘要

目前**不可**把 ChurchReport 的 `Package01FeeReadsEnabled` 改成 `true`。阻擋理由不是 ProductClient 的基本 HTTP transport，而是 ChurchReport host 仍有數個 release-blocking 的 isolation／retention 問題：

1. `Security:EnforceGlobalAuthorization=false`，且 filter 的 session fallback 預設為開啟；`_LoginPassword` 可以成為替代 authority。這不是 fail-closed。
2. `ContextDictionary` 以 static dictionary + static timer 持有 session context，並從 request scope 捕獲付款 adapter 與 LINE workflow；logout 沒有 production eviction，context 本身也沒有有效的 deterministic disposal。
3. `InMemoryDataContextSmallGroup` 把帶有 user／CRM／payment 狀態的 manager 放進 singleton `IMemoryCache` 30 分鐘；相同 ASP.NET Session ID 重新登入另一帳號時，舊狀態可能繼續被同一組 cache key 命中。
4. `Session.Clear() + CommitAsync()` 不會輪替 ASP.NET Core Session ID。現行登入流程把它當成 session fixation 防護，但實際 owner key 沒變。
5. 多個 ChurchReport 類別仍使用 `new LineMessagingClient(token)`。該建構式會自行建立 `HttpClient`，但多數 owner 沒有 Dispose；`DonationPaymentManager` 又被 session cache 長時間保留。
6. `ToolUtilityProvider` 回傳 process-wide factory singleton，但 controller／manager 的 Dispose 規則不一致：有的 request owner 釋放 shared singleton、有的長期持有、有的完全不釋放。這是 owner inversion，會同時造成 race、use-after-dispose 與 retention 風險。

相對地，Dynamics ProductClient 已具備可保留的良好基礎：

- `SocketsHttpHandler` 明確關閉 cookies、proxy、redirect 與 decompression，使用 `DefaultNetworkCredentials`，並設定連線上限與 pool lifetime。
- `GatewayDynamicsOperationExecutor` 只有 readonly dependency，每次呼叫建立自己的 `HttpRequestMessage`，response 有 byte bound，租用 buffer 歸還前清零。
- `DonationDynamicsAccessBootstrap` 已限制為單一 process generation，設定變更時拒絕熱切換，並由 hosted service 在 host shutdown Dispose provider。

因此第一個安全切片應是**ChurchReport configuration-only + contract tests**：Development 明確選 Gateway endpoint `https://localhost:7244/`，但 feature flag 保持 `false`。不得在這一切片順手新增 Gateway binding、擴張 profile alias 或 operation 權限。

## 2. Local Gateway 有效設定與信任邊界

### 2.1 Host configuration 的唯一 owner

`SpeechMessageProducts.ChurchReport/Program.cs:41-64` 使用 `WebApplication.CreateBuilder(args)`，再把同一份 `builder.Configuration` 傳給 `Startup`。這份 host-owned `IConfiguration` 應是整個 ChurchReport process 的唯一設定快照。

在 Development，預設 precedence 為：

1. `appsettings.json`
2. `appsettings.Development.json`
3. Development User Secrets（專案在 `SpeechMessageProducts.ChurchReport.csproj:3-4` 宣告 `UserSecretsId`）
4. environment variables
5. command-line arguments

後面的 provider 必須覆蓋前面的 provider。任何 manager／utility 自行 `new ConfigurationBuilder().AddJsonFile("appsettings.json")`，都會繞過 environment-specific JSON、User Secrets、environment variables 與 command line，形成第二個、不受 host 管理的 configuration authority。

### 2.2 目前值與安全目標

| 設定 | 目前 base 值 | Development 應擁有的值 | 啟用條件 |
|---|---|---|---|
| `DynamicsAccess:Package01FeeReadsEnabled` | `false`（`appsettings.json:559`） | `false` | Local Gateway profile、binding、operation 與 E2E 全部證明後才可另案開啟 |
| `DynamicsAccess:ExecutionMode` | `Embedded`（`:560`） | `Gateway` | 只改選路意圖，不代表 fee-read 已開啟 |
| `DynamicsAccess:ProfileAlias` | `sunnyvalechback-prod`（`:562`） | `sunnyvalechback-prod` | Gateway 必須另案 provision 同名 profile 與 exact binding |
| `DynamicsAccess:Gateway:Endpoint` | `https://localhost:5101/`（`:565`） | `https://localhost:7244/` | Development Kestrel HTTPS loopback endpoint |
| `DynamicsAccess:Gateway:ApiPrefix` | `/v1`（`:566`） | `/v1` | 保持現有 API contract |

目前 `SpeechMessageProducts.ChurchReport/appsettings.Development.json:1-5` 只有 profiling 設定，尚未擁有上述 Local Gateway override。因此 base JSON 的 `Embedded`／`5101` 仍是有效 Development 值（除非被 User Secrets、environment 或 command line 覆蓋）。

### 2.3 ChurchReport inbound 與 Gateway outbound 不可混為一談

- `SpeechMessageProducts.ChurchReport/Properties/launchSettings.json:28-34` 的 IIS Express inbound 設定是 `windowsAuthentication=false`、`anonymousAuthentication=true`，application URL 為 `http://localhost:43371/`。
- 這只代表使用者進 ChurchReport 的 browser request 不靠 IIS Windows Authentication；ChurchReport 自己仍以 cookie authentication 管理 end-user principal。
- ChurchReport 呼叫 Local Gateway 是另一條 outbound trust boundary。`SpeechMessage.Dynamics.ProductClient/DependencyInjection/ProductClientServiceCollectionExtensions.cs:49-73` 使用 `CredentialCache.DefaultNetworkCredentials`，讓目前 Windows process identity 參與 Negotiate handshake。
- 使用者的 cookie claim、Session value、header 或 request body 都不得被轉成 Gateway workload identity。Gateway workload identity 必須由 OS principal 與 server-owned binding 決定。

### 2.4 Local Gateway 現有能力不涵蓋目標 profile

`SpeechMessage.Dynamics.Gateway/appsettings.Development.json:10-51` 目前只有：

- exact Windows SID／principal name；
- workload subject `church-report-development`；
- profile alias `crm82`；
- capability operation `runtime.health.whoami`；
- `crm82` 指向 `.invalid` 的 fail-closed fake target，且 warm-up 關閉。

目前沒有 `sunnyvalechback-prod` profile，也沒有授權該 alias 的 binding。故 ChurchReport 即使把 `ExecutionMode` 設為 `Gateway`，也必須保持 fee-read feature flag 關閉；未來 profile provision／operation authorization 是獨立 Gateway 任務，不可混入 ChurchReport configuration slice。

## 3. Responsibility／trust／owner 對照表

| 資源或身分 | 唯一 authority | 正確 owner | 並行不變量 | failure／cleanup 契約 |
|---|---|---|---|---|
| End-user 身分 | 經 cookie handler 驗證的 `ClaimsPrincipal` | auth ticket + ASP.NET authentication middleware | 任一 request 只能看見自己的 principal；Session 不得提升 authority | 未驗證即 401／登入 redirect；logout 必須 sign out 並清除所有 session-owned state |
| Session metadata | 已驗證 principal 的衍生 metadata，不是 authority | 一個明確的 session state owner | 同一 session 換 principal 必須先 drain／evict 舊 owner | 缺失、過期、mismatch 時 fail closed；不得以 password value 自動修補 authority |
| Session mutable managers | 不應是跨 request service graph；必要資料應是 immutable DTO／bounded cache entry | request scope 或可列舉、可 eviction 的 session store | 不同 principal／session 不共用 mutable object | logout、expiry、host stop 都要 exactly-once disposal；移除後 cache count 回 baseline |
| Host configuration | `builder.Configuration` | ASP.NET host | process generation 內只讀一致 snapshot | 設定變更需 restart；不得由 static builder 產生分岔 |
| Dynamics ProductClient transport | product process identity + host options | process-level DI／HttpClientFactory | per-call request message，不共享可變 request data | host stop drain；handler／provider Dispose；設定 generation 不可熱混用 |
| Gateway authorization | Windows principal + Gateway server-owned binding | Gateway host | alias／operation exact match，無 wildcard、無 client self-assertion | 未 mapping 一律 403；本研究不改這一層 |
| LINE transport | host DI 中唯一 transport registration path | process-level HttpClientFactory／明確 DI owner | client 不保存 user／reply token；每次 request payload 獨立 | host stop 釋放；禁止 per-session `new HttpClient`；reply token 不快取 |
| Timer／background task | host lifecycle | `IHostedService`／`BackgroundService` | stop 後不得再排 callback；callback 不重疊或有明確序列化 | cancellation → drain → dispose timer／gate → clear state |
| Semaphore／lock | 擁有被保護狀態的 bounded owner | 同一 scoped/process component | 不跨 user 借用 mutable state；停止後拒絕新 wait | 先停止新工作，再等 active work，最後 Dispose；不得由 GC finalizer 承擔 |

## 4. 依嚴重度排序的發現

### Critical C-1：Authorization 目前是 fail-open，Session password 可成為替代 authority

**證據**

- `SpeechMessageProducts.ChurchReport/appsettings.json:69-72`：`EnforceGlobalAuthorization=false`、`AllowSessionIdentityFallback=true`。
- `Filters/GlobalAuthorizationFilter.cs:23-35`：enforcement 關閉時所有 action 直接通過；enforcement 開啟後，session fallback 仍預設為 `true`。
- `GlobalAuthorizationFilter.cs:65-75`：只要 `_SessionUserId` **或** `_LoginPassword` 非空就視為有身分。
- `Middleware/SessionValidationMiddleware.cs:106-129`：非 excluded path 缺 `_SessionUserId` 時仍直接呼叫 `_next`。
- `Startup.cs:834-861`：Session validation 在 `UseAuthentication()` 前執行，當下還沒有 canonical cookie principal 可供比對。
- 全專案明確 `[Authorize]` 很少，不能把 attribute coverage 當成 global filter 的替代品。

**風險**

- session store 中殘留的 password／LINE user ID 可以繞過未驗證 cookie principal。
- authentication cookie 過期、被刪除或 sign-out 後，只要 session value 尚存，就可能繼續被視為 authorized。
- filter、middleware 與 controller 各自有不同「已登入」判斷，導致 request 在某層通過、另一層使用舊 user state。

**必要契約**

1. authority 只能是 `HttpContext.User.Identity.IsAuthenticated == true`，並且必要 claim 完整、格式正確。
2. `Security:EnforceGlobalAuthorization` 預設與有效設定都要 `true`。
3. `AllowSessionIdentityFallback` 必須移除，或至少有效設定固定為 `false` 並以測試禁止回歸。
4. Session middleware 應在 `UseAuthentication()` 後執行；它只驗證 principal 與 session metadata 的一致性，不創造 authority。
5. mismatch 時先禁止後續 request，再 sign out、evict session owner、清 cookie；失敗路徑不可只寫 log 後繼續。

### Critical C-2：Static ContextDictionary 保留 request-scoped dependency，且沒有完整 eviction owner

**證據**

- `Models/ContextDictionary.cs:34-58`：static `ConcurrentDictionary` 與 static `Timer`；timer 在 type initializer 啟動，沒有 host shutdown owner。
- `ContextDictionary.cs:79-137`：key 只有 raw `session.Id`；建立 context 時從 `HttpContext.RequestServices` 解析 `IDonationPaymentCreateGatewayAdapter`、`ILineNotificationWorkflow`、`ILineReplyWorkflow`。
- 上述服務在 `Startup.cs:503-541` 的 ChurchReport graph 中屬 scoped／transient product workflow；static dictionary 會把 request scope 的 service graph 延長到最多 30 分鐘以上。
- `ContextDictionary.cs:149-210`：expiry／manual remove 只嘗試 `(removed.Context as IDisposable)?.Dispose()`；但 `InMemoryDataContextSmallGroup` 沒有實作 `IDisposable`。
- production source 找不到 logout 或 session invalidation 呼叫 `ContextDictionary.Remove`；只有 tests 呼叫它。
- dictionary 最大 1000 筆時會移除最舊 context，但這不會移除 `IMemoryCache` 裡以 session prefix 儲存的 managers。

**風險**

- scoped adapter／workflow 被 static root 持有，scope Dispose 後仍可能被呼叫，或其整個 dependency graph 永遠無法收回。
- dictionary entry 移除與 manager cache eviction 是兩套互不一致的生命週期；看似 cleanup，實際仍保留 user、CRM entity、payment state、socket owner。
- timer callback 可能在 host shutdown、test teardown 或 hot reload 後繼續執行。
- `LastAccessTime` 是可變 property；雖 dictionary thread-safe，entry 的 ownership 與 removal race 沒有 generation／disposed state 防護。

**必要契約**

1. 不得把 request-scoped service 放入 static／singleton session cache。
2. session store 必須由 DI singleton hosted owner 管理，但 entry 只能保存純資料或自己建立、自己 Dispose 的明確 resource；不能捕獲 RequestServices。
3. 每個 session owner 要追蹤其所有 cache key／resource，logout、expiry、capacity eviction、host stop 使用同一條 exactly-once cleanup path。
4. cleanup timer 改由 `BackgroundService` + cancellation／`TimeProvider` 擁有；stop 後先拒絕新增，再 drain callback，最後清空。
5. 若暫時保留 store，entry 至少要有 generation id、atomic disposed flag 與 active lease count，避免 removal 與 request 同時使用同一 mutable manager。

### Critical C-3：Session ID 沒有真正輪替；同一 browser 換帳號可命中舊 manager

**證據**

- `AuthenticationController.Private.cs:171-205`：login 前 `Session.Clear()` 後 `CommitAsync()`，註解宣稱會重新產生 Session ID。
- ASP.NET Core 的 `ISession.Clear()` 只清 key/value；`CommitAsync()` 只提交變更，不能保證輪替既有 session cookie key。
- `AuthenticationController.Private.cs:212-268`：新 user 資料寫回同一 session，並把真實帳號密碼寫入 `_LoginAccount`／`_LoginPassword`。
- `AuthenticationController.Session.cs:33-65`：logout 會 clear、commit、`SignOutAsync` 與 delete cookies，但不 eviction `ContextDictionary` 或 `IMemoryCache` session keys。
- `InMemoryDataContextSmallGroup.cs:543-587` 等多個 property 以 `GetCurrentSessionId() + manager name` 作 cache key；同一 Session ID 會取得舊 instance。

**風險**

- A 登入後在同一 browser/session 登入 B，B 可能取得 A 的 `ListManager`、`FeeList`、`DonationPaymentManager` 或其他 manager 內的 mutable state。
- logout 雖刪 cookie，舊 resource 仍保留到 cache timeout；若 session key 被重用或其他程式路徑仍持有 object reference，資料仍可被讀取。
- `Session.Clear()` 與 cache eviction 沒有 transaction ordering；高併發 request 可能一邊 clear，一邊繼續使用舊 manager。

**必要契約**

1. login identity switch 必須先標記舊 owner `closing`，阻止新 lease，等待 active request drain，再 eviction／Dispose，最後發新 auth/session identity。
2. logout 走同一 cleanup orchestration，不得只清 session key/value。
3. mutable state cache key 至少綁定 canonical principal subject + session generation；但長期方向應把 manager 改回 request scope，只快取 immutable／serializable data。
4. 任一 cleanup failure 都要 fail closed：不能因 eviction 失敗而繼續登入新 user 使用舊 graph。

### Critical C-4：InMemoryDataContext 會製造無 Session churn key，並快取不可 Dispose 的 manager graph

**證據**

- `InMemoryDataContextSmallGroup.cs:180-195`：沒有 Session 時建立 `NOSESSION_{machine}_{thread}_{ticks}`，每次存取產生新 key。
- `InMemoryDataContextSmallGroup.cs:511-531`：context 保存 `IHttpContextAccessor`、付款 adapter、LINE workflows 與 ToolUtility provider。
- `InMemoryDataContextSmallGroup.cs:549-587` 與其他 property：manager 放進 singleton `IMemoryCache`，absolute + sliding expiration 都是 30 分鐘。
- eviction callback 只在 optional `ManualResetEvent` state 非空時 Set；沒有 Dispose cached value。
- `InMemoryDataContextSmallGroup.cs:1180-1222`：`DonationPaymentManager` 也被同樣 cache 30 分鐘。

**風險**

- 非 HTTP／session 尚未建立／背景執行路徑每讀一次 property 都新增無法再次定位的 cache key，形成確定性 churn。
- sliding + absolute 同設 30 分鐘不能取代 owner cleanup；logout 應立即釋放，不是等 timeout。
- cache 裡不是純資料，而是 controller-like manager、CRM object、semaphore、LINE client 與 scoped adapter graph；cache eviction 沒有釋放它們。

**必要契約**

- 缺 Session／principal 時直接回傳 unauthorized／invalid lifecycle error，cache count 必須保持不變。
- manager 不得放 singleton memory cache；若需要效能 cache，只存 bounded immutable DTO，key 包含 tenant/profile/principal/generation，value 不可持有 HttpContext、service provider、controller 或 disposable transport。
- 所有 cache entry 必須有 size、absolute lifetime、eviction reason telemetry 與 deterministic removal API。

### Critical C-5：LINE client 仍有 per-instance socket owner，且多數 owner 沒有 Dispose

**證據**

- `Line.Messaging/LineMessagingClient.cs:60-132`：token-only 建構式會 `new HttpClient()` 並設定 `_disposeClient=true`，且已標記 obsolete；只有呼叫 `LineMessagingClient.Dispose()` 才會釋放內部 client（`:2818-2829`）。
- `Models/DonationPaymentManager.cs:137-209`：每個 manager 建立自己的 `LineMessagingClient(token)`、`PushUtility`、`ReplyUtility` 與 processor；class 沒有 IDisposable。
- `InMemoryDataContextSmallGroup.cs:1180-1222`：該 manager 被 session cache 30 分鐘，eviction 不 Dispose。
- production 仍有多個相同建立點：
  - `Tools/DonationFeePaymentProcessor.cs:110,154`
  - `Tools/LineUtilityClass.cs:190,331`
  - `Tools/PersonalQrCodeUtility.cs:79`
  - `Tools/QrCodeUtility.cs:91`
  - `Tools/RecurringDonationPaymentProcessor.cs:80`
  - `Tools/SmallGroupQrCodeUtility.cs:89`
  - `Tools/SundayQrCodeUtility.cs:79`
  - `WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:130`
  - `WebServiceConnector/LineNotifyUtility.cs:65`
- `Tools/DonationFeePaymentProcessor.cs:194-218` 與 `RecurringDonationPaymentProcessor.cs:98-121` 雖宣告 IDisposable，Dispose 仍沒有釋放 `m_LineMessagingClient`。
- `Startup.cs:508-515` 與 `LineMessagingProcessor.AspNetCore/...Extensions.cs:39-68` 已有 HttpClientFactory-based 共用 workflow DI 路徑，代表 direct-new 可以逐步消除，不需再造新 transport abstraction。

**必要契約**

1. production ChurchReport 只能經 DI workflow／processor 使用 LINE transport；source boundary test 禁止 token-only constructor。
2. 全 process 只有一條命名 HttpClient／handler ownership path；可以有 stateless wrapper，但不得為每個 session 建新 handler pool。
3. reply token、mark-as-read token 與 user-specific request payload 只能存在單一 operation stack，不得存入 singleton client、Session 或 static cache。
4. host shutdown 後 created/disposed transport owner 計數一致，沒有 background callback 再送出 LINE request。

### Critical C-6：ToolUtility 的 shared singleton owner 被 request／manager Dispose 規則破壞

**證據**

- `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs:24-36`：`IToolUtilityProvider` 註冊 singleton。
- `ToolUtilityProvider.cs:24-33`：每次 `GetToolUtility()` 回傳 `ToolUtilityFactory.GetInstance()` 的 process singleton。
- `Controllers/BaseChurchController.cs:1235-1242`：controller Dispose 會呼叫 `ToolUtility?.Dispose()`。
- 其他 manager 有的保存 factory singleton、有的 Dispose、有的完全不 Dispose；例如 `RecurringDonationPaymentProcessor.cs:98-105` 會 Dispose factory instance，而 `DonationPaymentManager` 長期保存卻沒有 lifecycle contract。

**風險**

- 任一 request／manager 完成時都可能把其他並行 request 正在使用的 shared ToolUtility／CRM dependency Dispose。
- 為避免 use-after-dispose 而「乾脆不 Dispose」又會讓 process resource 永久保留。根因不是缺一個 Dispose call，而是 owner 不唯一。

**必要契約**

- process singleton 只能由 process host Dispose；request／session owner不得 Dispose。
- 若 ToolUtility 其實包含不可安全共享的 request state，則改為 scoped factory，每個 scope 建立與 Dispose 自己 instance；不能同時宣稱 singleton 又由 consumer Dispose。
- 先以 concurrency test 證明選定模型；不要只改某一個 caller 的 Dispose。

### High H-1：CheckSessionOutAttribute 自身保存跨 request mutable SessionId，且使用 async void

`SessionAttribute.cs:25-70` 的 attribute instance field `SessionId` 會記住第一個 request 的 session，後續不同 session 被當成錯誤。`OnActionExecuting` 又是 `async void`，pipeline 無法 await、捕捉例外或保證完成順序。雖目前 source search 找不到使用者，仍應移除或封存，並加 boundary test 防止重新套用。

### High H-2：BaseChurchController 的 static password validation cache 沒有 deterministic owner

- `BaseChurchController.cs:89,141-142`：static concurrent cache。
- `:647-773`：key 使用 Session ID + 截短 password hash；session password 與 `ListManager.m_Password` 相同時視為 valid，LINE claim 還會回寫 `_LoginPassword`。
- `:821-858`：只有其他 request 進入 `EnsureCorrectUserData()` 時才 opportunistic 掃描 5 分鐘舊 entry，沒有 timer／host owner／logout cleanup。

即使 hash 不是明文，它仍是 credential-derived identifier，且 authority 判斷與 mutable manager 修補混在 controller method。應以 authenticated principal subject + session generation 驗證，不保留 password-derived static key。

### High H-3：Configuration authority 分岔會讓 Local Gateway 選路在不同類別得到不同答案

代表性 static／self-built configuration：

- `Models/DonationPaymentManager.cs:47-48`
- `Services/PaymentNotificationService.cs:43-57,310-338`
- `Services/ChurchReportLineAdminNotificationService.cs:35`
- `Models/PollManager.cs:47`
- `Tools/DonationFeePaymentProcessor.cs:56-64`
- `Tools/DonationPaymentDebugLogger.cs:32`
- `Tools/LineUtilityClass.cs:56-59`
- `Tools/PersonalQrCodeUtility.cs:64-67`
- `Tools/QrCodeUtility.cs:70-73`
- `Tools/RecurringDonationPaymentProcessor.cs:43-48`
- `Tools/SmallGroupQrCodeUtility.cs:74-77`
- `Tools/SundayQrCodeUtility.cs:64-67`
- `WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:52-58`
- `WebServiceConnector/DownloadEquipment.cs:46`
- `WebServiceConnector/EquipmentStatusCalculator.cs:35`
- `WebServiceConnector/FeeDownUpLoader.cs:50`
- `WebServiceConnector/LineNotifyUtility.cs:49-52`

Local Gateway 遷移若只改 `appsettings.Development.json`，上述類別仍可能讀 base `Embedded`、舊 endpoint 或舊 LINE token。後續必須全部改為 constructor-injected `IConfiguration`／typed options；static configuration 不得 `reloadOnChange`，因為 process-level transport generation 明確要求設定改變時 restart。

### High H-4：LoginClaims／Session 仍攜帶 credential-like value

- `Security/LoginClaimsFactory.cs:9-26` 定義 `church:pwdkey` claim。
- LINE login 把 LINE user ID 放入該 claim；多處又把 LINE user ID 放進 `_LoginPassword`。
- 帳密登入把實際 password 放入 server session（`AuthenticationController.Private.cs:242-254`）。

認證完成後不應繼續保存可重放 password。需要 CRM query 的穩定 subject 應是 contact ID／account key，並由受控 service 使用 server-side credential，不應把原 password 當成後續資料 cache 的 identity key。

### Medium M-1：Background resource 的 start／stop 邊界不一致

- `Program.cs:232-263`：DEBUG GC monitor 是無 cancellation 的 `Task.Run` + `while(true)`。
- `SessionMonitorService.cs:55-90`：兩個 timer 在 constructor 即啟動，早於 hosted `StartAsync`。
- `SessionMonitorService.cs:248-287`：`StopAsync` 只記錄統計；真正 timer Dispose 等容器稍後呼叫 `Dispose`，且沒有等待正在執行的 callback drain。
- `IdentityAuditCleanupService.cs:87-164` 使用 `BackgroundService` cancellation，方向較正確；可作為改寫參考。

所有 background work 都應由 hosted lifecycle 擁有，Start 才排程，Stop 先取消並等待 callback 完成，Dispose 只做最後不可逆資源釋放。

### Medium M-2：DonationDynamicsAccess ProcessHost 已改善，但 shutdown state 尚未完全封閉

- `DonationDynamicsAccessBootstrap.cs:447-575` 以 `_gate` 保護單一 provider generation，設定 key 改變時拒絕熱切換，這是正確方向。
- `:517-533` Dispose provider 後把欄位清空；但 `_gate` 沒有 Dispose，且沒有 `stopping/disposed` state。理論上 shutdown 後再進入 `GetOrCreate` 可以重建 provider。
- `DonationDynamicsAccessBootstrapLifetime.StopAsync`（`:596-603`）沒有把 host cancellation token 傳入等待。

功能仍關閉時風險不會被觸發，但 feature enable 前應補上「停止後拒絕新 generation、bounded drain、provider exactly-once Dispose、gate final Dispose」測試。

## 5. 應保留的 ProductClient concurrency／performance 特性

### 5.1 Transport safety

`ProductClientServiceCollectionExtensions.cs:49-73` 的設定應保留：

- `UseCookies=false`：避免 session/cookie 跨 request 污染。
- `AllowAutoRedirect=false`：避免 Negotiate／授權失敗被隱藏成跨 origin redirect。
- `UseProxy=false`：local loopback 不受不明 proxy authority 影響。
- `Credentials=DefaultNetworkCredentials` + `PreAuthenticate=false`：由 OS Negotiate challenge 決定，不自行送出 application credential。
- `MaxConnectionsPerServer=8`：有界 concurrency；後續壓測後才能調整。
- pooled lifetime 10 分鐘、idle timeout 2 分鐘、connect timeout 10 秒、request timeout 60 秒：都有上限。

### 5.2 Executor safety

`GatewayDynamicsOperationExecutor.cs:28-205` 沒有 request-specific mutable field；每次呼叫建立新的 request body、message 與 response owner，適合 process-level reuse。`ReadBoundedPayloadAsync`（`:209-270`）限制 response bytes，並在歸還 ArrayPool buffer 前清零。後續重構不得把 principal、session、request parameters 或 correlation state 放到 executor field。

### 5.3 Performance 原則

- 重用 transport／handler，不重用 user mutable state。
- cache 純資料，不 cache controller、manager、service provider、HttpContext、ClaimsPrincipal 或 disposable client。
- 以 bounded channel／semaphore 控制並行，不以無界 static dictionary 換取低 latency。
- cleanup telemetry 是 correctness 指標：created、active、closing、disposed、evicted、callback-inflight 都應可量測。
- 所有效能優化都要同時證明跨 user isolation；吞吐變快但 owner 未釋放視為失敗。

## 6. 精確 TDD RED 測試清單

以下測試先 RED，再做最小 GREEN。名稱可依專案慣例調整，但 assertion 不應弱化。

### 6.1 Configuration contract

1. `Development_configuration_selects_local_gateway_but_keeps_package01_disabled`
   - 用 `WebApplicationOptions` 指定 ChurchReport content root 與 `EnvironmentName=Development` 建 builder。
   - Assert：`ExecutionMode=Gateway`、endpoint `https://localhost:7244/`、profile `sunnyvalechback-prod`、flag `false`。
   - 目前 RED：Development JSON 未提供 override，讀到 base `Embedded`／`5101`。

2. `Environment_variable_overrides_development_json`
   - 暫時設定 `DynamicsAccess__Gateway__Endpoint=https://localhost:7444/`，建立 builder 後 assert 7444。
   - test collection 必須 serial，finally 還原 process environment。

3. `Command_line_overrides_environment_variable`
   - 同時提供 environment endpoint 7444 與 command line `--DynamicsAccess:Gateway:Endpoint=https://localhost:7555/`，assert 7555。

4. `Development_defaults_never_enable_package01`
   - 不載入開發者個人 secrets 的純 JSON contract test，直接 assert Development-owned flag 是 `false`。
   - 避免某位開發者的 User Secrets 讓 CI 測試不穩定；有效 precedence 與 repository default 要分兩個測試。

5. `Base_configuration_remains_safe_when_development_file_is_absent`
   - Assert base flag `false`；避免 publish 缺 environment JSON 時意外啟用。

### 6.2 Authentication／authority

6. `Global_authorization_rejects_session_password_without_authenticated_principal`
   - `EnforceGlobalAuthorization=true`，Session 只放 `_LoginPassword`，principal anonymous。
   - Assert AJAX 401／browser redirect。
   - 目前 RED：session fallback 通過。

7. `Global_authorization_defaults_fail_closed_when_security_keys_are_missing`
   - 空 configuration + anonymous principal + non-anonymous action。
   - Assert 拒絕；同時禁止任何 session fallback default。

8. `Repository_security_configuration_enforces_cookie_authority`
   - 直接讀 repository JSON，assert `EnforceGlobalAuthorization=true`、fallback `false`／不存在。
   - 目前 RED：base 是 false／true。

9. `Session_validation_runs_after_authentication_and_compares_subject`
   - integration pipeline 建 authenticated principal A、session metadata B。
   - Assert request 不進 controller，auth cookie sign-out，session owner eviction 被呼叫一次。

10. `Missing_session_metadata_never_creates_authority`
    - anonymous principal、protected path、空 session。
    - Assert 401／redirect；cache/store count 保持 0。

11. `AllowAnonymous_is_the_only_explicit_public_route_escape`
    - 對公開 action 加 `[AllowAnonymous]`；assert 通過。
    - 未標示 action 即使 path 長得像 login，也不能只靠字串 prefix 意外公開。

12. `CheckSessionOutAttribute_is_not_applied_anywhere`
    - reflection／source boundary 掃描 production controller metadata，assert 0 使用；刪除 class 後持續防回歸。

### 6.3 Cross-user／session isolation

13. `Two_concurrent_users_never_share_mutable_manager_instances`
    - 建立兩個 scope、兩個 HttpContext、不同 principal subject 與 session generation；以 barrier 同時讀 List/Fee/Donation state。
    - Assert object reference 不同，A 寫入的 marker 在 B 永遠不存在。

14. `Same_browser_identity_switch_drains_old_generation_before_new_state_is_visible`
    - session generation A 持有一個 active lease；啟動 B login。
    - Assert B 在 A lease release 前不能取得新 owner；release 後 A disposed exactly once，B 拿到全新 empty state。

15. `Logout_evicts_all_session_keys_and_disposes_resources_exactly_once`
    - 建立包含 fake disposable adapter、manager、LINE workflow 的 session owner，呼叫 logout orchestration。
    - Assert store count 0、所有 tracked cache key 不存在、每個 fake Dispose count = 1。

16. `Expired_session_and_manual_logout_share_the_same_cleanup_path`
    - 分別以 expiry 與 logout 清除，assert cleanup event sequence 相同：closing → drain → evict → dispose → removed。

17. `No_session_access_does_not_create_churn_cache_keys`
    - HttpContext／Session 為 null 時連續呼叫 100 次。
    - Assert 明確 exception／unauthorized result，cache entry count 前後相同，禁止 `NOSESSION_` key。

18. `Context_capacity_eviction_removes_underlying_cache_entries`
    - 容量設很小，超限觸發 oldest eviction。
    - Assert dictionary/store entry 與所有 child cache keys 同時移除，resource disposed。

### 6.4 Resource ownership

19. `Request_scope_disposal_is_not_delayed_by_session_store`
    - fake scoped `IDonationPaymentCreateGatewayAdapter` 記錄 Dispose；request scope 結束後 assert store 沒有持有它的 WeakReference。

20. `ChurchReport_production_source_has_no_token_only_LineMessagingClient_constructor`
    - source boundary 掃描 production `.cs`，排除 tests／docs，assert 無 `new LineMessagingClient(token)`。
    - 目前 RED：至少 12 個建立點。

21. `Line_transport_registration_has_one_process_owned_handler_path`
    - 建 ServiceProvider，解析所有 LINE workflow／processor；用 counting handler factory assert handler generation 有界且不隨 session/request 數成長。

22. `Concurrent_LINE_sends_do_not_cross_payload_or_retry_key`
    - 64 個並行 user，每個不同 recipient/message/retry key。
    - Assert captured requests 一一對應，singleton transport field 不保存任何 user-specific state。

23. `DonationPaymentManager_disposal_releases_semaphore_and_owned_resources`
    - 若 manager 仍保留 `_feeRefreshLock`，Dispose 後新 wait 應拒絕；LINE client／owned resource exactly once Dispose。
    - 更佳 GREEN 是 manager 回到 scope，由 DI owner Dispose，而不是 memory cache。

24. `ToolUtility_process_singleton_is_not_disposed_by_controller_scope`
    - 兩個並行 controller 共用 counting ToolUtility；dispose controller A 後，B 仍可用；host stop 才 Dispose singleton 一次。

25. `ProcessHost_reuses_one_generation_under_concurrency`
    - 100 個並行 `TryCreatePackage01Client`，assert provider/handler generation = 1，request executor 不共享 payload。

26. `ProcessHost_rejects_new_work_after_stop_begins`
    - 讓一個 operation 持有 lease，呼叫 Stop；assert新 request 被拒絕，舊 request bounded drain，provider／gate Dispose exactly once，不能重建 generation。

### 6.5 Timer／shutdown／soak

27. `Hosted_cleanup_timer_does_not_start_in_constructor`
    - 只 new service 不呼叫 Start，推進 fake time，assert callback count 0。

28. `Host_stop_waits_for_inflight_cleanup_and_prevents_future_callbacks`
    - callback 卡在 barrier，呼叫 Stop；release 後 assert Stop 完成，繼續推進時間也沒有 callback。

29. `Debug_GC_monitor_observes_host_cancellation`
    - fake time 觸發一次，取消 token，assert task 結束且沒有 orphan task。

30. `Session_soak_returns_to_declared_baseline_after_drain`
    - 以固定 seed 建立至少 1,000 個 session、每個多次 request 與 identity switch；完成 logout／expiry／host stop。
    - 必要 assertion：active owner=0、inflight callback=0、created resource count=disposed resource count、tracked cache key=0、WeakReference 可回收。
    - 記憶體門檻要以 baseline + 明確容忍值表示，並搭配 resource counter；不能只用一次 `GC.GetTotalMemory` 判斷。

31. `Gateway_transport_soak_keeps_connection_and_memory_bounds`
    - fake HTTPS/Negotiate endpoint 進行有界並行呼叫，完成後等待 idle drain。
    - Assert handler generation 不隨 request 數成長、active connection 回到 0／declared idle baseline、response buffer 無 retained payload、process private bytes 回到事前定義的容忍範圍。

## 7. 建議的 bounded implementation slices

切片依序執行；每一片都有獨立 RED/GREEN 與 rollback 點。任何片失敗都維持 `Package01FeeReadsEnabled=false`。Gateway AuthN/AuthZ 檔案不在下列 ownership 內。

### Slice 1（最先做）：ChurchReport Development configuration contract

**範圍**

- `SpeechMessageProducts.ChurchReport/appsettings.Development.json`
- 新增 ChurchReport configuration contract test（建議放 `ChurchReport.MemberInfo.Tests/Configuration/`）

**行為**

- Development 設 `ExecutionMode=Gateway`、endpoint `https://localhost:7244/`、profile `sunnyvalechback-prod`、flag `false`。
- 驗證 environment／command line precedence。
- 不改 base production defaults、不改 Gateway profile／binding、不呼叫真實 CRM。

**Rollback**

- 還原 Development JSON 與其 test 即可；因 flag 仍 false，runtime 行為不切換。

### Slice 2：Canonical cookie authority，移除 session authorization fallback

**範圍**

- `Filters/GlobalAuthorizationFilter.cs`
- `Middleware/SessionValidationMiddleware.cs`
- `Startup.cs` 的 authentication／session validation ordering
- `appsettings.json` 的 Security fail-closed defaults
- 對應 security tests

**行為**

- authenticated cookie principal 是唯一 authority。
- Session 只做一致性 metadata；mismatch 走 fail-closed cleanup hook。
- 明確 `[AllowAnonymous]` 才能公開。

**注意**

- 這是 ChurchReport end-user AuthN/AuthZ，不是 Dynamics Gateway AuthN/AuthZ；不得修改 Gateway security files。

### Slice 3：Session generation 與 login/logout cleanup orchestration

**範圍**

- `AuthenticationController.Session.cs`
- `AuthenticationController.Private.cs`
- LINE login 寫入 session 的 partial controllers
- `Security/LoginClaimsFactory.cs`
- 新增 session generation／cleanup abstraction 與 tests

**行為**

- 不再保存實際 password；claims/session 只保存 stable subject 與 login type。
- identity switch／logout 先 close 舊 generation、drain、evict、Dispose，再建立新 generation。
- `Session.Clear()+Commit` 不再被視為 ID rotation 證據。

### Slice 4：移除 static ContextDictionary 與 manager-in-IMemoryCache

**範圍**

- `Models/ContextDictionary.cs`
- `Models/InMemoryDataContextSmallGroup.cs`
- manager cache ownership tests
- 必要的新 hosted session data store（只保存純資料）

**行為**

- 禁止捕獲 RequestServices／HttpContext／scoped adapter。
- 禁止 `NOSESSION_*` key。
- mutable manager 回 request scope；需要 cache 的 DTO 具 size、identity/generation key、統一 eviction。

**Rollback**

- 此片風險高，應以 adapter seam 讓舊 store 可在 feature flag 下暫時回復；但 rollback 期間仍不得啟用 Gateway fee-read。

### Slice 5：LINE／ToolUtility process resource ownership

**範圍**

- 所有 production direct `new LineMessagingClient(token)` 建立點
- `DonationPaymentManager` 與相關 payment/QR/LINE utility
- `ToolUtilityProvider`／factory owner contract
- DI registration 與 resource-count tests

**行為**

- LINE 只走 HttpClientFactory-backed workflow。
- ToolUtility 明確選擇 process singleton 或 scope-owned instance，不允許 consumer 自行 Dispose shared singleton。
- 所有 semaphore/client/processor 有 exactly-once owner。

### Slice 6：Hosted lifecycle、shutdown drain 與 soak gate

**範圍**

- `ContextDictionary` 的 replacement hosted cleanup
- `SessionMonitorService`
- DEBUG GC monitor replacement
- `DonationDynamicsAccessBootstrap` process host stopping state
- stress／soak／profiling tests

**行為**

- Start 前無 callback；Stop 後拒絕新 work；bounded drain；resource counters 回 baseline。
- 只有本片與前五片全部通過，才允許規劃 feature enable proof。

### Slice 7（另案，不屬本研究 implementation ownership）：Gateway profile provision 與 feature enable proof

此片必須由 Gateway security owner 執行，且不得與 Slice 1 合併：

- provision `sunnyvalechback-prod` profile；
- exact Windows SID/name → workload → alias → capability operation binding；
- authenticated WhoAmI／實際 Package01 operation proof；
- browser → ChurchReport → Local Gateway E2E；
- 完成後才以獨立變更把 `Package01FeeReadsEnabled` 切為 true。

## 8. Release gate 與可量測 baseline

以下任一項未達成，視為 release blocker：

- anonymous principal 無法進入任何未標 `[AllowAnonymous]` 的 action。
- Session password／LINE ID 永遠不能單獨授權。
- 兩個並行 user 的 mutable object graph reference 全部不同。
- identity switch／logout／expiry／capacity eviction／host stop 都走同一 cleanup state machine。
- drain 後：session owner 0、cache key 0、timer callback 0、background task 0、resource created=disposed。
- production ChurchReport source 不再使用 token-only `LineMessagingClient` constructor。
- ToolUtility shared owner 在 request dispose 時不被釋放，host stop exactly once Dispose。
- ProductClient handler generation 有界，沒有 request/session principal、payload 或 token 被 static/singleton field 保存。
- Gateway feature flag 維持 false，直到 `sunnyvalechback-prod` exact authorization 與真實 E2E 證據完成。

## 9. 最終建議

先交付 Slice 1。它能讓 repository 清楚表達「Development 將來要走 Local Gateway 7244」，又因 kill switch 保持關閉而不改變 production behavior。接著先修 ChurchReport 自己的 cookie authority、session generation 與 resource ownership，再談 Gateway feature enable。

不要用增加 timeout、延長 cache、提高 `MaxItems`、增加 GC、加更多 static timer 或在更多 caller 補零散 `Dispose()` 的方式處理。這些作法沒有建立唯一 owner，反而會讓跨 user isolation 與 shutdown correctness 更難證明。正確方向是：**authority 單一、owner 單一、generation 可辨識、cleanup 可等待、資源計數可回 baseline、設定只來自 host configuration。**
