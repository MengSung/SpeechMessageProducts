# SaveIntegrate 背景上傳安全修補第一階段

## Goal

修正 SaveIntegrate 的 C3 快照一致性與 C4 背景工作可觀測性，使同一份 Session 週報圖的前景寫入與背景快照使用共同同步邊界，並以不含敏感資料的結構化 Trace 區分 accepted、succeeded 與 failed。

## Confirmed facts

- `CreateIsolatedSnapshot()` 目前只在讀取端持有 `SmallGroupDataList._syncRoot`；多個前景端點直接呼叫 `SmallGroupData` 的更新、插入或刪除方法，故可在逐欄深拷貝時原地改寫同一個 `Member`。
- `SaveIntegrate` 的背景 lambda 目前只在一般 Trace 寫入例外型別，且 `bg.end` 是 scope 釋放事件，不能代表 CRM 上傳成功。
- `71b42c31` 已合併至 `1.0.0.6.DesignNewArchitector`；本任務不修改歷史、不 rebase、不 force push。

## Requirements

- 每份 `SmallGroupDataList` 必須擁有自己的同步根；快照、前景更新、插入、刪除與跨集合同步寫入必須遵守同一同步協定。
- 鎖只保護短暫的記憶體讀寫；不得在鎖內做 CRM、HTTP、DI scope、`Task.Run`、網路或檔案 I/O。
- 背景上傳仍只持有與清理自己的 snapshot，絕不回寫 Session／IMemoryCache 前景圖。
- Trace 必須使用固定、安全的 `operationId`、`stage`、`outcome`、`errorClass` 值；不得記錄例外文字、stack、帳密、成員資料或 CRM payload。
- `accepted` 只表示要求已排程；僅正常完成 upload 才可寫 `succeeded`。`bg.end` 保持只代表 scope 釋放。
- 先寫並執行會失敗的測試，再寫最小實作；不操作正式 CRM、不 commit、不 push。
- 使用單一模型 inline 實作；使用者已豁免 Gemini／Claude 雙模型要求。

## Acceptance Criteria

- [ ] C3 測試可證明寫入端在來源資料圖同步根被持有時必須等待，且兩份快照只可能是完整舊狀態或完整新狀態，絕不允許混合欄位。
- [ ] 所有本次盤點到的已發布 SmallGroup Session 圖前景寫入端都改用共同同步協定；背景副本清理不加入來源鎖。
- [ ] JSONL 具有可關聯的 `bg.accepted` 與 `bg.outcome` 事件，並提供固定 `operationId`、`stage`、`outcome`、`errorClass` schema。
- [ ] 測試涵蓋背景 upload 成功、初始化失敗與 upload 失敗；不將 `bg.end` 視為成功證明。
- [ ] 相關單元測試、建置、格式、UTF-8 無 BOM、CRLF 與 final CRLF 驗證通過。
- [ ] 最終報告明確保留「尚未以正式 CRM 實測證明」的限制，並提供人工煙霧測試步驟。

## Out of scope

- 清除 cache 以觸發重載、ListManager cache miss 修補、前端 CRM reload 語意。
- Hosted Service／Queue 架構重寫、Git 歷史重寫、正式 CRM 資料操作。
