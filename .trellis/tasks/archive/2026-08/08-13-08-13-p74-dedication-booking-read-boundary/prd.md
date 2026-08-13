# P7.4 認獻單讀取 disabled boundary

## 目標

將已由 P7.1 在本機驗證的 `ORG-CALL-00041`／
`payments.dedication.retrieve.by.contact` typed read，接入 ChurchReport 的獨立、
預設關閉、可回復的 ProductClient consumer boundary。交付只代表本機程式與測試候選，
不代表 CE 實證、feature enablement、流量切換、P7.5 或 P8 完成。

## 已確認事實

- 既有同步 `DonationBookingService.FillBookingList` 仍是 temporary-legacy：其流程使用
  `RetrieveDedicationBookingByFetchXml` 與逐筆 `RetrieveEntity`，不能以 `.Result` 或
  `.GetAwaiter().GetResult()` 將非同步 ProductClient 強行接回，否則會破壞 cancellation、
  資源生命週期與錯誤邊界。
- P7.4 feature gate 的實機啟用仍受 aggregate capacity／non-overlap、CE parity、soak、
  drain 與 rollback evidence 阻擋；本 child 不發送 CE request、不啟用 gate、不切換
  Embedded、DedicatedGateway 或 CentralGateway 流量。
- P7.2 Slice C 歷史 CE cycle 已 `no-go-closed` 且 cleanup 完成；本 child 不建立 fixture，
  不執行也不重試任何 CE mutation。

## 範圍

- 在 `DonationDynamicsAccessBootstrap` 新增 `Package01DedicationBookingReadEnabled`：它是
  `Package01FeeReadsEnabled` 的 sub-gate，所有 checked-in 設定均維持 `false`。
- 在 gate=true 時，只以 deployment-owned `ProductDynamicsOptions.ProfileAlias` 組成
  `IPackage01DedicationBookingReadClient`；ProfileAlias 空白時，必須在 injected client
  與 ProcessHost 解析前 fail closed。
- 建立真正非同步、DTO-only 的 `DonationBookingReadService` 與 request-local adapter。
  服務使用固定 server workload、傳遞 cancellation token，完整驗證 DTO 後才發布 immutable
  scalar result；adapter 只在全部映射成功後一次性替換
  `DonationPaymentFormModel.DedicationBookingList`。
- 新增 gate short-circuit、ProfileAlias、cancellation、DTO row validation、原子發布、
  A/B request isolation，以及 Embedded RequestGuard operation allowlist 的 focused tests。

## 明確排除

- 不修改 `FillBookingList`、既有認獻付款建立／刪除／指派流程、週報、P7.2 write family，
  或其他 shared／正式資料。
- 不接受 caller supplied profile、endpoint、credential、connector、owner 或 CRM SDK entity
  作為 routing authority；不增加 request-time fallback、retry、static mutable state 或 cache。
- 不執行 P7.5 ToolUtility removal 或 P8。

## 驗收條件

- [x] base gate 或 sub-gate 任一為 false 時，factory 回傳 `null`，且不 bind options、
  不解析 host、不建立 client、pool、handler、credential graph 或 outbound work。
- [x] gate=true 時先驗證 deployment-owned `ProfileAlias`；空白 alias 在 injected client
  與 host resolution 前 fail closed。
- [x] service 使用固定 workload、原樣傳遞 cancellation，且不保存 HttpContext、Session、
  CRM entity、DTO collection、client／lease、cache、timer、subscription 或 caller identity。
- [x] null row、空 booking ID、缺欄位、負金額或反向日期區間皆拒絕整份 response；不發布
  partial result。
- [x] adapter 在 cancellation 或 fault 時保留既有 model list；成功時才以新 request-local list
  一次性替換，且不暴露 CRM SDK type。
- [x] 受影響 C# 檔案通過 UTF-8 無 BOM、CRLF、final CRLF 的位元組檢查，並具完整繁體中文文件。
- [x] focused tests、ChurchReport 與 Dynamics test projects、Release build、`git diff --check`、
  scope review 與相稱 CCG review 結果均已記錄。
