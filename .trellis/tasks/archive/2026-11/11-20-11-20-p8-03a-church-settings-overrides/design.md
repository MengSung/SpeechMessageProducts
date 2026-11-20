# P8-03a 技術設計

## 邊界與資料流

`ChurchSettingsService` 位於純核心層，接收不可變的 `AuthorizedChurch` 與完整 `TextContext`，依賴由組合根提供的設定資料存取契約、覆寫 cache invalidator 與系統字典／覆寫 validator。授權在 ChurchReport 呼叫服務前由 `MemberInfoAccessResolver.Resolve` 判定；核心服務不自行解讀 HTTP 欄位、request 字串或裸 Guid 作為權限。

文字儲存流程為：呼叫端建立授權教會及 context → 核心白名單和長度／模板參數驗證 → 以教會＋key＋culture 替代索引鍵 Upsert → 更新同一教會的 UTC `new_textchangedon` → 立即使相同完整 context 的本機 cache invalidate → 回傳成功。來源寫入或時間戳更新失敗時，保留原例外並回報失敗；只有 Upsert 和時間戳都完成後才使本機快取失效。不得把另一教會的 cache scope 失效。

奉獻類別以伺服器側教會範圍的讀寫契約管理；新增代碼由核心使用密碼學隨機數產生 `custom-` 加 8 位小寫十六進位字串，並經教會範圍衝突檢查後儲存。修改、停用、排序、會計科目及舊選項值皆在服務端再次約束教會身分。查詢稽核記錄固定教會範圍並限制最多 200 筆。

## 主機組合

- 新增嚴格布林組態驗證與一致的開關讀取；缺值為 false，非布林拒絕啟動。
- 開啟時使用既有 Hosting、Tenancy 與 ToolUtility factory/source/cache/polling 元件；不複製 cache、source 或 polling 實作，不建立永久 SDK 連線。
- ChurchReport 保留既有 `"ChurchReport"` 部署連線 owner，依 08A 設計借用或從既有工廠取得每輪短期 lease；不可引入第二個長期連線 owner。
- LineWebhook 以正式 `IChurchTextOverrideSnapshotProvider` 取代 `EmptyWebhookTextOverrideSnapshotProvider`，並確保 FeeNotify 建構文字使用該 provider 所屬的完整 context。
- Host 關閉順序為 polling Stop／Drain → cache async dispose → 既有連線 owner 最後 Dispose。DI 註冊必須明確表達依賴順序，並由測試實際 Stop/Dispose 驗證。
- 開關關閉時不建立輪詢器或 D365 source；保留原本無覆寫 catalog/provider 的輸出。

## 驗證策略

所有核心資料存取以合成 fake 記錄順序及故障點；主機測試使用本機 DI／測試 Host，禁止外部連線。測試明確涵蓋替代索引鍵、寫入失敗邊界、兩教會隔離、缺省／無效開關、通知範本覆寫與輪詢 drain。遵守 08A 指定的先紅後綠及 18 個精確名稱。
