# CCG architect Task: churchreport-local-gateway-session-lifecycle

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# ChurchReport Local Gateway 與 Session 資源生命週期：實作前架構分析

## 目標

為 ChurchReport 建立下一個可獨立驗證的 Local Gateway 安全切片，同時修正目前 release-blocking 的 Session／HttpClient／Semaphore 資源 retention。此分析只制定架構、TDD 契約與檔案拆分，不得修改程式。

## 已確認的程式證據

1. `DonationPaymentManager` 會自行建立 `LineMessagingClient`，而該 client 擁有自行建立的 `HttpClient`；Manager 另擁有 `SemaphoreSlim`，但沒有 `IDisposable`／`IAsyncDisposable`。
2. `InMemoryDataContextSmallGroup` 以 session-derived key 將 `DonationPaymentManager` 放入 `IMemoryCache`；eviction callback 未 Dispose value。
3. logout 與 re-login 只清 ASP.NET Session／Cookie，沒有移除並 Dispose session-owned cache graph；舊物件可留到 TTL。
4. `DonationDynamicsAccessBootstrap` 目前使用 static `ProcessHost` 與 child `ServiceProvider`，未把 ProductClient／DynamicsAccess 的 owner 納入 ChurchReport 主 DI；Local Gateway config 也尚未具備可安全啟用的 fail-closed preflight。
5. `Package01FeeReadsEnabled` 必須繼續保持 `false`，直到 Local Gateway host ownership、WhoAmI preflight、真實 CE 9.1／browser E2E 等 gate 完成。
6. `PowerPlatform.Dataverse.Client` 與 Embedded 現在都不能移除。

## 限定分析檔案

- `SpeechMessageProducts.ChurchReport/Managers/DonationPaymentManager.cs`
- `SpeechMessageProducts.ChurchReport/Data/InMemoryDataContextSmallGroup.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController.Session.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController.Private.cs`
- `SpeechMessageProducts.ChurchReport/Startup.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationFeePaymentProcessor.cs`
- `SpeechMessageProducts.ChurchReport/Utilities/LineUtilityClass.cs`
- 相關 ChurchReport／Dynamics test projects and existing test patterns

如果實際路徑不同，可用 `rg --files` 找到同名檔案；不要掃描或回顯 `appsettings*.json` 的內容、密碼、Token、Credential、ClientId、SecretReference 實際值或真實 Dynamics 位址。

## 必須回答的設計問題

1. Session-owned graph 的唯一 owner 應放在哪個服務／抽象？登入前、登出、session invalidation、cache eviction、host shutdown 如何共用同一個 concurrent-idempotent evict＋Dispose 路徑？
2. `DonationPaymentManager` 應實作 `IDisposable` 或 `IAsyncDisposable`？`LineMessagingClient`、`SemaphoreSlim`、其他可釋放成員的順序與並行競爭如何處理？
3. 如何避免 logout Dispose 與正在執行的 request 競爭造成 ObjectDisposedException 或跨 Session 提前釋放？是否需要 lease/ref-count、per-session gate 或延後 drain？
4. `IMemoryCache` eviction callback 的 state/value owner 如何設計，避免 callback closure 保留 Session、Controller、HttpContext、Credential 或整個 DI graph？
5. ChurchReport 主 DI 應如何擁有 ProductClient／Dynamics bootstrap，而不是 static child provider？在 flag=false 時不得解析／建立 client；flag=true+Gateway 時 endpoint／alias／preflight 不符合必須 startup fail-closed。
6. 第一個 Local Gateway preflight 是否只做 `runtime.health.whoami`，以及如何證明不接受 `X-Principal`／`X-Workload` spoof header？
7. 請提出可平行實作的非重疊檔案 ownership：
   - Layer 1A：Session resource owner／Manager disposal／cache eviction tests。
   - Layer 1B：ChurchReport main DI／Gateway preflight／configuration contract tests。
   - Layer 2：登入／登出／re-login integration wiring（必須等 Layer 1 完成）。
8. 每個 RED test 必須先能因現況缺口失敗，並包含資源 baseline、重複 Dispose、競爭、logout/relogin 與 host shutdown assertions。

## 強制品質規則

- 零容忍 Session、Token、Credential、Memory、Socket、HttpClient、Semaphore、Timer、Task、Subscription 或 Cache graph leakage。
- 所有新增或實質修改的 Production／Test 程式都要有完整、深入、詳細的繁體中文 XML／實作註解，說明信任邊界、唯一 owner、並行競爭、fail-closed、取消／逾時、drain／Dispose／cleanup 順序，以及效能／記憶體取捨。
- UTF-8 without BOM、CRLF、final CRLF。
- 不開啟 `Package01FeeReadsEnabled`，不修改真實 credential，不做遠端 WinRM mutation，不移除 Embedded 或 Data8。

## 輸出格式

- 建議架構與資料／資源生命週期流程。
- 精確 RED test matrix。
- 建議檔案變更與平行 ownership。
- 相容性／rollback 風險。
- Critical／Warning／Info 分級發現。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.