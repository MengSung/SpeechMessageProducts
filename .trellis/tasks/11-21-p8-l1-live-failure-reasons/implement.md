# P8-L1 執行清單

1. 讀取 `AGENTS.md`、08A §5 L1／§4.7、部署後端與測試規範及共用指南；核對目前工作樹、既有 P7 證據與 goodshepherdback 最新狀態，不輸出機密。
2. 先加入六個指定測試，逐一確認紅燈並將測試名稱／結果記在證據紀錄。
3. 以最小變更補齊 activity rejection 繼續檢查、LIVE-PREP-03 子檢查 ID、fault code/stage/entity、church-defaults row state/欄位可更新狀態、relay 固定原因碼、trace exception 安全分組。
4. 執行六個測試及 `dotnet test SpeechMessage.Dynamics.Deployment.Tests -c Release`。
5. 依序調查 R7、R9、church-defaults，各最多 30 分鐘；只用部署專案既有正式唯讀指令。不得建立或使用一次性 PowerShell probe；不執行任何寫入操作。
6. 編寫一頁回報與決策紀錄，更新父任務清單及進度頁 DATA；完成文件與位元組檢查，提交訊息以 `P8-L1` 開頭。
7. 僅使用 `C:\smv\guard-p8` checkout 新提交並完整執行 00 §8.1；證據寫至 `C:\smv\guard-p8-evidence`。守門全通過後推送 P8 分支並以 `git ls-remote` 核對 SHA。
8. 封存 L1 Trellis 子任務後，依固定順序轉入 L2。

## 不得擴張

- 不調整既有 reason code 數字，不修改無關 Live 模組行為。
- 不連正式組織，不在 goodshepherdback 寫入資料或變更狀態。
- 不建立另一個 guard clone、工作樹或一次性診斷 probe。
- 不執行 CCG／外部審查；僅於階段末依核定執行一次 ClaudeOnly。
