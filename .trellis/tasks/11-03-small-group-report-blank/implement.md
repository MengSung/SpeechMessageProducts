# 執行與驗證

- [x] 沿用既有任務，核對使用者最新修復授權、Git blame 與截圖。
- [x] 以實際 jQuery Deferred 與實際 Razor adapter 寫回歸測試，保存 10 個紅燈。
- [x] 完成最小 Deferred 相容修正，保留多參數、錯誤、世代及釋放。
- [x] 執行既有 Node 時序測試及新增 adapter 回歸、隔離與 drain 測試：16／16。
- [x] 以可重現證據修正 14 個缺失引用，不改 DevExtreme 版本；圖表仍列未完整驗證。
- [x] 執行 .NET 測試、Release 建置、編碼及差異檢查，如實記錄 P0-12A 的 8 個失敗及 Debug 鎖檔限制。
- [x] 更新 research/fix-report.md、results.json，記錄使用者確認表格恢復與實際驗證限制。
- [ ] 完成 Gemini／Claude 修正審查。自癒入口中 Claude 兩次逾時，已準備審查包，不能標示雙模型完成。
- [ ] 依使用者 2026-11-03 核定併階段 0 第 ⑧ 批，通過後收尾／封存；P0-12A 現在可續行。

不啟動連向實際組織的網站，不讀 User Secrets，不輸出真實設定、token、ID 或會員資料。新測試不增加第三方套件；使用 Node 內建 runner 與 repo 現有 jQuery 的 Deferred 實作。
