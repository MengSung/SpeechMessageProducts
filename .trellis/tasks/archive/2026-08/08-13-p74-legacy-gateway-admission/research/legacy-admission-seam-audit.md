# P7.4 Legacy Admission Seam 唯讀盤點

> 日期：2026-08-13
> 範圍：repository source only；零 CE、零設定變更、零 deployment 操作。

## 結論

現在是 feature-gate enablement 的 **NO-GO**。現有 ToolUtility legacy path 沒有進入 durable
SQL admission / host-slot authority，不能僅以 SQL coordinator 的獨立測試或單一 Package01 adapter
推論已取得組織總容量安全性。

## 呼叫鏈證據

1. `DonationFeeQueryService.FillFeeListAsync` 在 Package01 關閉時直接呼叫
   `_utility.RetrieveDedicationFeeByDateFetchXml`。
2. `ToolUtilityClass.Query1` 轉交 facade，`FeeService.RetrieveDedicationFeeByDateRange` 對
   constructor-captured `_organizationService` 直接執行 `RetrieveMultiple`。
3. `ToolUtilityFactory` 是 static singleton；`ToolUtilityClass` 建構時建立並長期保留 CRM service，
   且保有 facade、configuration 與 process-global trace listener/file。
4. ChurchReport DI 所註冊的 `CrmConnectionPool` 為不同的 local `SemaphoreSlim` / timer pool，
   fee path 不會從它 acquire；它不含 canonical Organization、SQL lease、epoch 或 fencing。

## 可行但不足的 seam

在 `DonationFeeQueryService` 的唯一 Package01 legacy fee invocation boundary 建立專屬 adapter，
可以在每次同步 fee read 前，以 server-owned `DispatchEnvelope` 向由 registry 取得的
`IOrganizationAdmissionManager` acquire permit，並在 `finally` / `await using` 釋放。這只能提供
該 operation 的 metering。

它不足以開旗標：同步 `IOrganizationService.RetrieveMultiple` 不接收 cancellation token，因此 lease loss
無法中斷 already-dispatched call；同一 singleton 的其他 ToolUtility call 仍可未受控地與 Gateway 並行。

## 必要的 enablement 證據

下列兩條路徑擇一，而且都需 actual deployment owner 的 evidence：

1. 所有同一 Organization 的 legacy 與 Gateway path 採用相同 canonical
   `OrganizationAdmissionPlan`、durable SQL coordinator、namespace、epoch、digest，且有 aggregate
   capacity / fencing / drain 測試及 read-back；或
2. 在 deployment 實際執行並保存 evidence 的 drain-first / non-overlap runbook：停止所有 legacy
   intake、等待 bounded drain、驗證無 legacy active work、讓 Gateway 對同一 Organization 建立 durable
   readiness，再受控 smoke；rollback 要先 drain typed work 再恢復 legacy intake。

## 不可採用的捷徑

- 把 ToolUtilityFactory、CrmConnectionPool 或新增 in-memory coordinator 當 durable authority。
- 接受 request / caller 指定 routing、profile、endpoint、credential 或 Organization。
- 在同一 request 內 fallback / dual-write / 自動重試。
- 將 adapter 或 unit test 視為 external non-overlap proof。
