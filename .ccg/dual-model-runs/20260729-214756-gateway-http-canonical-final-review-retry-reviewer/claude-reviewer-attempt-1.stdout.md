# Gateway HTTP 與 Canonical Queue 最終程式審查結果

## 審查方法
逐檔閱讀 `git status --short` 列出的 tracked diff 與全部 13 個 untracked/modified 目標檔案（Program.cs、appsettings.json、RequestLimits 兩檔、四份 Runtime/Capacity 檔案、四份測試檔、LineMessagingClient.cs diff、trellis spec diff），並交叉比對 Program.cs 新舊 diff 以確認本次變更的核心目的（把 body binding 從 minimal-API 隱式 model binding 改成明確 authorization-after body reader）。同時獨立驗證了 13 個 in-scope 檔案的 BOM/CRLF 編碼狀態、`Package01FeeReadsEnabled=false` 不變式，以及 `DispatchEnvelope.EstimatedEnvelopeBytes`/`CanonicalEnvelopeBytes` 共用 backing field 沒有被任何呼叫端同時設定造成 last-write-wins 風險。未修改任何程式檔案。

## Critical 🔴
**無。** 未發現會導致驗證繞過、記憶體/憑證外洩、或 session/resource 洩漏的問題。九項必查契約（authn/authz 先於 body I/O、JSON-only fail-closed、Kestrel/IIS/reader 共用 hard ceiling、strict UTF-8 + depth + duplicate 拒絕 + zero-before-return、prepare-before-first-await、deterministic canonical bytes、PreparedOperationDispatch 單一 owner 與並行 idempotent Dispose、zero-tolerance leakage）在程式與對應測試中都有一致且可驗證的實作。

## Warning 🟡

- **檔案：`review_diff.patch`（repo 根目錄，639 行，untracked）**
  - 情境：此檔內容與 `SpeechMessage.Dynamics.Gateway/Program.cs` 的 tracked diff 高度重複，且不在 `.ccg/dual-model-runs/` 既定產出路徑下，屬於前次審查流程遺留的暫存輸出。
  - 影響：若被誤 `git add -A`/`git add .` 提交，會把過時 diff 快照混入版本庫，造成文件漂移與 reviewer 混淆；也不符合「.ccg/dual-model-runs 之外不留暫存審查產物」的收斂要求。
  - 建議修正：審查完成後刪除此檔，或搬移至 `.ccg/dual-model-runs/` 底下歸檔；後續可在 pre-commit/CI 增加「repo 根目錄不得有 `*.patch`」的檢查。
  - 應新增的 assertion：CI 層級檢查（非程式測試）：`git status --short` 中不得出現根目錄層級的 untracked `*.patch`/`*.diff`。

## Info 🟢

- **檔案：`SpeechMessage.Dynamics.Gateway/Program.cs:47-51`（ConfigureKestrel 重複驗證路徑）**
  - 觀察：`ConfigureKestrel` callback 呼叫 `GatewayRequestBodyLimitOptions.BindAndValidate(context.Configuration)`，繞過 DI Options pipeline 直接丟出裸 `InvalidOperationException`；而 `.ValidateOnStart()` 觸發的是 `OptionsValidationException`。兩者對同一種「限制值超過 hard ceiling」情境給出不同例外型別，目前的 fail-closed 安全語意不受影響（兩者都會讓 Host 在接流量前中止），但 `Request_limit_above_hard_ceiling_fails_host_startup` 測試斷言的確切例外型別，依賴 Generic Host 對 `IStartupValidator` 早於 `GenericWebHostService`（Kestrel 啟動）執行的內部順序保證。
  - 建議：非阻塞，可加註記說明此順序依賴，或評估讓 Kestrel callback 改讀已通過驗證的 `IOptions<GatewayRequestBodyLimitOptions>` snapshot（例如透過 `context.ApplicationServices`,若該時機可行）以消除雙重驗證路徑。

- **檔案：`SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationRequestBodyReader.cs:142-145`（`declaredLength is < 0` 分支）**
  - 觀察：實際 ASP.NET Core 的 `HttpRequest.ContentLength` getter（`HeaderUtilities.TryParseNonNegativeInt64`）解析失敗或負值時會回傳 `null`，不會回傳負數；因此此分支在真實流量下無法觸發，只有測試以 `DefaultHttpContext` 直接賦值屬性時才可達，屬防禦性 dead code，無害。
  - 建議：可選擇性在 `GatewayRequestBodyBoundaryTests` 補一個 `declaredContentLength: -1` 的直接 reader 呼叫案例作為回歸保護，或在程式註解中明確標註「此分支透過真實 HTTP header 不可達，僅為 defense-in-depth」，避免未來讀者誤以為是可由外部觸發的路徑。

- **檔案：`SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayRequestBodyLimitOptions.cs:44-48,108-115`**
  - 觀察：`BindAndValidate` 與 reader 建構子都各自呼叫一次 `Validate()`，屬良性重複，非功能缺陷，僅供紀錄，不需修正。

- **檔案：`Line.Messaging/LineMessagingClient.cs`**
  - 觀察：本次 diff 僅為 XML 文件註解重寫（把已知的 `MarkAsReadByTokenAsync` 契約說明改為強調 token 的一次性生命週期與不可快取）與兩處多餘空白修正（`. ConfigureAwait` → `.ConfigureAwait`），未觸及任何邏輯或簽章；不影響本次 Gateway 安全邊界，無需動作。

## Session／Memory／Resource Leakage 專項檢查結果
逐一核對以下路徑，均未發現洩漏：
- `GatewayOperationRequestBodyReader.ReadAsync`：唯一 rent 對應唯一 return，所有 success/413/InvalidJson/cancel/exception 路徑的 `finally` 都執行 `CryptographicOperations.ZeroMemory` 後 `Return`；不 Dispose ASP.NET Core 擁有的 request stream（已用 `TrackingReadStream`/`TrackingArrayPool` 測試驗證）。
- `OperationDispatchPreparer.TryPrepare`：`rentedBuffer`/`normalized` 的 ownership 轉移旗標（`ownershipTransferred`）正確涵蓋建構子呼叫前後的所有例外路徑；`sourceParameters` 中的 `JsonElement` 全部經 `TryNormalizeValue` 轉為 detached CLR scalar（`string`/`Guid`/`DateTimeOffset`/`long`/`decimal`/`bool`），不會有 `JsonElement`/`JsonDocument` 存活進入 `PreparedOperationDispatch`。
- `PreparedOperationDispatch.Dispose`：以 `Interlocked.Exchange(ref _disposed,1)` 保證並行 idempotent；清除順序為 envelope/hash/wrapper → dictionary.Clear() → 全陣列 `ZeroMemory` → `Return`，符合單一 owner 與 zero-before-return 契約。
- `ControlledOperationExecutor.ExecutePreparedAsync`：`prepared.Dispose()` 固定在 `finally`，且晚於 `await using (acquisition.Lease...)` 區塊結束，確保 lease cleanup 早於 buffer 回收（`OperationDispatchQueueLifecycleTests.Lease_cleanup_completes_before_prepared_buffer_return` 已驗證此順序）。
- `DispatchEnvelope`：只含非秘密 bounded 欄位，`EstimatedEnvelopeBytes`/`CanonicalEnvelopeBytes` 共用 backing field，經 grep 確認生產與測試程式碼都未在同一物件初始化式中同時設定兩者，不存在 last-write-wins 風險。

## 總結
本次變更（Gateway JSON-only body reader、deployment-owned request-body hard ceiling、canonical dispatch preparer 與 `PreparedOperationDispatch` 生命週期）在正確性、信任邊界排序、記憶體/資源清理與測試覆蓋上均達到高標準，且中文 XML 文件完整深入，符合契約 #9。僅有 1 項 Warning（根目錄殘留暫存 diff 檔，屬 repo 衛生問題而非程式缺陷）與 3 項 Info（不影響安全邊界的健壯性/文件建議）。**建議：批准合併，但請先清除 `review_diff.patch`。**

---
SESSION_ID: 9ef8d083-8c30-4f20-8a6a-ec03124e2ce2
