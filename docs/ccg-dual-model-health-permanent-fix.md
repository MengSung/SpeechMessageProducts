# CCG Gemini + Claude 雙模型健康檢查與永久修復手冊

本文件說明本專案以後執行 CCG analysis / review 時，如何讓 Gemini + Claude 雙模型流程在失敗時自動先修復本機環境、重新執行，並在可恢復時繼續任務，而不是停在「雙模型失敗」。

## 核心結論

以後不要直接手動呼叫：

- `codeagent-wrapper --backend gemini`
- `codeagent-wrapper --backend claude`
- `gemini`
- `claude`

所有 CCG analysis / review 都要先走專案自修復 runner：

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

## 自動修復流程

`Invoke-CcgDualModelWithSelfHealing.ps1` 會先呼叫 `Test-CcgDualModelHealth.ps1`，再執行 Gemini 與 Claude。它負責：

- 確認 `codeagent-wrapper.exe` 是否存在。
- 確認 `gemini.cmd`、`claude.cmd`、`python.exe` 是否可用。
- 修復目前 PowerShell process 的 `PATH`。
- 修復 Windows User `PATH`，避免下次新開終端機又找不到工具。
- 設定 `GEMINI_CLI_TRUST_WORKSPACE=true`，避免新 worktree 的信任問題。
- 設定 `CODEAGENT_LITE_MODE=true`，避免 Windows 上 Gemini progress mode 的不穩定路徑。
- 設定 `PYTHONIOENCODING=utf-8`，避免中文輸出亂碼。
- 將 prompt、stdout、stderr、health check、summary 全部寫入 `.ccg/dual-model-runs/`。
- 要求模型真的輸出內容；不能只看 exit code。
- 預設略過 backend smoke test，避免還沒 review 就先消耗模型額度。需要診斷登入或 provider 狀態時才加上 `-RunHealthBackendSmoke`。

## 未來失敗時的固定處理規則

當 CCG analysis / review 發生失敗，不要停下任務，也不要直接從零手動查 Gemini 或 Claude。固定照以下流程：

1. 將原本要分析或 review 的內容寫成 UTF-8 prompt 檔，放在 `.ccg/dual-model-runs/`。
2. 用 `Invoke-CcgDualModelWithSelfHealing.ps1` 重新執行同一個任務。
3. 讀取本次 run folder 內的 `summary.json`。
4. 如果 `ok=true`，代表 Gemini + Claude 都完成，繼續整理雙模型結果。
5. 如果 exit code 是 `2`，代表還有本機工具鏈問題；依 run folder 中的 health/stdout/stderr 修復後，再跑同一支 runner。
6. 如果 `quotaBlocked=true`，代表 Gemini / Claude provider 額度、session limit、HTTP 429、登入狀態等外部因素阻擋。這不是本機可修復問題，不可以宣稱雙模型 review 成功。
7. 只有在任務明確允許單模型 fallback 時，才可以加上 `-AllowSingleModelWhenQuotaBlocked`，而且報告中必須註明這不是完整雙模型 review。

## Claude wrapper exit 1 的處理

有時候 `codeagent-wrapper.exe --backend claude` 只會回：

```text
claude exited with status 1
```

這個訊息本身不足以判斷是本機壞掉，還是 Claude provider / session limit。runner 會自動再做 direct Claude probe：

```powershell
claude -p "Smoke test only..." --dangerously-skip-permissions --output-format text
```

如果 direct probe 顯示 `You've hit your session limit`、`rate limit`、`quota`、`429` 等訊息，runner 會將它分類為：

```text
quotaBlocked=true
```

這樣可以避免一直對不可本機修復的 provider 限制做錯誤修復。

## Analyze / Review 指令

專案的 CCG 指令已改成使用自修復 runner：

- `C:\Users\Administrator\.claude\commands\ccg\analyze.md`
- `C:\Users\Administrator\.claude\commands\ccg\review.md`

以後呼叫 CCG analysis / review 時，這兩份指令會要求先建立 task prompt，再透過 `Invoke-CcgDualModelWithSelfHealing.ps1` 執行。

## 快速健康檢查

如果只想檢查工具鏈，不想跑正式 review：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Test-CcgDualModelHealth.ps1" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs" `
  -SkipBackendSmoke
```

如果要同時確認 Gemini / Claude provider 是否能真的回覆，才使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Test-CcgDualModelHealth.ps1" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs"
```

## 給未來 agent 的規則

- 不要因為第一次 Gemini / Claude 失敗就停止任務。
- 不要跳過自修復 runner 直接手動呼叫模型。
- 不要把 `quotaBlocked=true` 報告成「雙模型 review 已完成」。
- 不要把只有 Gemini 或只有 Claude 的結果包裝成雙模型結果。
- 如果 runner 成功，任務要繼續往 implementation / review / fix 前進。
- 如果 runner 失敗但不是 quota，先修本機工具鏈，再重跑 runner。

此流程的目的不是保證 provider 永遠有額度，而是讓所有可本機修復的問題自動被修掉，並讓不可本機修復的 provider 限制被清楚分類，避免任務卡在模糊的「雙模型壞了」。
