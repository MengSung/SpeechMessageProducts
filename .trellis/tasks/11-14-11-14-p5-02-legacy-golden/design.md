# 技術設計

PluginHarness 使用 net48 console，採輸入來源路徑與情境 JSON，所有輸出先寫 operation-local candidate，掃描秘密與不連網契約後才原子發布。編譯器沿用中央 Roslyn，舊來源只在暫存 process/domain 執行；執行 owner 在 finally 卸載並清除自有暫存。不能安全攔截的連外 API 直接分類 BlockedByNetworkApi，不嘗試執行。

LegacyGolden net10 既有工具不擴張到 CRM 舊入口；PluginHarness 專責 P5-02 的舊 Plugin／Workflow 盤點與 attendance 示範。PluginHarness 與測試互不依賴真 LINE、D365 或 User Secrets。
