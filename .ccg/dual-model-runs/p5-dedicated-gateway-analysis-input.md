# P5 Dedicated Gateway Alignment：設計與現況分析

請分析目前未提交的 P5 變更。目標是讓 ChurchReport 在 Visual Studio Multiple Startup Projects 以 DedicatedGateway mode 經 `https://localhost:7244/` 存取 Data8 runtime，同時保留 Development 預設的 Embedded F5 體驗。

請檢查：

1. Embedded 與 Dedicated 是否共用 `Data8ProfileRuntime`，但每個 host 是否維持獨立 runtime、pool、admission、client 與 permit，避免跨 Profile/Organization/模式洩漏。
2. Dedicated 是否確實排除 Official Worker 與 SQL coordinator，並使用 in-memory host slot coordinator。
3. Dedicated HTTP pipeline 是否保留 HTTPS loopback、Negotiate、workload authorization、RequestGuard、no-store，且 POST 呼叫是否使用 `RequestOrigin.DedicatedGateway`。
4. ChurchReport 是否應保留 `appsettings.Development.json` 為 Embedded，並以獨立 launchSettings profile 使用環境變數覆寫 DedicatedGateway。
5. 請只列出可由目前程式碼證實的 Critical / Warning / Info；特別注意 deterministic disposal、ServiceProvider、Data8 pool、permit、CTS、timer、task、cookie/credential/session retention 與測試缺口。

禁止建議使用 Web API、IFD、CRMWeb、SQL、IIS、DNS、ADFS 或外部 CE 真機操作。請輸出具體檔案與行數或可搜尋符號的審查報告。
