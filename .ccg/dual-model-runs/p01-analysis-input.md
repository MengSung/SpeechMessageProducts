# P0-01 基準測試修復分析

使用者已授權先修復 24 個既有測試失敗，全綠後接續階段 0，且明確只使用 Claude。
請僅分析，不修改任何檔案。閱讀 AGENTS.md 與 .trellis/tasks/11-02-integration-p0/research/baseline-repair-recommendation.md。
範圍為建議內的 11 個測試檔：修復舊 ChurchReport 組件名稱、舊方案與專案路徑、Release 停用 legacy tracer 的測試契約。
確認不得刪除或略過任何既有案例，負向型別測試不能因找錯組件而假綠燈，Debug 的檔案釋放與 Release 的無檔案副作用必須各有斷言。
為遵守 Claude-only 且仍經既有自癒入口，另以可選 ClaudeOnly 開關調整 Start-CcgDualModelRun.ps1 與 Invoke-CcgDualModelWithSelfHealing.ps1；預設雙模型行為保留。
輸出具體分析、最小修復建議與潛在契約風險；任何需要改產品行為的項目請明確指出。
