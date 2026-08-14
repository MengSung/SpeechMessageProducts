# P7 MemberInfo 伺服器擁有指派證據來源實作計畫

## Phase 1：契約與 TDD

1. [x] 在 `SpeechMessage.Dynamics.Tests` 建立 registry、Data8、ProductClient contract tests，先固定 operation
   ID、read kind、單一 subject parameter、top-count/page/512 bound、exact response branch、wrong branch、
   invalid routing、cancellation 及 A/B interleaving的 RED 行為。
2. [x] 在 `ChurchReport.MemberInfo.Tests/Security` 建立 server assignment source tests，先證明 Cookie scope
   建立早於 locator/legacy state/I/O，Church role priority、six-field assignments、date boundaries、zero/duplicate/
   overflow/malformed failure、defensive copy 與 A/B subject isolation。
3. [x] 執行新增 focused tests，確認未實作 capability 時為預期 RED，不得修改 controller 或 legacy manager。

## Phase 2：Gateway data plane

4. [x] 修改 `SpeechMessage.Dynamics.Abstractions/Operations` 的 operation ID、registry definition、response
   discriminator 及 immutable wire record；只允許 subject GUID 與 bounded assignment result。
5. [x] 修改 `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs` 與其固定 query helper，
   實作 contact job-title direct retrieve、six-field list OR filter、active/purpose/app-named/date validation、
   top count 513／MoreRecords sentinel 及 fail-closed result。
6. [x] 在 `SpeechMessage.Dynamics.ProductClient/MemberInfoAuthorization` 建立 typed read client、DTO 與 DI extension；
   每次呼叫驗證 deployment-owned routing、要求 exact operation/branch、defensive-copy response 並原樣傳遞取消。
7. [x] 執行 Dynamics focused tests，確認 GREEN，並驗證 fault/cancellation 不使 faulted client 回池。

## Phase 3：ChurchReport security adapter

8. [x] 在 `SpeechMessageProducts.ChurchReport/Security` 建立以 `P7GatewayRequestScope` 為唯一 identity input 的
   server assignment evidence source。它只把 ProductClient immutable result 轉為 assembly-internal
   `MemberInfoTargetAuthorizationEvidence`，不讀寫 Session、`InMemoryContext`、ListManager、cookie claims、
   CRM SDK 或 cache。
9. [x] 完成 ChurchReport focused A/B、source-unavailable、malformed、duplicate、overflow、cancellation 與
   reflection/public-surface tests；不得接線 `MemberInfoController`。

## Phase 4：檢查、封存與後續

10. [x] 執行 targeted tests、完整 `dotnet test SpeechMessageProducts.sln --configuration Release --no-restore`、
    Release build、UTF-8 無 BOM/CRLF/final CRLF、`git diff --check` 與 scope check。
11. [x] 以 CCG self-healing runner 發起 Gemini/Claude reviewer，最多等待 45 秒；若未完成，寫入
    「雙模型未完成」並以本機 review 繼續。修正所有已驗證 Critical finding。
12. [x] 更新 70-row matrix 與 parent checkpoint，但不將 local data plane 升格為 consumer/CE/traffic/P7.5/P8；
    scope-only commit/archive 後再以 source audit 決定 00031/00032/00033 是否可建立各自 child。

## rollback points

- 任一 fixed-query/DTO test 失敗、query paging、資料型別歧義、超出 bound、read-back 不符或 resource cleanup
  無法證實時，不接線 consumer、不啟用 gate；回到 child 的 local data-plane boundary。
- 此 child 沒有 CE mutation、feature gate 或 traffic，因此 rollback 為停用/移除尚未接線的 local source；
  不修改 legacy `MemberInfoController` 或既有 Session/ListManager flow。

## 2026-08-14 實際檢查證據

- 新增 P7.4 focused tests 共 24 項（Data8 9、ProductClient 7、registry 2、ChurchReport security adapter 6）均通過；
  連同相依 registry agreement tests 的 targeted filter 共 53 項通過。
- `dotnet test .\\SpeechMessageProducts.sln --configuration Release --no-restore` 最終通過：Dynamics 904 passed／7 skipped，
  ChurchReport 658 passed／14 skipped，其他 solution test projects 亦通過。第一次完整 run 的 Kestrel request-body
  boundary transport failure 無法在單獨或第二次完整 run 重現，未修改其不屬於本 child 的測試。
- `dotnet build .\\SpeechMessageProducts.sln --configuration Release --no-restore` 為 0 warning／0 error。
- byte-level UTF-8 no-BOM／CRLF／final CRLF 與 `git diff --check` 已在 scope-only 檢查中驗證。
- CCG self-healing reviewer 以 45 秒上限執行；時限內沒有 accepted dual-model result。runner 後續留下 Gemini
  output，但 Claude 始終無 usable output；Gemini 的 UTF-8/BOM Critical 已以 strict byte-level decode、無 U+FFFD
  replacement character、正確繁中 literal 與 mutation-proven tests 反證。因此結果仍為「雙模型未完成」，本機
  source review、A/B isolation、cancellation 與 scope check 繼續完成；不得將本結果稱為完整雙模型審查。
- 本 child 沒有 CE request、mutation、fixture、feature gate、traffic、controller cutover、ToolUtility removal、P7.5 或 P8 操作。
