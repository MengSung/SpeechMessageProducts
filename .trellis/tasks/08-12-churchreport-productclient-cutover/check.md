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

## P7.4 child：ORG-CALL-00066 fee-editor read boundary

Child `08-13-p74-fee-editor-read-boundary` 已完成並準備封存。它沒有把既有 `Fee`、`Present`、
`GetFeeData` 或可寫 `FeeList.FeeDataList` 改接 Gateway；改為新增獨立、JSON-only、DTO-only 的
`GetFeeEditorRows` route。兩個 deployment-owned gates 在 browser GUID parsing、session snapshot、
ProductClient composition 或 I/O 前 short-circuit，checked-in config 全數保持 false。

gate=true 時，route 只接受目前登入者已載入的 server lesson snapshot：先 scope、驗證 loaded 狀態與
snapshot ID 完整性，再建立 request-local unique allowlist，最後才解析 browser locator 與 dispatch。它不會
呼叫 legacy lesson loader、ToolUtility、`RetrieveEntity`、fallback 或 retry。typed service 固定使用
`fees.editor.load.by.disciplelesson`、server profile、`church-report-service` workload，完整驗證每一列
lesson ID 並 defensive-copy read-only scalar result；A/B interleaved tests 證明 result、collection、row 與
marker 沒有跨 request 共用。

final review 的 cancellation finding 已先 RED 後 GREEN：controller generic catch 現以
`catch (Exception ex) when (ex is not OperationCanceledException)` 排除所有取消來源，避免非
`RequestAborted` token 的取消被轉譯為一般 unavailable。新 ProductClient mapping regression 證明
ORG-CALL-00066 既有實作已固定映射到正確 operation，沒有宣稱新增 executor。

品質證據：focused 12+12 tests、ChurchReport Release 568 passed／14 existing environment skips、Dynamics
Release 737 passed／7 existing live SQL skips、solution Release tests 全綠、solution Release build 0 warnings／
0 errors；UTF-8 no-BOM、CRLF、final CRLF、`git diff --check`、forbidden API 與 gate=false scans 通過。
CCG final review 先依 45 秒上限以 Gemini usable 結果繼續；runner 後續自行完成 Claude，`summary.json` 證明
Gemini+Claude 均完成，兩者無 Critical/Warning。Gemini 的 UTF-8 BOM Info 與 AGENTS.md no-BOM 強制規則衝突，
故未採用並有 byte-level evidence；Claude 記錄的同一 session `FeeList` cache 並行風險是既有架構項目，
新 route 對其 fail closed，不形成此 child 的資料洩漏或交付阻擋。

此 child 沒有 CE、Dedicated、traffic、P7.5 或 P8 evidence，也沒有 fixture 或外部 cleanup；rollback 是保持／
設回 editor gate=false。capacity enablement no-go 不變：legacy ToolUtility 尚未證明與 Gateway 共用 durable
organization admission，legacy ingress coverage 與 drain-first/non-overlap 實機 evidence 不足。因此 P7.5／P8
仍不可啟動；下一 child 必須繼續從 authoritative gap matrix 選擇獨立 local-only consumer 或 P7.5 prerequisite。

## Batch B review follow-up：取消例外不可進入 HandleError

P7.4 Batch B review 指出的兩個 `catch (Exception)` 經本機 root-cause tracing 確認為真實生命週期風險：
`LoadContactStorLessons` 與 `LoadEquipmentStorLessons` 雖先以
`catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)` 重新擲出，
但只有 request token 已標示取消才會命中。若下游 ProductClient／Data8 lease 以不同 token、timeout 或內部
取消來源擲出 `OperationCanceledException`，該例外會落入後續 catch-all，被 `HandleError` 轉成回應或診斷；
這會違反原始取消語意，並可能在已中止 request 後延長例外／回應資料生命週期。

先新增並執行 focused source-contract test，RED 如預期：

```text
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~StorLessonControllerProductClientContractTests
失敗：Stor_lesson_actions_leave_operation_cancellation_outside_generic_error_handling
原因：MemberInfo action 尚未以 exception filter 排除 OperationCanceledException。
```

最小修正將兩個 action 的一般處理改為
`catch (Exception ex) when (ex is not OperationCanceledException)`（Equipment 同理使用 `e`）。因此所有
`OperationCanceledException` 均保留原始 token／堆疊並自然離開 action，由 ASP.NET Core 與既有
ProductClient／lease owner 完成取消與釋放；非取消錯誤仍完全維持既有 `HandleError` 行為。沒有新增
fallback、DI、ToolUtility、feature gate、CE 或流量變更。

GREEN 與格式證據：

```text
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~StorLessonControllerProductClientContractTests
通過：3，失敗：0。

git diff --check
通過（無輸出）。
```

三個 changed C# 檔案均以 byte-level 檢查確認 UTF-8 without BOM、僅 CRLF、且 final CRLF。此次只處理
review 指定的兩個 action；同檔其他既有 catch-all 不屬本次 Package01 stor-lesson async cancellation
call-chain，未擴張修改。

## Batch C：Package01 fee caller-shape inventory

本輪只讀盤點已寫入 `batch-c-caller-shape-inventory.md`。它確認既有 typed ProductClient capability
存在不等於 consumer 可安全遷移：

- `ORG-CALL-00005` 的實際 action 邊界接收 browser-supplied contact ID，且既有流程會復用可變付款
  form model。未先完成 server-side selected-contact authorization 與 request-local AJAX response 前，接上
  ProductClient 會讓 caller-provided CRM ID 跨越授權邊界，故維持 `temporary-legacy`。
- `ORG-CALL-00064` 是 recurring payment-return 中建立 fee、更新 booking、可能更新 contact/card 前的
  金融判定；它需要 payment write 的 idempotency、timeout-after-dispatch、read-back、reconciliation 與
  rollback owner，不能冒充成 P7.4 的純 read migration。
- `ORG-CALL-00066` 綁定 fee editor 的 `Entity`、`EntityCollection`、formatted values 與 mutable `FeeList`，
  並相鄰 update/create/assign-owner 路徑；DTO rehydration 會把 SDK bridge 偽裝成遷移，故維持
  `temporary-legacy`。

Batch C 未執行 CE request、mutation、feature gate enablement、流量切換、P7.5 或 P8。CCG architect
run 遵守 45 秒上限：Gemini 僅產生逾時的部分輸出、Claude 受 provider quota/session limit 阻擋；其對
repository caller 的不一致推論未採用。本批狀態為「雙模型未完成」，結論以本機 call-chain evidence 為準。

下一步是盤點 P7.3 已完成的 special-resource ProductClient 是否已有完整 server-authorized、DTO-only、
read-only ChurchReport consumer；若沒有，記錄精確 no-go 後再檢視下一個 capability。所有 gate 繼續為 false。

## P7.4 feature gate capacity enablement audit

唯讀 audit 結論為 **NO-GO**，詳細內容見 `capacity-enablement-audit.md`。Package01 feature gate 維持 false，
且沒有 CE、流量、P7.5 或 P8 操作。原因不是 ProductClient 或已遷移 read projection 的本機測試失敗，而是
實際部署流量尚未能證明 aggregate capacity/non-overlap：Dedicated/Embedded Data8 runtime 建立行程內
`InMemoryRuntimeHostSlotCoordinator`，legacy ToolUtility 使用另一個 process-wide singleton/connection pool，
兩者沒有 shared durable admission；另外沒有實際演練的 drain-first runbook。

`SqlRuntimeHostSlotCoordinator` 與其跨 process tests 是日後的可用基礎，但沒有證明現有 legacy ToolUtility
和 ChurchReport Package01 deployment 使用相同 canonical Organization、namespace、epoch/fencing 與 admission
permit。不得把元件存在或 isolated test 誤當成切流證據。

已嘗試依 45 秒規則執行 CCG capacity architect audit；在時間上限內沒有產生 usable backend output，故立刻
改採本機 call-chain/configuration evidence。本 audit 狀態為「雙模型未完成」，不能稱為雙模型審查完成。
恢復條件是把兩條路徑接到同一 durable authority 並以兩 host/path 證明 aggregate capacity/drain baseline，
或由 deployment owner 演練並記錄 drain-first non-overlap runbook；在此之前只繼續 local-only P7 work。
