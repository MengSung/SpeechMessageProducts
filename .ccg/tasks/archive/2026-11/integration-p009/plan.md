# P0-09 執行清單
- [x] 沿用既有任務，依使用者核定同步 01 的 P0-09／P0-10 及共用 error-handling 契約；不改設計原檔、AGENTS、3.2。
- [x] 新增嚴格雙目標 Core：LineChannelCredential、LinePushRequest、ILinePushClient、LinePushResult、LinePushOutcome、LinePushClient；測試專案雙目標且既有四檔只在 net10 編譯（保留原 33 案例）。
- [x] 保留原 11 方法，補 caller 取消／網路原始例外／HTTP 結果／安全／所有權／邊界案例；兩目標先跑 NotImplementedException 功能紅燈。
- [x] 實作單人 text、原子輸入快照、逐請求標頭、10 秒有界逾時、Dispose 與無靜態憑證；假 handler 隔離／排空測試。
- [x] 雙目標綠燈、行覆蓋率≥80%、原 33 全保留、00 §8、進度與位元組檢查，附錄 F 報告與提交。

逾時計時以內部可注入取消來源工廠統一抽象 net48／net10 排程；正式路徑固定 10 秒，測試以手動取消來源注入故障，不等真實時鐘或連網。工廠產物由單一呼叫 using 擁有，不能回傳共用來源。
