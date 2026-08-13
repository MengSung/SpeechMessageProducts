# ORG-CALL-00063 週日出席寫入家族：唯讀 source audit

## 直接 production caller

- `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:202`
  `PersonalQrCodeUtility.SetupQrCodeIdString` 呼叫
  `ToolUtilityClass.RetrieveMeetingStatisticsByFetchXml(m_Sunday)`，取第一筆 raw
  `EntityCollection` ID，再於 `:205` 重新取得 `new_meeting_statistics` SDK Entity。
  它隨即進入 `SigningMeetingStatistics`，不是獨立 read consumer。
- HTTP 入口為 `SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:406`
  `PersonalQrCodeGetLineId`。
- `SundayQrCodeUtility.SetupQrCodeIdString` 不呼叫 ORG-CALL-00063 的日期查詢，而是從
  QR locator 直接載入 `new_meeting_statistics`；它同樣進入 `SigningMeetingStatistics`，
  因此仍是相同 write-adjacent family。

## 已存在的 Package03 read contract

- `SpeechMessage.Dynamics.ProductClient/SpecialResources/IPackage03SpecialResourceClient.cs:50`
  `RetrieveMeetingStatisticsAsync` 只接受 server-supplied profile/workload 與 UTC midnight Sunday。
- `Package03SpecialResourceClient.cs:158` 固定 dispatch
  `stats.meeting.retrieve.by.sunday`，結果只含 meeting-statistic ID、名稱、created-on、Sunday。
- `Package01OperationRegistry.cs:203` 固定 read-only policy：最多 4 pages、每頁 128 rows、總計
  4096 rows、每頁 64 KiB、總計 256 KiB。
- `Package03Data8SpecialResourceOperations.cs:359` 使用固定 active
  `new_meeting_statistics` QueryExpression、固定欄位與排序，逐頁檢查 cancellation、schema、
  row／byte 上限，任何錯誤不回傳 partial response。

## Mutation graph 與缺口

目前 QR signing 會讀取完整 meeting-statistics／present-record Entity，然後執行：

1. present-record 查詢，必要時 Create；
2. present-record 的簽到／簽退時間與出席欄位 Update；
3. 將第一筆 present-record 設定 meeting-statistics relationship；
4. 讀取其 weekly-report lookup，更新 `new_saved_flag` 觸發週報重算；
5. 某些分支另有 LINE notification 副作用。

現有 static lock 只在單一 process 內，不能作為跨 host／process concurrency authority。
`InMemoryContext`／QR locator 目前保存 caller input，不能視為已證明的 request-local server
authorization boundary。

## 安全結論

目前沒有可立即切換的 DTO-only、gate-false weekly consumer。缺少：

- weekly-specific deployment-owned gate，且必須在 session hydration、locator parsing、client
  composition 與 outbound I/O 前 short-circuit；
- server-derived scanner／target authorization 與固定 UTC Sunday mapping；
- 可涵蓋 dynamic sign-on/sign-off 欄位、relationship 與 weekly-report semantics 的新 typed
  command/result；Package03 read DTO 不足以支援這些 mutation；
- 每個 mutation 的 idempotency、pre-write ledger、exact read-back/reconcile、rollback owner、
  deterministic cleanup、timeout/no-replay 與 A/B isolation。

因此本 family 不能採 read-new/write-legacy，也不能將 DTO rehydrate 成 Entity。下一步應建立
更小的 attendance write child，先只處理一個可由 fresh task-owned fixture 完整建立、讀回與清除的
mutation；若無法證明其授權與回復邊界，交付 precise local no-go，不執行 CE。

## 相關測試

- `SpeechMessage.Dynamics.Tests/SpecialResourceProductClientTests.cs:114`
- `SpeechMessage.Dynamics.Tests/OnPremiseData8ConnectorClientFactoryTests.cs:609`
- `SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs:585`
- `SpeechMessage.Dynamics.Tests/Data8ProfileOperationExecutorTests.cs:614`
- `SpeechMessage.Dynamics.Tests/Package01OperationRegistryTests.cs:267`
- `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs:401`
