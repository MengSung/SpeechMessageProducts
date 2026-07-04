# CCG Gemini + Claude 雙模型自我修復固定流程

本文件說明未來執行 CCG analysis / review 時，如何避免 Gemini 或 Claude 工具鏈失敗後整個任務停住。

## 結論

以後不要直接手動呼叫：

- `codeagent-wrapper --backend gemini`
- `codeagent-wrapper --backend claude`
- `gemini`
- `claude`

請一律改用自我修復入口：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Invoke-CcgDualModelWithSelfHealing.ps1" `
  -TaskFile ".\.ccg\dual-model-runs\<task>.md" `
  -Role reviewer `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs"
```

`-Role` 可使用：

- `analyzer`
- `architect`
- `reviewer`
- `debugger`
- `tester`
- `optimizer`
- `builder`

## 自我修復會處理什麼

`Invoke-CcgDualModelWithSelfHealing.ps1` 會先呼叫 `Test-CcgDualModelHealth.ps1`，並在同一個執行流程內處理下列問題：

- 確認 `codeagent-wrapper.exe` 是否存在。
- 確認 `gemini.cmd`、`claude.cmd`、`python.exe` 是否可找到。
- 自動補齊目前 process PATH 與 Windows User PATH。
- 設定 `GEMINI_CLI_TRUST_WORKSPACE=true`，避免新 worktree 信任狀態卡住。
- 設定 `CODEAGENT_LITE_MODE=true`，避免 Windows 下 Gemini progress mode 不穩。
- 設定 `PYTHONIOENCODING=utf-8`，降低中文輸出亂碼風險。
- 將 prompt、stdout、stderr、health check、summary 全部保存到 `.ccg/dual-model-runs/`。
- 健康檢查預設只檢查本機工具鏈，不額外呼叫 Gemini / Claude，以免正式 review 前先消耗模型額度。
- 如需連 backend smoke test 也一起跑，可加上 `-RunHealthBackendSmoke`。
- 健康檢查若仍屬於可修復問題，會依 `-MaxAttempts` 自動重新嘗試。
- backend 成功不只看 exit code，還會確認模型確實產生 reviewer / analyzer 輸出。

## 什麼情況不能本機修復

下列狀況不是本機工具鏈壞掉，不能靠腳本修復：

- Claude 或 Gemini 額度用完。
- Claude session limit。
- Provider HTTP 429。
- 帳號登入、授權、付款、服務端限制。

這類情況 runner 會標記：

```text
quotaBlocked=true
```

這表示不是程式壞掉，而是外部模型供應商暫時拒絕服務。不可把這種情況回報成「雙模型 review 成功」。

## Claude wrapper exit 1 的處理

`codeagent-wrapper.exe --backend claude` 有時只輸出：

```text
claude exited with status 1
```

這種訊息不足以判斷是本機壞掉還是 Claude 額度問題。因此 runner 會自動再做一次 direct Claude probe：

```powershell
claude -p "Smoke test only..." --dangerously-skip-permissions --output-format text
```

如果 direct probe 顯示 `You've hit your session limit`、`rate limit`、`quota` 或 `429`，runner 會正確歸類為外部額度問題，而不是反覆做無效的本機修復。

## analyze / review 指令入口

以下兩個 CCG 指令已固定改走自我修復入口：

- `C:\Users\Administrator\.claude\commands\ccg\analyze.md`
- `C:\Users\Administrator\.claude\commands\ccg\review.md`

因此未來進行 CCG analysis / review 時，應該讓指令建立 task prompt，然後交給 `Invoke-CcgDualModelWithSelfHealing.ps1` 執行。

## 未來任務遇到雙模型失敗時的標準處理

1. 不要停止任務。
2. 不要立刻手動重查 Gemini / Claude。
3. 直接改用 `Invoke-CcgDualModelWithSelfHealing.ps1`。
4. 讀取 runner 的 `summary.json`。
5. 如果 `ok=true`，繼續整理雙模型分析或 review 結果。
6. 如果 `quotaBlocked=true`，明確回報外部額度阻塞；若任務允許，可使用 `-AllowSingleModelWhenQuotaBlocked` 暫時取得可用模型的意見，但不可宣稱完成雙模型 review。
7. 如果 exit code 是 `2`，代表仍有本機可修復問題，應查看該 run 目錄中的 health / stderr 檔案，再修腳本或環境。

## 設計原則

這個流程的目的不是掩蓋錯誤，而是把問題分成三類：

- 本機環境問題：自動修復後重試。
- 模型供應商額度問題：正確標記，不做假成功。
- 真正 review 發現的程式問題：寫入 review 結果並回到程式修正。

這樣可以讓 CCG review 成為穩定流程，而不是每次失敗都重新手動除錯。
