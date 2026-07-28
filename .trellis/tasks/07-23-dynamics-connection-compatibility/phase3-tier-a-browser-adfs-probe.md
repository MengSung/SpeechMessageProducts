# 最簡單的 ADFS 探測方式（不用跑 PowerShell）

你已經在 VS IIS Express 登入成功，且 fee list 走 legacy 可查到資料。
Codex 這邊連不到你的 localhost:43371，也無法用 sandbox 身分打 ADFS TLS。

## 你只要做 2 步

1. 在 Visual Studio 按 停止 再 F5（讓新的 DiagnosticsController 編譯進去）
2. 保持登入狀態，瀏覽器新分頁打開：

http://localhost:43371/diagnostics/adfs-token-probe

## 之後

- 畫面會出現 JSON
- 同時會寫檔：SpeechMessageProducts.ChurchReport/Logs/adfs-token-probe-latest.json
- 你只要回我「好了」或貼畫面，我會自己讀這個檔判斷 token / WhoAmI / ClientId

## 不要做

- 不必再手動跑 Invoke-AdfsTokenProbe.ps1
- 現在不要開 Package01（仍是 false，避免 fee list 再 302）

## 目前已知

- Legacy baseline: 胡夢嵩 2026-01-01~2026-07-25 Returned=56（Trace.log 已確認）
- 你的截圖是 legacy 路徑正常
- Package01 先前失敗：unauthorized / HTTP 302（IFD）