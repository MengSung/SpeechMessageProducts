# CCG Gemini + Claude 雙模型自我修復永久手冊

> 最後更新：2026-07-04  
> 目的：讓 CCG 分析 / Review 遇到 Gemini、Claude 或 `codeagent-wrapper` 失敗時，不再停在人工排錯，而是先自動健康檢查、修復本機環境、重試雙模型，並清楚區分「本機可修復」與「provider 額度 / session 限制」。

## 結論

以後只要要跑 CCG 雙模型分析或 Review，標準入口都是：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Invoke-CcgDualModelWithSelfHealing.ps1" `
  -TaskFile ".\.ccg\dual-model-runs\<task>.md" `
  -Role reviewer `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs"
```

不要直接手動呼叫：

```powershell
codeagent-wrapper --backend gemini
codeagent-wrapper --backend claude
gemini
claude
```

原因是直接呼叫只會暴露單點錯誤；自我修復 runner 才會統一處理 PATH、UTF-8、Gemini trust、Claude quota probe、stdout / stderr 保存、summary 判讀與重試。

## 核心檔案

- `docs/scripts/Test-CcgDualModelHealth.ps1`  
  負責健康檢查與本機環境修復。

- `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`  
  負責正式執行 Gemini + Claude，並在失敗時自動先跑健康檢查、修復、重試。

- `.trellis/spec/guides/ccg-external-review-thinking-guide.md`  
  專案層思考指南，規定 CCG 外部 Review 的標準入口與故障分類。

- `AGENTS.md`  
  專案根目錄規則，明確要求未來 CCG 分析 / Review 失敗時，不可直接停下，必須先走自我修復 runner。

- `C:\Users\Administrator\.claude\commands\ccg\analyze.md`  
  `/ccg:analyze` 指令模板，已改成呼叫自我修復 runner。

- `C:\Users\Administrator\.claude\commands\ccg\review.md`  
  `/ccg:review` 指令模板，已改成呼叫自我修復 runner。

## Runner 會自動修復什麼

`Invoke-CcgDualModelWithSelfHealing.ps1` 會先呼叫 `Test-CcgDualModelHealth.ps1`，處理下列事項：

1. 設定 PowerShell / console 為 UTF-8。
2. 設定 `GEMINI_CLI_TRUST_WORKSPACE=true`。
3. 設定 `CODEAGENT_LITE_MODE=true`，避免 Windows + Gemini progress 模式造成不穩。
4. 設定 `PYTHONIOENCODING=utf-8`。
5. 補齊目前 process 的 PATH。
6. 補齊 Windows User PATH。
7. 確認 `codeagent-wrapper.exe` 存在。
8. 確認 `gemini.cmd` 存在。
9. 確認 `claude.cmd` 存在。
10. 確認 `python.exe` 存在，避免 Gemini hooks 執行失敗。
11. 將 prompt、stdout、stderr、health report、summary 全部寫到 `.ccg/dual-model-runs/`。
12. 對 Claude wrapper 只回 `claude exited with status 1` 的情況，額外跑 direct Claude probe，判斷是否其實是 quota / session limit。

## 標準恢復流程

當雙模型分析或 Review 失敗時：

1. 把原本要分析或 review 的內容寫成 UTF-8 prompt 檔，放到 `.ccg/dual-model-runs/`。
2. 使用 `Invoke-CcgDualModelWithSelfHealing.ps1`，指定正確的 `-Role`。
3. 讀取產生的 `summary.json`。
4. 若 `ok=true`，代表 Gemini + Claude 都成功產出可用結果，可以繼續任務。
5. 若 exit code 是 `2`，代表仍有本機工具鏈問題；查看該 run folder 的 `health-attempt-*.json`、`*.stdout.md`、`*.stderr.md`，修復後再次執行同一個 runner。
6. 若 `quotaBlocked=true`，代表 Gemini / Claude provider 額度、session limit、HTTP 429 或登入狀態阻擋。這不是本機可修復問題，不可宣稱雙模型 review 成功。
7. 只有在任務明確允許單模型 fallback 時，才可以加上 `-AllowSingleModelWhenQuotaBlocked`，而且報告中必須清楚說明不是完整雙模型 review。

## Role 對照

`-Role` 可使用：

- `analyzer`
- `architect`
- `reviewer`
- `debugger`
- `tester`
- `optimizer`
- `builder`

常用情境：

- 需求 / 架構判斷：`-Role analyzer`
- 架構設計：`-Role architect`
- 程式碼審查：`-Role reviewer`
- 錯誤排查：`-Role debugger`

## 健康檢查

如果只想確認本機工具鏈，不想真的跑雙模型 Review：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Test-CcgDualModelHealth.ps1" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs" `
  -SkipBackendSmoke
```

如果要連 Gemini / Claude backend smoke 一起測：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Test-CcgDualModelHealth.ps1" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs"
```

注意：backend smoke 會消耗 provider 額度；正式分析 / Review 前通常不需要特別開啟，除非正在診斷登入、quota 或 provider 狀態。

## 常見失敗分類

| 現象 | 分類 | 處理方式 |
|---|---|---|
| `codeagent-wrapper.exe not found` | 本機工具鏈 | 確認 `C:\Users\Administrator\.claude\bin` 存在並在 PATH |
| `gemini.cmd not found` | 本機工具鏈 | 確認 `C:\Users\Administrator\AppData\Roaming\npm` 存在並在 PATH |
| `claude.cmd not found` | 本機工具鏈 | 確認 Claude CLI 安裝與 npm shim |
| `python.exe not found` | 本機工具鏈 | 補 Python 路徑，避免 Gemini hooks 失敗 |
| Gemini trust / workspace 錯誤 | 本機環境 | 確認 runner 有設定 `GEMINI_CLI_TRUST_WORKSPACE=true` |
| Gemini libuv / progress crash | 本機執行模式 | 使用 `--lite`，不要用 progress UI 當穩定 review 入口 |
| Claude `Not logged in` | 外部登入狀態 | 需手動 `claude auth login --claudeai` |
| `session limit` / `rate limit` / `quota` / `429` | provider 阻擋 | 等待額度恢復，或在明確允許時使用單模型 fallback |

## Exit Code

- `0`：成功，雙模型都完成。
- `2`：本機工具鏈仍有可修復問題。
- `3`：provider quota / session limit 等外部阻擋。

## Agent 行為規則

未來任何 agent 執行 CCG 分析 / Review 時，必須遵守：

1. 先建立 UTF-8 task prompt。
2. 先跑 `Invoke-CcgDualModelWithSelfHealing.ps1`。
3. 不要先手動 debug Gemini / Claude。
4. 不要把 `quotaBlocked=true` 說成雙模型成功。
5. 本機可修復錯誤要修復後重跑同一個 runner。
6. 如果 runner 成功，繼續原任務，不要因為第一次失敗就停止整個開發流程。

## 範例：Review git diff

```powershell
$repo = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.7.WorktreeRefactorRichMenu"
$taskFile = Join-Path $repo ".ccg\dual-model-runs\my-review.md"

$status = git -C $repo status --short
$diff = git -C $repo diff

$prompt = @"
# Review Task

請用 reviewer role 審查以下變更，分類 Critical / Warning / Info。

## Git Status

```text
$status
```

## Git Diff

```diff
$diff
```
"@

[System.IO.File]::WriteAllText($taskFile, $prompt, [System.Text.UTF8Encoding]::new($false))

powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo "docs\scripts\Invoke-CcgDualModelWithSelfHealing.ps1") `
  -TaskFile $taskFile `
  -Role reviewer `
  -RepositoryPath $repo `
  -OutputDirectory (Join-Path $repo ".ccg\dual-model-runs")
```

## 對未來工作的幫助

這個永久修復的重點不是「保證 Gemini / Claude 永遠不會失敗」，而是把失敗處理標準化：

- 本機問題由 runner 自動修復或明確指出。
- 外部 quota / session 問題被正確分類，不再誤判成工具鏈壞掉。
- 所有輸入輸出都有紀錄，之後可追查。
- `/ccg:analyze` 與 `/ccg:review` 不再各自手寫不同命令。
- 後續任務遇到雙模型失敗時，可以自我修復後繼續，不會停在同一類問題反覆人工排查。
