# Gateway HTTP 與 Canonical Queue 最終程式審查報告

（角色：Claude reviewer；已讀取 `git status --short`、全部 tracked diff 與列出的 untracked 檔案，含 `Program.cs`、`appsettings.json`、`RequestLimits/*`、`WebApi/Runtime/*`、`WebApi/Capacity/DispatchEnvelope.cs`、`Line.Messaging/LineMessagingClient.cs`、`.trellis` spec 與對應測試檔。已交叉比對 Gemini 同輪審查結果，但獨立驗證每一項契約。）

## 總結

**Critical：無。** 未發現任何會讓未授權 caller 取得 body-contract oracle、繞過 415/413 邊界、洩漏秘密、或造成資源／記憶體洩漏的問題。授權→媒體型別→Content-Length→body I/O 的順序、Kestrel/IIS/reader 共用上限、`ArrayPool` 全陣列清零、`PreparedOperationDispatch` 單一 owner 與 lease-before-buffer 清理順序，皆有對應真實 TestServer/Kestrel 測試覆蓋且邏輯正確（`Kestrel_http11_rejects_declared_and_chunked_limit_plus_one`、`Lease_cleanup_completes_before_prepared_buffer_return` 等）。編碼（UTF-8 無 BOM、CRLF、結尾 CRLF）已逐檔驗證通過，appsettings.json 只含 `SecretReference` 名稱，無秘密值。

以下為 Warning／Info 級發現。

---

## Warning

### 1. `decimal` canonical 編碼對「字面量 scale」敏感，非對「數值」敏感，可能破壞 canonical hash 的 deterministic 承諾
- **檔案／成員**：`SpeechMessage.Dynamics.WebApi/Runtime/OperationDispatchPreparer.cs:687`（`GetCanonicalText`，`DecimalTag => ((decimal)parameter.Value!).ToString("G29", ...)`）
- **可重現情境**：呼叫端對同一個 decimal 型參數分別傳入 JSON `"amount": 10.5` 與 `"amount": 10.50`。`JsonElement.GetDecimal()`／`decimal.Parse` 會保留字面量的 scale（`10.5m` scale=1 vs `10.50m` scale=2），兩者以 `10.5m == 10.50m` 數值相等，但 `ToString("G29")` 輸出不同字串（`"10.5"` vs `"10.50"`），因此 `WriteCanonical` 產生的 bytes 不同，`CanonicalSha256` 也不同。
- **影響**：契約要求「Canonical bytes 必須 deterministic」。目前 `CanonicalBytes`/`CanonicalSha256` 只在測試中驗證「同一物件呼叫兩次结果相同」（`Prepare_is_order_independent_and_matches_fixed_versioned_representation`），並未驗證「數值相等但字面量不同的輸入」。一旦未來把此 hash 接上 idempotency-key 去重或 admission dedup（目前尚未接上，屬於潛在風險而非已發生的資料錯誤），邏輯相同的重送請求會被誤判為不同 dispatch。
- **建議修正**：在寫入 canonical text 前正規化 decimal scale（例如以 `decimal.Parse(value.ToString("G17"))` 或自訂 trim-trailing-zero 正規化，確保 `10.5m` 與 `10.50m` 產生相同 canonical text），或在文件中明確聲明「canonical hash 是輸入字面量的指紋，不是數值等價的指紋」以避免未來誤用。
- **應新增的 assertion**：`OperationDispatchPreparerTests` 新增一組理論測試，對同一 decimal 參數分別傳入 `10.5`、`10.50`、`10.500` 三種 JSON 字面量，斷言 `CanonicalBytes` 與 `CanonicalSha256` 完全相同（或若維持現狀，改為明確斷言三者「刻意不同」並在 XML 文件標明此限制）。

### 2. `PreparedOperationDispatch.Parameters` 暴露的 dictionary 會在 `Dispose()` 時被原地 `Clear()`，但 `IDynamicsWebApiClient.ExecuteRegisteredOperationAsync` 介面契約未明文禁止呼叫端保留該參考
- **檔案／成員**：`SpeechMessage.Dynamics.WebApi/Runtime/PreparedOperationDispatch.cs:70-73`（`Parameters` getter）與 `:116`（`parameters?.Clear();`）；介面定義於 `SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsWebApiClient.cs:24-30`（doc 僅寫「執行已驗證的 OperationDefinition」，未聲明 parameters 生命週期邊界）。
- **可重現情境**：若未來任何 `IDynamicsWebApiClient` 實作（含 Gateway/Embedded 共用之 client）在 `ExecuteRegisteredOperationAsync` 內把 `parameters` 參考捕捉進 fire-and-forget 背景工作（例如診斷紀錄、重試佇列）而未在 await 完成前讀完，`ControlledOperationExecutor.ExecutePreparedAsync` 的 `finally { prepared.Dispose(); }` 會呼叫 `parameters?.Clear()`，該背景工作稍後讀到的會是「已被清空的同一個 dictionary」，而不是拋出例外——屬於靜默資料損毀而非快速失敗（fail-loud）。
- **影響**：目前審查範圍內的 `DynamicsWebApiClient`／測試 double 皆在 awaited 呼叫內同步消費 `parameters`，故現況不會觸發；但此邊界只靠「約定成俗」而非型別/介面層面強制，屬於信任邊界文件缺口，符合本次審查要求特別標示的「Session／Memory／Resource Leakage」風險類別的鄰近議題。
- **建議修正**：在 `IDynamicsWebApiClient.ExecuteRegisteredOperationAsync` 的 XML doc 明確加一句「實作不得在方法回傳後保留 parameters 參考」；或在 `PreparedOperationDispatch.Dispose()` 改用建立新的空 dictionary 取代原地 `Clear()`（即 `Interlocked.Exchange` 後不呼叫 `Clear()`，讓任何殘留參考仍讀到 Dispose 當下的最後一份快照而非被清空），以「不可變快照」取代「原地清空」語意。
- **應新增的 assertion**：新增測試以自訂 client 實作，在 `ExecuteRegisteredOperationAsync` 內部把 `parameters` 存到欄位並在方法回傳*之後*讀取，斷言目前是否讀到清空字典（用以量化目前實際風險），並依此決定是否要修正或只文件化。

---

## Info

### 3. 中文註解疑似打字錯誤，影響契約可讀性
- **檔案／行**：`SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs:166`
  ```csharp
  // 全部收旂至此；Dispose 是並行 idempotent，且一定晚於 lease cleanup。
  ```
  「收旂」非標準詞（旂為古代旗幟用字），研判應為「收斂」或「收攏」之誤植。
- **影響**：純文件品質問題，不影響執行邏輯；但契約 #9 要求「完整、深入、詳細的繁體中文 XML 與必要實作註解」，錯字會降低審查與維運可讀性。
- **建議修正**：改為「全部收斂至此」或「全部匯集至此」。
- **應新增的 assertion**：無需程式測試；建議加入 repo 既有的 `dotnet format`/文件 lint 流程之外的一次性文字校對（可用中文拼字檢查腳本掃描新增/修改行）。

### 4. Gemini 已於同輪指出的兩項限制（enum 僅接受字串、JSON 禁止尾隨逗號/註解）
複核後判定為既有設計選擇且與契約一致（enum 走 `TryGetString` 符合 Dynamics Web API 慣例；`AllowTrailingCommas=false`／`CommentHandling.Disallow` 符合 fail-closed JSON-only 要求）。維持現狀，不需修正。

---

## 契約逐項複核結果（供交叉確認）

| # | 契約 | 結果 |
|---|---|---|
| 1 | AuthN/AuthZ 先於 Content-Type/body/executor | ✅ `Program.cs:221-236`，`RequireAuthorization()` 401 + `operationAuthorizer.Authorize` 403 皆先於 `bodyReader.ReadAsync` |
| 2 | JSON-only fail-closed 415 | ✅ `IsSupportedJsonContentType`（`GatewayOperationRequestBodyReader.cs:212-239`），涵蓋大小寫、單一 UTF-8 charset、重複/未知參數、structured suffix |
| 3 | Kestrel/IIS/reader 共用同一硬上限 | ✅ `Program.cs:47-57`；真實 Kestrel 測試 `Kestrel_iis_and_application_reader_share_the_configured_maximum` 驗證三者相同值 |
| 4 | 嚴格 UTF-8 wire bytes、深度、duplicate/unknown 拒絕、全清零 | ✅ `GatewayOperationRequestBodyReader.ReadAsync/TryMaterialize`，`finally` 內 `CryptographicOperations.ZeroMemory` 涵蓋所有 return/throw/cancel 路徑 |
| 5 | Executor 第一個 await 前完成 registry/canonical 準備 | ✅ `ExecuteAsync` 非 async，`TryPrepare` 全同步；`OperationDispatchQueueLifecycleTests.Blocked_queue_does_not_retain_original_request_graph` 以 GC 弱參考驗證 |
| 6 | Canonical bytes deterministic/typed/versioned/Ordinal/UInt32 big-endian | ⚠️ 見 Warning #1（decimal scale 敏感） |
| 7 | `PreparedOperationDispatch` 單一 owner、並行 idempotent Dispose、lease 先於 buffer | ✅ `Interlocked.Exchange` 保護、`await using` 先於外層 `finally`，`Lease_cleanup_completes_before_prepared_buffer_return` 驗證順序 |
| 8 | Zero-tolerance leakage | ✅ 主要路徑已驗證；見 Warning #2 之邊界文件缺口 |
| 9 | 繁中 XML/實作註解完整 | ✅ 大致完整；見 Info #3 錯字 |
| 10 | 編碼與秘密掃描 | ✅ 全部 14 檔皆 UTF-8 無 BOM、CRLF、結尾 CRLF；appsettings.json 僅含 SecretReference 名稱，`Package01FeeReadsEnabled` 相關檔案未在本次 diff 範圍內改動 |

## 建議

Warning #1、#2 建議在合併前處理或至少於 PR 說明中明確記錄限制範圍；Info #3 為輕量文件修正，可隨手處理。整體實作品質高，測試覆蓋（尤其真實 Kestrel/TestServer 與 GC 弱參考驗證）遠超一般水準，**建議 PASS，但附帶上述 2 項 Warning 待處理或記錄**。

---
SESSION_ID: 320eefb9-d700-4634-9cd9-923f0c3e7ff4
