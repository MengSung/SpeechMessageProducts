ROLE_FILE: C:\[LOCAL_WINDOWS_IDENTITY_REDACTED]\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: churchreport-local-gateway-documentation-lifecycle-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# ChurchReport Local Gateway 文件、設定與 Session Lifecycle 最終審查

## 角色

請以高風險架構／生命週期 reviewer 身分，唯讀審查目前工作樹。不得修改檔案；不得輸出或轉述帳號、密碼、Token、Credential、Secret Reference、Session ID、Client ID、Callback 實值、完整 CRM／AD FS 私密 endpoint 或私有網路資訊。

## 核准架構

- Central Gateway 是正式環境目標。
- Local Gateway 是目前 Visual Studio／ChurchReport Development 路徑。
- Embedded 保留但延後，不建立第二套 ChurchReport Dynamics transport／pool。
- CE 8.2 與 CE 9.1 只共用產品 Gateway 契約；client、authentication state、credential、token、worker 與實體 pool 必須隔離。
- Data8 與 checked-in `PowerPlatform.Dataverse.Client` 專案目前保留，Phase 6 Gate 尚未完成。
- `DynamicsAccess:Package01FeeReadsEnabled=false` 必須維持；本次不能啟用 consumer traffic。

## 主要審查範圍

程式與測試：

- `SpeechMessageProducts.ChurchReport/Services/Caching/SessionScopedResourceDisposalCoordinator.cs`
- `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
- `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
- `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
- `SpeechMessageProducts.ChurchReport/Services/DynamicsGatewayPreflightHostedService.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Session.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs`
- `SpeechMessageProducts.ChurchReport/Startup.cs`
- `ChurchReport.MemberInfo.Tests/SessionLifecycle/`
- `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`
- `ChurchReport.MemberInfo.Tests/DynamicsGatewayPreflightHostedServiceTests.cs`
- `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorGatewayAdapterTests.cs`
- `SpeechMessage.Dynamics.Gateway/appsettings.Development.json`
- `SpeechMessageProducts.ChurchReport/appsettings.Development.json`
- `docs/scripts/Invoke-AdfsTokenProbe.ps1`

SPEC 與證據文件：

- `.trellis/spec/backend/quality-guidelines.md`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-central-boundary-verification.md`
- `.ccg/tasks/dynamics-connection-compatibility/requirements.md`
- `.ccg/tasks/dynamics-connection-compatibility/review.md`

## 必須驗證的 Session／Resource 契約

1. Session resource generation 有唯一 bounded owner、request ref-counted lease 與確定性 drain／dispose。
2. Scope lookup／creation、generation publication 與 request-lease publication 對 logout／re-login identity reset 具有同一線性化邊界。
3. no-slot drain 不會刪除線性化點之後發布的新 generation。
4. stale cache／delayed callback 不會在已移除 dictionary slot 發布孤兒 generation。
5. cleanup 失敗不會讓 Active 假歸零或遺失 owner；後續 host drain 只能由一個序列化 owner 重試同一 resource。
6. host stop 發生在 factory return 與 cache publication 間時，禁止發布，且 cleanup failure 仍保有可重試 owner。
7. response `OnCompleted` 與 `RegisterForDispose` 共用一個冪等 request lease；singleton coordinator 不保留 `HttpContext`、Controller、Session、identity、credential 或 token graph。
8. Logout 與 re-login 在 `Session.Clear` 前 drain；失敗時 fail closed。
9. Manager／Processor 只 Dispose 自建 LINE client／semaphore，不越權 Dispose Factory／DI-owned dependency。
10. process host Dispose 後 terminal；並行 caller 共用同一 cleanup，不得重建第二 generation。

## 必須驗證的 Development Gateway 契約

1. Gateway Development 使用明確 provision 的 same-user LocalDB、專用 control-plane DB、Integrated Security、有界 pool／connect timeout；startup 只驗證 schema，不連 Dynamics native SQL、不自建、不降級為 in-memory。
2. Development CRM target 保持不可路由；失敗不得 fallback 到 Central、Embedded、Data8、其他 alias 或正式 endpoint。
3. ChurchReport Development 選擇 Gateway／crm82／8.2／HTTPS loopback／`/v1`，但 Package 1 保持 false；flag=false 不建立 ProductClient、handler/pool、token cache、timer 或 operation/preflight traffic。
4. Windows Negotiate principal、workload、alias 與 operation 授權均為 server-owned；client JSON／header 無授權效果。
5. retired AD FS probe 不接受 credential/token/result 參數、不讀 appsettings、不做 network/file I/O、不建立背景資源。
6. .NET Configuration array 依 index 合併的 inherited workload-binding Warning 必須被正確記錄，不得誤宣稱 Development entry 已嚴格取代 base binding。

## 文件與編碼契約

- 所有新增或實質修改的 Production／Test／Tool／Script 程式，都必須有完整、深入、詳細的繁體中文註解，說明適用的信任邊界、owner、競爭條件、fail-closed、取消／逾時、rollback／drain／dispose／cleanup、效能與記憶體取捨；不得只翻譯語法或只使用 `<inheritdoc />`。
- 測試註解必須說明保護契約、故障注入與主要 assertion。
- 所有 scoped source／test／config／script／SPEC／Markdown 必須為 UTF-8 without BOM、CRLF、final CRLF。
- 解釋說明書必須清楚區分：Development Local Gateway／Browser fail-closed 切片已通過；真實 CE 8.2／9.1、OData annotation 投影、跨 process capacity、fault／soak／performance、Phase 5 與 Phase 6 尚未完成。

## 已有本地證據（仍須檢查程式與文件，不得只相信摘要）

- Session lifecycle focused tests：20 passed、0 failed。
- ChurchReport full tests：367 passed、0 failed。
- Dynamics ordinary run：230 passed、0 failed、1 environment skip；被 skip 的 LocalDB live contract 已另行啟用並通過。
- Release solution build：0 warnings、0 errors。
- 真實 Development Gateway：health／ready 200；anonymous 401；wrong alias／unauthorized operation 403；核准 operation 對不可路由 target 回受控 400 且無 fallback。
- ChurchReport 與 Gateway 同時啟動，Browser `readyState=complete`、JavaScript error 0；兩個 host 停止後 listener 釋放。
- AD FS 僅完成不輸出敏感值的唯讀 marker 驗證。
- Development config／retired probe 先前完整雙模型 run 已 PASS；ChurchReport lifecycle 先前只有 Gemini PASS、Claude quota-blocked，因此本輪必須重新完整檢查 lifecycle。

## 輸出格式

1. 第一行只寫 `PASS` 或 `FAIL`。
2. 依 `Critical`、`Warning`、`Info` 分組；每項提供檔案／行號、具體失敗時序與最小修正方向。
3. 明確判斷 Session／Memory／Socket／Timer／Task／Handler／Semaphore／Cache／Connection／Cancellation Registration 是否存在可信 leakage 或失去 owner 的路徑。
4. 明確判斷說明書／SPEC 是否與目前程式、設定及驗證證據一致。
5. 列出仍阻擋真實 CE 8.2／9.1、Phase 5 與 Phase 6 的 Gate，但不要把文件已正確保留的 open gate 誤報成本切片程式缺陷。
6. 明確確認 `Package01FeeReadsEnabled=false`，以及 Embedded、Data8、`PowerPlatform.Dataverse.Client` 均保留。

任何可信的跨 request／session／user／tenant 狀態洩漏、資源 retention、use-after-dispose、無界 queue/cache/task、credential disclosure、silent fallback 或 production target exposure 都是 release blocker。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.
