# Dynamics Phase 4 隔離強化程式碼審查

## 審查方法
已完整檢視 `git diff`（7 個已追蹤檔案 + 4 個新增檔案）、`InMemoryRuntimeHostSlotCoordinator.cs`、`OrganizationAdmissionManager.cs` 全文、`DynamicsHttpTransport.cs`、`AdfsOAuthTokenProvider.cs`、`WebApiServiceCollectionExtensions.cs`，並在本地執行：

```
dotnet test SpeechMessage.Dynamics.Tests --no-restore
已通過! - 失敗: 0，通過: 53，總計: 53
```

與 `.trellis/.../phase4-isolation-hardening-verification.md` 聲稱的 53/53 一致。未修改任何檔案、VM 或遠端系統。

## 授權範圍核對
- `DynamicsAccess:Package01FeeReadsEnabled` 於 `SpeechMessageProducts.ChurchReport/appsettings.json:559` 仍為 `false`；`.ccg` 任務筆記也明示保持 false。**未發現消費端遷移或功能開啟**。
- 未發現密碼、私鑰、token、cookie、瀏覽器儲存、Authorization header、原始 session id 或使用者識別被記錄、保留或回吐於新增文件（`docs/superpowers/plans/...`、`.trellis/.../phase4-isolation-hardening-verification.md`）中；`AdfsOAuthTokenProvider.cs:141` 錯誤訊息已將回應 body 截斷至 300 字元，未整段外洩。
- 範圍符合「僅限本機/單進程強化，非 durable 協調」的自我聲明，未見誇大為分散式協調的說法（見下方 Info-1）。

## 總體結論：**PASS**（僅限本次窄範圍的本機強化增量；不構成 Package01 或多 Gateway 正式上線放行）

---

## Critical 🔴
無。

## Warning 🟡

**W1 — `_lease` 欄位在併發 `EnsureHostSlotAsync` 呼叫下仍缺乏同步保護（既有問題，非本次 diff 引入，但與本次強化直接互動）**
檔案：`SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs:66-105`（尤其 `line 100 _lease = lease;` 與 `line 77 await DisposeLeaseAsync()`）

`OrganizationAdmissionManager` 以 Singleton 註冊（`WebApiServiceCollectionExtensions.cs:93`），每次 `AcquireAsync` 都會呼叫 `EnsureHostSlotAsync`，但此方法讀寫 `_lease` 完全沒有鎖保護。冷啟動或續租窗口（到期前 30 秒內）若有多個並發請求同時進入，會各自呼叫 `TryAcquireAsync`/`TryRenewAsync`，因為 coordinator 端已用相同 `hostInstanceId` 視為續租而各自成功，產生多個 `RuntimeHostSlotLease` 物件、最後只有一個被指派給 `_lease`，其餘物件被丟棄且從未呼叫 `ReleaseAsync`／`DisposeAsync`——這正是任務要求核對的「lease 不得逾生命週期保留」情境。雖然 coordinator 內部記錄的 fencing token 最終仍指向同一把 slot（不會造成永久性槽位洩漏），但 `_lease` 可能落後於 coordinator 實際 token，導致後續 `TryRenewAsync` 恆定失敗、觸發不必要的重新取得，屬於自我修復但有效能/日誌雜訊成本的競態。

本次新增的 `var lease = _lease;`（`AcquireAsync` line 143）修正了同一次呼叫內先前「先判斷 null 再重新讀取 `_lease.FencingToken`」的 TOCTOU（原本可能因並發改變而讀到不一致值甚至 NRE），這是本次診斷範圍內確實的正向修復，值得肯定；但其上游 `EnsureHostSlotAsync` 的寫入路徑本身未同步，仍是殘留風險。

- **修復建議（最小）**：在 `EnsureHostSlotAsync` 內對 `_lease` 的讀取判斷、`TryRenewAsync`/`TryAcquireAsync` 呼叫與寫回，用既有 `_gate`（或新增專屬鎖）包起來，避免多執行緒重入同一段續租/取得邏輯。
- 未被本次新增測試覆蓋（`Concurrent_host_slot_acquire_...` 測試只驗證 coordinator 本身，未驗證 `OrganizationAdmissionManager._lease` 欄位競態），建議補一個「多執行緒併發呼叫 `EnsureHostSlotAsync`」的迴歸測試。

## Info 🔵

**I1 — 「不可宣稱分散式協調」核對：通過**
`InMemoryRuntimeHostSlotCoordinator.cs:1-10` 的教學註解明確聲明 `IsDurable = false`、僅保證同進程內生效、正式多 Gateway 部署禁止當作最終方案。新加入的 `lock (_sync)`（`InMemoryRuntimeHostSlotCoordinator.cs:21,41,89,117`）只是讓單進程內的操作變成原子化，文件與程式碼皆未誇大為跨進程/跨機器協調，符合要求。

**I2 — `GetSnapshot()` 非原子讀取**
`OrganizationAdmissionManager.cs:253-267` 讀取 `_queued`、`_inFlight.CurrentCount`、`_lease` 未持 `_gate` 鎖，屬純觀測性 metrics，可能出現短暫不一致快照，非功能性錯誤，無需在本增量修復。

**I3 — 具名 `dynamics-adfs-token` HttpClient 未顯式設定 `Timeout`**
`WebApiServiceCollectionExtensions.cs:80-89` 新增了 handler 層強化（cookie/redirect/proxy/decompression/pre-auth），但未如 `AdfsOAuthTokenProvider.CreateHttpClient` 的 fallback 路徑（`AdfsOAuthTokenProvider.cs:383`）般以 `_options.TimeoutSeconds` 限制逾時，工廠建立的 client 沿用 .NET 預設 100 秒。非本次 diff 引入的迴歸，但既然在做「掛起連線」相關強化，建議一併補上 `.ConfigureHttpClient(c => c.Timeout = ...)` 保持兩條路徑一致。

**I4 — 測試以反射讀取私有欄位**
`WebApiServiceCollectionExtensionsTests.cs:79-98` 用反射抓 `HttpMessageInvoker._handler`，在未來 .NET 版本更名時會靜默失效（`Should().NotBeNull()` 會直接讓測試失敗，非靜默，可接受），僅供留意。

---

## 正確性/併發驗證結果（本次改動核心）

以下均已對照程式碼與 53/53 綠測試逐一驗證，**未發現邏輯錯誤**：

1. **`OrganizationAdmissionManager` 原子化准入**（`OrganizationAdmissionManager.cs:49-51,168-182`）：新增 `_totalAdmission = new SemaphoreSlim(LocalMaxInFlight + LocalQueueCapacity, ...)`，取代舊有非原子的 `_inFlight.CurrentCount == 0 && _queued >= LocalQueueCapacity` 判斷（該判斷在瞬時視窗下可讓 `_queued` 無界增長，違反類別頂端教學註解「queue 滿了直接拒絕，不可無限成長」）。新邏輯在同一把 `_gate` 鎖內完成 workload 上限檢查、`_totalAdmission.Wait(0)` 保留、計數器遞增，具原子性；經 32-caller burst 併發測試證實（`OrganizationAdmissionManagerTests.cs` 新增兩個測試，含 16 輪重複），`InFlight + Queued` 恆不超過 `LocalMaxInFlight + LocalQueueCapacity`。
2. **取消/逾時/例外路徑釋放**：`catch (OperationCanceledException)`、通用 `catch`、`if (!acquired)`、permit 建構失敗後的 `catch` 均呼叫 `ReleaseReservation`/`ReleasePermit`，經追蹤未發現任一路徑遺漏釋放 `_totalAdmission`、`_inFlight` 或 `workloadCounts`，也未發現雙重釋放（`SemaphoreFullException` 防護僅作為 defence-in-depth）。
3. **`InMemoryRuntimeHostSlotCoordinator` 序列化**（`InMemoryRuntimeHostSlotCoordinator.cs:41,89,117`）：`TryAcquireAsync`/`TryRenewAsync`/`ReleaseAsync` 均在 `lock (_sync)` 內完成「purge 過期 → 讀 → 判斷 → 寫」，消除舊版本 check-then-act 競態；`PurgeExpired` 現為 namespace-無關的全域清理，邏輯正確（不再需要 namespace 參數）。新測試 `Concurrent_host_slot_acquire_allows_only_one_lease_and_releases_capacity`（64 併發只拿到 1 把租約）與 `Expired_host_slot_cannot_be_renewed_or_resurrected`（過期租約無法續租或復活）均通過。
4. **HTTP handler 強化**（`DynamicsHttpTransport.cs:97` `PreAuthenticate: true→false`；`WebApiServiceCollectionExtensions.cs:80-89` 新增具名 handler）：`UseCookies/AllowAutoRedirect/UseProxy/AutomaticDecompression/PreAuthenticate` 均已鎖定為安全值，兩條路徑（ADFS token 與 CRM WebApi）一致，且有對應測試驗證 handler 實際套用值（非僅檢查程式碼字面）。

---

## 本次窄範圍之外仍存在的上線阻擋項（Release Blockers，非本增量修復範圍）

以下引用自 `.trellis/.../phase4-isolation-hardening-verification.md:63-83`，經審查確認其陳述與程式碼現狀一致，**在其修復並驗證前不得開啟 Package01 或任何正式多 Gateway 部署**：

1. 尚無 durable 跨主機協調器（epoch/fencing/quarantine），目前僅單進程有效（見 I1）。
2. Profile 產生隔離、replace-and-drain、非同步 runtime 確定性釋放尚未實作。
3. ADFS/CRM 路徑尚未有全面的有界回應串流與 token/body redaction。
4. Gateway workload 尚未有 JWT/mTLS 驗證，呼叫端仍可自控 workload subject 資料。
5. 缺兩代 profile、reload/drain、cancellation/fault、socket/timer/heap soak、Gateway+Embedded 聚合容量測試組。
6. 缺已驗證的 CE 8.2/9.1 正式登入 live smoke matrix 與雙副本容量驗證。

另補充本次審查新發現、應併入待辦：
7. `OrganizationAdmissionManager.EnsureHostSlotAsync` 對 `_lease` 欄位的併發存取仍缺鎖保護（見 Warning W1），建議在啟用任何多副本/高並發正式流量前一併修復並補上迴歸測試。

## 最小修復建議彙總
- （建議，非阻擋本增量）為 `EnsureHostSlotAsync` 加鎖，避免 `_lease` 併發覆寫與孤兒 lease 物件（W1）。
- （可選）`dynamics-adfs-token` 具名 HttpClient 補上與 `_options.TimeoutSeconds` 一致的逾時設定（I3）。

以上為本次 Phase 4 本機隔離強化的完整審查結果；核心的准入原子化、host-slot 序列化與 HTTP handler 加固均正確且經測試驗證，**PASS**，但不代表已具備正式多 Gateway 上線或 Package01 開啟資格。

---
SESSION_ID: 143f2667-146b-440f-aa75-8b49267020ba
