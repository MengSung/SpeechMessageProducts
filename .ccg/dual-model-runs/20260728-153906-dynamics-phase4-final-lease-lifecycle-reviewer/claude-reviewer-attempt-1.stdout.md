# Dynamics Phase 4 最終租約生命週期審查報告

## 審查範圍確認

`git status` 顯示目前**沒有真正未提交（unstaged/staged）的原始碼差異**——本回合要求審查的變更已於 `7c12ccb1`（"Dynamics Phase 4 隔離強化最終審查與驗證"）提交。我依 Review focus 對該 commit 的三個原始碼檔案做了完整核對（非 `.ccg` 稽核紀錄檔）：

- `SpeechMessage.Dynamics.WebApi/Capacity/IRuntimeHostSlotCoordinator.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`
- `SpeechMessage.Dynamics.Tests/OrganizationAdmissionManagerTests.cs`

並交叉核對了 `InMemoryRuntimeHostSlotCoordinator.cs`、`OrganizationAdmissionManager.cs`（呼叫端）與 `appsettings.json:559` 的旗標狀態。

---

## 總體結論：**非無條件 PASS**（無 Critical，仍有 2 項 Warning 待確認/記錄為已知風險）

此結論與同批次 Gemini 審查結果一致收斂。

---

## Critical 🔴
無。單機隔離增量範圍內未發現資源洩漏、cross-host 容量外溢、或會實際觸發的死鎖。

## Warning 🟡

1. **`RuntimeHostSlotLease.Dispose()` 的同步阻塞屬於「信任實作」的死鎖地雷，非本次即時可觸發但缺乏防護**
   - **檔案**：`IRuntimeHostSlotCoordinator.cs:46-57`
   - **說明**：`Dispose()` 以 `_coordinator.ReleaseAsync(this, CancellationToken.None).AsTask().GetAwaiter().GetResult()` 同步阻塞等待。目前唯一實作 `InMemoryRuntimeHostSlotCoordinator.ReleaseAsync`（`InMemoryRuntimeHostSlotCoordinator.cs:114-129`）全程同步完成（`lock` + 立即回傳 `ValueTask.CompletedTask`），因此**現況不會死鎖**。但介面契約本身沒有強制未來 durable 實作（Redis/SQL/HTTP coordinator）內部必須對所有 `await` 使用 `ConfigureAwait(false)`；一旦未來實作在有 `SynchronizationContext` 的執行緒（例如 Embedded 端 UI 執行緒）上被同步 `Dispose()`，將產生真實死鎖。
   - **建議**：兩者擇一即可徹底消除風險而非僅靠約定：(a) 在 `IRuntimeHostSlotCoordinator.ReleaseAsync` 加上 XML 文件強制要求實作內部須 `ConfigureAwait(false)`；或 (b) 更穩妥地把 `Dispose()` 內部改成 `Task.Run(() => _coordinator.ReleaseAsync(this, CancellationToken.None).AsTask()).GetAwaiter().GetResult();`，強制在執行緒集區執行，徹底脫離呼叫端的 SynchronizationContext，不再依賴未來實作者的自律。

2. **ADFS token 字串（`AccessToken`/`RefreshToken`）在受管理堆積上無法歸零，僅原始位元組緩衝區被清空**
   - **檔案**：`AdfsOAuthTokenProvider.cs:142-152`（`body` 於 `finally` 以 `CryptographicOperations.ZeroMemory(body)` 歸零）、`AdfsOAuthTokenProvider.cs:400-459`（`ParseTokenResponse` 以 `reader.GetString()` 產生不可變字串）
   - **說明**：`ReadBoundedResponseAsync`（`AdfsOAuthTokenProvider.cs:365-398`）正確以 `ArrayPool<byte>.Shared.Return(buffer, clearArray: true)` 歸零租用緩衝區，`body` 複本也在使用後歸零，這部分做得對。但 `token.AccessToken` / `token.RefreshToken` 一旦以 `.GetString()` 產生即為 .NET 不可變字串，無法主動清除，會殘留在 GC Heap 直到回收。這是 .NET 字串模型的固有限制，非本次改動可完全解決，但仍是真實殘留風險，應明確記錄為「已知、可接受」而非視為已完全緩解。
   - **建議**：維持現狀即可（與前幾輪審查結論一致），但建議在檔頭教學註解補一句明確聲明此殘留限制，避免未來讀者誤以為 token 字串也已被歸零。

## Info 🟢

1. **新增回歸測試的盲點：未驗證例外傳遞路徑，也未在具 `SynchronizationContext` 環境下驗證**
   - **檔案**：`OrganizationAdmissionManagerTests.cs:478-497`（`Synchronous_host_slot_lease_dispose_waits_for_release_completion`）
   - **說明**：測試以 `Task.Run(lease.Dispose)` 於執行緒集區執行，正確證明了同步 `Dispose()` 會阻塞至 `ReleaseAsync` 完成（對應 Warning #1 的「現況安全」結論）。但因 `Task.Run` 本身不帶 `SynchronizationContext`，此測試**無法**證明沒有死鎖風險，只證明了「會等待」而非「不會卡死」。另外也未涵蓋 `ReleaseAsync` 拋出例外時，`Dispose()` 是否正確傳遞例外（目前程式邏輯上會透過 `GetResult()` 正確重新拋出，但沒有對應斷言）。
   - **建議**：可補一個 `ReleaseAsync` 拋例外的測試，斷言 `Dispose()` 會同步拋出同一例外；`SynchronizationContext` 死鎖驗證可留待實際導入 durable coordinator 時再補。

2. **`ParseTokenResponse` 對非字串型別的 `access_token`/`refresh_token` 值缺少 `Skip()` 防護（理論邊界情況）**
   - **檔案**：`AdfsOAuthTokenProvider.cs:427-434`
   - **說明**：當屬性名稱符合 `access_token` 或 `refresh_token`，但其值型別非 `String`（例如惡意或異常的 ADFS 回應把它包成物件/陣列）時，程式碼僅將對應變數設為 `null`，未呼叫 `reader.Skip()`。若該值為巢狀物件/陣列，`Utf8JsonReader` 的游標會停在 `StartObject`/`StartArray`，下一輪 `while (reader.Read() ...)` 會誤讀巢狀內部的 token 而非外層下一個屬性，可能導致解析邏輯錯亂（多數情況下最終仍會因游標不一致而拋出「malformed」例外，但不保證）。正常 ADFS 回應中 `access_token`/`refresh_token` 恆為字串，故此為防禦性邊界情況，非攻擊者可控（ADFS 為受信任端點）。
   - **建議**：在 `isAccessToken`/`isRefreshToken` 為真但 `reader.TokenType != JsonTokenType.String` 時也呼叫 `reader.Skip()`，讓解析器對任意格式回應都有一致行為。

3. **`RuntimeHostSlotLease` 的 `Dispose()`/`DisposeAsync()` 併發呼叫時，敗者不會等待勝者完成**
   - **檔案**：`IRuntimeHostSlotCoordinator.cs:46-67`
   - **說明**：兩者皆以 `Interlocked.Exchange(ref _disposed, 1)` 做一次性保護，僅有一者會實際執行 `ReleaseAsync`；若另一執行緒同時呼叫另一個方法，該呼叫會立即返回而不等待勝者的釋放完成。在目前程式碼中，`lease` 皆由單一擁有者（`OrganizationAdmissionManager`）透過 `DisposeLeaseUnderHostSlotGateAsync` 唯一路徑釋放，不存在多執行緒同時處置同一個 lease 的情境，故屬低風險。若未來有其他呼叫端可能並行處置同一租約，建議留意此語意差異。

4. **HTTP 隔離設定與 `Package01FeeReadsEnabled` 旗標核對通過**
   - **檔案**：`AdfsOAuthTokenProvider.cs:349-357`（`SocketsHttpHandler`：`UseCookies=false`、`AllowAutoRedirect=false`、`UseProxy=false`、`AutomaticDecompression=DecompressionMethods.None`、`PreAuthenticate=false`）；`SpeechMessageProducts.ChurchReport/appsettings.json:559`（`"Package01FeeReadsEnabled": false`）
   - **說明**：與需求逐項核對皆一致，未發現任何開啟消費端 CRM 流量的變更。`ReadBoundedResponseAsync` 對 32 KiB 邊界的處理（`AdfsOAuthTokenProvider.cs:369-397`）在精確 32768 bytes 時允許、32769 bytes 時正確拒絕，邊界正確；失敗回應只回傳狀態碼與端點字串，未外露 body（`AdfsOAuthTokenProvider.cs:135-140`），符合「error bodies must not be surfaced」要求。

---

## 需求對照清單

| 需求 | 狀態 |
|---|---|
| 同步 `Dispose()` 不得在未觀察的 fire-and-forget 中釋放 | ✅ 已阻塞等待完成（見 Warning #1 補充建議） |
| `await using`/`DisposeAsync` 為正式路徑 | ✅（`OrganizationAdmissionManager` 全面使用） |
| 防止 session/token/handler/timer/socket/queue/memory 洩漏 | ✅ 大致達成，token 字串殘留為已知固有限制（Warning #2） |
| ADFS token 回應綁定 32 KiB、錯誤 body 不外露、成功位元組事後歸零 | ✅ |
| cookies=false / redirects=false / proxies=false / decompression=false / pre-auth=false | ✅ |
| `Package01FeeReadsEnabled` 維持 `false` | ✅（`appsettings.json:559`） |

## 結語

無 Critical。兩項 Warning 皆非本次程式碼庫中會立即觸發的缺陷，而是「目前安全、但未來擴充（durable coordinator）時的地雷」與「.NET 字串不可歸零的固有限制」，與前幾輪審查（`20260728-152209`、`20260728-150858`）判定一致，可視為已知並記錄的殘留風險。若要達成嚴格意義的無條件 `PASS`，建議至少落實 Warning #1 的兩個修復選項之一（尤其是 `Task.Run` 包裝方案，成本低且能徹底消除未來風險）。

---
SESSION_ID: 96da117d-df41-4f86-9867-0b5d8b9a8d23
