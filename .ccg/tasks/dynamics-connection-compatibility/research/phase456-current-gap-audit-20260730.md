# Phase 4～6 current gap audit（2026-07-30）

本報告是唯讀稽核。沒有修改 Production／Test／Tool／Configuration 程式，也沒有啟動 Gateway、ChurchReport、WinRM、DC 或 D365 VM；本輪沒有重跑測試，因此所有既有 pass count 都只視為歷史證據，不視為 fresh verification。

結論：Phase 4、Phase 5、Phase 6 必須全部保留。2026-07-29 的新 SPEC 只取代衝突的架構決策，不會自動關閉任何 Gate：Central Gateway 仍是正式環境目標；Local Gateway 是目前 Visual Studio／ChurchReport 驗證路徑，兩者使用同一個 `ExecutionMode=Gateway` 契約；Embedded 保留但延後；官方 SDK 只能位於 Gateway adapter／worker 後方；Data8 仍是暫時相容層。`DynamicsAccess:Package01FeeReadsEnabled` 現在不可改成 `true`。

### Files Found

- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md:3-13` — 2026-07-29 方向修正；明確保留 Central、Local、Embedded 與暫時 Data8 的角色。
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md:1319-1359` — 權威 Phase 映射：Phase 4=Prove、Phase 5=consumer migration、Phase 6=removal/enforcement。
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md:435-596` — Phase 4～6 的實作與驗收清單；目前不能因局部 slice 完成而跳過。
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md:112-180` — Product/Gateway 邊界、Central/Local ownership、Embedded 延後與 OData annotation 規則。
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md:689-745` — Development Local Gateway、AD FS、artifact sanitization、browser 與 shutdown 驗收契約。
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-central-boundary-verification.md:392-399` — 目前文件承認仍開放的 CE 8.2/9.1、OData、跨程序 capacity、fault/soak/performance、Phase 5 與 Phase 6 Gate。
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-multi-profile-runtime-verification.md:285-296` — Multi-profile runtime 是已完成的本地 deterministic slice，但不是 Phase 4 完成證據。
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-gateway-security-verification.md:145-151` — LocalDB 證據只涵蓋同一 Windows user／同一程序環境，不涵蓋 Central、多主機、partition 或 HA。
- `SpeechMessage.Dynamics.Abstractions/Operations/OperationExecutionResult.cs:16-29` — `Data` 仍是 `object?`，沒有 typed product projection contract。
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs:340-390` — bounded 讀取後仍把 upstream `JsonElement` 放進 success envelope，且要求全部 OData annotations；沒有安全投影或 `nextLink` server-side loop。
- `SpeechMessage.Dynamics.Gateway/Program.cs:165-204` — `/health`、`/ready` 設定 `no-store`。
- `SpeechMessage.Dynamics.Gateway/Program.cs:210-270` — operation response 沒有明確 `Cache-Control: no-store, private`。
- `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs:210-320` — 目前是 per-workload count、總量 semaphore 與 in-flight semaphore；不是 bounded fair／deficit scheduler。
- `SpeechMessage.Dynamics.Tests/SqlRuntimeHostSlotCoordinatorTests.cs:210-383` — 32 個 acquisition 是同一 test process 內的 concurrent tasks，不是多個 Host process。
- `SpeechMessage.Dynamics.SmokeTests/LiveDynamicsWebApiSmokeTests.cs:34-135` — 直接建立 WebApi client/executor，使用 placeholder organization identity，且 `RequireDurableHostCoordinator=false`；不是實際 Gateway、真實 durable capacity 或 CE 8.2/9.1 矩陣證據。
- `SpeechMessage.Dynamics.Tests/Phase4IsolationSoakTests.cs:31-238` — 主要使用 fake/runtime-local handler、GC、Handle 與 Thread baseline。
- `SpeechMessage.Dynamics.Tests/DynamicsHttpTransportSocketSoakTests.cs:78-214` — 有 loopback TCP socket lifecycle 證據，但不是 CRM、Gateway multi-process 或長時間 production-like soak。
- `SpeechMessageProducts.ChurchReport/Services/Caching/SessionScopedResourceDisposalCoordinator.cs:24-176,405-500,648-864` — Donation Session scope、lease、drain、failed-cleanup retry 與 host disposal ownership 已有明確實作；opaque scope 不保存 Session ID、token 或 credential。
- `ToolUtility/Factory/ToolUtilityFactory.cs:25-116` — process-wide static singleton 只有 internal test reset；沒有 Production host shutdown owner。
- `SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs:19-85` — access/refresh token 以明文 JSON `File.WriteAllText` 保存；沒有 OS protection、owner-only ACL、atomic replace、expiry cleanup 或 deterministic delete contract。
- `SpeechMessage.Dynamics.WebApi/Runtime/LocalDevAdfsTokenStore.cs.bak:1-113` — 已被 Git 追蹤的舊 token-store source backup；內容自己聲明必須 gitignore，卻仍保留可直接復活的危險實作。
- `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs:32-140,143-421,545-581` — DEBUG only，但只有一般 `[Authorize]`；會回傳／記錄完整 authority、resource、client identifier、callback、process identity、token-store path、authorize URL 與部分 upstream body，並把 probe result 寫入 Logs。
- `SpeechMessageProducts.ChurchReport/appsettings.json:547-590` 與 `appsettings.Development.json:4-18` — feature flag 仍為 false；Development 選擇 Gateway，但 base configuration 仍保留 inactive Embedded 的 CRM/OAuth/token-store routing metadata。這與新 Product boundary SPEC 不一致，必須在 Phase 5 前清除或移到 deployment-owned Gateway/secret boundary。

### Dependencies

目前的主要呼叫鏈如下：

```text
ChurchReport
  -> ProductClient / DonationDynamicsAccessProcessHost
  -> HTTPS Gateway contract
  -> Negotiate authentication
  -> server-owned workload/alias/operation authorization
  -> bounded request-body reader + canonical dispatch envelope
  -> ControlledOperationExecutor
  -> OrganizationAdmissionManager
  -> durable RuntimeHostSlotCoordinator
  -> active immutable profile generation
  -> CE-specific Gateway adapter/worker
  -> Dynamics CE 8.2 or CE 9.1
```

關鍵 dependency 不變量：

1. Product 不得持有 CRM endpoint、credential、token、SDK transport 選擇或 raw OData continuation URL。
2. Central 與 Local 只由 deployment endpoint 區分，使用同一個 ProductClient、operation registry、authorization、projection、audit 與 error contract。
3. `crm82` 與 `crm91` 可以共用產品契約與 canonical organization capacity，但不能共用 mutable client、handler、token、credential、WCF channel、metadata cache 或 generation。
4. Admission queue 必須先保留 bounded canonical envelope；dequeue 後才解析目前 active runtime，且 runtime/config revision 不相容時 fail closed。
5. Phase 5 依賴 Phase 4 的 real-server、projection、paging、capacity、fault、soak、performance、shutdown 與 rollback 證據。Phase 6 再依賴 Phase 5 所有 consumer 完成 migration 與 bypass scan。
6. Donation Session coordinator 只負責新 Donation-owned resource；它不能替 legacy `ToolUtilityFactory` singleton 提供 process shutdown owner。
7. AD FS Public Client 驗證若繼續存在，必須是 operator-only、sanitized、bounded 且有唯一 token/cache owner；目前 ChurchReport DEBUG diagnostic 與 plaintext file store 尚未滿足此依賴。

### Patterns

#### 權威 Phase 映射

- Phase 4 — Prove：完成真實 CE 8.2/9.1 authentication、WhoAmI、representative operation matrix、typed projection、server-side paging、aggregate capacity、coordinator fault、isolation、soak、performance、shutdown baseline 與 rollback。
- Phase 5 — Migrate：只先遷移一個 bounded ChurchReport workflow，保留 `Package01FeeReadsEnabled=false` 直到所有解鎖證據成立；用 parity/shadow、browser E2E 與 rollback 證明後，才逐產品遷移。
- Phase 6 — Remove/enforce：移除 Data8、CRM SDK/WCF、`PowerPlatform.Dataverse.Client`、ToolUtility CRM helpers、ProjectReference／HintPath／solution entry，輪替 legacy credentials，並加入 CI source-root bypass scan。

#### 已有且應沿用的實作模式

- Product boundary：Gateway executor 只複製 Gateway alias/endpoint/prefix/response limit，不把 Embedded graph 傳入 ProductClient。
- Security order：transport guard → authentication → authorization → media type/body byte limit → canonical request → executor；未授權要求不得讀 body 或建立 admission/token/outbound work。
- Runtime replacement：validate → create isolated generation → acquire host slot → publish atomically → drain old generation → deterministic disposal；cleanup failure 保留唯一 retry owner。
- Session lifecycle：opaque 256-bit scope、per-key/striped locking、request lease、identity reset 前 drain、最後 lease Dispose、failed-cleanup host retry。
- Verification evidence：每個 slice 要有 RED→GREEN contract test、fresh command output、strict encoding/format checks、sensitive artifact scan 與不誇大 Gate 的文件。

#### 已證明完成的 bounded slices

- Local/Central ProductClient contract boundary與 bounded ProductClient response reading。
- Multi-profile generation publication、replace/drain 與 rollback ownership。
- Gateway authentication/authorization 在 body parsing 前完成；canonical queue envelope 有 byte/depth/member/retention boundary。
- Gateway success envelope 已移除 runtime-owned `ApprovedWebApiRoot` 欄位。
- Donation Session resource 的 scope/lease/drain/retry/host-disposal ownership。
- Development Local Gateway configuration、localhost listener、基本 browser login smoke。
- Named workload binding sets、strict active selector，以及 valid SID authoritative/fail-closed 行為。
- LocalDB coordinator 在單一 test process 內的 atomic、epoch、fencing、namespace isolation 與 quarantine contract。

#### 下一個最小實作順序

0. **先關閉 credential/artifact blocker。**
   - 檔案：`SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs`、`SpeechMessage.Dynamics.WebApi/Runtime/LocalDevAdfsTokenStore.cs.bak`、`SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs`、ChurchReport Dynamics configuration 與相對應 tests。
   - 要求：移除 tracked backup；將 Public Client surface 限制為明確 operator policy；禁止回傳／寫入完整 endpoint、client/callback、process identity、path 或 upstream body；token store 必須移到核准的 OS-protected secret owner，具 owner-only ACL、bounded/atomic write、expiry/revoke/delete 與 host cleanup，或改成不落地的短生命週期流程。
   - 測試：非 operator 403 且零 network/file/token work；artifact schema 只允許 status category/count；store fault/partial write/cancel/expiry/revoke/host stop 後沒有 token file、stream、timer、handler 或 retained token reference；secret/literal scan 不得輸出值。
   - rollback：保持 feature flag false；若 operator flow 不可安全完成，整個 diagnostic fail closed，不回退 password grant、raw script 或 product-owned token file。

1. **typed OData projection + bounded server-side paging + operation cache policy。**
   - 檔案：`OperationExecutionResult.cs`、新增 Abstractions projection DTO、`DynamicsWebApiClient.cs`、operation/template definitions、`DynamicsWebApiClientTests.cs`、Gateway response tests。
   - RED tests：移除/投影 absolute `@odata.context`；只允許同 validated root 的 relative/absolute `@odata.nextLink`；拒絕 cross-origin、wrong base path/version、malformed、loop、page/byte limit overflow；所有 response/stream/document 都確定釋放；operation response 必須是 `Cache-Control: no-store, private`。
   - rollback：回復上一個 Gateway generation 並 replace-and-drain；不得回傳 raw OData JSON、不得改走 Embedded/Data8/其他 alias。

2. **真實 CE 8.2 與 CE 9.1 的 Gateway read-only matrix。**
   - 更新 `LiveDynamicsWebApiSmokeTests.cs` 或建立獨立 Gateway live suite；不能直接 new WebApi client 作為最終證據。
   - 每個版本都通過：authenticated Gateway WhoAmI、typed projection、至少一個代表性 read、paging（若資料量可觸發）、token renewal、restart/profile reload、sanitized failure 與 zero fallback。
   - 使用真實 organization identity 與 durable coordinator；不得使用 placeholder identity 或 `RequireDurableHostCoordinator=false`。

3. **真正 multi-process capacity/fault proof。**
   - 至少啟動兩個獨立 Gateway host process，共用同一 durable coordinator namespace；證明總 permit 不超過 organization budget、fencing 單調、stale host 不可 renew/release、outage/timeout/partition fail closed、drain 後 durable rows/connection/tasks 回 baseline。
   - 接著實作 bounded fair/deficit scheduling與 starvation bound；目前 semaphore FIFO 行為不等同 workload fairness。

4. **durable audit/idempotency state machines。**
   - 實作 `AuditIntent` reserve-before-dispatch、hard byte/entry quota、recovery/terminal state，以及 durable `IdempotencyLedger` 的 operation revision binding、`OutcomeUnknown` 與 retry/takeover contract。
   - 沒有這兩項，不可把 Phase 4 宣告為 production-ready，也不可在多 Host 下宣告寫入操作安全。

5. **representative soak/performance/shutdown baseline。**
   - 真實 Gateway + CRM、token renewal、profile reload、429/503/timeout/malformed metadata/coordinator outage、process restart；量測 memory/socket/handle/thread/timer/task/connection-pool/durable-row baseline。
   - 接著才能進 Phase 5 的單一 ChurchReport workflow parity/browser/rollback，最後進 Phase 6 removal/enforcement。

#### Local Gateway + ChurchReport + browser 驗收矩陣

1. Provisioned LocalDB schema 只做 read/verify；缺 schema 時 Gateway NotReady，不能 auto-create 或降級 in-memory。
2. 啟動 Local Gateway：`/health=200`、durable `/ready=200`；anonymous operation=401；unmapped/incorrect SID、wrong alias、unauthorized operation=403，且 executor/token/outbound count=0。
3. authorized read-only operation 經 Gateway 執行；CE 8.2 與 CE 9.1 分開記錄 sanitized status，不顯示 CRM URL、identifier 或 payload。
4. 測試 typed projection與 paging；browser/產品回應不得含 `@odata.context`、absolute `@odata.nextLink`、host、base path 或 credential-derived metadata。
5. 啟動 ChurchReport，仍保持 feature flag false；login page `readyState=complete`、JavaScript error count=0，且沒有 Package 1 preflight/token/handler/pool。
6. 只有 operator policy 可進 sanitized verification surface；一般 authenticated user 仍為 403。任何 artifact 只保存 status category、count、duration、readiness 與 JS error count。
7. 停止 ChurchReport 與 Gateway；listener 釋放、active requests/leases=0、queue=0、DB operations=0、token/HTTP handler disposed、background tasks/timers/cancellation registrations=0，memory/handle/socket 回到宣告容差內。

#### WinRM／DC／D365 VM 安全前置與 rollback

- 只使用已核准的管理身分與 encrypted transport；不要在 command line、transcript、prompt、stdout/stderr 或 task artifact 傳遞 credential、token、private address、完整 endpoint、client identifier、callback 或 relying-party identifier。
- 第一步只做 read-only health/config probe：回傳 boolean/count/version/status category；AD FS 驗證只證明「唯一 Public Client、唯一 callback、marker 符合」，不輸出實際值。
- 每個 PSSession/WSMan connection 都由單一 scope 擁有並在 `finally` 關閉；禁止持久化 `PSCredential`、session object 或 remote output object graph。
- 所有 mutation 必須有 pre-state snapshot、精確 target、idempotent command、post-condition 與反向 rollback；失敗時不得改用較寬 TrustedHosts、關閉驗證、停用 firewall、啟用 Basic/unencrypted WinRM 或輸出秘密除錯。
- 本輪未執行任何 WinRM/DC/D365 mutation，也沒有足夠證據宣告 VM configuration Gate 完成。

### Risks

#### Critical

1. **AD FS local token/artifact boundary 不符合 zero-leak 與 SPEC。** Plaintext access/refresh token file、tracked `.bak` source、一般 `[Authorize]` DEBUG diagnostics、完整 identifier/endpoint/path/body artifact 形成 credential disclosure與 retention risk。這是任何 browser/operator AD FS 驗證前必須先修的 release blocker。
2. **Product configuration 仍含 inactive Embedded 的 direct CRM/OAuth/token-store metadata。** Development override 選擇 Gateway 並不會從 merged configuration 刪除 base subtree。雖然 feature flag false 且 Gateway executor 只複製 Gateway fields，checked-in product config 仍違反新 Product boundary；Phase 5 前必須把這些資料移到 deployment-owned Gateway/secret boundary。
3. **Raw OData annotation exposure。** `JsonElement` 仍可攜帶 absolute `@odata.context`／`@odata.nextLink` 穿越 Gateway；沒有 typed projection與 validated server-side paging，不可開始 consumer migration。
4. **真實 CE 8.2/9.1 Gate 未完成。** 現有 live harness 是 direct WebApi、durable coordinator opt-out、placeholder organization identity；不能證明 Gateway authentication、authorization、projection、paging、rollback 或版本相容性。
5. **跨 Host capacity與 durable write safety 未完成。** 單程序 concurrent tasks 不能證明 multi-process/partition/fencing behavior；fair scheduling、durable audit、idempotency與 `OutcomeUnknown` 也尚不存在。
6. **Repository configuration 需另做 credential rotation/removal audit。** 在本次必讀 configuration 中觀察到非 Dynamics 區域的 credential-like literals。本報告不保存或重述其值，也沒有判定是否仍有效；在專責 secret scan、撤銷/輪替與移出 source control 完成前，應視為可能的 credential exposure。

#### Warning

1. operation endpoints 沒有 `Cache-Control: no-store, private`，與 PRD 明確契約不一致；health/readiness 的 `no-store` 不能代替 operation response policy。
2. Admission 只有 semaphore/count cap，沒有 per-workload fair/deficit scheduling或 starvation bound；高流量 workload 仍可能長期壓制其他 workload。
3. 現有 soak 主要是 fake/runtime-local；loopback socket test 有價值，但不能替代真實 CRM、token refresh、multi-process、coordinator outage 與長時間 baseline。
4. `ToolUtilityFactory` 沒有 Production host shutdown cleanup owner；部分 consumer 直接持有/Dispose process-wide singleton 的模式也有 use-after-dispose 與 ownership 模糊風險。這是 Phase 6 前既有 lifecycle blocker。
5. 歷史文件列出的 test/build/dual-review count 可能有效，但本輪未重跑，不能作為 2026-07-30 fresh completion evidence。

#### Info

1. Donation Session coordinator 的 opaque scope、striped locking、lease publication、drain與 failed-cleanup retry 是目前最完整的 lifecycle pattern，後續新 cache/session owner 應照此契約實作。
2. `Package01FeeReadsEnabled=false` 在 base 與 Development 均保持，這是正確狀態；唯一可接受的解鎖證據是上述 Critical Gate 全部完成，加上單一 ChurchReport workflow parity、browser E2E、rollback與 fresh resource baseline。
3. Embedded、Data8 與 `PowerPlatform.Dataverse.Client` 必須繼續保留，直到 Phase 6 removal/enforcement Gate 真正通過；不能把暫時保留誤寫成推薦的長期架構。

DONE_WITH_CONCERNS

