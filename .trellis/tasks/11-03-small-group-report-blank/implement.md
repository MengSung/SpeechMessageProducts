# 執行與驗證

- [x] 沿用既有任務，核對使用者最新修復授權、Git blame 與截圖。
- [ ] 以實際 jQuery Deferred 與實際 Razor adapter 寫回歸測試，保存紅燈。
- [ ] 完成最小 Deferred 相容修正，保留多參數、錯誤、世代及釋放。
- [ ] 執行既有 Node 時序測試及新增 adapter 回歸、隔離與 drain 測試。
- [ ] 以可重現證據判斷圖表及靜態資源；不改 DevExtreme 版本。
- [ ] 執行相關 .NET 測試、Release 建置、文字位元組與差異檢查。
- [ ] 更新研究結果與修復回報，依實際驗證結果記錄限制。

不啟動連向實際組織的網站，不讀 User Secrets，不輸出真實設定、token、ID 或會員資料。新測試不增加第三方套件；使用 Node 內建 runner 與 repo 現有 jQuery 的 Deferred 實作。
