# P7.4 ChurchReport ProductClient 逐能力切換：品質檢查

## 檢查範圍與未執行事項

本次只完成 P7.4 planning 與 Batch A 的本機 Package01 fee date-range read consumer hardening。
沒有執行 CE mutation、read-only CE request、feature gate enablement、ChurchReport traffic switch、
CE 8.2、Official Worker、P7.5、P8、雲端資源、push 或 PR。

`Package01FeeReadsEnabled`、`Package02ContactBasicInfoUpdatesEnabled` 與
`Package02ContactProfileOperationsEnabled` 均維持現有 false 設定。第一個實機 gate 仍因未證明
shared durable admission 或 verified drain-first non-overlap runbook 而 no-go；此 no-go 只限制
enablement，不限制 local-only implementation。

## Batch A：fee date-range projection atomicity

`DonationFeeQueryService` 的 typed Package01 path 以前在 DTO mapping 完成前就把既有 model 的
`TotalAmount` 歸零。新增 regression test 用 invalid DTO 重現此 fault，第一次執行如預期失敗：
`model.TotalAmount` 預期 88、實際為 0。修正後，DTO mapping 與加總均在 request-local local
variables 內完成，所有 mapping 成功且總額可安全表達為 `Int32` 後才 commit 到 model。

外部 Gemini reviewer 指出 Int32 加總可能 unchecked wrap。此 finding 已以另一次 red test 重現；
修正使用 Int64 total、範圍檢查與 `OverflowException` fail-closed。投影 fault、cancel 與 overflow
都不會改變原 model，也沒有 shared cache、static request state、new client、lease 或 resource owner。

## 跨層與隔離結論

Batch A 的資料流是：request-local form model → existing server-derived ProfileAlias/workload subject
→ stateless `IPackage01FeeReadClient` → existing process-host-owned executor generation → request-local
DTO projection → atomic form-model commit。consumer 未接受 caller-controlled endpoint、profile、connector、
organization、credential 或 owner；也沒有在 model、DTO、exception 或 cancellation token 上建立跨 request
retention。typed client 的 cancellation token 仍以原樣傳遞且 `ConfigureAwait(false)` 保持既有非同步契約。

`StorLessonQueryService` 的 `EntityCollection`/`RetrieveEntity` bridge 已確認存在，沒有被 Batch A
誤列為遷移完成；它保留為 Batch B 的 legacy inventory，而非 P7.5 zero-reference evidence。

## 驗證證據

- `dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~DonationFeeQueryServiceAsyncTests`
  ：4 passed。
- `dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore`
  ：530 passed、14 skipped。skipped 都是既有 live/environment fixture 類測試，未因本 batch 啟用。
- `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore`
  ：0 warnings、0 errors。
- `git diff --check`：通過。
- 變更的 `.cs` 和 task artifacts 已 byte-level 確認 UTF-8 無 BOM、CRLF-only、final CRLF。

## 審查狀態

planning 和 Batch A 均已透過 `Start-CcgDualModelRun.ps1` 發起 Gemini/Claude review。每輪超過
使用者核准的 45 秒上限時立即停止等待並先以本機驗證繼續；兩個 run 的 Gemini 和 Claude 後續皆已
完成並寫入 task-owned artifacts，故最終不是降級審查。planning 對 StorLesson bridge 的 Critical
finding 已列為 Batch B 的前置工作；Gemini 的 Batch A overflow Warning 已修正並以 red/green
regression test 證明。其 UTF-8 with BOM 建議違反 AGENTS.md 的 UTF-8 無 BOM 強制規則，未採用。

## 下一步

繼續 Batch B 的 StorLesson caller inventory。只有改為 projection-only、無 `Entity`/
`EntityCollection` rehydration 的 consumer 才可計為 P7.4 migrated-disabled；所有 legacy bridge 繼續
保留 temporary-legacy/P7.5 blocker。不得開啟 gate 或開始 P7.5/P8。

## Batch B：StorLesson read-only projection consumer

本批將 `lessons.stor.retrieve.by.contact`、`lessons.stor.retrieve.by.disciplelesson` 和共用的
`fees.editor.load.by.disciplelesson` connector projection 補齊為 lesson inner link 的名稱、開課 UTC
時間與階段名稱，並依序通過 wire record、ProductClient DTO、request-local projection 和兩個
controller action。只有 `MemberInfoController.LoadContactStorLessons` 與
`EquipmentController.LoadEquipmentStorLessons` 被列為 migrated-disabled；`DownloadEquipment`、
`FeeDownUpLoader`、`EquipmentStatusCalculator`、`FindStorLessonId` 與所有 `EntityCollection` caller
仍明確是 temporary-legacy，沒有被本批誤列為遷移。

typed path 使用 `GetByContactAsync`／`GetByDiscipleLessonAsync` 與 `RequestAborted`，不會使用
`GetAwaiter().GetResult()`、`RetrieveEntity`、DTO-to-Entity rehydration、request-time fallback 或 shared
mutable collection。connector 對 lesson date/stage alias 型別不符與 page/cumulative byte budget 均 fail
closed；controller 對 request cancellation 重新擲出，避免在中止 request 建立錯誤回應或延長例外生命週期。

外部 Gemini reviewer 指出極端 UTC 最小日期會被本機正偏移誤顯示為 `0001-01-01 08:00`，負偏移可能
無法表示。已新增先 red 的 regression（舊實作實際顯示 `08:00`），再以受限
`ToLegacyDisplayDateTime` 將 null／UTC minimum 保持為既有 `DateTime.MinValue` 哨兵，並在其餘邊界值
先檢查時區 offset 的可表示範圍。此 helper 不快取時區、DTO 或 request 資料。

### Batch B 驗證證據

- `dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore`
  ：735 passed、7 skipped（既有 live SQL tests）。
- `dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore`
  ：539 passed、14 skipped（既有 live／environment fixture tests）。
- `dotnet test .\SpeechMessageProducts.sln --configuration Release --no-restore`
  ：solution 所有可執行 tests passed；Dynamics 735 passed／7 skipped，ChurchReport 539 passed／14 skipped。
- `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore`
  ：0 warnings、0 errors。
- changed Batch B C#／task files：UTF-8 no-BOM、CRLF-only、final CRLF；`git diff --check`、typed-path
  forbidden-pattern scan 與 source-only scope scan 通過。

UTC 最小日期修正後重新執行完整 regression：`ChurchReport.MemberInfo.Tests` 為 540 passed、14 skipped，
`SpeechMessage.Dynamics.Tests` 為 735 passed、7 skipped，完整 `SpeechMessageProducts.sln` tests 均通過，
Release build 為 0 warnings、0 errors。skip 皆為既有 live SQL／CE／environment fixture，沒有被本批啟用或
重新分類為通過。

### Batch B 審查與範圍

CCG self-healing runner 的第一次 review 得到 Gemini 可用輸出，且五項本機 Warning 已修正；最後
45 秒限時 run 的 Gemini 又找到極端日期 Warning，已用上述 red/green 修正。Claude 在限時內沒有完成，
所以本批是「雙模型未完成」，不是 completed dual-model review。沒有 feature gate enablement、CE request／
mutation、traffic switch、CE 8.2、Official Worker、P7.5、P8、雲端資源、push 或 PR。本機既存的
`launchSettings.json` 旗標值未被讀取作為 enablement 證據，也未被本批改動；實機 enablement 的
aggregate-capacity／drain-first no-go 不變。

## 下一步（更新）

P7.4 保持 `in_progress`。下一個可進行的本機範圍是 Phase 4 的 ORG-CALL-00005、00064、00066 caller-shape
inventory；先確認每個 consumer 的 DTO／response contract、legacy bridge、rollback owner 與 required evidence，
再建立獨立 sub-batch。不得因 Batch B 本機通過而啟用 gate、開始 P7.5 或建立 P8。
