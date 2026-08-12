# P7.4 Fee Editor Read Boundary：品質檢查

## 範圍與交付結論

本 child 新增一條未接入既有 Fee／Present editable Grid 的 JSON-only 唯讀 route：
`/FeeManagement/Api/FeeEditorRows/{discipleLessonsId?}`。它只在 checked-in 的
`Package01FeeReadsEnabled` 與 `Package01FeeEditorReadEnabled` 同時為 `true` 時，才會使用
目前登入者相符、已載入的 server lesson snapshot 授權 browser locator，並經
`fees.editor.load.by.disciplelesson` 回傳 request-local immutable scalar DTO。

這是 disabled-by-default 的本機 consumer contract，不是 CE、Dedicated、traffic cutover、
P7.5 ToolUtility removal 或 P8 Central Gateway evidence。兩個 checked-in gates 在
`appsettings.json` 與 `appsettings.Development.json` 均維持 `false`；本 child 沒有執行
CE request、mutation、fixture、feature enablement、流量切換、push 或 PR。

## TDD 與取消回歸

- Resolver、service 與 controller source contract 的第一輪已依 implement plan 完成 RED → GREEN。
- 最後審查發現 controller 原本的 generic catch 只排除「已標示 RequestAborted」的情況；若上游用
  其他 token 擲出 `OperationCanceledException`，它會被轉為一般 unavailable 回應。
- 先把 controller contract assertion 改為要求
  `catch (Exception ex) when (ex is not OperationCanceledException)`，focused run 如預期 RED：1 failed、
  3 passed。
- 最小修正後同一 focused run GREEN：4 passed。所有 `OperationCanceledException` 現在離開 generic
  catch，維持 ASP.NET Core／typed client／lease owner 的原始取消與釋放語意；非取消 exception 仍只回傳
  固定、去識別化 unavailable payload。
- ProductClient 原已有 exact mapping；新增的 ORG-CALL-00066 mapping regression 首次即 GREEN，故記錄為
  既有行為的直接證據，不宣稱是本 child 新增 operation 實作。

## 本機驗證證據

| 驗證 | 結果 |
| --- | --- |
| 新 resolver/service/controller focused tests | 12 passed、0 failed |
| `Package01FeeReadClientTests` | 12 passed、0 failed |
| `ChurchReport.MemberInfo.Tests` Release | 568 passed、14 skipped |
| `SpeechMessage.Dynamics.Tests` Release | 737 passed、7 skipped |
| solution Release tests | 全部可執行 test project passed；既有 CE／environment／live SQL tests 依環境條件 skipped |
| solution Release build | 0 warnings、0 errors |
| byte-level encoding | child 檔案與實質修改 C#／config：UTF-8 無 BOM、僅 CRLF、final CRLF |
| `git diff --check` | passed |
| source scope scan | 新 action 沒有 `FeeDataList`、`UpdateFeeData`、`SaveBatch`、`Fee` rehydration、`RetrieveEntity`、`ToolUtility` 或 legacy loader |
| gate scan | 四個 checked-in values 均為 false |

所有略過的 live／SQL 測試維持既有外部前置條件，沒有因本 child 啟用、也不構成 CE 或 capacity evidence。

## 隔離、資源與 rollback

- browser GUID 只在 server snapshot 建立 request-local allowlist 後才解析；snapshot null、未載入、
  invalid、duplicate 或 target 外一律在 dispatch 前 fail closed。
- 上游 rows 先完整 materialize 並逐列確認 `DiscipleLessonId`；null、mismatch、fault 或 cancellation
  不會發布 partial result，也沒有 retry／fallback／legacy query。
- `FeeEditorReadResult` 對 row projection defensive-copy，再用 `ReadOnlyCollection` 發佈。A/B tests 證明
  交錯 request 使用不同 result、collection、row 與 marker，沒有 static/cache/session DTO retention。
- 本 child 沒有建立 fixture、connection、lease、timer、stream、background task 或 cache；沒有外部 cleanup
  需求。existing executor／process-host／transport 的 ownership 不被重新指派。
- 唯一 rollback 是 deployment owner 保持／設回 editor gate=false；沒有外部資料、CE 寫入或 fixture 要回滾。

## CCG 審查狀態

透過 `Start-CcgDualModelRun.ps1` 發起 final reviewer。依使用者的 45 秒上限，49 秒時停止等待並先以
Gemini 的可用結果完成本機檢查；runner 後續自行完成 Claude，生成的 `summary.json` 證明兩個 backend 都
成功完成，最終是完整雙模型 review（不是降級結果）。兩個 reviewer 均未提出 Critical 或 Warning。Gemini
唯一 Info 建議改為 UTF-8 with BOM，與 AGENTS.md 的 UTF-8 無 BOM 強制規則衝突，已以 byte-level evidence
駁回並維持 no-BOM。Claude 另記錄既有、同一 session `FeeList` 共用 cache 的並行風險；新 route 在此風險下
fail closed 為固定 unavailable，沒有授權繞過或跨使用者發布，該既有架構風險不阻擋本 child。

產物保留於 `.ccg/dual-model-runs/20260813-063835-p74-fee-editor-read-boundary-final-review-reviewer/`，
並只作本 child 的 review record。沒有重新等待或重試 provider。

## 未解除的外部 gate

P7.4 實機 enablement 仍為 no-go：legacy ToolUtility 沒有和 Gateway 使用同一個 durable organization
admission authority，legacy ingress coverage 與實機 drain-first/non-overlap evidence 也尚未完成。
因此此 child 不解除 P7.5 zero-reference、parity、soak、drain、rollback gate，也不允許建立或啟動 P8。
