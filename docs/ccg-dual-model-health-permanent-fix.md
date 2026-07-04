# CCG Gemini + Claude 雙模型自我修復永久手冊

> 最後更新：2026-07-04  
> 目的：讓 CCG 分析 / REVIEW 遇到 Gemini、Claude 或 `codeagent-wrapper` 失敗時，不再停在人工排錯，而是先自動建立 prompt、健康檢查、修復本機環境、重試模型，並清楚區分「本機可修復」與「provider 額度 / session 限制」。

## 結論

未來所有 CCG 分析與 REVIEW，第一入口一律使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Start-CcgDualModelRun.ps1" `
  -Role reviewer `
  -Title "<short-task-name>" `
  -PromptFile ".\.ccg\dual-model-runs\<task>-review-input.md" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs" `
  -AllowSingleModelWhenQuotaBlocked
```

不要直接手動呼叫：

```powershell
codeagent-wrapper --backend gemini
codeagent-wrapper --backend claude
gemini
claude
```

原因是直接呼叫只會暴露單點錯誤；`Start-CcgDualModelRun.ps1` 會先把任務轉成 UTF-8 prompt，再委派給 `Invoke-CcgDualModelWithSelfHealing.ps1` 統一處理 PATH、UTF-8、Gemini trust、Claude quota probe、stdout / stderr 保存、summary 判讀與重試。

## 核心檔案

- `docs/scripts/Start-CcgDualModelRun.ps1`  
  高階入口。負責接收 `-Prompt` 或 `-PromptFile`、建立 UTF-8 任務檔、補上固定恢復規則，再呼叫底層 self-healing runner。

- `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`  
  正式執行 Gemini + Claude。每次執行前先跑健康檢查，遇到可修復的本機工具鏈問題會重試。

- `docs/scripts/Test-CcgDualModelHealth.ps1`  
  健康檢查與本機環境修復。負責 PATH、UTF-8、必要 CLI、Python、Gemini trust 與 Claude quota probe。

- `.trellis/spec/guides/ccg-external-review-thinking-guide.md`  
  專案層思考指南。規定 CCG 外部 REVIEW 的標準入口與故障分類。

- `AGENTS.md`  
  專案根目錄規則。明確要求未來 CCG 分析 / REVIEW 失敗時，不可直接停下，必須先走自我修復入口。

- `C:\Users\Administrator\.claude\commands\ccg\analyze.md`  
  `/ccg:analyze` 指令模板，已改成呼叫 `Start-CcgDualModelRun.ps1`。

- `C:\Users\Administrator\.claude\commands\ccg\review.md`  
  `/ccg:review` 指令模板，已改成呼叫 `Start-CcgDualModelRun.ps1`。

## 自動修復流程

`Start-CcgDualModelRun.ps1` 的工作：

1. 接收 `-Role analyzer`、`-Role reviewer` 等 CCG 角色。
2. 接收短文字 `-Prompt`，或讀取大型 `-PromptFile`。
3. 在 `.ccg/dual-model-runs/` 建立 UTF-8 task prompt。
4. 在 prompt 內寫入固定恢復規則，避免後續 agent 忘記處理失敗。
5. 呼叫 `Invoke-CcgDualModelWithSelfHealing.ps1`。

`Invoke-CcgDualModelWithSelfHealing.ps1` 的工作：

1. 呼叫 `Test-CcgDualModelHealth.ps1`。
2. 設定 PowerShell / console UTF-8。
3. 設定 `GEMINI_CLI_TRUST_WORKSPACE=true`。
4. 設定 `CODEAGENT_LITE_MODE=true`，避免 Windows + Gemini progress 模式不穩。
5. 設定 `PYTHONIOENCODING=utf-8`。
6. 補齊目前 process PATH。
7. 補齊 Windows User PATH。
8. 確認 `codeagent-wrapper.exe` 存在。
9. 確認 `gemini.cmd` 存在。
10. 確認 `claude.cmd` 存在。
11. 確認 `python.exe` 存在，避免 Gemini hooks 失敗。
12. 執行 Gemini 與 Claude。
13. 保存 prompt、stdout、stderr、health report、summary。
14. 對 Claude wrapper 只回 `claude exited with status 1` 的情況，額外跑 direct Claude probe，判斷是否其實是 quota / session limit。

## 標準恢復規則

當雙模型分析或 REVIEW 失敗時：

1. 不要先手動 debug Gemini / Claude。
2. 將原本分析或 REVIEW 內容放進 UTF-8 prompt，或直接傳給 `Start-CcgDualModelRun.ps1 -Prompt`。
3. 使用 `Start-CcgDualModelRun.ps1`，指定正確的 `-Role`。
4. 讀取產生 run folder 內的 `summary.json`。
5. 若 `ok=true`，代表 Gemini + Claude 都成功產出可用結果，可以繼續任務。
6. 若 exit code 是 `2`，代表仍有本機工具鏈問題；查看該 run folder 的 `health-attempt-*.json`、`*.stdout.md`、`*.stderr.md`，修復後再次執行同一個入口。
7. 若 `quotaBlocked=true`，代表 Gemini / Claude provider 額度、session limit、HTTP 429 或登入狀態阻擋。這不是本機可修復問題，不可宣稱雙模型 REVIEW 成功。
8. 本專案目前已授權：如果只有 Gemini 或 Claude 其中一個因 provider quota / session limit 被擋住，而另一個模型已成功產出可用內容，可以使用 `-AllowSingleModelWhenQuotaBlocked` 降級繼續任務。報告中必須清楚說明這是「quota/session fallback」，不是完整雙模型 REVIEW。
9. 如果兩個模型都沒有成功產出內容，就不能說外部 REVIEW 已完成；只能先依本機測試、建置、人工檢查繼續推進，並在 quota/session 恢復後再補跑外部 REVIEW。
10. 如果任何成功產出的模型提出 Critical，不能因另一個模型失敗就忽略；必須先驗證、修正或提出技術理由。

## Role 對照

- `analyzer`：需求分析、架構方向、風險盤點。
- `architect`：架構設計、模組邊界、資料流。
- `reviewer`：程式碼 REVIEW，輸出 Critical / Warning / Info。
- `debugger`：錯誤根因分析。
- `tester`：測試策略與測試缺口。
- `optimizer`：效能、可維護性與重複性改善。
- `builder`：實作建議。

## 常用命令

### 分析

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Start-CcgDualModelRun.ps1" `
  -Role analyzer `
  -Title "line-richmenu-analysis" `
  -PromptFile ".\.ccg\dual-model-runs\line-richmenu-analysis-input.md" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs" `
  -AllowSingleModelWhenQuotaBlocked
```

### REVIEW

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Start-CcgDualModelRun.ps1" `
  -Role reviewer `
  -Title "line-richmenu-review" `
  -PromptFile ".\.ccg\dual-model-runs\line-richmenu-review-input.md" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs" `
  -AllowSingleModelWhenQuotaBlocked
```

### 只跑健康檢查

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Test-CcgDualModelHealth.ps1" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs" `
  -SkipBackendSmoke
```

注意：backend smoke 會消耗 provider 額度；正式分析 / REVIEW 前通常不需要特別開啟，除非正在診斷登入、quota 或 provider 狀態。

## 常見失敗分類

| 現象 | 分類 | 處理方式 |
|---|---|---|
| `codeagent-wrapper.exe not found` | 本機工具鏈 | 由 health script 補 PATH；若檔案不存在，需重新安裝 wrapper |
| `gemini.cmd not found` | 本機工具鏈 | 由 health script 補 npm shim PATH；若檔案不存在，需重新安裝 Gemini CLI |
| `claude.cmd not found` | 本機工具鏈 | 由 health script 補 npm shim PATH；若檔案不存在，需重新安裝 Claude CLI |
| `python.exe not found` | 本機工具鏈 | 補 Python 路徑，避免 Gemini hooks 失敗 |
| Gemini trust / workspace 錯誤 | 本機環境 | runner 會設定 `GEMINI_CLI_TRUST_WORKSPACE=true` |
| Gemini libuv / progress crash | 本機執行模式 | 使用 `--lite`，不要用 progress UI 當穩定 REVIEW 入口 |
| Claude `Not logged in` | 外部登入狀態 | 需手動 `claude auth login --claudeai` |
| `session limit` / `rate limit` / `quota` / `429` | provider 阻擋 | 等待額度恢復，或在明確允許時使用單模型 fallback |

## Exit Code

- `0`：成功，雙模型都完成。
- `0` 且 `degradedFallback=true`：至少一個模型成功，另一個模型被 quota / session 擋住；任務可以繼續，但不能宣稱完整雙模型成功。
- `2`：本機工具鏈仍有可修復問題。
- `3`：provider quota / session limit 等外部阻擋，而且沒有可用的單模型 fallback。

## 對未來工作的幫助

這個永久修復的重點不是「保證 Gemini / Claude 永遠不會失敗」，而是把失敗處理標準化：

- 本機問題由 runner 自動修復或明確指出。
- 外部 quota / session 問題被正確分類，不再誤判成工具鏈壞掉。
- 所有輸入輸出都有紀錄，之後可追查。
- `/ccg:analyze` 與 `/ccg:review` 不再各自手寫不同命令。
- 後續任務遇到雙模型失敗時，可以自我修復後繼續，不會停在同一類問題反覆人工排查。
