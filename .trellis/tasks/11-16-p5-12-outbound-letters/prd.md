# P5-12 訊息外送與失敗彙總

## 需求與邊界
依 05A 修訂 2 的 P5-12，僅承接外送 letter。入站不觸發推播；每筆正式活動只送一人，以活動 ID 為 Retry Key。內容原文不翻譯，sender 與固定框架經字典。未綁定、跨教會、永久 4xx 回寫明確原因；429／5xx 交系統作業重試。

a 為單人外送與狀態回寫；b 為快速行銷活動及失敗彙總。時間盒自本包開始兩天，屆時照 D5-12 拆分。P5-PROP-006 的 Live 查詢前拒絕與 Shadow 完整意圖不變；不啟用新步驟、不執行舊 DLL，live=deferred、differences=null。

盤點舊 Tools 22 檔及 WeeklyReport，逐檔列外送／已有具名入站承接／入站缺口／其他去向；入站缺口僅登錄階段末提案，不擴充本包。

## 驗收
- 同名測試：Letter_Inbound_Ignored、Letter_Outbound_PushOneRecipient_RetryKeyIsActivityId、Letter_RecipientOtherChurch_FailedReasonChurchMismatch_NoHttp、Letter_NotBound_FailedReasonNotBound_NoThrow、Letter_Blocked4xx_FailedReasonBlocked_NoThrow、Letter_429_ThrowsForSystemJobRetry、Letter_ContentKeptAsIs_FrameTextLocalized、QuickCampaign_100Activities_100IndependentSends、QuickCampaign_SystemJobRerun_NoDuplicateDelivery、Source_NoMulticastOrBroadcast。
- 同名 Hosting 測試：FailureSummary_Disabled_NotRegistered、FailureSummary_OverThreshold_NotifiesOncePerWindow、FailureSummary_ChurchACountsExcludeChurchB、FailureSummary_StopDrainsAndDisposes。
- 模組情境／Shadow／Depth／UnexpectedError／RecipientOtherChurch／NotBound／RetryKey，Core 兩目標、真外殼與 CAS。
- reading 六案不手造 legacy.json，不把離線當實機；兩 DLL 打包、goodshepherdback 全停用 plan/apply/verify=0、乾淨固定八項 guard、一頁報告、封存、推送核對。
