# P3-05 有界佇列與背景處理

唯一驗收依據：實作計畫3.5的04 P3-05、00§5與§7.1；目前planning，依賴P3-02。與P3-01預計檔案無重疊，可在P3-02驗收後兩包並行。

## 需求／先寫測試／完成定義

- Channel<WebhookEnvelope>，總容量預設1000可配置、BoundedChannelFullMode.Wait，寫入最多2秒，滿載HTTP回503。
- 預設4個處理迴圈，hash(userId)%N分區，同一會友接收順序依序處理，不同分區並行。
- 每事件獨立CancellationTokenSource，30秒逾時；只放已解析事件及Channel識別，不保留HttpContext／credential。
- 停止順序：拒絕新接收→完成在途發布→排空最多30秒→取消剩餘→Dispose；每個scope／task／timer／registration與buffer有唯一owner、有限生命週期及確定性清理。
- 指定同名測試全部存在並通過：Queue_Full_Returns503、SameUser_EventsProcessedInOrder、DifferentUsers_ProcessedConcurrently、Shutdown_DrainsWithinTimeout、Envelope_ContainsNoHttpContext、ResourceCounts_ReturnToBaseline。
- P3-02的claim／queue兩階段契約必須遵守：先保留有界容量，再Commit，提交後完成發布，不因caller取消撤銷；false／失敗仍可重送，不能先commit再等待queue。
- §7.1五項：指定測試、完整guard、無新專案故coverage門檻N/A、本包實機N/A、一頁回報與進度同步。不能以尚未實機延後驗收。
