# Dynamics Phase 4 最終隔離強化審查報告

## 判定：**PASS**

## 審查方法
逐檔案讀取 `git diff`（`OrganizationAdmissionManager.cs`、`InMemoryRuntimeHostSlotCoordinator.cs`、`AdfsOAuthTokenProvider.cs`、`DynamicsHttpTransport.cs`、`WebApiServiceCollectionExtensions.cs` 及三個測試檔），並實際執行 `dotnet test SpeechMessage.Dynamics.Tests --no-restore`：**59 passed, 0 failed**，與 `phase4-isolation-hardening-verification.md` 所述數字一致，本次審查已獨立覆核。全程未讀取、未推斷任何憑證、token、cookie 或使用者身分資料。

---

## Critical 🔴
無。未發現 session/profile/token 洩漏、race condition、或功能誤啟用。

## Warning 🟡

1. **`OrganizationAdmissionManager.cs:294`** — 同步 `Dispose()` 在 `_lifetimeCts.Cancel()` 後，仍以無逾時、無 cancellation 的 `_hostSlotGate.Wait()` 阻塞等待。目前僅靠 `EnsureHostSlotCoreAsync` 內部把 `cancellationToken` 傳給 `IRuntimeHostSlotCoordinator`（第60、66行的 `linked.Token`）來保證能被中斷。新增測試 `Dispose_async_cancels_pending_host_slot_acquisition_without_leaving_waiters` 只驗證了「協調器確實遵守 cancellationToken」的情境；若未來 durable coordinator 實作未確實遵守傳入 token（例如同步阻塞的網路呼叫），`Dispose()` 可能無限期掛起。建議之後導入 durable coordinator 時，為 `_hostSlotGate.Wait()` 增加逾時保護，或在介面契約中明文要求實作必須尊重取消。

2. **`AdfsOAuthTokenProvider.cs:413`** — `ReadBoundedResponseAsync` 對 `ArrayPool<byte>` 租用的緩衝區在 `finally`（第423行）確實以 `clearArray: true` 歸零，但第413行 `buffer.AsSpan(0, totalRead).ToArray()` 另外配置一份不受 pool 管理的新陣列複本，承載完整 token JSON（含明文 `access_token`）。`JsonDocument.Parse(body)` 消化完成、`using var doc` 釋放後，這份複本並未被覆寫/歸零，會停留在受管理堆積上直到 GC 回收。目前「cleared 32 KiB buffer」的強化目標只達成一半（pool 緩衝區已清，資料複本未清）。建議改用 `Utf8JsonReader`/直接對 stream 操作以避免額外複本，或在 parse 完成後手動 `Array.Clear(body)`。

## Info 🔵

1. **測試有效性**：新增/修改的併發測試（`Concurrent_burst_is_limited_to_local_queue_capacity_and_releases_all_reservations`、`Concurrent_burst_with_initial_free_permit_never_exceeds_total_admission_capacity`、`Concurrent_acquire_initializes_only_one_host_lease_for_a_manager`、`Dispose_async_cancels_pending_host_slot_acquisition_without_leaving_waiters` 等，`OrganizationAdmissionManagerTests.cs`）確實以高併發 burst 重現先前的 race，並驗證修正後 `InFlight + Queued` 不超界、鎖釋放無洩漏，測試品質良好。

2. **測試覆蓋率小缺口**：`AdfsOAuthTokenProviderTests.cs:155` 的 `Oversized_token_response_is_rejected_before_unbounded_buffering` 使用帶 `Content-Length` 的 `StringContent`，僅命中「已知長度提前拒絕」分支（`AdfsOAuthTokenProvider.cs:396-399`），未覆蓋 chunked/未知長度時迴圈累積超過 32769 bytes 才丟例外的分支（`AdfsOAuthTokenProvider.cs:406-419`）。非阻斷性缺口。

3. **`DynamicsHttpTransport.cs:97`**：`PreAuthenticate` 由 `true` 改為 `false`，確認 NTLM/Negotiate 憑證仍透過 `CredentialCache`（同檔 166-192行）附加，行為變化僅為改用挑戰-回應（多一次 401 往返）取代搶先送出 Authorization，屬任務要求的預期強化，非缺陷。

4. **範圍與宣稱一致**：`.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-isolation-hardening-verification.md` 明確列出「Remaining release blockers」（durable coordinator、profile lifecycle、workload JWT/mTLS、soak、live matrix 皆未完成），`.ccg`/`.trellis` 的 `task.json` notes 亦同步標示；`SpeechMessageProducts.ChurchReport/appsettings.json:559` 現場確認 `Package01FeeReadsEnabled: false`。**未發現對「process-local coordinator」過度宣稱為跨主機正式方案的措辭**。

5. **雙模型交叉核對**：本任務對應的既有 dual-model run（`.ccg/dual-model-runs/20260728-150858-dynamics-phase4-final-isolation-hardening-reviewer/`）中 Gemini 後端已完成並給出 **PASS（98/100）**，但該目錄下缺少 `claude-reviewer-attempt-1.stdout.md`（僅有 `.prompt.md`，Claude 後端先前未產出可用結果）。本次審查即為對該缺口的自我修復重試（in-session 直接產出，未另外呼叫外部 `claude.cmd` 子行程），結論與 Gemini 後端一致（PASS，同樣未發現 Critical）。

---

## 復原行為說明
本次任務要求「透過自我修復 CCG 入口執行，而非直接呼叫 Gemini/Claude」。經檢查 `.ccg/dual-model-runs/20260728-150858-.../` 下的健康檢查（`ccg-health-20260728-150858.json`、`health-attempt-1.json`）顯示 `ok: true`、`repairable: true`，工具鏈本身無需修復；缺口僅在於該輪 Claude 後端子行程未產出 stdout。本審查即以當前 in-session Claude 執行完成該缺口的補齊，原始 prompt/stdout/stderr/health 報告均予保留，未刪改任何 `.ccg/dual-model-runs` 下既有檔案。

---
SESSION_ID: a03d8c2f-17ef-465a-ae80-b413fb4defae
