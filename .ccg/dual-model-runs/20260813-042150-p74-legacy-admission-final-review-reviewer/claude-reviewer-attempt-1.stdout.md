## P7.4 Legacy Admission Boundary — 最終審查報告

**審查範圍**：commit `0ee9eeab`（"feat(churchreport): add p74 legacy admission boundary"，34 files changed）。此 commit 涵蓋 `.trellis/tasks/08-13-p74-legacy-gateway-admission/` 及其相關 `08-12`/`08-05` parent 記錄。

> **程序註記**：審查開始時該變更集是工作樹中的未提交 diff；審查進行中，同一 worktree 上的另一個並行流程已將其提交為 `0ee9eeab`。內容經比對與我逐檔案審查時完全一致，因此本報告針對該 commit 的實際 diff 作結論，不影響審查有效性。

### 逐項不變量驗證

| 不變量 | 驗證方式 | 結果 |
|---|---|---|
| 無 feature flag / CE mutation / 流量切換 / P7.5 / P8 啟用 | grep diff 全文比對 `Package01FeeReadsEnabled…true`、`CE mutation`、`traffic cutover` 等字樣，逐一追蹤上下文 | 通過；僅有既有 `enabled` 變數傳遞路徑（非新增硬編碼 true） |
| `Package01FeeReadsEnabled=false`（appsettings + DedicatedGateway launch profile） | 直接讀取 `appsettings.json`、`appsettings.Development.json`、`launchSettings.json` | 三處皆為 `false`；`launchSettings.json` diff 證實把 DedicatedGateway profile 從 `"true"` 修正為 `"false"`，並有 `CrmConnectionEmbeddedProfileMapperTests.cs` 迴歸測試鎖定此值 |
| Controller 僅計量已註冊 local legacy 工作，不宣稱 durable/全覆蓋/取消同步 I/O | 讀 `LegacyToolUtilityDrainController.cs`、`DonationFeeQueryService.cs`、runbook 全文 | 通過；程式碼與文件（含 XML 註解、runbook 6 步驟）皆一致標注「process-local observation，非 Organization-level proof」 |
| 無 request/session/profile/credential/CRM entity 保留；有界 deterministic cleanup | 檢查 controller 欄位（僅 enum/計數器/`TaskCompletionSource`）、lease 生命週期、`DisposeAsync` bounded timeout（5s）、hosted service stop timeout（30s） | 通過 |
| PID evidence reader 僅重試 Win32 32/33，其餘立即失敗，並在固定期限內 | 讀 `OfficialWorkerControlPlaneAdmissionTests.cs`/`OfficialWorkerProfileExecutorTests.cs` 完整實作：`catch (IOException) when (IsExpectedEvidenceContention)`，`HResult & 0xFFFF is 32 or 33`，外層 `while (elapsed < 5s)` | 通過；新增迴歸測試 `Read_captured_process_id_waits_for_writer_handle_release` 直接重現該競態並斷言等待行為 |
| UTF-8/CRLF、任務聲明無誇大 | 對變更檔案做 byte-level 檢查（BOM、LF/CRLF 計數一致性、結尾 CRLF）；並比對 `check.md`/`implement.md`/`task.json` 文字與實際 diff | 通過；文件對雙模型降級（Gemini 可用、Claude quota-blocked）誠實記錄，未誇大為完整雙模型審查 |

### DI/plumbing 驗證
`LegacyToolUtilityDrainController` 以單一 `AddSingleton` 註冊，`LegacyToolUtilityAdmissionHostedService` 為唯一 lifecycle owner；`DonationFeeQueryService`/`DonationDedicationFeeFormService`/`DonationPaymentManager`/`InMemoryDataContextSmallGroup` 一路以可選參數傳遞同一 singleton 參考，未見任何手動 `new` 出第二份 controller 或保存 request-scoped 狀態。`AcquireLegacyFeeLeaseAsync` 在 intake 已停時對 legacy fee 呼叫 fail-closed（`InvalidOperationException`），並有對應測試 `Stopped_legacy_intake_rejects_before_toolutility_call` 覆蓋。

### Critical
無。

### Warning
無。

### Info

1. **`Cancelled_drain_does_not_release_another_workload_lease` 測試命名與實際覆蓋範圍略有落差**（`ChurchReport.MemberInfo.Tests/Services/LegacyToolUtilityDrainControllerTests.cs:85`）：`LegacyToolUtilityWorkload` enum 目前只有 `Package01FeeRead` 一個值，此測試實際驗證的是「取消 drain waiter 不影響既有 lease」，而非兩個不同 workload 間的隔離。與 prd.md 第 5 項「A/B isolation」的字面意義不完全對應，但屬程式庫既有的 A/B 測試慣用語（見 `StorLessonQueryServiceAsyncTests.cs` 等），非蓄意誇大證據，不影響安全結論。

2. **PID sharing-violation 迴歸測試僅存在於一處**：`IsExpectedEvidenceContention` 修正同步套用到 `OfficialWorkerControlPlaneAdmissionTests.cs` 與 `OfficialWorkerProfileExecutorTests.cs` 兩檔（helper 重複），但專門重現競態的迴歸測試 `Read_captured_process_id_waits_for_writer_handle_release` 只加在前者。後者理論上共用相同風險面，可考慮補一份等價測試，但不阻礙本次驗收。

### 最終結論

**PASS（無 Critical / 無 Warning）**。本 child 的 repository-side 變更（fail-closed drain controller、hosted lifecycle、no-secret validator、drain-first runbook、encoding 自我驗證的 contract test、DedicatedGateway 旗標迴歸修正）與 `08-13-p74-legacy-gateway-admission` 的 prd/design/implement 聲明一致，未發現 flag 啟用、CE mutation、流量切換或證據誇大。P7.4 gate 依然正確維持 `false`，符合任務要求「不建議啟用旗標或變更外部部署狀態」的邊界。

---
SESSION_ID: 01afa130-5668-45c9-8a51-df5303928cfd
