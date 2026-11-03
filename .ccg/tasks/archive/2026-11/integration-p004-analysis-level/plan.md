# P0-04 分析層級補修

- [x] 讀完整 AGENTS.md、00、既有探測與規範；保留所有 P0-08 未提交內容，不加入本補修提交。
- [x] 原 20 項保留，加入 net48 CA1707、net48 CA1716、兩目標非空 EffectiveAnalysisLevel；先執行取得紅燈。
- [x] 嚴格群組僅在 AnalysisLevel 空白時設定 latest；不得 NoWarn 新規則，必要時修正探測或補丁來源。
- [x] 同步 01 P0-04 與 strict-analyzer-contract；明確記錄補修前 net48 Recommended 未實際啟用。
- [x] 全探測、75 工具測試、771 方案測試、7 Debug tracer 及全部 §8 通過。
- [x] 回報、任務／進度與單獨提交；之後才對 P0-08 改名、重新取得雙目標功能紅燈。
