請審查目前工作區中新增的 ChurchReport PowerShell 開發啟動腳本。

目標：使用者執行一個 .ps1 後，應完成 UTF-8 設定、dotnet 編譯、啟動 ChurchReport、等待 http://localhost:5000/ 可用、開啟預設瀏覽器，並在 Ctrl+C／錯誤／正常結束時清理啟動的網站程序。

請檢查：PowerShell 5.1 與 PowerShell 7 語法相容性、路徑／參數處理、程序樹清理、埠與啟動競態、dotnet 參數正確性、編碼、錯誤處理，以及是否有超出需求的變更。

變更檔案：SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1
請執行 git diff -- SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1，並輸出 Critical/Warning/Info 分級結果；若無問題請明確寫出 No findings。
