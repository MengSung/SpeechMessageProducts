# Phase 4 Local/Central Gateway boundary verification

## Scope

This milestone makes Central Gateway and Local Gateway safe deployments of the
same product-facing `ExecutionMode=Gateway` contract. It does not enable product
traffic, create the CE 8.2 worker, remove Data8, expand Embedded, or change
`Package01FeeReadsEnabled=false`.

## Implemented boundary

- Added a dedicated `GatewayProductDynamicsOptionsValidator` registered through
  `IValidateOptions<ProductDynamicsOptions>` and `ValidateOnStart()`.
- Accepted both the Central endpoint example
  `https://dynamics-gateway.internal/` and the current Local endpoint
  `https://localhost:7244/` without adding Central/Local enum values.
- Rejected HTTP, URI user-info/query/fragment, raw `/api/data/`,
  `/XRMServices/`, `Organization.svc`, unsafe API prefixes, unsafe or oversized
  profile aliases, an inactive Embedded branch, and response limits outside
  1 KiB through 8 MiB.
- Added `Gateway.MaxResponseBytes`, defaulting to 2 MiB.
- Pinned every request to the deployment-configured profile alias. A differing
  request alias is rejected before any HTTP send.
- Changed the ProductClient to `ResponseHeadersRead` and one bounded reader for
  both declared `Content-Length` and chunked responses.
- Limited every rented read buffer to at most 16 KiB, disposed response/stream
  objects deterministically, and zeroed both rented and temporary payload
  buffers before release.
- Preserved caller cancellation as `OperationCanceledException`; other
  transport/read failures return sanitized errors and log only exception type.
- Upgraded only `System.Security.Cryptography.Xml` from `10.0.9` to `10.0.10`
  in the temporary Data8 project.

## TDD evidence

### Startup validator

Before implementation:

```text
ProductModeOptionsTests
Failed 12, Passed 4
```

The failures were the expected unsafe endpoint, API-prefix, and inactive
Embedded cases. Additional red tests then covered unsafe/oversized aliases and
response-limit bounds.

After implementation:

```text
ProductModeOptionsTests
Failed 0, Passed 26
```

### ProductClient profile and response boundary

Before implementation, the following tests failed for their expected reasons:

- request alias override reached HTTP instead of failing before send;
- declared oversized content was read by the default buffered completion path;
- chunked oversized content produced only a JSON parse error rather than a byte-limit error;
- caller cancellation was converted to a failure result.

After implementation:

```text
GatewayProductClientTests
Failed 0, Passed 7
```

The tests also prove that a chunked oversized response stream is disposed and a
declared oversized body is rejected before a body read is attempted.

## Dependency-security evidence

Before the package change, NuGet reported five High advisories for:

```text
System.Security.Cryptography.Xml 10.0.9
```

After changing only that package to `10.0.10`:

```text
No vulnerable package is reported for PowerPlatform.Dataverse.Client.
```

This patch does not change the architectural status of Data8. It remains a
temporary CE 8.2 compatibility dependency that must be process-isolated and
removed after a proven Web API v8.2 or official net48 `CrmServiceClient` worker
replacement passes the documented gates.

## Final local verification

```text
dotnet test SpeechMessage.Dynamics.Tests --configuration Release --no-restore
Passed 125, Failed 0, Skipped 0

dotnet build SpeechMessageProducts.sln --configuration Release --no-restore
Build succeeded, 0 warnings, 0 errors

dotnet list PowerPlatform.Dataverse.Client package --vulnerable --include-transitive
No vulnerable package reported

git diff --check
Passed
```

Additional checks passed for nine touched text files:

- UTF-8 without BOM;
- CRLF line endings;
- no added password, bearer token, client secret, private key, or refresh token;
- no static/shared ProductClient session, token, credential, `HttpClient`,
  `AsyncLocal`, `ThreadLocal`, or default authorization-header state.

## CCG review

Run:

```text
20260729-135309-dynamics-local-central-boundary-implementation-reviewer
```

- Gemini completed and recommended PASS with no Critical finding.
- Claude was blocked by provider session quota and produced no output.
- Runner state: `degradedFallback=true`, `fallbackAccepted=true`,
  `quotaBlocked=true`.

This is a single-model degraded review, not a completed dual-model review.
Gemini's one Warning was that Data8/WS-Trust remains legacy technical debt. The
finding is valid and already enforced by the SPEC: Data8 is temporary, cannot
become the permanent Gateway pool, and remains subject to worker isolation and
Phase 6 removal gates. No new unresolved Warning was identified in the boundary
implementation itself.

## Remaining gates

This milestone is complete, but the overall Dynamics objective is not. The next
required work remains:

1. immutable `crm82`/`crm91` profile generations, routing, replace-and-drain,
   shared organization admission, and multi-profile soak tests;
2. a bounded recyclable CE 8.2 Legacy Worker and later official replacement;
3. authenticated WinRM administration and DC/D365 VM role/configuration proof;
4. live CE 8.2 and CE 9.1 smoke tests without recording secrets;
5. ChurchReport Local Gateway startup, feature-flagged migration, rollback, and
   browser end-to-end verification;
6. Phase 4 resource/performance soak, Phase 5 product migration, and Phase 6
   final Data8/SDK removal.

---

## 2026-07-29 Gateway inbound body 與 canonical queue 增量

### 實作契約

- Gateway 先完成 authentication 與 server-owned
  principal→workload→alias→operation authorization，之後才驗證 Content-Type、
  Content-Length、讀取 body、租用 buffer、解析 JSON 或建立 executor request。
- Operation endpoint 採 fail-closed JSON-only 契約：只接受大小寫不敏感的
  `application/json`，且只能省略參數或宣告一個 UTF-8 charset；缺漏、無法解析、
  `application/*+json`、未知／重複參數與非 UTF-8 charset 都在 body I/O 前回 415。
- Kestrel、IIS 與 application reader 共用同一個 deployment-owned hard body
  ceiling；Content-Length 與 chunked limit+1 都有明確 413 行為。
- Reader 限制 UTF-8 wire bytes、JSON depth、root member allowlist 與所有 object
  的 case-insensitive duplicate property，並在成功、失敗、取消與 exception 路徑
  將整個 rented array 清零後 Return；ASP.NET Core request stream 不由 reader Dispose。
- Canonical preparer 在 public non-async executor frame 內完成 registry/type validation
  與 detached scalar normalization。Queue 只保存 bounded prepared state 與 exact
  canonical bytes，不保存原始 request、mutable dictionary、`JsonElement`、
  `JsonDocument`、`HttpContext`、principal、session、token 或 credential graph。
- `PreparedOperationDispatch` 是 prepared buffer 的唯一 owner；Dispose 具並行冪等性，
  且 lease cleanup 必須先完成，才能清零並歸還 prepared buffer。

### RED／GREEN 證據

新增 JSON-only 媒體型別 theory 後，六個不支援案例先以預期原因 RED：目前 endpoint
回 200，而契約要求 415。完成最小實作後：

```text
GatewayRequestBodyBoundaryTests
Passed 24, Failed 0, Skipped 0
```

完整 Dynamics 測試與方案驗證：

```text
SpeechMessage.Dynamics.Tests Release
Passed 227, Failed 0, Skipped 1 (live SQL contract)

SpeechMessageProducts.sln Release build
Build succeeded, 0 warnings, 0 errors
```

完整 solution 測試排除一個與本增量無關的既有 RichMenus root-detection 測試後，
其餘專案全部通過。該既有測試硬編碼尋找不存在的 `ChurchReport.sln`，實際根方案為
`SpeechMessageProducts.sln`；本增量沒有修改該測試。

### 編碼、格式與秘密值

- 15 個 scoped source/config/test/spec/review-input 檔案通過 strict UTF-8 without
  BOM、CRLF-only、final CRLF。
- Scoped `dotnet format --verify-no-changes` 與 `git diff --check` 通過。
- 未發現 literal credential、private key、bearer token 或只以 `<inheritdoc />`
  取代實質繁體中文說明的新增成員。
- `Package01FeeReadsEnabled=false` 維持不變。

### 外部與獨立審查

完整 CCG retry run：

```text
20260729-214756-gateway-http-canonical-final-review-retry-reviewer
ok=true
degradedFallback=false
quotaBlocked=false
Gemini=completed
Claude=completed
```

兩個 reviewer 都沒有發現 Gateway inbound-body、canonical dispatch、queue lifecycle、
Session／Memory／Resource Leakage 的 Critical。根目錄暫存 `review_diff.patch` 已保留並移至
`.ccg/dual-model-runs/legacy-review-diff-20260729.patch`，避免誤加到 production commit。

Trellis 獨立 reviewer 同樣回報 Gateway 範圍沒有 Critical 或 Warning。它與 Gemini 都指出
`LineMessagingClient` 多個既有 HTTP 路徑沒有確定性 Dispose request／response；本次 LINE diff
只有 XML 文件與空白調整，沒有新增或擴大該問題。此 finding 已記錄為獨立 repository-level
zero-tolerance lifecycle blocker，必須另立任務以 TDD、handler disposal assertion 與 soak
baseline 修復，不能把它當成此 Gateway 增量已解決的項目。

### 本增量狀態

Gateway inbound-body 與 canonical-queue 增量沒有剩餘 Gateway-specific Critical 或 Warning。
整體 Phase 4 仍未完成；ChurchReport Local Gateway、AD FS PKCE、真實 CE 8.2／9.1、durable
coordinator、fault／soak／performance、Phase 5 migration 與 Phase 6 Data8／SDK removal 仍是後續 gate。

---

## 2026-07-29 Gateway-owned success endpoint disclosure 修正

### 已修正的精確邊界

`DynamicsWebApiClient` 原本在每個成功 `OperationExecutionResult.Data` 主動加入
`approvedWebApiRoot`，因此 Gateway 序列化後會把內部 CRM hostname 與 `/api/data/v8.2|v9.1/`
基底路徑交給產品。現在成功 envelope 只保留 `operationId`、`ceVersion` 與 `data`；
`ApprovedWebApiRoot` 仍由 Profile Runtime 唯一擁有，只用於 outbound HTTPS／origin／port／
base-path allowlist，不再跨越產品信任邊界。

這項最小修正沒有改動 Authentication、取消、逾時、429／503 重試、`HttpRequestMessage`、
`HttpResponseMessage`、Stream 或 ArrayPool buffer 的 owner 與釋放順序。成功路徑也少建立一個
URI 字串並減少對外 JSON bytes。

### TDD 與驗證證據

- 新 regression test 先因 `approvedWebApiRoot` 存在而 RED。
- 最小 Production 修正後，單一 regression GREEN。
- `DynamicsWebApiClientTests`：17 passed、0 failed、0 skipped。
- 完整 `SpeechMessage.Dynamics.Tests`（排除明確 opt-in live SQL case）：228 passed、0 failed、0 skipped。
- `SpeechMessageProducts.sln` Release build：0 warnings、0 errors。
- Scoped `dotnet format` 完成；兩個 C# 檔均為 UTF-8 without BOM、CRLF、final CRLF。

完整雙模型審查：

```text
20260729-223644-dynamics-endpoint-disclosure-final-review-reviewer
ok=true
degradedFallback=false
quotaBlocked=false
Gemini=PASS
Claude=PASS
Critical=0
Warning=0
```

### 尚未被這個最小切片宣告完成的部分

## 2026-07-29 ChurchReport Local Gateway host 與 Session resource lifecycle 增量

### 實作邊界

- ChurchReport 主 DI 現在擁有唯一 `DonationDynamicsAccessProcessHost` singleton；legacy static facade 只是非 owner 路由。
- Gateway flag 啟用時，startup 透過正式 ProductClient executor 執行 bounded `runtime.health.whoami`；flag=false 與 Embedded 為 strict no-op。
- `DonationPaymentManager` 與 `DonationFeePaymentProcessor` 只 Dispose 自建 LINE client／semaphore，不越權 Dispose Factory/DI-owned dependency。
- `SessionScopedResourceDisposalCoordinator<T>` 以 opaque scope、request lease、drain、failed-cleanup retry 管理 Donation generation。
- Logout 與 re-login 在 `Session.Clear` 前撤銷 scope；實際 action／initialization 路徑已有測試，不再只測 private helper。
- `InMemoryDataContextSmallGroup` 使用 response `OnCompleted`＋`RegisterForDispose` 歸還 request-shared lease；這是因 legacy controller 手動建構 context 而核准的 owner 契約，不依賴 scoped context Dispose。

### 規格審查發現與 TDD 修正

唯讀 Trellis 規格審查最初回報三項 Critical，均已用 deterministic RED→GREEN 測試修正：

1. no-slot drain 不能刪除線性化點之後建立的新 cache generation；Session-bound acquire 必須與身份重設共用 stripe 到 lease publication 完成。
2. cache stale/callback delayed 時，不得在已移出 dictionary 的 slot 發佈新世代；必須重新取得 registered slot。
3. resource cleanup failure 不得讓 Active 假歸零或失去 owner；entry 保持 `CleanupFailed`，後續 host Dispose 序列化重試。

本機 code-quality 檢查另補上 host stop 介於 factory return 與 cache publication 之間的 failure ownership 測試，確保尚未進 cache 的資源也走相同 retry state machine。

### 當時本地驗證（後續 2026-07-30 實機證據見下節）

```text
ChurchReport.MemberInfo.Tests Release
  Passed 366 / Failed 0 / Skipped 0

SpeechMessage.Dynamics.Tests Release
  Filter: FullyQualifiedName!~SqlServerReplicaLeaseStoreLiveTests
  Passed 228 / Failed 0 / Skipped 1

SpeechMessageProducts.sln Release Build
  0 warnings / 0 errors
```

`DynamicsAccess:Package01FeeReadsEnabled=false` 維持不變。Embedded、Data8 與 `PowerPlatform.Dataverse.Client` 均未移除。

### 當時尚未完成的 Gate

- 真實 Local Gateway localhost 啟動與 ChurchReport 瀏覽器 E2E。
- CE 8.2／9.1 真實 WhoAmI、Authentication、Operation Matrix 與 rollback。
- 跨 Process 容量、Fault／Soak／Performance 與資源基準。
- Phase 5 產品遷移與 Phase 6 Data8／SDK removal gates。
- 本增量的 scoped format、最終 23 檔 strict UTF-8 without BOM／CRLF／final-CRLF、`git diff --check` 與 sensitive literal assignment gates 已通過。最終 CCG run 中 Gemini PASS 且無 Critical／Warning；Claude 因 provider session quota 未產生輸出，因此只完成核准的 degraded single-model fallback，不能宣稱完整雙模型成功。Quota 重置後仍應補跑 Claude／full dual review。

上游 OData `data` 仍可能在真實回應中帶有包含絕對 CRM URL 的 `@odata.context` 或
`@odata.nextLink`。本切片修正的是 Gateway 自己主動加入的 routing metadata；在任何真實
production operation 啟用前，OData annotation 必須改由 server-side paging 驗證／消費，
或投影成不含絕對 CRM URL 的 typed product contract。這項剩餘 gate 不能用本次假回應測試取代。

---

## 2026-07-30 Development Local Gateway／ChurchReport Browser／AD FS 增量

### Development 設定契約

- Gateway Development 使用 `(localdb)\\MSSQLLocalDB`、專用
  `SpeechMessageDynamicsControlPlane`、Integrated Security、有界 pool 32 與 5 秒 connect timeout。
- LocalDB schema 由操作者明確 provision；Gateway startup 只驗證，不連接 Dynamics 原生 SQL、
  不自建資料庫、不降級為 in-memory coordinator。
- CRM Development target 保持不可路由；允許的 operation 只會受控失敗，不得 fallback 到
  Central Gateway、Embedded、Data8、其他 alias 或正式 endpoint。
- ChurchReport Development 固定 `ExecutionMode=Gateway`、`ProfileAlias=crm82`、
  `CeVersion=8.2`、HTTPS localhost 與 `/v1`，但 `Package01FeeReadsEnabled=false`。

### 真實執行證據

LocalDB durable live contract 已另行明確啟用並通過。真實 Development Local Gateway：

```text
/health                                      200
/ready                                       200
anonymous /v1                                401
authorized Windows workload catalog          200
wrong alias                                  403
unauthorized operation                       403
allowed operation against fail-closed target controlled 400, no fallback
```

ChurchReport 與 Local Gateway 同時啟動時，ChurchReport root 回 200；in-app Browser 登入頁
`readyState=complete`、JavaScript error 0，只有兩個既有 DevExtreme deprecated warning。
驗證後兩個 process 均停止，localhost 5080／7244 listener 均釋放。

### AD FS 與 retired probe

- WinRM／Negotiate 唯讀驗證確認唯一 Public Client、單一 callback，以及 shared IFD／Gateway／
  fail-closed marker；未輸出或保存 ClientId、callback、RP identifiers、完整 endpoint 或 description。
- `docs/scripts/Invoke-AdfsTokenProbe.ps1` 現為固定 fail-closed 退役入口：不接受 credential／token／
  result 參數、不讀 appsettings、不做網路或檔案 I/O、不建立背景資源，改指向既有 Public Client
  authorization-code 診斷流程。

### 測試、格式與雙模型審查

```text
SpeechMessage.Dynamics.Tests ordinary run
  Passed 235 / Failed 0 / Skipped 1
  skipped LocalDB live contract separately enabled and passed

ChurchReport.MemberInfo.Tests
  Passed 367 / Failed 0 / Skipped 0

SpeechMessageProducts.sln Release Build
  0 warnings / 0 errors

CCG 20260730-022825-local-gateway-development-config-adfs-probe-final-review-reviewer
  ok=true / degradedFallback=false / quotaBlocked=false
  Gemini=PASS / Claude=PASS
```

Changed-file `dotnet format`、strict UTF-8 without BOM／CRLF／final CRLF、
`git diff --check` 與 added-line sensitive literal scan 均通過。Claude artifact 曾帶 provider Session
marker；該 marker 已立即自 run artifacts 移除，後續掃描為 0，文件不得保存或轉述其值。

### 仍開放的 Phase 4～6 Gate

- 真實 CE 8.2／9.1 WhoAmI、Authentication、Operation Matrix 與 rollback。
- OData `@odata.context`／`@odata.nextLink` server-side consume／typed projection。
- 跨 Process aggregate capacity、coordinator outage、fault／soak／performance 與資源 baseline。
- deployment readiness preflight 與 Package 1 consumer flag 解耦評估；consumer flag 仍須 false。
- Phase 5 單一 ChurchReport workflow parity／browser／rollback migration。
- Phase 6 Data8／SDK 強制移除 Gate；Embedded、Data8 與 `PowerPlatform.Dataverse.Client` 目前保留。

### Development workload binding hardening 已關閉

.NET Configuration 會把 array 與 nested list 依數字 leaf key 合併；因此只把 Development entry
由 index `1` 改成 `0`，仍不能清除 base `CapabilityOperationIds:1..N`。目前已改為：

```text
DynamicsGateway:ActiveWorkloadBindingSet = Central | Local | Testing
DynamicsGateway:WorkloadBindingSets:Central[*]
DynamicsGateway:WorkloadBindingSets:Local[*]
DynamicsGateway:WorkloadBindingSets:Testing[*]
```

Authorizer 先將 selector 解析成唯一直接 child set，只 materialize 該 set，再發布 frozen SID／principal
lookup。空白、wildcard、未知、scalar-only 與 childless set 都在 listener 前 fail closed，不會回退到
Central、base provider、第一組或所有集合聯集。Testing factories 也明確使用非空 `Testing` set。

TDD RED 已先證明原實作在載入 base＋Development JSON 後，會讓 Central principal 得到
`Succeeded=true`；GREEN 後同一 principal 在 Local 得到 `unmapped-principal`。核心 targeted tests：

```text
GatewayWorkloadBoundaryTests      23 passed
GatewayRequestBodyBoundaryTests   24 passed
GatewayKestrelNegotiateTests       7 passed
GatewayReadinessTests              4 passed
```

真實 Development Gateway 也重新通過 `/health=200`、`/ready=200`、anonymous 401、catalog 200、
wrong alias 403、unauthorized operation 403 與 fail-closed target controlled 400；停止後 7244 listener
與暫存 process log 均為 0。此項只關閉授權繼承 Warning，不改變仍開放的 Phase 4～6 Gate。

外部最終審查目前仍是 open gate。Gemini 多次完成並回報 PASS、無 Critical／Warning；Claude provider CLI
連續回傳 status 1 且沒有 usable output。正式 retry
`20260730-040201-development-workload-binding-set-final-review-retry-reviewer` 為 `ok=false`、
`quotaBlocked=false`、`degradedFallback=false`，因此不得稱為完整雙模型成功。Generated artifacts 已移除
Session marker、本機 profile path、設定 identity／SID／secret-reference 值；中斷 runner 留下的 temporary shim
檔案與空目錄也已清除。後續仍須重跑完整 Gemini＋Claude review，但不得因此回滾已通過的本地安全修正。

### Windows SID 身分權威與 selector 邊界補強

獨立審查找到一個真實授權漏洞：authenticated principal 同時提供「語法有效但未 mapping 的 SID」
與「已存在 binding 的相同 principal name」時，舊 `ResolveAuthenticatedBinding` 會在 SID lookup 未命中後
繼續查名稱。這會讓已移除帳號的名稱被不同 SID 新帳號取得後，錯誤繼承舊 workload 的
alias、operation、容量與稽核權限。

修正後的唯一契約是：

1. principal 提供語法有效 SID 時，該 SID 是唯一身分權威；只查 SID，未命中就回傳 `unmapped-principal`。
2. 只有 principal 完全沒有可用 SID 時，才允許 exact principal-name 相容路徑。
3. 拒絕必須發生在 executor request、admission permit、secret、token 或 outbound transport 建立之前。
4. request 熱路徑仍只讀 frozen dictionaries，不新增 lock、cache、timer、Task、socket 或 cleanup owner。

TDD 先將舊「SID 未命中可名稱 fallback」測試改成預期 403。在未改 Production 程式前，測試如預期 RED：

```text
Expected 403 Forbidden, actual 200 OK
```

最小修正將有效 SID 分支直接回傳 SID lookup 結果。修正後，「有效但未 mapping SID 必須拒絕」與
「完全沒有 SID 仍可 exact name 相容」兩個核心案例同時通過。Selector 測試也補齊：

- selector 缺少、前後空白、`*`、`?`、未知名稱與 `Local:0` 都在 Host startup fail closed；
- 真實 JSON childless object、scalar-only 與 scalar-plus-children 都各自有準確測試；
- exact set name 比對不分大小寫，但不導入 prefix、wildcard 或 configuration-path 語意。

本次修正後的 fresh 本地證據：

```text
GatewayWorkloadBoundaryTests                 31 passed / 0 failed
SpeechMessage.Dynamics.Tests ordinary run   243 passed / 0 failed / 1 skipped
ChurchReport.MemberInfo.Tests               367 passed / 0 failed
SpeechMessageProducts.sln Release build       0 warnings / 0 errors
```

本增量的強制雙模型審查已透過專案 self-healing runner 完成：

```text
20260730-045814-valid-unmapped-sid-selector-final-review-reviewer
ok=true
degradedFallback=false
quotaBlocked=false
completedBackends=gemini,claude
Gemini=PASS / Critical 0 / Warning 0
Claude=PASS / Critical 0 / Warning 0
```

兩個 reviewer 都接受「有效 SID 是唯一身分權威、未 mapping 立即拒絕、只有完全沒有 SID 才能走
exact principal-name 相容路徑」的契約，也接受 selector 只比對直接 child set、`Local:0` 不得成為
configuration path、request 熱路徑只讀 frozen lookup，且沒有新增 mutable cache、lock、timer、背景
Task、socket 或 cleanup owner。這個結果補足先前 Claude 無輸出的限制，只關閉本次 SID／selector
授權隔離增量的外部審查 Gate，不代表整體 Phase 4 完成。

審查產物已在不改變 reviewer finding 的前提下完成遮罩與生命週期清理；後續掃描為：

```text
SESSION_LEAKS=0
PROFILE_LEAKS=0
SID_LEAKS=0
CONFIG_VALUE_LEAKS=0
RECENT_SHIM_DIRECTORIES=0
LISTENER_7244=0
```

完成文件與 artifact 正規化後，fresh 最終品質 Gate 為：

```text
GatewayWorkloadBoundaryTests       31 passed / 0 failed / 0 skipped
SpeechMessage.Dynamics.Tests      243 passed / 0 failed / 1 opt-in live SQL skipped
ChurchReport.MemberInfo.Tests     367 passed / 0 failed / 0 skipped
SpeechMessageProducts.sln Release   0 warnings / 0 errors
Scoped dotnet format               35 C# files / passed
Traditional Chinese comment audit  36 program files / passed
Strict text encoding               60 delivery files / passed
git diff --check                   passed
```

Strict text gate 使用會拒絕無效位元組的 UTF-8 decoder，並拒絕 BOM、bare LF、bare CR、缺少 final
CRLF 與 Unicode replacement character；註解 Gate 則涵蓋全部變更或新增的 `.cs`／`.ps1` 檔案。

依專案擁有者最新硬性要求，所有新增或實質修改的 Production／Test／Tool／Script 程式，型別、方法與
生命週期成員都必須有完整、深入、詳細的繁體中文註解，說明 trust boundary、唯一 owner、並行、
fail-closed、取消／逾時、rollback／drain／dispose／cleanup 與效能／記憶體取捨。所有變更檔案必須為
UTF-8 without BOM、CRLF-only 且具有 final CRLF；缺漏視為交付阻擋。

此項只修正 Windows workload 身分授權邊界並擴充 selector 證據。它不啟用
`Package01FeeReadsEnabled`，不刪除 Embedded、Data8 或 `PowerPlatform.Dataverse.Client`，也不關閉真實
CE 8.2／9.1、OData 安全投影、跨 Process 容量、fault／soak／performance、Phase 5 與 Phase 6 Gate。

### ChurchReport lifecycle／文件完整雙模型補審

```text
20260730-024616-churchreport-local-gateway-documentation-lifecycle-final-review-reviewer
ok=true
degradedFallback=false
quotaBlocked=false
Gemini=completed
Claude=completed
```

Claude 逐檔回報 PASS，未發現 Donation Session coordinator、Local Gateway Development、retired
probe 或文件一致性的 Critical。Gemini 唯一 Critical 為繁體中文 mojibake；對審查範圍中的 18 個
Production／Test／Config／Script 檔案（包含 Gemini 明列的 12 檔）
重新執行 strict UTF-8 decoder、BOM、CRLF、final CRLF 與常見 mojibake pattern scan，結果為：

```text
SCOPED_ENCODING_OK
MOJIBAKE_PATTERN_MATCHES=0
```

因此 Gemini finding 判定為 reviewer 解碼誤判，而不是實際檔案損壞。兩者在該次審查共同留下的實質 Warning
是 Development `WorkloadBindings` index merge hardening；其後已由本文件前述具名 binding set 實作關閉。

Claude 對其他 legacy Session cache manager 的 Info 已做 root-cause tracing：這些 manager 本身不實作
`IDisposable`，其 CRM dependency 來自同一 process-wide `ToolUtilityFactory` singleton。並行 `Get`→`Set`
可能重複建立短命 wrapper／資料，但不會為每個 Session 建立獨立 ToolUtility connection graph；eviction
也不得直接 Dispose 共享 singleton，否則會讓其他 Session use-after-dispose。真正仍開放的是該 singleton
沒有 Production host-shutdown cleanup owner，此項列為 Phase 6 前既有 lifecycle/removal blocker。

新 run 產生的 provider Session marker 與本機 Windows identity 已從 prompts／stdout／stderr artifacts
移除；後續敏感 assignment 與 local identity scan 均為 0，未在本文件保存其值。

文件整合終審 `20260730-030439-dynamics-gateway-documentation-reconciliation-final-review-reviewer`
再由 Gemini＋Claude 完整 PASS，沒有 quota／degraded fallback；確認本 SPEC、Phase 4 證據與繁體中文
解釋說明書可作為後續 Phase 4～6 的權威依據；當時 workload-binding Warning 正確保持 open，
其後續關閉由新的實作、測試與審查 artifact 另行記錄，不能反向改寫歷史審查結果。

---

## 2026-07-30 Diagnostics operator, WinRM readiness, and browser/runtime follow-up

### Security implementation and review

- Diagnostics now requires the dedicated `diagnostics-operator` policy. The policy accepts only an authenticated cookie `NameIdentifier` GUID that appears in deployment-owned `Diagnostics:OperatorContactIds`; empty, missing, duplicate, invalid, or unlisted claims fail closed.
- Diagnostics outbound HTTP uses the bounded factory-owned `adfs-diagnostics` client. Cookies, redirects, proxy, decompression, and pre-authentication are disabled; connection count and handler/socket lifetimes are bounded.
- Production-owned ADFS handler/client disposal and the real LINE callback read-and-remove replay path have targeted lifecycle tests.
- Full self-healing external review run `20260730-140714-dynamics-adfs-operator-lifecycle-retry-reviewer` completed with Gemini and Claude, no quota fallback, and zero Critical/Warning findings. Generated provider Session markers and local absolute paths were redacted without changing the findings.

### Fresh local evidence

- `SpeechMessage.Dynamics.Tests`: 252 passed, 0 failed, 1 opt-in SQL test skipped.
- `ChurchReport.MemberInfo.Tests`: 374 passed, 0 failed.
- Release solution build: 0 warnings, 0 errors.
- Debug Gateway and ChurchReport builds: 0 warnings, 0 errors.
- Runtime matrix from project content roots: Gateway health/ready 200/200; anonymous catalog 401; authenticated catalog 200; wrong alias 403; unauthorized operation 403; approved operation against the deliberately non-routable target controlled 400; ChurchReport root/health 200/200; Diagnostics anonymous response redirected to the existing login flow without content disclosure.
- In-app browser: ChurchReport login reached `readyState=complete` with zero JavaScript errors. Diagnostics redirected away from `/diagnostics` to `/Login`, also with zero JavaScript errors. Gateway browser navigation remained gated by the local self-signed development certificate; no interstitial bypass or trust-store mutation was performed. CLI loopback HTTPS evidence remained green.
- Cleanup stopped only the verified Gateway and ChurchReport DLL listener owners. Ports 7244 and 5080 and owned `PSSession` count returned to zero.

### WinRM/DC/D365 VM result

- Both approved DNS targets resolved, TCP 5985 answered, and WSMan Identify reported a Microsoft WSMan endpoint. TCP 5986 remained closed.
- The current workstation was not domain joined, the current process was not elevated, no approved target credential entry existed, and the current Negotiate token could not create an administrative `PSSession`.
- Therefore no remote mutation was authorized or attempted. No password was tried, Basic/unencrypted WinRM was not used, TrustedHosts was not broadened, and every temporary session reference was cleared.
- The local WinRM client already had Basic and unencrypted transport enabled before this work. That insecure pre-state was not created, used, or silently modified by this task; hardening it requires a separately owned elevated change with rollback.

### Remaining gates

- Authenticated administrative WinRM configuration of the DC and D365 VMs remains blocked until an approved Kerberos/Negotiate administrative identity or existing approved session is available.
- A deployment-trusted HTTPS certificate remains required for full in-app browser proof of the Gateway page.
- Real CE 8.2/9.1 operation evidence, OData projection, cross-process capacity/fault behavior, soak/performance and shutdown baselines remain open. Phase 5 and Phase 6 remain open; `Package01FeeReadsEnabled=false`, Embedded, Data8, and `PowerPlatform.Dataverse.Client` remain unchanged.
