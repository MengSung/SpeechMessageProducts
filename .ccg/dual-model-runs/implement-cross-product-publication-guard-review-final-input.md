# 最終審查：跨產品資料發布防重複與網路時序防護

請審查目前工作樹相對 HEAD 的全部變更，尤其確認上一輪 Warning 已完成修正：

1. `docs/publication-contracts.json` 的 consumer 名稱必須與 `RowPublicationGuard` 常數及實際 API consumer 一致，且測試不得退回舊名稱。
2. `_GeneralGroupGrids.cshtml` 的 Grid publication guard 初始化失敗時，必須記錄不含資料列／Session／credential 的診斷，清理已建立 coordinator，並 fail closed；不可回退到未防護 store.load。

再次檢查全部永久契約：
- 只能以權威資料庫 `PresentRecordId` 作 row identity；同名不同 ID 保留；相同 ID／空 ID fail closed。
- cache-hit 也必須 detached + revalidate；不可公開 Session-owned mutable graph。
- instance synchronization root 下原子 check-and-add；不得 fire-and-forget 捕獲 Session graph。
- generation/abort/dispose/pending refresh 有界，WeakMap 不得造成 owner registry 無界保留；所有資源 deterministic cleanup。
- 所有新增/修改 `.cs`/`.cshtml` 有深入繁體中文註解，UTF-8 無 BOM、CRLF、final CRLF。

請依 Critical / Warning / Info 輸出精確檔案與位置。不要修改檔案。若沒有 Critical/Warning，請明確寫出。請注意既有 Payment naming/source-inspection 測試失敗與本次 diff 的區隔。
