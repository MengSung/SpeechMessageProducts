# P4-03 永豐撥款查詢處理器

## Goal

依 04A §5 實作永豐 AllotQuery、撥款比對、費用回寫及 LineWebhook 對外待辦承載；遵守教會隔離與資源釋放，不連正式服務或執行真實付款。

## Requirements

- 依 `文件/規劃設計書/實作計畫-3.5/04A_階段4_執行細則.md` 的 P4-03 與 §4A 實作，不擴大產品範圍。P4-06 才執行核定的測試沙箱實機步驟。
- Payments provider 新增 AllotQuery gateway/provider 介面與資料模型；使用既有 nonce、AES、簽章、HTTP 傳輸模式。Request 的 `APIService` 固定為 `AllotQuery`；Response 成功時回傳撥款批次的日期及 `Money`（永豐整數金額除以 100、TWD），簽章不符分類為 `SignatureInvalid`。
- GoodShepherd Core 的 reconciliation rule 依舊行為決定付款類型、查詢日期窗、訂單比對及四個回寫欄位；日期比較以教會時區的「今天」為基準。空白教會時區採 `Asia/Taipei`。
- Handler 僅信任待辦的伺服器端 `ChurchId`、待辦來源收費單 ID 與 payload 的 `feeId`。讀取收費單、會友、會友 `parentcustomerid`、教會 profile 後，必須確認收費單、會友及待辦屬於同一教會，才查詢永豐或寫入。Profile 由 `ChurchPaymentProfile.Read` 按 account ID 讀取；不接受 caller/payload 指定 profile，不建立跨教會快取。
- 處理順序固定為：解析 feeId → `FeeAllotRecords.Read` 並核對教會範圍、讀時區 → 讀具名 payment profile → `ResolvePayType` → `ResolveWindow` → `QueryAllotAsync` → `Match` → 有結果才以列版本樂觀並行更新。
- 空 pay type、無撥款資料或無符合訂單時正常完成、不更新。`NetworkFailure`、`ProviderUnavailable`、`SerializationFailure` 及不可讀回應須成為可重試的 HTTP 503 類錯誤；`ProviderRejected`、`SignatureInvalid`、`ConfigurationInvalid`、非法 payload、跨教會與 profile 缺漏以只含穩定錯誤代碼的非重試失敗處理。列版本衝突不重複寫入。
- 同一待辦重複執行得到相同結果；工作儲存查詢限制在該 host 允許處理的類型。Outbound disabled 時不註冊 worker；enabled 時每筆工作以獨立 DI scope 取得並釋放 D365 service，不快取連線或跨批次保留 scope。
- 缺少 `Payment` 設定時 LineWebhook 仍可啟動；新增註冊不得在未啟用時建立 payment worker、D365 client 或處理器。
- 任何程式碼都不得將付款設定值、token、個資、request/response 原文或錯誤本文寫入測試輸出、診斷或回報；不修改 ChurchReport 設定或舊正式金鑰，不新增 `.csproj`，不擴充 `tools/LegacyGolden`。

## Acceptance Criteria

- 下列 04A §5 測試皆以原名稱存在且通過；雙目標測試在 `GoodShepherd.Core.Tests` 的兩個目標皆通過：
  - `AllotQuery_Request_MatchesGolden`
  - `AllotQuery_Response_MapsBatchesToMoney`
  - `Reconcile_LegacyCases_SameFourFields`
  - `ResolvePayType_ByPayWayAndPrefix`
  - `ResolveWindow_ByPayDateAndToday`
  - `Handler_NoData_CompletesWithoutUpdate`
  - `Handler_Http5xxOrNetwork_IsRetryable`
  - `Handler_ProviderRejected_NotRetried`
  - `Handler_FeeOfOtherChurch_Rejected`
  - `Handler_SameTaskTwice_SameResult`
  - `Match_BlankOrderNumbers_NeverMatch`
  - `Handler_UnreadableResponse_IsRetryable`
  - `Handler_InvalidPayload_Rejected`
  - `Handler_UsesChurchToday_AcrossUtcMidnight`
  - `Update_AllotDate_IsSameCalendarDay`
  - `Store_AllowedTypes_QueriesOnlyThoseTypes`
  - `Worker_Disabled_NotRegistered`
  - `Factory_ScopeDisposed_ResourcesReturnToBaseline`
  - `Host_WithoutPaymentSection_StillStarts`
- 全包測試命令通過：`dotnet test SpeechMessage.Payments.Tests -c Release`、`dotnet test SpeechMessageProducts.GoodShepherd.Core.Tests -c Release`、`dotnet test ToolUtility.Tests -c Release`、`dotnet test SpeechMessageProducts.LineWebhook.Tests -c Release`、`dotnet test SpeechMessage.Hosting.Tests -c Release --filter "FullyQualifiedName~Architecture"`。
- `Factory_ScopeDisposed_ResourcesReturnToBaseline` 必須執行 100 次，確認 scope 與 handler 活躍數量回到 0。`Reconcile_LegacyCases_SameFourFields` 與 `Match_BlankOrderNumbers_NeverMatch` 必須涵蓋兩個目標框架。
- 固定 guard `C:\smv\guard` 的 00 §8.1 八項檢查通過；繁中檔案文件、UTF-8 無 BOM/CRLF/結尾 CRLF、錯誤差異與秘密掃描通過。不得連 D365 或永豐沙箱。
- 提交訊息固定為 `P4-03 新增永豐撥款查詢處理器並由 LineWebhook 承載對外待辦`；完成一頁 `wp-reports/P4-03.md`、進度頁 DATA，驗收後推送 P4 分支並核對 `git ls-remote` SHA。

## Constraints

- 不建立或部署任何 D365 欄位，不呼叫正式組織、正式商店或正式 LINE Channel；不做真實付款。
- 不啟動或部署 `LineWebhookP309`，不在本機啟動整台 LineWebhook。
- P4-03 只採用既有合成 golden、假 HTTP handler、假組織服務與 host 測試；實機行為留在 P4-06。
- 時間盒為 1 天，提交上限 15 個；超過時依 04A §7 暫停本包並一次列明狀況、已嘗試作法與三個選項。
