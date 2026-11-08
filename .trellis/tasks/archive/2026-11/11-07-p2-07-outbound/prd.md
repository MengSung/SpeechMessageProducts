# P2-07 對外待辦Writer與有界Worker

## 依據與範圍
僅依實作計畫3.4的03 P2-07、00 §7.1、90附錄A.4/H。依賴P2-01與P2-02均已驗收。只有RichMenuGenerate、QrImageGenerate、AllotQuery；本包無D365實機。P2-09a已驗收，不與其同寫csproj。

## 必要契約
- GoodShepherd.Core提供雙目標OutboundTaskWriter；payload僅允許閉合白名單的ID與數值，寫入前拒絕自由文字。
- 相同事件用含教會及類型的伺服器穩定鍵，資料庫替代鍵原子防重，不靠程序鎖；不得因重送把已完成列改回待處理，或覆寫既有payload／教會。
- Hosting的BackgroundService每5秒、每批至多50筆；以RowVersion認領，10分鐘卡住可重認領。
- 429／5xx／逾時退避1、2、4、8、16分鐘，超過5次改失敗並交共用通知；真實Exception保留，正常owner取消不通知。
- 每筆獨立已驗證教會scope；停止輪詢後本批最多排空30秒，未完成維持處理中，資源單一owner確定歸還。
- 完成30天、失敗90天的D365大量刪除設定寫入本包操作導讀，不執行實機。

## 先寫的測試（計畫同名）
Enqueue_SameIdempotencyKey_SingleRecord、Enqueue_PayloadWithText_Rejected、Claim_TwoWorkersParallel_OnlyOneExecutes、Retry_429_BackoffSchedule、Retry_ExceedsMax_FailedAndNotified、StaleProcessing_Reclaimed、Shutdown_DrainsCurrentBatch_WithinTimeout、Worker_ResourceCountsReturnToBaseline。

## 完成定義（00 §7.1，不得加碼）
- [x] 1. 上列測試同名存在並通過；Writer雙目標。
- [x] 2. 00 §8.1全部命令提交後乾淨複本通過。
- [x] 3. 無新專案，覆蓋率不適用。
- [x] 4. 本包無實機，不適用。
- [x] 5. 一頁回報與3.4 DATA已更新。

## 時間盒
開始：2026-11-07 22:26 Asia/Taipei (UTC+08:00)；1個工作天或15個提交。到期依00 §12列三個選項，不加碼硬撐。

驗收：2026-11-08 12:42 Asia/Taipei (UTC+08:00)；產品 413fde67，乾淨守門 49f67029 全部通過；本包無新專案、無實機。
