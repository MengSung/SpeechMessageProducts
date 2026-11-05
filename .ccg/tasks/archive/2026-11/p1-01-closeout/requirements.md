# P1-01 收尾範圍與授權

使用者於 2026-11-05 明確要求本對話接手 P1-01 收尾，並已通知主實作對話。這是既有 P1 的收尾分工，不建立新的 Trellis 任務，不覆寫主線 task、進度 HTML、Activity/Core 或其 staging。

- 依正式計畫 02 的 P1-01：51 個已稽核孤兒檔無有效引用後刪除、原 15 個效能案例保留並進正式測試專案、Performance trait、ToolUtility.Tests 在方案。
- 在固定提交 03c8aa28 的獨立 worktree 重跑本機 00 §8、架構／Plugin 打包守門；比較前後 duplicates、測試清單與正執行 TRX。
- 交付 wp-reports/P1-01-acceptance.md、可核對證據、主會話審查及分離提交／歸檔。
- 使用者最新決策：現有 DevExpress 已足夠，繼續使用現有版本；不升級、不續訂、不替換現有元件。暫緩新來源與 hosted 完整 CI 恢復，不作為本次 P1-01 阻擋，但不得聲稱 hosted CI 通過。
- 不修改產品程式、正式設計決定、授權／套件版本；不連 D365、不發 LINE、不 push／merge。外部模型分析與審查按自癒入口及 quota fallback 如實揭露。
