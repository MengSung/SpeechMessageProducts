# P7.2 實作計畫

## 前置基線

- [x] P6.1、P7.0、P7.1 已完成並封存；P6.2 Official Worker 仍是獨立 `evidence-pending` 支線。
- [x] P7.0 matrix、P7.2 activation input 與 parent `p7.2-write-environment-readiness.md` 已讀取。
- [x] 24 個 P7.2 candidates 已按交易與回退責任拆成 8 個切片。
- [x] 讀取本 task 涵蓋 package 的 `.trellis/spec/` index 與 required guideline docs，再開始修改程式。
- [x] 以 `git status --short` 建立 scoped baseline；本輪開始時工作區乾淨，未混入其他 task 變更。

## Phase 1：Activation material

- [x] 建立 `prd.md`、`design.md`、本計畫及 `p7.2-fixture-activation-matrix.json`。
- [x] 驗證 matrix JSON、source matrix SHA-256、UTF-8 no-BOM、CRLF-only、final CRLF 與 `git diff --check`。
- [x] 以 task-local `implement.jsonl` 記錄 matrix、P7.1 contract、Data8／ProductClient／ChurchReport source 與 relevant specs；不得把 credential、endpoint、Organization ID 或 fixture ID 寫進 jsonl。
- [x] 依連續 Goal 授權執行 `task.py start`；目前 task status 為 `in_progress`，只允許本機測試與實作，尚不允許 CE write，直到 slice A bridge preflight 變為 go。

## Phase 2：Slice A — Contact basic-info（TDD）

1. 在 `SpeechMessage.Dynamics.Abstractions` 新增封閉 request／result／response discriminator 與固定 registry definition；測試先證明未知欄位、unknown operation、CE 8.2、wrong connector、missing idempotency key 與無變更均 fail closed。
2. 在 `SpeechMessage.Dynamics.Connectors.Data8` 新增唯一的 exact contact-update template 與 read-back projection；不得讓 `Entity`、FetchXML 或 query／field map 離開 connector-internal scope。每條 `AcquireAsync` 路徑用 `await using` 確定釋放 lease。
3. 在 `SpeechMessage.Dynamics.ProductClient` 新增具名 typed client；建立 Gateway／Embedded contract parity tests，驗證 profile、CE version、connector 均是 deployment-owned，沒有 request-time switch。
4. 在 ChurchReport 僅新增尚未啟用的 adapter／composition support，維持 feature gate false 與現有使用者授權語意。P7.4 才會切換 consumer。
5. 每個檔案完成即跑最小相關測試；修正任何 compilation、test、lifecycle、encoding 或 static-scan failure，最多三個不同假設的 self-repair cycles，同一根因連續兩次即停止並記錄。

## Phase 3：CE 9.1 fixture bridge 與證據

1. 先完成 repository 內 contract／unit tests，再建立 Windows PowerShell 5.1-compatible bridge。它必須 bounded、fail-closed，只允許 `crm91`／Data8／P7.2 marker contact／兩個字串欄位，且只輸出去識別化 JSON。已完成 preflight bridge、明確 `-ExecuteFixture` opt-in runner、固定 Credential Manager 讀取、bounded child process、strict TRX parser 與 20 項 PowerShell 契約測試；2026-08-08 preflight 已回傳 `outcome=go`、`operationExecuted=false`、`featureFlagChanged=false`。
2. 受控 live-evidence slice 依序執行 baseline read → sentinel update → read-back reconciliation → baseline restore → restore read-back；任何 ambiguous timeout、owner mismatch、profile mismatch 或 cleanup 失敗都停在 no-go，絕不自動重送不確定 write。使用者於 2026-08-08 明確授權 `sunnyvalechback` 全資料庫作為可新增／修改／刪除的虛構研發資料。Slice A 已任選既有 contact 並完成真實 CE 9.1 Data8 flow：`outcome=go`、`sentinelState=confirmed`、`cleanupState=restored`、`featureFlagChanged=false`。
3. 只有 preflight 與後續 live bridge 都回傳 sanitized `outcome=go` 才可寫入 task evidence；不得貼入密碼、token、cookie、endpoint、Organization ID、contact GUID、原始 baseline 或例外內容。
4. 如需要使用者在 Lenovo 執行 bridge，直接提供可 copy/paste 的完整命令與預期 JSON；不得重新要求 P6 credential profile 或手動輸入可由 bridge 建立的資料。

## Phase 4：其餘 P7.2 slices

依序完成 B 到 H，每個切片都重複以下 gate，且不得共享不相容的 fixture／rollback owner：

- Slice B 先依 repository 證據拆成 B1 LINE profile、B2 ungrouped commitment aggregate 與 B3 image media handoff。B1／B2 在 P7.2 以 TDD 完成 typed contract、Data8 executor、ProductClient、未啟用 ChurchReport composition 與 bounded CE 9.1 evidence；B3 的兩個 5 MiB image row 只完成 P7.3 可執行 handoff，P7.3 bounded media contract 完成前不得放入 JSON／canonical envelope、不得取得 lease、不得宣稱 live evidence。
- B1 的 LINE token、profile fetch 與 URL probe 留在 ChurchReport；Gateway 只接受三個固定欄位的 set／clear／preserve scalar。B2 由 connector 依 server-owned 小組規則建立 aggregate，不接受任意 FetchXML、QueryExpression 或無界 grouped-ID array。
- [x] 2026-08-08 已完成 Slice B1／B2：typed DTO、registry、Data8 executor、ProductClient、未啟用 ChurchReport composition、fixture bridge、Windows PowerShell 5.1 handoff 與 lifecycle tests 均已建立。真實 sunnyvalechback CE 9.1 Data8 evidence 為 `outcome=go`；B1 的 sentinel read-back 已確認且 baseline 已復原，B2 與 legacy 的四組 raw OptionSet value/count 結果一致。兩者均未變更 feature flag；evidence 保存於 `p7.2-slice-b-live-evidence.json`。
- [x] Slice B2 的 parity 修正已在 server-owned aggregate template 明確排除 null `customertypecode`，避免 CE 回傳沒有 `commitmenttype` alias 的 null group；live runner 在 TRX 無法穩定保存 stdout 時改用嚴格 OS-temp evidence file，並由 handoff 在 `finally` 清除。兩項可重複契約已寫入 backend Gateway spec。

- machine-readable matrix row 先從 `fixture-pending` 變為 `required-for-activation`，並寫明 exact operation IDs、fixture graph、allowed mutation、idempotency、timeout policy、cleanup／reconciliation、CE support、rollout／rollback owner。
- 先補 unit／fault／lifecycle tests，再補 registry、Data8 template、ProductClient 和未啟用 ChurchReport adapter。
- 取得一次 bounded real CE 9.1 evidence，完成 cleanup 和 sanitized evidence，才可將 row 標成 `evidence-complete`。
- CE 8.2／Official Worker／缺 fixture 的 row 保持 fail closed；不將 mock 當作 real CE evidence。

## Phase 5：P7.2 收尾

- [ ] coverage validator 對全部 P7.2 matrix-required row 成功，沒有 `required` row 缺 DTO、registry、executor、consumer、fixture、evidence、rollback owner 或 lifecycle owner。
- [x] 執行 focused tests、完整 `SpeechMessage.Dynamics.Tests`、ChurchReport tests 與 Release build；resource stress／soak、drain／dispose 與 rollback drill 仍待 live fixture／最後切片。完整 solution Release test 必須使用 `dotnet test .\SpeechMessageProducts.sln --configuration Release --no-build --no-restore -m:1`：process-boundary tests 會觀察全機器的 Gateway／WorkerTestHost 程序，預設跨 test project 平行排程會使另一個 test assembly 的短生命期 worker 落入 snapshot，形成 false positive；`-m:1` 保留測試原本的 fail-closed 程序隔離語意，並非放寬 assertion 或忽略殘留程序。
- [x] 執行 `git diff --check`、byte-level UTF-8 no-BOM／CRLF-only／final CRLF check，並確認目前變更只含 P7.2-owned files。
- [ ] 執行 Trellis check、spec update judgment、task-owned staged commit 與 archive；不得 push、PR、啟動 P8 或啟用 ChurchReport 流量。
- [ ] 封存後自動建立／規劃 P7.3，不需額外 PROCEED。

## 2026-08-09 Slice C fixture provenance remediation（TDD）

審閱發現 Slice C 的 descriptor 雖然驗證本機 schema、marker 與目前 Windows identity，卻尚未在 CE 讀取層證明五個 list、兩個 fixture contact 與 relationship-derived area leader 都是 task-owned，且 list 為 static。因此在任何 `-ExecuteFixture` 前必須完成下列最小修正；不得以使用者對隔離開發資料庫的廣泛操作授權取代程式內的可重複 fail-closed 邊界。

1. 先在 `ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureStoreTests.cs` 新增失敗測試：固定的 list/contact retrieval 必須拒絕不含 `P7.2-SC-` task marker、dynamic list、錯誤 logical name、空或錯誤投影；任何拒絕都不得發出 mutation。
2. 在 `P72Data8ListManagementFixtureStore.cs` 新增唯讀、固定欄位的 provenance projection；它只讀 descriptor 已列出的 list/contact identities，驗證固定名稱 marker、static list type、contact marker，並在 `ResolveSmallGroupExpected` 對 relationship area-leader contact 重複驗證。不得提供 generic discovery、caller query、field map 或跨 request cache。
3. 在 `LivePackage02Data8ListManagementEvidenceTests.cs` 的 `TryProveFixtureGraph` 最前方呼叫 provenance projection。任何 provenance failure 必須使 execution lane 在第一個 dispatch 前輸出 `fixture-precondition-failed`，且 reconciliation lane 不得將資源釋放失敗覆寫成 `baseline-unprovable`。
4. 先在 `docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1` 新增失敗測試，鎖定 child non-zero exit code 即使遺留外觀正確的 evidence file 也必須輸出 no-go；再在 runner 於 parse evidence 前以固定 `child-process-failed` reason fail closed。
5. 增加 child 非零退出、reconciliation cleanup failure 與 strict parser 的 focused tests；完成後執行 PowerShell contract suite、focused C# tests、P7.2 coverage validator、Release build、serial solution test、text/encoding gate。只有全部通過才回到已登入的 D365 分頁補 task-owned relationship fixture，並先執行唯讀 reconciliation。
## 2026-08-10 Slice C relationship-list repair lane

- Repair is a separate, explicit runner mode; it is not `-ExecuteFixture`, is not reconciliation, and is never selected by ordinary test discovery.
- The repair child proves the current Windows identity through the existing descriptor and the same CE 9.1/Data8 WhoAmI path, then validates only the descriptor-owned source contact, small-group list, expected relationship list, and task-owned leader.
- The relationship list must be static and marker-bound. Its area-leader and area-name fields must both be blank, or both already equal the deterministic expected state. Partial or unexpected state fails closed before Update.
- At most one Data8 Update is sent, containing only `new_contact_list_arealeader` and `new_area_name`. A transport exception after the request begins is `repair-ambiguous` and is never retried; the child immediately performs one read-back when transport returns.
- The parent runner accepts only the bounded repair evidence schema, emits `safeToRetry=false`, and restores every process environment variable and temporary evidence directory in `finally`.

## 2026-08-10 Slice C fresh-fixture implementation order

- [ ] Re-read the read-only `RepairProbe` evidence and preserve the stale
  source/relationship rows as permanently non-mutable inputs.
- [ ] Add a failing C# fixture-provisioner test that supplies the exact
  descriptor-bound reused list IDs plus a task-marked owner-source leader and
  asserts zero mutation before every precondition is proven.
- [ ] Run that focused test and record its expected RED failure because the
  provisioner/control-plane type does not yet exist.
- [ ] Add the minimal test-only provisioner and fixed request templates:
  three Creates, two `AddListMembersListRequest` calls, and one `AssignRequest`;
  require exact read-back after each stage and publish no descriptor directly
  from C#.
- [ ] Add fault tests for each mutation boundary, including timeout-after-
  dispatch, missing weekly report, same-owner rejection, and descriptor-
  publication refusal. Each ambiguity must be non-retryable and retain only a
  current-user local pending ledger.
- [ ] Extract or add one bounded graph-proof helper only if both the
  provisioner and the existing live bridge need the identical full graph
  predicate; do not duplicate a generic CRM query API.
- [ ] Add failing PowerShell contract tests for `-ProvisionFreshFixture`,
  `-CleanupFreshFixture`, and any required read-only reconciliation mode:
  mutually exclusive parameter sets, explicit confirmation, no credential or
  child process before authorization, strict evidence/ledger schema,
  environment restoration, and temporary-directory cleanup.
- [ ] Implement the PowerShell parent only after the RED checks: generate
  paths itself, pass fixed environment variables to one child, validate the
  child exit code before any evidence, atomically publish only a fully proven
  descriptor, and remove no stale descriptor or stale remote row.
- [ ] Run focused C#, PowerShell, coverage-validator, Release build, serial
  solution, isolation, lifecycle/soak, encoding, and `git diff --check` gates.
  Do not offer the new operator command until all repository gates are green.
- [ ] Run one explicit, bounded CE 9.1 provision/probe/evidence/cleanup cycle
  only through the final operator handoff. Preserve an ambiguous or unclean
  result as `no-go`; do not retry, switch connector, change a feature flag, or
  start P6.2.

## 2026-08-11 Slice C Fresh Preflight Probe 實作計畫（TDD）

- [ ] 在 `P72Data8ListManagementFreshFixtureProvisionerTests.cs` 先新增 failing tests：對完整
  valid fake projection 預期 `fresh-preconditions-proven`，對無效 request shape、任何一個
  operational list、leader marker、非 `systemuser` owner、disabled owner、same-as-WhoAmI owner、
  零/多筆 weekly report 與 CRM read exception 預期固定分類；每個 test 均斷言 `Create`、`Update`、
  `Execute`、`Delete` 和 ledger persistence 均為零。執行 focused test，確認因 probe type/method
  尚不存在而 RED。
- [ ] 以最小實作新增 `P72FreshSliceCFixturePreflightProbe` 及其 immutable request/result；固定使用
  existing provision request 的 descriptor scalars，但不接受 nonce/ledger，也不公開 Entity、GUID
  或 raw result。它只發出 five list Retrieve、leader Retrieve、owner Retrieve、以及一個
  `TopCount=2` weekly-report `RetrieveMultiple`；同一 service scope 內依 fixed category 回傳。
  重新執行上述 suite 至 GREEN，並加入同 profile two-invocation isolation test，證明 classifications
  不跨 invocation 保留。
- [ ] 在 `LivePackage02Data8ListManagementFreshFixtureTests.cs` 先新增 failing opt-in fact 與
  strict environment reader；它以既有 deployment resolver 呼叫 WhoAmI，然後建立一個 direct
  Data8 service 給 probe，任何 runtime/cleanup failure 都只產生 bounded no-go。再新增一個
  專屬 evidence writer/parser tests；wire evidence 必須固定為 version 1 schema 且永遠 zero mutation。
- [ ] 在 `Invoke-Package02Data8ListManagementFreshFixture.Tests.ps1` 先新增 `-FreshPreflightProbe`
  的 failing contract checks：所有 mode 互斥、缺 credential/descriptor 不建立 child、strict
  evidence rejects IDs/names/extra properties、result 永遠 operationExecuted=false、environment
  restored、temporary root deleted，以及 read-only mode 絕不生成 ledger/descriptor publication path。
  執行 script，確認 binder/schema checks RED。
- [ ] 最小化更新 `Invoke-Package02Data8ListManagementEvidence.ps1`：新增互斥 parameter set、
  read-only mode flag、無 ledger 的 temporary/evidence env bindings、60/180-second existing
  child bound、strict parser 和 parent JSON projection。明確拒絕所有 fresh provision/cleanup
  environment variables與子測試 filter 混用，並在 child nonzero/timeout 回傳 no-go/zero mutation。
  執行 PowerShell suite 至 GREEN。
- [ ] 跑 focused C#、PowerShell contract、P7.2 coverage validator、Release build、serial solution
  test、UTF-8 no-BOM/CRLF/final-CRLF byte gate、`git diff --check` 和 scope check。開始 review 前
  將 major results 寫入 `p7.2-slice-c-continuation-2026-08-10.md` 與 CCG task record。
- [ ] 以 project self-healing runner 平行嘗試 Gemini/Claude review，最多等待 45 秒；逾時停止
  等待、記錄 `雙模型未完成`，只以本機 review/test 繼續。
- [ ] 所有本機 gate green 後，只執行一次 `-FreshPreflightProbe -Json`。若 non-go，記錄其
  deidentified categories並停止；若 go，才重新評估是否滿足新的獨立 fresh-fixture cycle 的所有
  allowlist 條件，絕不把 probe 視為先前 provision 的重試。

## 2026-08-11 Slice C weekly-report cardinality diagnostic 修正（TDD）

- [ ] 先更新 PRD／design／本計畫，明確將 unique 定義為 exact transfer-target list、active state、
  fixed UTC Sunday 的交集，而非同日全 Organization 資料列；此文件修正不改動 CE。
- [ ] 在 `P72Data8ListManagementFreshFixtureProvisionerTests.cs` 先分開 zero-row、two-row／paging
  weekly-report fake scenarios，斷言只產生 `zero-active`／`duplicate-active` 的 no-go、仍為一次
  bounded RetrieveMultiple 且 mutation/ledger 為零；先執行並確認現有 `not-exactly-one-active` 實作
  使該測試 RED。
- [ ] 最小化更新 `P72FreshSliceCFixturePreflightProbe`：保留 exact target-list、statecode、UTC Sunday
  與 `TopCount=2` query；只改 method-local deidentified cardinality classifier，不新增 generic search、
  count、ID、名稱、cache、retry 或 CRM mutation。
- [ ] 更新 `Invoke-Package02Data8ListManagementEvidence.ps1` 與其 contract tests，使 strict schema 只
  接受 `exactly-one-active`、`zero-active`、`duplicate-active`、`unavailable`；拒絕 legacy ambiguous
  值、raw count、extra field 或 identity leakage，且維持 preflightOnly/operationExecuted/feature flag
  固定 false。
- [ ] 此段的 `zero-active` no-go 假設已由 2026-08-11 使用者確認撤銷；以下新增的相容行為計畫優先。

## 2026-08-11 Slice C zero-active 相容行為修正（TDD）

- [ ] 先以已確認的 ChurchReport `UploadIntegrateData.PresentRecord` 行為更新 PRD／design／CCG record：
  zero weekly report 時，present record 的 weekly-report lookup 缺席是正常且必須精確讀回的狀態；
  不建立、選擇或修補 weekly report。
- [ ] 在 `P72Data8ListManagementFreshFixtureProvisionerTests.cs` 先把「zero weekly report 拒絕」
  改成 RED 測試：預期 provision `go`、固定 Create/Add/Assign trace 不變、pending ledger 完整，且
  final present-record-absence query 不帶不存在的 weekly-report lookup。保留並擴充 duplicate/paging
  對應的 zero-mutation no-go 測試。
- [ ] 在 Data8 connector tests 先加入 RED：zero-row weekly report 可完成 transfer，created
  `new_present_record` 不含 weekly-report lookup；one-row 時 lookup 精確相符；two-row/paging、
  existing/malformed present record 皆在任何 mutation 前拒絕。測試 fake 必須拒絕 generic query、
  unbounded enumeration 與未 allowlist 的 mutation。
- [ ] 最小化實作 optional weekly-report resolver 與 exact read-back：只讓 `zero-active` 或
  `exactly-one-active` 通過；zero 時以 contact/list/UTC-Sunday 的 bounded present-record query
  證明 absence，one 時額外比對 exact lookup；duplicate/unavailable 永遠不取得候選 ID、不可寫入。
- [ ] 更新 `P72FreshSliceCFixturePreflightProbe` 與 strict PowerShell evidence contract：`zero-active`
  與 `exactly-one-active` 都是 `go` 的允許 weeklyReport 分支；`duplicate-active`、`unavailable` 維持
  `no-go`。輸出仍不得包含 count、ID、名稱、日期、例外或 raw entity。
- [ ] 完成 focused C#／PowerShell RED-GREEN、P7.2 validator、Release build、serial solution tests、
  isolation/lifecycle、encoding/CRLF 與 diff gate；CCG run 看到 Claude quota/session block 時立即停止
  等待並記錄 `雙模型未完成`，不重試 provider。
- [ ] 全部本機 gate green 後才執行一次新版 `-FreshPreflightProbe -Json`。`zero-active` 與
  `exactly-one-active` 可作為新的 fresh-fixture cycle 的必要前置證據；`duplicate-active`／
  `unavailable` 則精確記錄後停止，絕不修改 CRM weekly report。
