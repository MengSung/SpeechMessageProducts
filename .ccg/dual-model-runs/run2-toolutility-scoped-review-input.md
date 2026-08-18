請審查 Run 2 commit HEAD（refactor(toolutility): ToolUtilityClass 改為 request 範圍）。

檢查重點：request scope 與跨請求隔離、IOrganizationService 擁有權與 Dispose、Facade 子服務是否誤釋放共用連線、Factory legacy 路徑是否捕獲 scoped 依賴、DI ValidateScopes、測試覆蓋、繁中 XML 文件、UTF-8/CRLF，以及是否超出 Run 2 白名單。請執行必要的唯讀檢查，輸出 Critical/Warning/Info 分級結果；不要修改檔案。
