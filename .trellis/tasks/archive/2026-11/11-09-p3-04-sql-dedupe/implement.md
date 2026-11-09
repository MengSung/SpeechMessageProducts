# P3-04 執行清單

2026-11-09 18:57 Asia/Taipei (UTC+08:00)：P3-02／03／05均已驗收推送（7394dd79），只與P3-01並行，本包沒有依賴P3-01。

- [x] 讀完整AGENTS、task manifest、04及附錄A.8，確認相關spec與既有contract。
- [x] 同名測試先有真正功能紅燈，不以環境失敗充當紅燈。
- [x] 實作SQLclaim、5秒各操作期限、提交／rollback確定清理、正式註冊及啟動拒絕。
- [x] 每日清7天前記錄及完整stop／dispose；驗隔離、取消、故障後重送與owner基線。
- [x] LocalDB實際整合測試、cleanup與殘留0；原始證據只放本task ignored evidence。
- [x] 相關測試、文件與byte檢查；不commit/push/merge，交協調者複核，規劃納入第一個程式提交。
- [x] 協調者rebase／ff、固定guard、一頁報告、DATA驗收、封存、push與ls-remote。
