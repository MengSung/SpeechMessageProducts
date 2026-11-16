# P5-11 收費觸發與通知

依 05A P5-11、D5-14、P5-PROP-006 實作；不執行舊 DLL、不啟用新 Live 或舊步驟。

- [x] 閱讀四個舊入口、核對收費單→會友→教會與欄位。
- [x] AllotTaskIdentity 使用 feeId＋教會當地 queryDate，payload 精確 feeId/queryDate，Handler 重算 sourceId；更新 P4 離線及 PaymentsLive 建立處，不跑實機付款。
- [x] FeeAllotPlugin／FeeAllotActivity 只入列 AllotQuery；未來繳費日在入列前拒絕無效日期窗。
- [x] FeeNotifyActivity Shadow 記完整通知意圖、Retry Key=feeId；Live 查詢前拒絕並由共用 notifier 告警，不讀 token。
- [x] 七項具名測試：Fees_AllotTask_SameFeeSameDay_OneTask、Fees_AllotTask_SameFeeNextDay_NewTask、Fees_AllotTask_DayUsesChurchTimeZone、Fees_AllotHandler_SourceIdMismatch_Rejected、Fees_AllotTask_FuturePayDate_NotEnqueued、Fees_Notify_RetryKeyIsFeeId、Fees_Notify_ContentMatchesLegacy；固定模組測試與reading基準。
- [x] 兩 DLL 打包、全停用部署、固定八項guard、單頁報告與封存，驗收後才推送及ls-remote。
