# ChurchReport 完全 Gateway 化與雲端 Central Gateway 實施計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` for Codex inline execution. 每個 child task 必須另有經審閱的 `prd.md`／`design.md`／`implement.md`，並以 checkbox 追蹤；本 parent 不直接修改產品程式碼。

**Goal:** 先在 Lenovo Legion 以可獨立驗證與回滾的 vertical slices 完成 P6 與 P7，將 ChurchReport 全部 D365 業務能力移至 ProductClient／Gateway 並移除產品端 ToolUtility；再由獨立 P8 將單一 ChurchReport 部署到雲端 Central Gateway。

### 2026-08-13 現行 child 執行補充

`ORG-CALL-00014` 已完成並封存；不得將它的 operation、template 或測試當作 `ORG-CALL-00065` 的完成證據。
`08-13-08-13-p71-appnamed-smallgroups-list-catalog-typed-read` 已完成並封存。其後續獨立 child 必須按 TDD 依序完成：

1. 從權威 matrix 選取沒有 write adjacency、shared mutable cache 或未解 authorization 的獨立 read-only candidate。
2. 先 RED，再 GREEN 地建立該 candidate 的 registry、closed response branch 與 immutable scalar wire contract。
3. 先 RED，再 GREEN 地建立完全固定、有界、零或明確 allowlisted caller parameter 的 Data8 query/executor 投影。
4. 先 RED，再 GREEN 地建立 request-local immutable ProductClient DTO、DI 與 A/B isolation/cancellation/source-mutation tests。
5. 只在 child 邊界更新 matrix local state，保留 consumer、CE、host、traffic、temporary-legacy、P7.5、P8 為 pending。
6. 完成 targeted/full quality gates、bounded external review、scope-only commit/archive 後，才依 authoritative matrix 選下一個 local child。

**Architecture:** 保留 P4 Embedded 與已封存的 P5 Dedicated 基礎；P6 完成 Official Worker Router／Pool／Lease 擴充點並把 live compatibility 如實記為 `evidence-pending`。P7.0～P7.5 以 Data8 完成可設定的 `Embedded + Data8` 與 `DedicatedGateway + Data8` capability migration、consumer cutover 與 removal。P8.0～P8.4 是後續獨立的 `CentralGateway + Data8` 雲端部署鏈，不包含第二、第三產品 onboarding。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、`IHttpClientFactory`、SpeechMessage.Dynamics Abstractions／ProductClient／ControlPlane／Gateway／Embedded／Connectors.Data8、CE 8.2／9.1 Organization Service、xUnit。

---

## 1. Parent／Child 交付樹

本 parent 只維護來源需求、子任務順序、跨 child 驗收與最終整合審查。P5 已封存；P6.1 已通過，P6.2 的 Official Worker live compatibility 保留為未來獨立、非阻塞的 `evidence-pending` 支線。P6 正式結案前，P7.0 維持 `planning`。固定交付樹為：

1. **P6** `official-worker-router-ce-integration` — 完成 P6.1 Router／Pool／Lease 離線擴充點的品質檢查、spec update、commit 與封存，並保留 Official Worker live compatibility=`evidence-pending`。
2. **P7.0** `gateway-capability-inventory` — 建立 70 call-site rows 到業務 capability 的權威矩陣與 deterministic coverage validator。
3. **P7.1** `churchreport-read-capability-migrations` — 先交付 Package01 vertical slice，再依矩陣完成全部 read capabilities；catalog module 是本階段的必要底層工作，不另編號。
4. **P7.2** `churchreport-write-action-function-migrations` — 讀取 parent-owned `p7.2-write-environment-readiness.md` 與 P7.0 per-family matrix，依 idempotency／transaction／authorization 邊界完成 Create／Update／Associate／Action／Function。
5. **P7.3** `churchreport-special-resource-migrations` — 完成 attachment、large paging、background／scheduler、metadata cache 的有界 contract 與生命週期。
6. **P7.4** `churchreport-productclient-cutover` — 將 Controller／Service／WebServiceConnector 逐 capability 切至 ProductClient，並在第一個 feature gate 前完成 aggregate-capacity authority 或 non-overlap drain runbook。
7. **P7.5** `churchreport-toolutility-removal` — 移除 ChurchReport project reference、DI／Factory、legacy settings／credential 與 SDK type，執行 zero-reference gate。
8. **P8.0～P8.4** — 另立 ChurchReport 雲端 Central Gateway parent／children，依 readiness、identity/TLS、deployment、cutover、live validation/closure 順序執行；不由 P6／P7 單一目標自動啟動。

## 2. 依賴順序

```text
P5（已封存）
  → P6.1（已通過）→ 記錄 Official Worker live evidence pending → P6 結案
  → P7.0 inventory + validator
  → P7.1 reads
  → P7.2 writes／actions／functions
  → P7.3 special resources
  → P7.4 product cutover
  → P7.5 ToolUtility removal → P6／P7 單一目標完成
  → P8.0～P8.4（獨立目標：ChurchReport 雲端 Central Gateway）
```

Package01 是 P7.1 第一個 slice，因既有 Registry、ProductClient 與 feature gate 已存在；它用來驗證完整交付模板，不代表其他 capability 可省略自己的設計與測試。

`docs/superpowers/plans/2026-08-06-p6-p7-integrated-execution.md` 定義單一 `/goal` 的連續執行規則。該 goal 可一次核准建立／啟動後續 P7 children、執行本機 feature-gated cutover、完成 task-local commit/archive；每個技術 gate 仍必須按順序通過，禁止為了「一次做完」跳過驗證。

該 Goal 不是完全無人值守：長跑前必須建立 scoped Git/text baseline，並把 P6.1 的已通過離線證據與 Official Worker live compatibility=`evidence-pending` 正確分開記錄；不重跑現有 P6.2 startup。P7.0 matrix 完成後，P7.2 activation gate 才逐 required operation family 確認 fixture owner、allowed mutations、cleanup/reconciliation 與 ambiguous-timeout policy。P6 與 P7.0 位於不同 parent；執行者直接使用兩個 task path，不靠 children traversal。相同 gate 最多三次自我修復，同一 root cause 連續兩次即停止並產生 operator handoff。

## 3. Child 共同 TDD 節奏

每個 child 的 `implement.md` 必須將每一個 operation 拆成以下可執行步驟：

- [ ] 先新增 failing contract／authorization／support-matrix tests，確認未實作 capability fail closed。
- [ ] 執行 focused test，預期因缺少 executor／projection／consumer mapping 而失敗。
- [ ] 實作最小 request／response contract 與 Registry definition。
- [ ] 實作 executor 與單一 request-owned permit／lease cleanup。
- [ ] 實作 ProductClient method 與 sanitized error mapping。
- [ ] 執行 focused tests，確認成功、取消、逾時、未授權、錯誤 Profile 與不支援 Connector 均符合契約。
- [ ] 加入 legacy parity／reconciliation harness；read 可 shadow，write 僅單一路徑。
- [ ] 執行 lifecycle／soak，確認 queue、permit、lease、connection、channel、task、timer、registration、handle 與 socket 回到基線。
- [ ] 啟用最小 feature tier，取得觀測結果；失敗只回滾該 capability。
- [ ] 更新 capability matrix、runbook 與 operation usage／deprecation metadata。

## 4. Parent 驗證命令

每個 child 完成時至少執行其 focused tests；每個 P7 wave 與最終 cutover 執行：

```powershell
dotnet test SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --nologo
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --nologo
dotnet build SpeechMessageProducts.sln --configuration Release --nologo
```

Capability inventory child 必須提供一個 deterministic validator；後續 parent gate 使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Test-DynamicsCapabilityCoverage.ps1
```

成功條件：所有 production D365 call sites 皆分類為 `gateway-enabled`、`gateway-evidence-pending` 或有明確 owner 的暫時 legacy slice；P7.5 前不得存在暫時 legacy 狀態，且 ChurchReport zero-reference scan 必須通過。

真機 gate 在受控環境對需要支援的 CE 8.2／9.1 組合執行，記錄結果一致性、p50／p95／p99、allocation、working set、handle、socket、channel、pool 與 queue baseline。測試不得將 credential、endpoint、OrganizationId 或原始例外寫入 artifact。

## 5. Review 與核准政策

- 使用者已明確要求本 parent 規劃不進行 Gemini／Claude analysis 或 review；後續只執行主代理 code inspection、Trellis check、編譯、測試、靜態掃描與真機證據核對。
- 一般模式下，每個 child 在 `task.py start` 前由使用者審閱；若使用者採用已核准的 P6／P7 單一 `/goal` 提示詞，該提示詞即構成後續 P7 child 建立與 activation 的預先授權，代理可在前置 gate 全綠後自動銜接，不需逐階段再次詢問。
- 任一 isolation、credential、session、memory、connection、process 或其他 resource leakage 為 release blocker。
- Registry declaration、離線 test pass 或 `/ready` 均不得單獨宣稱 operation 支援 CE 8.2／9.1。

## 6. Rollback 點

| 交付 | 回滾單位 | 不得破壞的邊界 |
|---|---|---|
| Catalog module | 還原該 module registration | 既有 Package01 ID 與授權語意不變 |
| Read capability | 關閉該 capability feature gate | Legacy authoritative response 保持可用，shadow task 完整取消／清理 |
| Write capability | 切回單一 legacy writer | 禁止未協議 dual-write；保留 idempotency／reconciliation evidence |
| Product cutover | 逐 capability 回退 | 不做整站自動 fallback，不更換 Profile／Connector |
| ToolUtility removal | 只在 observation window 後執行 | 若仍需回滾，先恢復已驗證的 project／DI／settings commit，不在 runtime 動態載入 legacy |
| P8 cloud deployment | 回復上一版部署包、endpoint 與 service identity allowlist | 不在 P6／P7 操作雲端；P8 必須先完成 rollback drill |

在任何 Product cutover feature gate 前，child 必須先通過 aggregate-capacity gate：若 legacy 與 Gateway
不同 process 同時連到同一 Organization，兩者必須共用 durable admission/host-slot authority；若沒有該
authority，runbook 必須證明先 drain 舊路徑再啟用新路徑。不可把每個 process 的 In-Memory coordinator
誤當成跨 process 總容量保證。

## 7. Parent 完成條件

- [ ] `design.md` 第 13 節所有條件都有完成 child 與證據連結。
- [ ] P7 capability matrix 無未分類與暫時 legacy ChurchReport production call site。
- [ ] ChurchReport zero-reference、完整 Dynamics／ChurchReport tests、Release build 與 byte-level encoding gate 通過。
- [ ] 需要支援的 CE 8.2／9.1 組合有真機結果、效能與資源基線；不支援組合在 startup／dispatch 前 fail closed。
- [ ] ToolUtility 只在仍有其他 consumer 或 rollback window 時保留；ownership 與最終退役 task 明確。
- [ ] P8.0～P8.4 文件明確承接 P7.5 artifact，並在獨立授權下完成 ChurchReport 雲端 Central Gateway deployment、cutover、monitoring、rollback 與 live evidence。
- [ ] 第二、第三產品 onboarding 已明確移出本 parent 完成條件，日後另立獨立 task。

## 8. 執行起點（已校正）

目前執行起點是 authoritative 70-row gap matrix 所列的下一個 independently verifiable P7 capability
child；不是重新啟動已封存的 P3～P6、P7.0 或 P7.3，也不是重播歷史 P7.2 Slice C。P6 Official
Worker live compatibility 仍為 `evidence-pending`，但不阻擋 Data8-first 的 local-only P7 read migration。
每個 child 必須先從矩陣與現行 call chain 證明其 capability family、DTO、authorization、resource
ownership、rollback owner 與 evidence scope，再依 Trellis 完成 Plan、Design、Implement、Check、
scope-only commit 與 archive。禁止在 capability 邊界尚未證實前一次性修改全部 ChurchReport CRM 呼叫，
也不得由此路線提前啟動 P7.5 或 P8。

## 2026-08-12 後續執行順序（取代過期 P6/P7.0 next action）

1. 完成並封存 `08-12-p7-remaining-work-rebaseline`：以 70-row P7.0 baseline 建立 deterministic authoritative gap matrix、validator、測試與 parent update；不執行 CE mutation。
2. 依 matrix 剩餘能力建立可獨立驗收的 P7.1/P7.2 child；每個寫入 family 必須使用新的 child/nonce/ledger/fresh fixture/preflight/read-back/reconcile/cleanup，historical CE cycle 一律不重試。
3. 完成 P7.3 special-resource resource-owner/lifecycle child，然後才依 evidence 建立 P7.4 per-capability disabled-by-default cutover；禁止 request-time fallback。
4. 僅在 matrix 沒有 temporary legacy/unclassified production row 且 ChurchReport zero-reference、parity、soak、drain、rollback gate 全綠時，建立 P7.5 ToolUtility removal child。
5. P7.5 已提交、封存與 immutable handoff 後，才建立 P8 parent 與 P8.0–P8.4；外部 host、DNS、TLS、service identity、secret provider、CE/ADFS reachability 或 permission 缺失時僅交付 repository-side validator/runbook/handoff，絕不猜測或假裝部署。

## 2026-08-13 P7.4 local admission boundary checkpoint

`08-13-p74-legacy-gateway-admission` 已完成本機 control-plane、runbook、validator、full solution tests、
Release build、UTF-8/CRLF/scope gate 與降級雙模型 review 記錄。它沒有啟用任何 gate，也沒有 CE/traffic
operation；同步 legacy I/O、legacy coverage、durable coordinator 三項 enablement blocker 仍存在。

後續實作直接回到 `08-12-churchreport-productclient-cutover` 的 authoritative matrix。建立下一個 child 前，
先為單一 consumer 確認 typed DTO、server-selected authorization、request-local projection、disabled gate、
rollback owner 與 no-SDK boundary；不允許以 Entity/EntityCollection bridge、request-time fallback 或猜測
資料語意冒充 migration。P7.5/P8 gate 不變。

## 2026-08-13 P7.2 recurring payment-return local boundary checkpoint

1. `08-13-p72-dedication-payment-return-write-boundary` 先完成 `prd.md`、`design.md`、`implement.md`
   與本機 TDD。它只處理 recurring dedication payment-return financial write boundary，禁止修改
   `RecurringDonationPaymentProcessor` 的 real CRM chain 或啟動 CE dispatch。
2. 先固定 six-family map：dedup fee-period read、card update、fee create、owner assignment、booking
   completion、notification。每個未來 CE family 都需要獨立 authorization、fixture/ledger、read-back/
   reconcile、rollback/cleanup；不得以 generic entity update 或 payment callback retry 取代。
3. local-only types 必須維持 `CeExecutorEnabled=false`、`ConsumerEnabled=false`；測試覆蓋 duplicate
   callback、timeout/ambiguous、partial outcome、A/B isolation、allowlist 與 no partial plan。
4. 完成 child quality gate 後，CE evidence 仍是 pending/no-go，除非另一個新 governed child 一次性通過
   preflight/provision/dispatch/read-back/cleanup。historical Slice C 永不重試。

## 2026-08-13 P7.5 前置 evidence 執行順序

1. `08-13-p75-prerequisite-evidence-zero-reference-gate` 已完成；其 report/validator 通過，而現況
   `--enforce-p75` 的 sanitized nonzero `no-go` 是預期 release gate，不允許省略或重跑歷史 P7.2 cycle。
2. 封存該 child 後，依 stable capability-family aggregate 選擇下一個可獨立驗收 P7.4/P7.2/P7.3 child；每個 child
   仍須自有 DTO、authorization、executor/consumer、CE、lifecycle、rollback evidence。
3. 只有未來 report=`prerequisite-ready` 且 full parity/soak/drain/rollback evidence 完成時才建立 P7.5 removal child；
   commit/archive 產生 immutable handoff 後才能建立 P8.0–P8.4。

## 2026-08-13 P7/P8 現況校正與後續排程

1. P7.4 已封存多個 disabled-by-default local consumer boundary，其中 authentication contact lookup
   與 credential-policy child 已完成本機安全邊界；它們不是登入切換、CE、host parity、traffic cutover、
   P7.5 或 P8 evidence。parent 的過期 next action 必須改為選取下一個 matrix-backed capability family，
   不得重複封存已封存 child。
2. 下一個優先研究項目是 list catalog family 的 `ORG-CALL-00014`
   (`list.catalog.retrieve.app.named`) 與 `ORG-CALL-00065`
   (`list.catalog.retrieve.appnamed.smallgroups`)。兩者均為 fixed FetchXML read，但矩陣明定其 template
   與 operation ID 不同，只可共享受控 family 的 design/research，不能合併為同一 operation 或猜測
   caller 的資料語意。先完成 caller-shape inventory；若任一 consumer 有 write、shared state、Entity bridge
   或無法證明 DTO-only projection 的耦合，該 consumer 維持 legacy，另選取獨立 candidate。
3. P7.2 舊 Slice C 的 `write-not-committed` no-go 與 exact cleanup 是 immutable historical evidence：不重試、
   不復用 nonce／ledger／fixture／descriptor。任何新的寫入 family 必須有新 child、全新 governed fixture
   cycle 與完整 preflight／single dispatch／read-back／reconcile／cleanup；否則只允許 local planning/tests。
4. P7.5 prerequisite report 目前仍是 deterministic `no-go`：70 rows temporary-legacy、legacy source/
   project/settings references 與 CE/host/parity/soak/drain/rollback gaps 尚存。因此不得建立 P7.5 removal
   child；P8.0～P8.4 仍等待 P7.5 scope-only commit/archive 的 immutable handoff，以及外部 host、DNS、TLS、
   service identity、secret provider、network、CE reachability 與 deployment authorization。

## 2026-08-14 P7/P8 現況校正與下一 family 排程

1. 已封存的 local read endpoint 或 registry/Data8/ProductClient contract 不得重做。`ORG-CALL-00066`
   fee-editor boundary 已封存，且不可接入 `FeeList.FeeDataList`／`UpdateFeeData`／`SaveBatch`；00014／00065
   的 shared legacy `EntityCollection` consumer 仍不具 DTO-only cutover 證據。
2. 每個下一 candidate 先以 source audit 分類為 read consumer、special-resource，或 write/action/function
   family。只有具 bounded DTO、server authorization、無 shared mutable bridge、無 write adjacency、明確
   rollback owner、gate=false zero-work 與 testable lifecycle 的 read consumer，才能建立新的 P7.4 child。
3. `ORG-CALL-00063` weekly meeting statistics 必須先處理 bounded `paging-result` 的 page/token/buffer/
   cancellation/retention owner；00064 payment-adjacent、00055/00056 credential/session、list action 及
   four-field contact update 皆必須使用自己的 family design，不得做 partial consumer wiring。
4. 歷史 Slice C 維持 non-replay。`08-14-p72-governed-recurring-payment-return-write-family` 的 local
   control plane 維持 `CeDispatchAllowed=false`／`ProductConsumerAllowed=false`；只有未來新 family 建立
   fresh task-owned fixture、preflight=go、single dispatch、exact read-back/reconcile、cleanup 並符合
   no-replay policy，才可獨立評估 CE evidence。
5. P7.5 prerequisite 仍為 `no-go`，P8 仍不可建立。每個 child 結束後只更新其 own evidence 與 parent
   checkpoint；matrix consumer status、feature gate、traffic 及 P7.5/P8 state 不得因 local evidence 自動升級。

## 2026-08-14 QR weekly-attendance local design checkpoint

`08-14-p72-weekly-attendance-write-family` 已完成本機 attendance reducer 的重驗、production QR mutation
graph 稽核與品質檢查，結論是只限該 family 的 local design no-go：`QrCodeController` 將 browser／route
locator 與 LINE/group/room/view 值寫入 process-wide `InMemoryContext`，再由 legacy utility 進行 CRM I/O；
同一 QR call 又混合 present-record、relationship、weekly-report、recomputation 與 notification effects。

這不是 CE Full-Text Search、測試資料庫寫入權限、P7 全域、P7.5 或 P8 的阻塞。此 child 未執行 CE
preflight、fixture、mutation、feature gate、traffic 或 cleanup，歷史 Slice C 也沒有被重試。未來若要處理
此 family，必須另立 request-local QR authorization-boundary child，先以 TDD 建立 server-derived scope、
fixed command、idempotency／ledger、read-back／reconcile 與 cleanup owner；在此之前繼續選取不依賴這條
legacy QR path 的 independent matrix capability。

## 2026-08-14 MemberInfo smallgroup tree source-only checkpoint

1. [x] 以 child `08-14-p74-memberinfo-smallgroup-tree-authorization-audit` 對 00031／00032 完成 matrix、
   route、Session/InMemoryContext、Church/Shepherd branch、legacy credential loader 與 SDK query trace。
2. [x] 判定為 source-only local design no-go：不得把 Session access、shared ListManager 或保存 credential
   loader 當作 Gateway scope，也不得只遷移 Church branch 或向 typed boundary 傳遞 raw legacy SDK state。
3. [x] 限時 CCG architect run 在 45 秒內未產生 usable output，記錄「雙模型未完成」，以本機 evidence 完成
   fail-closed decision；不重試等待。
4. [x] child 已完成 scope-only commit/archive；從 matrix 選擇不依賴 MemberInfo tree authorization chain 的下一個
   P7 capability。只有先完成另一個 server-derived MemberInfo scope child，才可重新評估 00031／00032。

## 2026-08-14 MemberInfo relation-goal source-only checkpoint

1. [x] 以 child `08-14-08-14-p74-memberinfo-relation-goal-read-boundary` 對 00033 完成 matrix、三個 caller、
   Session/InMemoryContext、Shepherd credential loader、connection paging、fault handling 與 formatter trace。
2. [x] 判定為 source-only local design no-go：不得將 legacy `allowedIds`／Session／shared ListManager 當作
   Gateway scope，也不得以無上限 page 或吞掉 fault 的 connection expansion 建立 typed response。
3. [x] architect/reviewer CCG runs 都在 45 秒內未有 usable output，均標記「雙模型未完成」，採本機驗證且不重試等待。
4. [x] child 已完成 scope-only commit/archive；從 matrix 選擇下一個不相依 P7 capability。唯有先完成 immutable
   server-derived MemberInfo authorization boundary，才能重新評估 00033。

## 2026-08-14 dedication capability identity checkpoint

1. [x] 以 child `08-14-p74-dedication-capability-identity-audit` 對 00059／00060 完成 matrix、legacy
   helper、typed registry/Data8/ProductClient、payment-form consumer、controller、Session/InMemoryContext 與
   ToolUtility Entity trace。
2. [x] 00059 判定為 00041 historical duplicate：不建造第二個 operation family；typed DTO 覆蓋現有
   form scalar，但 consumer、CE、host、traffic、P7.5/P8 evidence 一律維持 pending。
3. [x] 00060 判定為 source-only local design no-go：在 immutable scope 出現前，禁止將 browser／Line locator、
   Session、mutable manager/form 或 Entity bridge 接到 Gateway。先建立 principal-to-scope child 才能恢復。
4. [x] architect run 在 45 秒內僅 Gemini 有 usable output；Claude 未完成，記錄「雙模型未完成」，以本機
   evidence 覆核。完成 child scope check、commit/archive 後，繼續從 authoritative matrix 選取不相依 family。

## 2026-08-14 current rebaseline follow-up

1. [x] `08-14-p7-current-state-rebaseline` 以 current Phase-0 source hash 產生 70-row task-owned matrix，
   並拒絕將 local-only、disabled gate、registry／executor／ProductClient implementation 升格為 consumer、CE、
   host 或 traffic evidence。
2. [x] 校正 P7.4 checkpoint：20 個封存 capability child；00057 為 local-only data plane；00011／00012 為
   direct consumer no-go。所有 checked-in feature gate 繼續 false。
3. [x] P7.4 direct local-only candidate audit 結果為零。00063 不得因既有 Package03 DTO data plane 而接到
   QR／weekly legacy graph；它需要先完成 server-derived immutable authorization boundary、command-family
   idempotency、read-back/reconcile、cleanup 與 rollback owner。
4. [ ] 下一 child 必須只規劃該 authorization-boundary recovery prerequisite，或由 matrix 找到另一個
   不依賴 Session、InMemoryContext、saved credential、stored FetchXML、Entity bridge 或 write adjacency 的 family。
   不可重做 00057、00011／00012、00063 或 historical Slice C。
5. [ ] current matrix 仍有 70 temporary-legacy rows，故不得建立 P7.5 removal child 或 P8。接近 P7.5 前，
   必須以 current matrix 建立新的 P7.5 source/project/settings reconciliation，而非復用 archived report 的 hash。

## 2026-08-14 runtime health child 執行順序

1. [x] 使用已封存 `08-14-p7-current-state-rebaseline` 的 current matrix 重新確認下一個 child；不重建
   duplicate rebaseline task，不重開 P3～P7.2，也不重播 Slice C。
2. [x] 為 `08-14-p7-runtime-health-whoami-productclient-boundary` 建立 Trellis／CCG planning artifacts，並以
   CCG architect runner 嘗試雙模型；45 秒內無 usable output 時記錄「雙模型未完成」，不得重送。
3. [x] review 並啟動此 child；以 TDD 新增 fixed operation、CE 9.1／WhoAmI branch、GUID validation、
   DI、A/B isolation、cancellation 與無效 UTF-8 input tests，再實作 stateless typed ProductClient。
4. [x] 對該 child 執行 focused tests、Release build、full solution tests、encoding／CRLF、`git diff --check`
   與 scope check；沒有接線 consumer、CE、feature gate、traffic、P7.5 或 P8。CCG reviewer 在 45 秒上限內
   未產生 usable output，已記錄「雙模型未完成」並以本機審查完成此 child 的 check。
5. [ ] child scope-only commit/archive 後以同一 current matrix 選下一個 independently-verifiable capability；只在每個 P7.5
   prerequisite 實證後才建立 ToolUtility removal child，並只在 immutable P7.5 handoff 與外部條件具備後建立
   P8.0～P8.4。
