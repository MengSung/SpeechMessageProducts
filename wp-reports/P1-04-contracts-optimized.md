# P1-04 純契約與優化查詢抽取驗證（2026-11-06）

狀態：本切片實作及雙宿主驗證已通過，末端守門通過／本地保存接續；**不是整包 P1-04 完成**。
P1-01、02、03 已驗收，主 P1 維持 3/12。正式 3.3 計畫未修改，無需求修正 Word 報告。

## 需求與實作對應

| 正式 P1-04 需求／永久契約 | 實作 | 決定性驗證 | 狀態 |
|---|---|---|---|
| 純 BCL／SDK 契約抽取，namespace／舊 API 保留 | Core/ContactOperations/IContactService、ListOperations/IListService／IMarketingListService、QueryOperations/IComplexQueryService／IQueryService | 搬前來源清單固定 golden，五介面全部 67 多載；型別／參數順序／名稱／ref／批次預設／取消參數相同 | 通過 |
| 不能誤把 net48 測試宿主當產品 | CoreTargetFrameworkTests | 原 CS0234 紅測後，反射實載 net462；net10 實載 v10，host 仍可指派至同一 Core 介面；單筆 nullability 如實標示 | 通過 |
| 優化資料操作共用一份，不帶 host logger／資源 | Core/EntityOperations/EntityOptimizedQueryService、原 namespace PagedResult | 原 host 三代表 golden 先綠；單筆委派 EntityQueryService、批次／分頁委派 CollectionQueryService，不複製另一套查詢驗證 | 通過 |
| 穩定 ID、同名保留、整頁及完整批次發布 | Query／Collection 共用守門 | 原 34 安全失敗／1 通過，修正後搬前 38/38；錯 ID／錯表／null／重複均拒絕，後頁錯誤保留同一 Exception、不能回第一頁部分結果 | 通過 |
| 不修改 caller filter、不擴張 OR 查詢 | RetrieveByCondition | OR 整體 AND statecode，原輸入樹不變；既有 AND 語義與 golden 不變，top 1..5000 且拒 SDK 超量輸出 | 通過 |
| 取消、scope、drain、確定釋放 | ExecuteAsync／GetService／Dispose | 1000 A/B、caller mutation、IO 中取消／內部取消分類、Dispose 競爭與 GC weak-reference；不持 logger、不 Dispose 借入 SDK | 通過 |
| 雙目標測試及覆蓋率 | ToolUtility.Tests | net48 652/652（實載 net462）、net10 778/778；Core 每目標獨立一份 coverage 92.13%／92.83%，優化服務本體各 95.38% | 通過 |
| 全方案／ChurchReport／架構相容 | 00 §8 | 詳 final-section8/solution-summary.json；ChurchReport 623/623、架構 67/67 | 通過 |

## 行為與生命週期

- 保留 ctor 必填 logger，但不持有、不反射、不在 Core 發送通知或記錄原例外；host 是最終共用告警與效能追蹤 owner。
- 保留原 Batch 字典沒有 statecode、原 Paged 缺省沒有 statecode 的差異，不能因共用 Collection 就偷偷改查詢。
- 修正原第三頁永遠用第 2 頁；保留 cookie 多載，新增明確 pageNumber 多載支援無 cookie 後續頁。DTO 仍可變，僅屬此次 caller，不能作共享快照。
- 同步 SDK 不建立 Task.Run；Task 只表達完成、故障與明確 caller 取消。host 擁有傳輸 timeout／併發上限；Dispose 不中斷已開始 I/O，先排空再釋放 SDK。
- 單頁保留分頁 metadata，不宣稱全表完整；完整批次沿用 5000 輸入／輸出、1000 頁的本地預算。跨頁不是資料庫快照，變動造成衝突時拒絕並讓 caller 明確重試。
- 抽純介面不代表留在 host 的 Contact／List／Query 實作已安全；介面文件明列這個邊界，不用 nullability 宣稱實作完成。

## 證據與審查

本輪原始證據：`artifacts/p1-04-contracts-continuation-20261106/`。
紅測包括 contracts-red.log（編譯缺契約）、optimized-safety-red.trx（34 失敗／1 通過）、
optimized-top-red.trx（超 top 輸出未拒絕）。API golden 首次僅多行簽章空白不一致，
確認為非 ABI 差異後正規化開括號空白；沒有刪多載、放寬型別或改預期值。
最新雙宿主 final-net48.trx／final-net10.trx、coverage、完整 section8 均保留。

使用者最新明確表示「不需要雙模型了」：後續分析及審查由主會話直接完成，不再呼叫外部模型。
先前 20261106-085049 analyzer 是 Gemini 403／Claude 週額度，兩者都無可用結果；
不是雙模型完成，也不是有成果的單模型降級。保留原紀錄，不因模型不可用降低驗收。
主會話已逐項核對 API、委派差異、候選發布、取消及資源 owner；末端結果接續寫入任務 review。

## 整包剩餘事項

Contact（cache／密碼核對結果與授權邊界）、List／MarketingList（有界批次及 drain）、
其餘 Query 與 CrmAsyncExtensions，以及其必要純契約仍須逐型別處理與驗證。
不把 logger／Trace 可移除就永久列 host，也不把 cache、連線或排程硬搬 Core。
四個指定方法與 product／舊 DI 多載已在既有切片實作，本輪完整方案再次實際執行其契約測試；
仍須最後逐項完成矩陣後才可宣告 P1-04 完成。本輪未操作 VM／D365／LINE，沒有實機或 hosted CI 新證據。


## 同輪阻斷性帳密快取修補

審查發現 ContactService.RetrieveByAccountNumberAsync 對只含帳號的 cache hit 直接回 Entity，會跳過本次密碼核對；已用原 host 七案全部真紅重現。移除帳密入口所有 cache I/O，不把密碼或雜湊放進 key；每次向原 SDK 核對當前密碼。同步 Task 包裝補 SDK 前後 caller 取消及內部取消故障分類。新增內部取消補證後，八個安全案例（含1000次A/B）與優化查詢合計54/54綠。這不是整個 Contact 或全域 cache 安全驗收，其他 host cache 仍待處理。完整方案在修補後重新執行，結果以 final-section8 為準。

最終完整回歸為16 TRX、2777/2777，0失敗/略過；最終coverage位於final-coverage/，各目標僅一份Core報告，正式80%門檻通過。strict產品零警告/錯誤；全方案4個既有obsolete警告，無新NoWarn。HTML歷史timeline逐字等於HEAD，未發現U+FFFD；原終端亂碼為Python輸出碼頁所致，不重寫歷史。
