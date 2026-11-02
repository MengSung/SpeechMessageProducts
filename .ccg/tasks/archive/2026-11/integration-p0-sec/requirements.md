# P0-SEC 核定範圍

使用者已核定安全工作包，沿用 Trellis 階段任務，不重建階段任務。
Development 不得取用基底正式 LINE token，CRM 只准 goodshepherdback；非 Development provider 與逐鍵值保持不變。
只可修改指定 11 個 ChurchReport 讀取點、LineMessagingProcessorClass、Program／Startup、csproj、Development 設定，以及新增設定層／守門與測試文件。
先做遮罩掃描；找到寫死 LINE token、其他不經設定的 token 來源、範圍外產品修改需求或 Production 行為改變即停止。
不連線 D365／LINE、不啟動網站、不讀 User Secrets 的實值。測試僅用 PROD-FAKE／DEV-FAKE，禁止改程序環境或目前目錄。
