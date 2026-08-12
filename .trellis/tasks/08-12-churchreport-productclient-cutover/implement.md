# P7.4 ChurchReport ProductClient 逐能力切換實作計畫

> 此計畫依 task-local Trellis artifact 執行。每個 checkbox 代表可獨立驗證的工作；未通過
> capacity/non-overlap gate 時只完成 local-only 項目，絕不開啟 feature gate 或發送 CE 流量。

## Phase 1：planning 與基線

- [x] 讀取正式 goal、parent roadmap、P7 remaining-work matrix、P7.1/P7.2/P7.3 archived evidence、
      現行 Package01/Package02 composition 與 ChurchReport consumer。
- [x] 以 `prd.md`、`design.md` 記錄 P7.4 scope、capability batch、legacy bridge、capacity gate、
      rollback 和 P7.5/P8 predecessors。
- [x] 執行一次 CCG self-healing dual-model planning review，最多等候 45 秒；兩個 backend 最終皆有
      可用 output，Critical findings 已轉成 Batch B 的明確前置工作。
- [x] 將適用 backend specs、matrix、parent roadmap 和 P7.3 lifecycle evidence 放入 JSONL context
      manifest；確認 manifest 沒有 seed placeholder。
- [x] 以本機 review 驗證文件沒有把 disabled path 宣稱為 enablement、沒有把 70 rows 當作一次性
      operation，且把 StorLesson SDK bridge 明確保留為 Batch B 的不得切流缺口。
- [x] 使用 `task.py start` 將 P7.4 轉為 `in_progress`；上述 artifacts 和 review 已完成。

## Phase 2：Batch A — Package01 fee date-range read consumer

- [x] 閱讀 `DonationFeeQueryService`、`DonationDedicationFeeFormService`、`DonationPaymentManager` 及
      其 focused tests；確認 typed response 僅投影 request-local model，contact identity Entity 是本批
      刻意未遷移的 legacy scope。
- [x] 先寫並實際觀察 fail-first tests：畸形 DTO 曾將 model 的總額歸零；金額總和超出 Int32 時曾
      靜默 wrap。取消路徑仍證明不改寫既有 model。
- [x] 僅修改 fee read consumer，使 typed branch 先在 request-local `DedicationFee` 與 Int64 total
      完成投影；投影或範圍驗證失敗時不改寫 model，不新增 SDK entity 回補、static cache、同步阻塞或
      fallback。flag=false legacy behavior 不變。
- [x] 執行 `dotnet test .\\ChurchReport.MemberInfo.Tests\\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~DonationFeeQueryServiceAsyncTests`；4 passed。
- [x] 完成 Batch A C# UTF-8 no-BOM/CRLF/final-CRLF、`git diff --check`、scoped diff、完整
      ChurchReport tests、Release build 及 CCG dual-model review；結果已寫入 `check.jsonl`。

## Phase 3：Batch B — Package01 stor projection consumers

- [x] 盤點 `StorLessonQueryService` 的所有 consumer，分辨只需 projection 的 caller 與要求
      `EntityCollection` 的 legacy caller；已確認 `MemberInfoController.LoadContactStorLessons` 與
      `EquipmentController.LoadEquipmentStorLessons` 是僅讀 `StorLessonProjection` 的候選。`DownloadEquipment`、
      `FeeDownUpLoader`、`EquipmentStatusCalculator` 與 `FindStorLessonId` 的 write-adjacent caller 保持
      temporary-legacy，不能列為 migrated。
- [x] 建立 shared wire/ProductClient/connector contract：課程開始時間與階段名稱經 `lesson` link 以 DTO
      傳遞，三個共用 stor executor 具有相同欄位；錯誤 aliased 型別、page-byte 超限或 branch mismatch
      都 fail closed。因 shared workspace 的 production 投影已由並行工作先存在，初始 connector/DTO
      測試沒有可觀察的 red；此限制已寫入 check 紀錄，不將其宣稱為 red/green 證據。
- [x] 建立 ChurchReport focused contract：Package01 stor query 全程非同步、原樣傳遞 cancellation，DTO
      projection 不呼叫 `RetrieveEntity`，且 A/B 交錯 request 不共享結果。最小 UTC 哨兵回歸測試先實際
      red（台北時區原本投影為 `0001-01-01 08:00`），修正後 green。
- [x] 對兩個 projection-only caller 建立 contract tests，證明 Package01 flag=false 保持 legacy，flag=true
      只走 `RetrieveStorLessonsByContactAsync`，並傳遞 `RequestAborted`；取消會重新擲出而不混入其他
      request/profile 結果或走 legacy fallback。
- [x] 將 `MemberInfoController.LoadContactStorLessons` 與 `EquipmentController.LoadEquipmentStorLessons` 改為
      typed projection；不呼叫 `RetrieveEntity`、不把 DTO 回轉為 `EntityCollection`。仍需 SDK entity 的
      caller 留在 temporary-legacy，並保留 P7.5 blocker。
- [x] 執行 StorLesson focused tests、兩個受影響 test project 與完整 solution regression；結果記入
      `check.md`／`check.jsonl`。
- [x] 完成 encoding、`git diff --check`、source-only scope 與 local review；已經由 CCG self-healing
      runner 嘗試 45 秒限時 Gemini/Claude review。Gemini 的一項極端日期 Warning 已以 red/green 測試修正；
      Claude 未在上限內完成，明確記為「雙模型未完成」。

## Phase 4：後續 read sub-batches 與 P7.4 release gate

- [ ] 對 `ORG-CALL-00005`、`00064`、`00066` 建立 caller-shape inventory；每個不同 response contract
      是獨立 sub-batch。缺 typed contract、CE parity、host evidence 或 rollback owner 時記錄 no-go，
      不以 ToolUtility bridge 假裝完成。
- [ ] 每個 capability 加入/更新 deployment-owned disabled gate 與 rollback document；任何 flag=true
      測試皆只在 local fake executor/client 中執行，不開啟環境設定或 CE 流量。
- [ ] 對 Gate enablement 做 read-only evidence audit：是否已有 durable shared admission authority 或
      verified drain-first non-overlap runbook。若沒有，寫入 exact no-go，保留所有 gates=false。
- [ ] 執行完整 P7.4 focused suites、`dotnet test .\\SpeechMessageProducts.sln --configuration Release --no-restore`、
      `dotnet build .\\SpeechMessageProducts.sln --configuration Release --no-restore`、encoding/CRLF scan、
      `git diff --check`、scope check 和 CCG review。
- [ ] 只有 matrix 所有 consumer rows、required CE/host evidence、capacity gate、zero-reference、soak/
      drain/rollback evidence 都綠燈時才封存 P7.4；否則留下已完成 local batches、未完成 owned rows 和
      precise next child。不得啟動 P7.5 或 P8。

## rollback points

1. 任一測試顯示 flag=false 建立 typed resource、typed fault 污染 model、跨 request/profile 資料混用、
   resource baseline 不回復或 response contract drift：還原該 capability 的未提交程式變更，保留測試與
   task failure record，直到問題可由本機修正。
2. 任一 CE/gate evidence 顯示 capacity 無法證明、timeout、ambiguous 或 no-go：不重試、不改 flag，
   記錄去識別化 blocker，繼續不相依的 local-only batch。
