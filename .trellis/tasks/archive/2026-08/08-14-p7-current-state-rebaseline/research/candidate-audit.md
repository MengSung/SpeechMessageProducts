# Research: P7.4 local-only candidate audit

- Query: 依現行 authoritative gap matrix、P7.4 children/checkpoints 與 ChurchReport/ToolUtility source audit，找出下一個可作為 P7.4 local-only child 的 capability family；必要條件為 server-derived immutable request-local authorization、bounded DTO-only、無 Session/InMemoryContext/shared mutable credential bridge/stored CRM FetchXML/write adjacency，且具有 rollback owner 與 gate=false zero-work。
- Scope: internal
- Date: 2026-08-14

## Findings

### Qualification verdict

**合格候選：0 個。**

現行 matrix 中最接近未處理的 P7.3 special-resource read 是
`ORG-CALL-00063`／`churchreport.weekly.reporting`／
`stats.meeting.retrieve.by.sunday`。它的 registry、Data8 executor、ProductClient 與
P7.4 capability rollback owner 都已存在，但 consumer 仍是 `not-migrated`、
`special-resource-pending`、`temporary-legacy`（`authoritative-gap-matrix.json:2361`）。
此資料層完成度不足以構成 P7.4 child 的資格；實際 ChurchReport 呼叫鏈違反全部必要的
authorization、shared-state、DTO-only、write-adjacency 與 gate=false zero-work 條件。

| 結果 | Capability family | Evidence path | 排除理由 |
| --- | --- | --- | --- |
| 不選定 | `ORG-CALL-00063` `churchreport.weekly.reporting` | Matrix -> Package03 read contract -> `QrCodeController` -> `PersonalQrCodeUtility` / `SundayQrCodeUtility` -> ToolUtility FetchXML | POST 的 `UserLineId`、群組與 QR locator 在授權前進入 `InMemoryContext`；utility 以 stored CRM FetchXML 回傳 `EntityCollection`、rehydrate CRM `Entity`，接著建立/更新 present record、關聯 meeting、更新 weekly report，部分分支另有通知；沒有 weekly-specific disabled gate 或 zero-work ChurchReport branch。 |
| 不選定 | `ORG-CALL-00014`、`ORG-CALL-00065` app-named list catalogs | P7.1 typed data-plane artifacts -> existing ChurchReport consumers | 兩個無參數／bounded DTO data plane 已完成，但 matrix consumer 仍未遷移；`00065` 既有 consumer 仍有 shared `EntityCollection` cache，未找到可直接復用的 immutable request-local authorization boundary。 |
| 不選定 | 其餘表面為 read 的未遷移 rows | P7.4 child checkpoints/source audits | 已完成 local boundary 的 rows 不能重複建立 child；其餘 rows 已被 source audit 判定為 payment/write adjacency、credential/session hydration，或 MemberInfo Session/InMemoryContext/legacy credential loader 依賴。 |

### ORG-CALL-00063 source trace

1. Matrix 將該 row 指定為 read，且標示 Data8 executor / ProductClient `implemented`、registry
   `declared`、rollback owner `p7.4-capability-owner`，但 consumer=`not-migrated`、
   special resource=`paging-result`、temporary legacy（`.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/authoritative-gap-matrix.json:2361`）。
2. Package03 的 transport contract 本身是 bounded read：request 僅有 deployment/server-supplied
   profile/workload 與 UTC Sunday（`SpeechMessage.Dynamics.ProductClient/SpecialResources/IPackage03SpecialResourceClient.cs:182`）；implementation dispatch 後只 map meeting-statistic scalar DTO
   (`SpeechMessage.Dynamics.ProductClient/SpecialResources/Package03SpecialResourceClient.cs:158`)；DTO 沒有 CRM `Entity` 或 paging state
   (`SpeechMessage.Dynamics.ProductClient/SpecialResources/IPackage03SpecialResourceClient.cs:248`)。
3. 這個安全的資料平面沒有 ChurchReport weekly consumer。搜尋 ChurchReport production code 未找到
   `RetrieveMeetingStatisticsAsync` 或 `IPackage03SpecialResourceClient` 的 weekly wiring；現有
   `Package03SpecialResourcesEnabled` wiring 僅覆蓋 MemberInfo image/metadata services。因而沒有可證明的 weekly-specific gate=false short-circuit / zero-work owner。
4. 現行 HTTP entry points 接受 browser POST 值，先呼叫 `SetupLineContext`，再從
   `InMemoryContext.ListManager.QrCodeId` 取得 locator：Sunday path
   `SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:328`，Personal path
   `SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:406`。這不是 server-derived immutable request scope。
5. Personal flow 有 `ToolUtilityFactory.GetInstance` 的 mutable utility field
   (`SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:35`)，使用 stored CRM FetchXML
   取得 `EntityCollection` 後再 `RetrieveEntity("new_meeting_statistics", ...)`
   (`SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:202`)，並立即進入
   `SigningMeetingStatistics`（`:238`）。
6. 同一 utility 隨後取得 present-record CRM entities、設定 meeting relationship、更新 present record
   與 weekly report（`SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:256`）。
   Sunday path 也直接取得 `new_meeting_statistics` Entity（`SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:181`）、
   進入同一 signing method（`:223`），並以 process-local static lock 包住部分 weekly update
   （`:58`、`:287`）；static lock 不是跨主機隔離或 rollback owner。
7. ToolUtility 的 underlying query 是 runtime-built FetchXML，透過 `_organizationService.Execute` 回傳
   CRM `EntityCollection`（`ToolUtility/QueryOperations/FetchXmlQueryService.cs:251`）。因此把 Package03
   DTO 接回既有 utility 會成為 DTO-to-Entity/read-new-write-legacy bridge，而非 DTO-only P7.4 boundary。

既有的 dedicated audit 已獨立確認同一結論：它將 `ORG-CALL-00063` 定義為 QR attendance write-adjacent
family，不是獨立 read consumer（`.trellis/tasks/archive/2026-08/08-14-p72-weekly-attendance-write-family/research/source-audit.md:5`）；
其 local no-go 指出 POST identity/locator 在安全邊界前寫入 process-wide `InMemoryContext`
（`.trellis/tasks/archive/2026-08/08-14-p72-weekly-attendance-write-family/local-no-go.md:32`），並指出
沒有 single-writer / idempotency / graph read-back / deterministic cleanup rollback owner（`:53`）。

### P7.4 checkpoints and other exclusions

- P7.4 parent checkpoint 要求先確認 P7.3 special-resource ProductClient 是否已有「完整
  server-authorized、DTO-only、read-only ChurchReport consumer」；若無，記錄 precise no-go
  （`.trellis/tasks/08-12-churchreport-productclient-cutover/check.md:202`）。本 audit 的
  `ORG-CALL-00063` source trace 正是該 no-go 的證據。
- `ORG-CALL-00005` 已有 default-disabled、server-authorized、request-local、DTO-only P7.4 local
  boundary，不能重複作為下一 child（`.trellis/tasks/archive/2026-08/08-13-08-13-p74-authorized-fee-contact-read/check.md:5`）。
  `ORG-CALL-00066` 亦已有已驗證 mapping/local boundary；其原 legacy consumer 仍含 `Entity`、
  `EntityCollection`、mutable `FeeList` 與 write adjacency，仍為 temporary legacy
  （`.trellis/tasks/08-12-churchreport-productclient-cutover/check.md:194`）。
- `ORG-CALL-00064` 位於 recurring payment-return 的 create/update graph，需要 payment write
  idempotency、read-back、reconciliation 與 rollback owner，不能包裝為 P7.4 pure read
  （`.trellis/tasks/08-12-churchreport-productclient-cutover/check.md:191`）。
- `ORG-CALL-00014` 已完成有界、zero-caller-parameter DTO data plane，但它的 P7.1 record 明示
  consumer remains `not-migrated`（`.trellis/tasks/archive/2026-08/08-13-p71-appnamed-list-catalog-typed-read/check.md:30`）。
  `ORG-CALL-00065` 是不同 contract，既有 consumer 仍為 shared `EntityCollection` cache，需另證
  authorization/cache/rollback boundary（`.trellis/tasks/archive/2026-08/08-13-p71-appnamed-list-catalog-typed-read/design.md:72`）。
- MemberInfo small-group rows `00031/00032` 的 `GetAccess` 依賴 Session、`InMemoryContext`，且可由
  legacy credential-bearing ListManager 載入資料；source audit 明確否認其為 immutable scope
  （`.trellis/tasks/archive/2026-08/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/source-audit.md:20`）。
  Dedication contact-resolve `00060` 也在 immutable authorization 之前使用 Session/
  `InMemoryContext`/mutable form/CRM Entity，已是 source-only local design no-go
  （`.trellis/tasks/archive/2026-08/08-14-p74-dedication-capability-identity-audit/audit.md:9`）。

## Files found

- `.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/authoritative-gap-matrix.json` — immutable 70-row matrix; `00063` is the apparent unconsumed special-resource read.
- `.trellis/tasks/08-12-churchreport-productclient-cutover/check.md` — P7.4 checkpoint and prior caller-shape exclusions.
- `SpeechMessage.Dynamics.ProductClient/SpecialResources/IPackage03SpecialResourceClient.cs` — bounded request/DTO contract for weekly statistics.
- `SpeechMessage.Dynamics.ProductClient/SpecialResources/Package03SpecialResourceClient.cs` — operation dispatch and scalar DTO mapping.
- `SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs` — browser POST / InMemoryContext QR entry points.
- `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs` — FetchXML-to-Entity-to-attendance-write path.
- `SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs` — QR locator Entity load, static lock, and write-adjacent path.
- `ToolUtility/QueryOperations/FetchXmlQueryService.cs` — stored CRM FetchXML and SDK `EntityCollection` return.
- `.trellis/tasks/archive/2026-08/08-14-p72-weekly-attendance-write-family/research/source-audit.md` — prior independent confirmation that `00063` is write-adjacent.
- `.trellis/tasks/archive/2026-08/08-14-p72-weekly-attendance-write-family/local-no-go.md` — precise state/authorization and rollback no-go evidence.
- `.trellis/tasks/archive/2026-08/08-13-p71-appnamed-list-catalog-typed-read/` and `08-13-08-13-p71-appnamed-smallgroups-list-catalog-typed-read/` — existing list data planes and explicit non-consumer/shared-cache boundaries.
- `.trellis/tasks/archive/2026-08/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/source-audit.md` — Session/InMemoryContext/credential-loader exclusion.
- `.trellis/tasks/archive/2026-08/08-14-p74-dedication-capability-identity-audit/audit.md` — Session/InMemoryContext/mutable form exclusion.

## Code patterns

- Required isolation pattern: validate immutable server-derived scope before cache, profile resolution, client allocation, outbound I/O, or response construction; failures fail closed (`.trellis/spec/backend/cross-user-isolation-and-performance.md:31`).
- Required lifecycle pattern: a disabled capability gate must be read before DI/host/client/HTTP construction and prove false-gate zero work; rollback sets the capability gate false and drains only owned in-flight requests (`.trellis/tasks/08-12-churchreport-productclient-cutover/design.md:71`).
- Unsafe pattern observed: caller POST/session/locator state -> `InMemoryContext` -> ToolUtility/CRM `Entity` -> related record writes and notification. This crosses the authorization boundary before it is proven and has no bounded single rollback owner.

## External references

- None. This was a repository-only audit; no external documentation, CE, service, feature, or traffic operation was used.

## Related specs

- `.trellis/spec/backend/cross-user-isolation-and-performance.md`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/spec/guides/cross-user-isolation-and-performance-review.md`

## Caveats / Not Found

- `task.py current --source` reported no session pointer; the dispatcher supplied the exact task path, so this audit writes only to that task's `research/` directory.
- No production ChurchReport weekly-statistics use of `RetrieveMeetingStatisticsAsync` or
  `IPackage03SpecialResourceClient` was found, and no weekly-specific deployment gate with a gate=false zero-work test was found.
- The matrix is authoritative for current status; local P7.1/P7.4 data-plane completions do not alter its
  `consumer=not-migrated`, CE/host evidence, or `temporary-legacy` facts.
- This audit deliberately makes no consumer, CE, feature, traffic, P7.5, or P8 recommendation.
