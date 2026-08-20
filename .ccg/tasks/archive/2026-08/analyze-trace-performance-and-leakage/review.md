# 唯讀分析結果

## 結論

現有 Dataverse lease／pool 邊界大致具備短命 lease、CallerId 歸還清除、Scoped Gateway、bounded trace queue 與確定性 Dispose 的設計；但目前尚不足以宣稱效能、Session 隔離與 Memory／Resource Leakage 已被證明安全。

## 已確認的重要證據

- `dataverse-trace.jsonl` 目前有 305 組 request begin/end、384 組 acquire/return，CallerId 歸還值為空，健康檢查失敗為 0，Trace dropped 為 0。
- 三次 `/Home/ProcessLogin` 失敗請求耗時約 21.9、41.7、61.3 秒；JSONL 的 semaphore wait 為 0，`Trace.log` 顯示是 OnPremiseClient 建立時收到 HTTP 503。
- `EnsureMinimum` 在 `SubPool.Sync` 鎖內同步建立網路 client，且 AcquireTimeout 只限制 semaphore wait，沒有涵蓋 client creation。
- 約九秒內有 22 個 faulted return／dispose；Gateway 對所有例外直接 MarkFaulted，現有事件沒有 fault category，無法判斷是否發生過度淘汰。
- JSONL 有 384 個 `crm.op`，但文字 `[Perf]` 聚合幾乎全部 `crm{n=0,ms=0}`，表示 Profiler 未被證明接到實際 Gateway 執行邊界。
- `Trace.log` 含原始 Session ID、IP、User-Agent、fingerprint、cache key 及登入識別資訊；分析器的敏感欄位規則未覆蓋這些本地化標籤，因此 0 命中不是安全證明。
- Memory／Session 只有低頻快照與估算值，沒有 heap／LOH／allocation rate、handle、active request、pool counters 或停止負載後的 drain baseline。

## 交付限制

本次未修改產品程式。CCG 交叉分析第一次完成 Gemini 結果但 Claude OAuth session 過期；同一入口重試時 Claude health check 仍為認證失敗，因此不可宣稱完整雙模型成功。結論以本機程式／檔案證據及已取得的單模型輔助分析交叉整理。
