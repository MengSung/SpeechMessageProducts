# P7.4 Fee Editor Read Boundary Implementation Plan

## 先決條件

- [x] 閱讀 goal objective、AGENTS.md、parent P7.4 artifacts、authoritative matrix、backend isolation
      contract 與 mandatory review checklist。
- [x] 確認 `ORG-CALL-00066` 不是既有 editable grid 可安全替換的 DTO parity；選定新獨立 read-only
      endpoint，並維持所有 checked-in deployment gates=false。
- [x] 記錄先前 dual-model analysis 在 45 秒內未取得可用結果且 repository path 編碼不可靠；不重試等待，
      改用目前程式碼與 archived evidence 的本機設計。

## TDD 執行順序

1. [x] 新增 `FeeEditorLessonAccessResolverTests`。先測 current login 未 match、server lesson list 未載入、
       null/invalid/duplicate lesson IDs、target 不在 snapshot 全部拒絕；只允許完全有效的 server snapshot。
       執行 focused test，確認因型別不存在而 RED。
2. [x] 新增 immutable `FeeEditorReadRow`／`FeeEditorReadResult` 與 pure resolver。不可將 browser input
       納入 resolver API，不可參考 `FeeList`、ToolUtility、CRM Entity、cache 或 session。重跑 focused tests
       為 GREEN。
3. [x] 新增 `FeeEditorReadServiceTests` 的 failing cases：正確 operation/profile/workload/cancellation；
       null/mismatch rows 拒絕；上游 array 被改動後 result 不變；兩個 interleaved request 的 rows/result
       reference 不相同。執行並確認 RED。
4. [x] 新增最小 service：materialize typed result、驗證每筆 `DiscipleLessonId`、建立 defensive immutable
       result。不要加入 retry、fallback、legacy read、CRM Entity 或背景工作。重跑 tests 為 GREEN。
5. [x] 新增 `FeeManagementControllerFeeEditorReadContractTests`。它必須鎖定 dual-gate first branch、
       scope/snapshot authorization 在 `Guid.TryParse` 前、fixed profile/workload、`RequestAborted` forwarding、
       既有 editor/write paths untouched，並確保 cancellation 在 generic catch 前。執行並確認 RED。
6. [x] 在 controller 增加唯一新 JSON route 及為其服務組成的 private helper。false gate 不可解析 locator
       或讀 `FeeList`；true gate 不可呼叫 legacy loader 或 editable model。重跑 source contract 為 GREEN。
7. [x] 若既有 tests 未明確驗證 ProductClient `fees.editor.load.by.disciplelesson` mapping，先寫 failing
       mapping test，再補最小 ProductClient assertion；若已涵蓋則記錄現有 evidence，不重複改 production。

## Child 邊界驗證

8. [x] 跑所有新的 focused resolver/service/controller/ProductClient test，並保留 RED/green command output
       summary 在 task check record。
9. [x] 跑 `ChurchReport.MemberInfo.Tests` Release suite、受影響 Dynamics suite、solution Release test 與
       solution Release build。失敗時依 TDD 修正並重跑，不以縮小 assertion 或 skip 取代。
10. [x] 用 byte-level check 驗證所有新增/實質修改 `.cs` 為 UTF-8 no BOM、CRLF only、final CRLF；跑
        `git diff --check`、forbidden API/scope scan、gate=false 設定 scan 與 A/B isolation review。
11. [x] 使用 `Start-CcgDualModelRun.ps1` 執行 implementation/final review，最多等 45 秒；逾時或 quota
        記錄「雙模型未完成」並以本機 review 繼續，不重試等待。
12. [x] 將 local-only result、Dedicated/CE 未升級、capacity no-go、tests/build/cleanup 狀態寫入 child
        `check.md`／`check.jsonl` 及 parent task notes；scope-only commit 後 archive child。只有全部 temporary-
        legacy row、evidence、capacity、zero-reference、soak/drain/rollback gate 都通過，才可評估 P7.5；
        本 child 不會自行建立 P7.5/P8。

## rollback points

1. authorization snapshot 不能確定為 current-login server-derived，或任何 test 顯示 A/B data/reference
   sharing：撤回未提交的 production 變更，保留 failing test 與 exact no-go，絕不查 CRM 補權限。
2. typed response mismatch、fault、cancellation 或 resource uncertainty：不 fallback/retry；service 不發佈
   result，controller 讓 cancellation 原樣傳遞或回傳固定非敏感 failure。
3. release/encoding/scope review 不通過：修正本 child 的程式、測試或文件後重新驗證；不改 gate、CE、
   traffic、P7.5 或 P8 作為捷徑。
