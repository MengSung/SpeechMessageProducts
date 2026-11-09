# P3-04 平台SQL去重

唯一依據：實作計畫3.5的04 P3-04、90附錄A.8、00§5與§7.1。P3-02已驗收；與P3-01無相依且檔案不重疊。

- 正式SQL資料表僅ChannelId／WebhookEventId／ReceivedUtc／IsRedelivery，主鍵(ChannelId,WebhookEventId)，ReceivedUtc索引；腳本在SpeechMessageProducts.LineWebhook/Database/001_LineWebhookEvent.sql。
- Microsoft.Data.SqlClient、ConnectionStrings:Platform。INSERT＋真正主鍵衝突判重，SQL每次5秒；SQL失敗必須503，不能記憶體fallback或失敗視不重複。
- TryRegisterAsync以未提交INSERT交易實作claim；queue先取得capacity再Commit，未提交Dispose以獨立5秒清理rollback，caller取消不可阻止清理；已提交不刪資料。跨channel複合鍵隔離，任何連線不確定狀態不再借他人。
- Hosting排程每日清除ReceivedUtc嚴格早於7天的已提交資料，不動邊界／新資料。
- 必須同名存在：Dedupe_SameEvent_SecondIsDuplicate、Dedupe_SqlTimeout_Returns503、Dedupe_SameEventIdDifferentChannels_NotDuplicate、Cleanup_DeletesOnlyOlderThan7Days。
- LocalDB實際SQL整合測試以Trait隔離，正常dotnet test不得連SQL也不得以skip冒稱通過；本包手動指定filter執行。建立唯一測試庫／表，全部finally清理且殘留0。先記錄LocalDB狀態，必要時啟停還原；不用遠端SQL替代。
- 五項DoD：指定測試、固定guard、無新專案coverage不另加、本機SQL整合證據及殘留0、一頁報告及進度。P3-09遠端主機前置仍另外等待。
