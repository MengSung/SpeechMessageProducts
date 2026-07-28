# Dynamics Phase 4 最終隔離強化審查報告（Claude Reviewer）

## 審查範圍確認
`git diff` 顯示目前唯一未提交變更為 `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`（69 行新增 / 34 行刪除），內容為將 token 回應解析由 `JsonDocument.Parse` 改為 `Utf8JsonReader` 直接解析 `byte[]`，並在 `finally` 呼叫 `CryptographicOperations.ZeroMemory` 歸零緩衝區。Admission/Coordinator/Manager/HTTP handler 強化（項目 1–3、4 的其餘部分）已於前一個 commit `5e675d14` 落地，本次審查依任務要求一併核對其與本增量的一致性，但變更本身以本次未提交 diff 為準。

## 總體結論：**PASS**
（僅適用本次窄範圍本機隔離強化增量；不構成 Package01 或多主機正式上線放行）

---

### Critical 🔴
無。

### Warning 🟡

1. **`RuntimeHostSlotLease.Dispose()` 同步路徑為 fire-and-forget，未等待釋放完成**
   - 檔案：`SpeechMessage.Dynamics.WebApi/Capacity/IRuntimeHostSlotCoordinator.cs:46-54`
   - `Dispose()` 內執行 `_ = _coordinator.ReleaseAsync(this, CancellationToken.None).AsTask();`，未 `await`。若日後有呼叫端改用同步 `using`（而非目前程式碼庫唯一使用的 `await using`／`DisposeAsync`），釋放可能尚未完成呼叫端就已離開，且該 Task 若拋例外會成為未觀察例外。目前 `OrganizationAdmissionManager.DisposeLeaseUnderHostSlotGateAsync()`（`OrganizationAdmissionManager.cs:416-426`）僅呼叫 `DisposeAsync()`，故此路徑目前未被實際觸發，屬潛伏風險而非現行缺陷。
   - 建議：之後若保留 `IDisposable`，讓同步路徑至少以 `.GetAwaiter().GetResult()` 或 blocking-safe 方式完成釋放，或直接移除同步介面僅留 `IAsyncDisposable`。

2. **Token 回應緩衝區歸零後，解析出的字串副本仍會殘留於 Managed Heap**
   - 檔案：`AdfsOAuthTokenProvider.cs:142-153`（`body` 歸零）、`AdfsOAuthTokenProvider.cs:400-459`（`ParseTokenResponse` 以 `reader.GetString()` 產生新字串）
   - `CryptographicOperations.ZeroMemory(body)` 只能清除 `byte[]` 副本，`ParseTokenResponse` 解出的 `accessToken`/`refreshToken` 屬 .NET 不可變字串，無法主動歸零，需等 GC 回收。這是 .NET 受控記憶體模型的固有限制，非本次程式碼的邏輯缺陷，且與任務描述「read successful token documents into a cleared 32 KiB maximum buffer」的字面範圍一致（只承諾清緩衝區，不承諾清字串）。列為 Warning 是提醒此非完整的敏感資料清除保證，而非要求本增量修正。

### Info 🔵

1. **修正 Gemini 審查中的一項誤判**：Gemini 報告（`.ccg/dual-model-runs/20260728-152209-.../gemini-reviewer-attempt-1.stdout.md:24-27`）指稱 `EnsureHostSlotAsync` 讀寫 `_lease` 欄位「並未持有任何鎖」，可能導致多執行緒同時進入 `TryAcquireAsync`/`TryRenewAsync` 產生孤兒租約。經核對程式碼，`EnsureHostSlotAsync`（`OrganizationAdmissionManager.cs:57-72`）在進入 `EnsureHostSlotCoreAsync` 前已透過 `_hostSlotGate`（`SemaphoreSlim(1,1)`）序列化整個租約讀寫流程，`_lease` 的所有寫入（`EnsureHostSlotCoreAsync` 第 117 行、`DisposeLeaseUnderHostSlotGateAsync` 第 424 行）皆在持有該鎖時發生。既有測試 `OrganizationAdmissionManagerTests.cs:301-350`（`Concurrent_acquire_initializes_only_one_host_lease_for_a_manager`）以 16 個併發 worker 實測並斷言 `coordinator.AcquireCalls.Should().Be(1)`，直接證偽該項 Warning。此為 Gemini 端的誤判，非本增量之實際缺陷。

2. **Admission 原子化與資源釋放正確**：`_totalAdmission`/`_inFlight` 雙 semaphore 搭配 `_gate` 鎖（`OrganizationAdmissionManager.cs:174-268`）正確地在逾時（`OperationCanceledException` 非取消請求分支）、取消、例外、以及 permit 釋放（`ReleasePermit`/`ReleaseReservation`, 第 332-414 行）等所有路徑釋放預約，未見洩漏。

3. **`AcquireAsync` 中 shutdown 取消被標記為 timeout（次要語意瑕疵）**：`AdmissionManager.cs:214-218` 的 `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` 分支代表 `_lifetimeCts`（manager 關閉）觸發取消，卻沿用下方 `if (!acquired)` 分支回傳 `AdmissionTimeout`「Timed out waiting for local in-flight capacity.」，語意上把「manager 正在關閉」誤標為逾時。不影響資源釋放正確性，僅影響可觀測性／除錯訊息精確度。

4. **HTTP handler 安全設定確認**：`WebApiServiceCollectionExtensions.cs:80-89` 與 `DynamicsHttpTransport.cs:92-103` 均正確停用 cookies/redirect/proxy/decompression/pre-authenticate；ADFS token client 使用短生命週期 `HttpClient`（`AdfsOAuthTokenProvider.cs:338-363`），每次請求後於 `finally` 明確 `Dispose()`（第 154-157 行）。

5. **回應大小界限與錯誤不回傳 body 確認**：`ReadBoundedResponseAsync`（第 365-398 行）以 `ArrayPool<byte>` 租用並限制 32KiB + 1，超限拋例外且 `finally` 以 `clearArray: true` 歸還；失敗回應僅回傳狀態碼與端點（第 135-140 行），測試 `Token_endpoint_error_does_not_retain_or_echo_response_body` 與 `Oversized_token_response_is_rejected_before_unbounded_buffering` 驗證有效。

6. **測試覆蓋落差**：新的 `Utf8JsonReader` 手寫解析器（`ParseTokenResponse`）針對「非 JSON 物件」「屬性非字串型別」「巢狀結構」等 malformed 輸入路徑（第 403-425 行的多個 `throw new InvalidOperationException("... malformed.")`）目前無對應單元測試，僅有正常路徑與超量體積測試覆蓋。建議後續補上至少一個 malformed-body 測試，以鎖定解析器的錯誤處理契約。

7. **Package01FeeReadsEnabled 旗標與範圍聲明確認**：`SpeechMessageProducts.ChurchReport/appsettings.json:559` 仍為 `false`；`docs/superpowers/plans/2026-07-28-dynamics-phase4-isolation-hardening.md:18,185` 明確聲明「No product feature flag changes」且要求驗證旗標維持 `false`。`InMemoryRuntimeHostSlotCoordinator.cs:1-25` 之 `IsDurable => false` 與檔頭註解也未誇大宣稱為跨主機方案，符合安全邊界約束。

### 本增量範疇外之既有 Release Blockers（未變更）
- Durable 多主機協調器（epoch/fencing/quarantine）
- Profile 生命週期隔離（replace-and-drain）
- Gateway workload 身份驗證（JWT/mTLS）
- 全量 Soak/Fault 測試與正式環境雙 CE 版本 live-matrix 驗證

---

## 與 Gemini 審查的差異摘要
Gemini 判定 PASS 且列出兩項 Warning；其中「`_lease` 缺乏同步保護」一項經程式碼與既有併發測試核對後，判定為**誤判**（應予駁回，理由見 Info #1）。另一項「token 字串殘留」判定與本審查一致，但本審查將其定性為 .NET 受控記憶體模型固有限制而非本增量缺陷，並補充了解析器測試覆蓋落差與 `RuntimeHostSlotLease.Dispose()` 潛伏風險兩項 Gemini 未提及的發現。

**最終判定：PASS**（本地隔離強化範疇內；不得作為 Package01 啟用或多主機正式上線之依據）

---
SESSION_ID: e7e549d5-7105-4a54-b0b0-5f55a1067f39
