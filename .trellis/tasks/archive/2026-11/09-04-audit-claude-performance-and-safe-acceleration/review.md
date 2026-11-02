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

## LINE 奉獻收費清單最終驗證（2026-09-04）

### Critical

- 無 Critical。LINE LIFF ID Token 會由 LINE Verify API 驗證 issuer、audience、subject 與 expiry；後端只接受與目前 Session/LineBindingViewModel 一致的 LINE user id。

### Warning / 修正

- 前端登入 AJAX 原本在後端回傳 `status != 1` 時仍會轉址，已改為只在明確成功時轉址，失敗時停留並顯示訊息。
- 前端登入請求新增頁面範圍 single-flight guard，避免重複點擊造成重複 CRM/Session 操作；旗標不進入 Session、cache 或 singleton。
- LINE 收費清單 GET 已在建模前還原 Session 保存的查詢日期，避免按查詢後日期跳回預設值。
- ID Token 不再寫入瀏覽器 console，除錯資料使用 `[REDACTED]`。

### Verification evidence

- `DonationPaymentViewDefaultsTests`: 29 passed。
- Session/LINE/生命週期 focused tests: 71 passed。
- `DonationPaymentProcessorMoneyToChineseTests`: 18 passed。
- `dotnet build -c Release --no-restore`: 0 warning / 0 error。
- 修改的 `.cs` / `.cshtml` 已逐檔確認 UTF-8 without BOM、無 bare LF、結尾 CRLF。
- `verify_trace_invariants.py`: 10 項通過，1 項既有 fixture FAIL（SaveIntegrate fixture 缺少 `bg.end`；本次未修改該背景流程）。
- 完整 `ChurchReport.MemberInfo.Tests`（最終重跑）：370 passed / 21 failed；失敗集中於既有測試尋找 `ChurchReport.sln`、舊命名/路徑契約，且 Release build 與本次 focused 測試均通過，未視為本次 LINE 變更回歸。

### External review status

本次依規定重新執行 `Start-CcgDualModelRun.ps1`。Gemini reviewer 有 usable output 且判定無 Critical；Claude 兩次皆 `no-usable-output`（exit code 1），所以本次仍是 Gemini-only degraded/incomplete review，不是完整雙模型通過。

## LINE HttpClient 最終核對（2026-09-04）

### 修正

- `Startup.cs` 新增 `LineLoginApi` named HttpClient，設定 `https://api.line.me/` BaseAddress 與 10 秒 timeout；token 不進 default headers、Session、cache 或 singleton。
- LINE ID Token verify、OAuth token exchange、profile 呼叫改用 named client 的相對路徑，避免繞過 BaseAddress 設定並固定外部端點邊界。

### 外部審查核對

- Gemini 第一次輸出：PASS，無 Critical；指出的絕對 URL 已修正。
- Gemini 另指出 `GetFeesByContactId` 可能 null；實際程式碼入口已先呼叫 `EnsureAuditFormModel(manager)`，因此該項為已存在防護下的 false positive，未再重複修改。
- Gemini 第二次因遠端 524 無 usable output；Claude 兩次皆 `no-usable-output`。本輪仍是 Gemini-only degraded/incomplete review，不能宣稱完整雙模型通過。

### 最終本地驗證

- `dotnet build -c Release --no-restore`：0 warning / 0 error。
- `DonationPaymentViewDefaultsTests`：30 passed。
- Session/LINE/生命週期與 SmallGroup cache isolation focused tests：72 passed。
- `DonationPaymentProcessorMoneyToChineseTests`：18 passed。
- `LineLoginOAuthSensitiveLoggingTests`：1 passed。
- `git diff --check`：通過。
- 修改的 `.cs` / `.cshtml`：UTF-8 without BOM、無 bare LF、結尾 CRLF。
- `verify_trace_invariants.py`：11 項中 10 項通過；唯一失敗仍是既有 SaveIntegrate fixture 缺少 `bg.end`，本次沒有修改該背景流程。
