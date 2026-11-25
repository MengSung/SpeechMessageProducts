# 三大產品共通層 1.3 線 B：之後再說

| 時間（Asia/Taipei） | 工作包 | 項目 | 本階段處置 |
|---|---|---|---|
| 2026-11-26 04:16 Asia/Taipei (UTC+08:00) | F10 | Payments core 的 `Logs_NoCardNumberOrBindToken` 宿主 `Exception.log` sink capture | 核心沒有 logger 或檔案 writer；本包只驗證 diagnostics sanitizer 邊界。由後續 host／告警整合補做實際 sink capture，不在 F10 建立第二個日誌 owner |
| 2026-11-26 04:16 Asia/Taipei (UTC+08:00) | F10 | callback 最終狀態的跨請求資料庫冪等 | provider parser 保持無狀態，只交付查詢線索；由產品持久層以銀行交易穩定鍵及原子 create-or-update 實作，F10 不虛構完成 |
| 2026-11-26 04:16 Asia/Taipei (UTC+08:00) | F10 | BindToken 的永豐實際效期與清除期限 | 本包只使密文 profile-bound 且憑證輪替可失效；正式效期列 G4 前置問題，未把未知限制寫成銀行規格 |
| 2026-11-26 04:16 Asia/Taipei (UTC+08:00) | F10 | 五個永豐沙箱情境 | 因 P0-1 交接設定尚未具備，標「實機待補」；不以合成 handler 或離線測試取代沙箱證據 |
