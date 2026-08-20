# 執行摘要

1. 集中 `DiagnosticsTrace` 設定，Release 編譯期 fail-closed，Debug runtime 開關控制三檔。
2. 以私有 writer 分離 legacy ToolUtility Trace，保留 Program 作為唯一 global `Trace.log` listener owner。
3. 以專屬 CTS 管理 Debug GC monitor，停止時先取消、再 drain、最後釋放 listener。
4. PowerShell 分析器以共享讀取、串流與有界聚合分析三檔，輸出遮罩後 Markdown 報告。
5. 以單元測試、Debug/Release build、PowerShell 5.1/7 fixture、三種 runtime smoke 與真實檔案 hash 驗證交付。
