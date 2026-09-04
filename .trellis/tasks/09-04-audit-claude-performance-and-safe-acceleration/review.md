# Review 結果：奉獻稽核入口當機修正

## Critical

- 無。`DedicationFeeAuditViewWeb` 不再把 request-scoped `DonationPaymentManager.m_Contact` 的 null 值傳入 `SetDedicationFeeList(Entity)`。
- fallback 會清除姓名、手機、奉獻編號、身分證字號、後六碼、奉獻清單、同名清單與總金額，不會跨 request/session 帶出前一位使用者資料。

## Warning

- 初次 Gemini 審查發現 Grid 與 LINE ViewBag 的 null 解參考風險；已改由 `EnsureAuditFormModel` 統一建立/回存模型，並對 `IsAOfficeWorker` 使用 null-safe 判斷。
- `GetFeesByContactId` 現在先確保模型再查詢，避免 valid contact 查詢因 null model 被吞掉，並安全讀取總額。

## Tests

- 新增 4 個回歸測試，涵蓋無登入 contact 的空白隔離模型、null model 回存，以及兩個 DataGrid 入口的 null 防衛。
- `dotnet build SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj -c Release --no-restore`：通過，0 warning/0 error（另一次與測試平行執行曾有 apphost 檔案鎖定重試，非程式碼警告）。
- 稽核與生命週期篩選測試：31 passed。
- 奉獻稽核回歸測試：4 passed。
- `git diff --check`：通過。
- `verify_trace_invariants.py`：既有 fixture 的 F4 `bg.end` 缺失 1 項；其餘租約、連線、trace 計數檢查通過。

## External CCG review

已依專案規定透過 `Start-CcgDualModelRun.ps1` 執行三次 reviewer run；Gemini 皆有 usable output，未發現 Critical，並指出的 null 防衛問題已修正。Claude 三次皆 `no-usable-output`（exit code 1），因此不是完整雙模型審查；結果只能標示為 Gemini-only degraded/incomplete review，不能宣稱 dual-model success。

