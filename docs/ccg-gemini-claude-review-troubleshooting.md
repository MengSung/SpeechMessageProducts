# CCG Gemini/Claude 雙模型 Review 故障排除手冊

> 目的：下次外部 Gemini/Claude CCG review 再出現 `command not found`、wrapper timeout、Gemini crash、Claude auth、Python hook 等問題時，先照本手冊排查，不要重新摸索。

## 0. 先講結論

這次不是單一 bug，而是多層環境問題疊在一起：

1. `gemini` / `claude` CLI 的 npm shim 曾經不在 PATH。
2. Codex sandbox 內可能無法讀取 `C:\Users\Administrator\AppData\Roaming\npm\*.cmd`。
3. Gemini 在 Windows 上搭配 `codeagent-wrapper --progress` 會有 libuv assertion / crash / hang 風險。
4. Claude CLI 需要登入授權，否則 wrapper 可以啟動但 review 不會成功。
5. Gemini hooks 會呼叫 `python .gemini/hooks/*.py`，所以 Python 也必須在外部執行 PATH 內。
6. PowerShell 直接打 `npm` 可能被 ExecutionPolicy 擋住，Windows 上診斷 npm 請用 `npm.cmd`。

目前狀態是「外部 / escalated execution 可運作」，不是「永久不可能再壞」。下次若壞，先跑本手冊的健康檢查。

## 1. 快速健康檢查

在 worktree 根目錄執行：

```powershell
cmd.exe /c "where gemini & where claude & where python & gemini --version & claude --version & python --version"
& "C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe" --version
```

預期至少要看到：

```text
C:\Users\Administrator\AppData\Roaming\npm\gemini.cmd
C:\Users\Administrator\AppData\Roaming\npm\claude.cmd
C:\Users\Administrator\AppData\Local\Programs\Python\Python314\python.exe
0.49.0
2.1.198 (Claude Code)
Python 3.14.2
codeagent-wrapper version 5.11.1
```

版本可以升級，但如果命令找不到，先不要跑 review，先照第 3 節修。

## 2. 穩定執行方式

Gemini reviewer：

```powershell
$env:GEMINI_CLI_TRUST_WORKSPACE = 'true'
$repo = (Get-Location).Path
$task | & "C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe" --lite --backend gemini - $repo
```

Claude reviewer：

```powershell
$repo = (Get-Location).Path
$task | & "C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe" --lite --backend claude - $repo
```

重要規則：

- Gemini 不要加 `--progress`。
- Codex 內呼叫外部 reviewer 時要用 escalated execution。
- `ROLE_FILE` 必須指到 reviewer role：
  - Gemini：`C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md`
  - Claude：`C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md`

## 3. 分層故障矩陣

| 症狀 | 最可能原因 | 先查什麼 | 修復方向 |
|---|---|---|---|
| `gemini command not found in PATH` | npm shim 不在 PATH，或 Codex sandbox 擋 AppData npm | `where gemini` | 補 User PATH；Codex 內用 escalated execution |
| `claude command not found in PATH` | npm shim 不在 PATH，或 Codex sandbox 擋 AppData npm | `where claude` | 補 User PATH；Codex 內用 escalated execution |
| `npm.ps1 cannot be loaded` | PowerShell ExecutionPolicy 擋 `npm.ps1` | 直接跑 `npm` 會失敗 | 改用 `npm.cmd` |
| Claude wrapper 啟動但 review 失敗 | Claude 未登入或 token 壞掉 | `claude auth status` | `claude auth login --claudeai` |
| Gemini wrapper hang / libuv assertion | Gemini backend + `--progress` | wrapper 命令是否含 `--progress` | 改用 `--lite --backend gemini`，不要加 `--progress` |
| Gemini hooks 顯示 `python not recognized` | Python 不在目前 process PATH | `where python` | 補 Python 到 User PATH，或用完整 Python 路徑 |
| 新 worktree Gemini 拒絕執行 | workspace trust 問題 | 是否有 trust / approval 訊息 | 設定 `$env:GEMINI_CLI_TRUST_WORKSPACE='true'` |
| `codeagent-wrapper` 找不到 | `.claude\bin` 不在目前 process PATH | `where codeagent-wrapper` | 用完整路徑 `C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe` |

## 4. PATH 修復

User PATH 至少要包含：

```text
C:\Users\Administrator\AppData\Roaming\npm
C:\Users\Administrator\.claude\bin
C:\Users\Administrator\AppData\Local\Programs\Python\Python314\Scripts\
C:\Users\Administrator\AppData\Local\Programs\Python\Python314\
C:\Users\Administrator\AppData\Local\Programs\Python\Launcher\
```

修復命令：

```powershell
$userPath = [Environment]::GetEnvironmentVariable('Path','User')
$parts = @()
if ($userPath) {
    $parts = $userPath -split ';' | Where-Object { $_ -and $_.Trim() -ne '' }
}

$wanted = @(
    'C:\Users\Administrator\AppData\Roaming\npm',
    'C:\Users\Administrator\.claude\bin',
    'C:\Users\Administrator\AppData\Local\Programs\Python\Python314\Scripts\',
    'C:\Users\Administrator\AppData\Local\Programs\Python\Python314\',
    'C:\Users\Administrator\AppData\Local\Programs\Python\Launcher\'
)

foreach ($path in $wanted) {
    if (-not ($parts -contains $path)) {
        $parts += $path
    }
}

$parts = $parts |
    Where-Object { $_ -ne 'C:\Users\Administrator\AppData\Roaming\Claude\claude-code\2.1.92' } |
    Select-Object -Unique

[Environment]::SetEnvironmentVariable('Path', ($parts -join ';'), 'User')
[Environment]::GetEnvironmentVariable('Path','User')
```

注意：

- 更新 User PATH 不會自動更新已經開著的 Codex / PowerShell parent process。
- 更新後最好開新終端，或在命令內明確設定 `$env:Path`。

## 5. CLI 安裝 / 修復

PowerShell 裡請用 `npm.cmd`：

```powershell
npm.cmd config get prefix
npm.cmd list -g --depth=0
npm.cmd install -g @google/gemini-cli @anthropic-ai/claude-code
```

預期 npm prefix：

```text
C:\Users\Administrator\AppData\Roaming\npm
```

如果 `npm.cmd install -g` 因權限或網路失敗，在 Codex 中要用 escalated execution。

## 6. Claude auth 修復

檢查：

```powershell
claude auth status
```

若看到 not logged in、no API key、could not resolve authentication method：

```powershell
claude auth login --claudeai
claude auth status
```

成功條件：

```text
loggedIn=true
authMethod=claude.ai
```

## 7. Gemini workspace trust / hooks

Gemini reviewer 前先設定：

```powershell
$env:GEMINI_CLI_TRUST_WORKSPACE='true'
```

Gemini 會執行 `.gemini/hooks/session-start.py` 和 `.gemini/hooks/inject-workflow-state.py`。如果 hook 顯示 `python not recognized`，先確認：

```powershell
where python
python --version
```

如果 Codex sandbox 內 `python` 找不到，但外部 `cmd.exe /c "where python"` 找得到，代表目前 process PATH 沒刷新；外部 reviewer 仍可用 escalated execution 執行。

## 8. CCG 模板與 `--progress`

這次確認：Gemini backend 不要跑 `--progress`。

請避免：

```powershell
codeagent-wrapper.exe --lite --progress --backend gemini
```

請使用：

```powershell
codeagent-wrapper.exe --lite --backend gemini
```

如果 `ccg-workflow` npm 套件升級，可能重新產生模板，把 `--progress` 放回去。升級後要重新搜尋：

```powershell
Select-String -Path "$env:USERPROFILE\.claude\.ccg\**\*.md" -Pattern "--progress --backend gemini","--progress --backend claude"
```

若又出現 Gemini + `--progress`，先改掉再跑 review。

## 9. 最短修復流程

下次雙模型壞掉時，不要先猜。照這個順序：

1. 跑健康檢查：

```powershell
cmd.exe /c "where gemini & where claude & where python & gemini --version & claude --version & python --version"
& "C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe" --version
```

2. 如果 `gemini` / `claude` 找不到：
   - 確認 `npm.cmd list -g --depth=0`
   - 補 User PATH
   - 新開 terminal 或用 escalated execution

3. 如果 Claude 不能 review：
   - 跑 `claude auth status`
   - 必要時 `claude auth login --claudeai`

4. 如果 Gemini 卡住或 crash：
   - 確認命令沒有 `--progress`
   - 設定 `GEMINI_CLI_TRUST_WORKSPACE=true`
   - 確認 `where python`

5. 用 smoke test 確認兩個 backend：

```powershell
$repo = (Get-Location).Path

$geminiTask = @'
ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
Smoke test only. Reply with exactly: GEMINI_BACKEND_OK
</TASK>
OUTPUT: one line
'@
$env:GEMINI_CLI_TRUST_WORKSPACE='true'
$geminiTask | & "C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe" --lite --backend gemini - $repo

$claudeTask = @'
ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
Smoke test only. Reply with exactly: CLAUDE_BACKEND_OK
</TASK>
OUTPUT: one line
'@
$claudeTask | & "C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe" --lite --backend claude - $repo
```

6. Smoke test 都過了，再跑正式 review。

## 10. 這次的根因分類

依 Trellis break-loop 分類：

- **A. Missing Spec**：之前沒有把 CCG external reviewer 的環境契約寫清楚。
- **B. Cross-Layer Contract**：Codex sandbox、Windows PATH、npm shim、wrapper、Gemini/Claude CLI、Python hook 之間是多層契約。
- **D. Test Coverage Gap**：沒有固定的 health check，所以問題只在正式 review 時爆出。
- **E. Implicit Assumption**：假設 `gemini` / `claude` 在 PATH 就代表 wrapper 一定能找到；實際上 sandbox 與 parent process PATH 可能不同。

防止再浪費時間的做法：

- 每次 CCG review 前先跑第 1 節健康檢查。
- Gemini reviewer 固定不用 `--progress`。
- Claude reviewer 前先確認 auth。
- 新 worktree 要設定 Gemini workspace trust。
- `.ccg/tmp/` 只作暫存，不要提交。
- 若修改了 PATH，務必知道目前 process 可能還是舊 PATH。

## 11. 已知不是永久解的地方

這些仍可能再次發生：

- Codex sandbox 政策改變。
- npm global 套件升級改變 CLI 行為。
- `ccg-workflow` 升級重生模板，把 `--progress` 加回 Gemini。
- Claude token 過期或 credentials 損壞。
- 新 Windows 使用者 / service / scheduled task 不吃目前 User PATH。
- 新 terminal 未開，仍使用舊 parent process PATH。

所以正確說法是：

> Gemini + Claude CCG review 目前在外部 / escalated execution 路徑可穩定運作；但不是永久無條件修復。未來若再壞，先照本 runbook 的健康檢查與矩陣排除。
