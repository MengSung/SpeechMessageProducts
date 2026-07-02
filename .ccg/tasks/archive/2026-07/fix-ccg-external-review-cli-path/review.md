# CCG Gemini/Claude backend 修復與雙模型 review 紀錄

## 修復結果

- `@google/gemini-cli@0.49.0` 已安裝於 npm global。
- `@anthropic-ai/claude-code@2.1.198` 已安裝於 npm global。
- `codeagent-wrapper` 版本為 `5.11.1`。
- 目前 Windows 使用者層 PATH 已加入：
  - `C:\Users\Administrator\AppData\Roaming\npm`
  - `C:\Users\Administrator\.claude\bin`
- 舊版 Claude Code 路徑 `C:\Users\Administrator\AppData\Roaming\Claude\claude-code\2.1.92` 不存在，不應作為可用 CLI 路徑。

## 根因分層

1. **npm shim 不在使用者 PATH**
   - 新開的 PowerShell / CCG 若沒有 `C:\Users\Administrator\AppData\Roaming\npm`，就會找不到 `gemini.cmd` 與 `claude.cmd`。
   - 已透過使用者層 PATH 修正。

2. **Codex sandbox 檔案權限限制**
   - 在 Codex sandbox 內直接讀取 `C:\Users\Administrator\AppData\Roaming\npm\*.cmd` 可能會被拒絕。
   - 這會讓 `codeagent-wrapper` 子程序回報 `gemini command not found in PATH` 或 `claude command not found in PATH`。
   - 因此在 Codex 內執行 CCG wrapper 時，需用外部 / escalated 執行。

3. **Gemini backend 的 `--progress` crash**
   - `codeagent-wrapper --lite --progress --backend gemini` 在 Windows 環境可觸發 libuv assertion failure。
   - `codeagent-wrapper --lite --backend gemini` 不加 `--progress` 可成功執行 reviewer role。
   - 目前這是 wrapper/Gemini CLI 互動路徑的限制，不應宣稱為已從 wrapper 程式碼層修復。

## 已驗證命令

外部 / escalated 環境：

```powershell
cmd.exe /c "where claude & where gemini & claude --version & gemini --version"
```

驗證結果：

- `where claude` 找到 `C:\Users\Administrator\AppData\Roaming\npm\claude.cmd`
- `where gemini` 找到 `C:\Users\Administrator\AppData\Roaming\npm\gemini.cmd`
- `claude --version` 回報 `2.1.198 (Claude Code)`
- `gemini --version` 回報 `0.49.0`

## 目前穩定 CCG reviewer 呼叫方式

Gemini reviewer：

```powershell
$env:GEMINI_CLI_TRUST_WORKSPACE='true'
$repo = 'D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.6.WorktreeRefactorLine'
$task | & 'C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe' --lite --backend gemini - $repo
```

Claude reviewer：

```powershell
$repo = 'D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.6.WorktreeRefactorLine'
$task | & 'C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe' --lite --backend claude - $repo
```

在 Codex sandbox 內呼叫上述 wrapper 時，需要 escalated execution；否則 wrapper 子程序可能無法讀取 npm shim。

## 雙模型 reviewer 結論

### Gemini reviewer

- 確認 npm 套件版本、PATH 修正、Claude backend 成功、Gemini backend 不加 `--progress` 可成功。
- 將 sandbox access denial 與 Gemini `--progress` crash 列為需要注意的 Critical 風險。

### Claude reviewer

- 指出這不是單一問題，而是三層問題：
  - PATH / npm shim
  - Codex sandbox 權限
  - Gemini `--progress` crash
- 要求不要把「外部執行可成功」誤稱為「sandbox 內已完全修復」。
- 建議未來若要根治，需在 `codeagent-wrapper` 層對 `gemini + --progress` 加 guard，或修正 wrapper / Gemini CLI 的 Windows stdout/stderr handling。

## 最終判定

- 一般 Windows 使用者終端的 `gemini/claude command not found` 已修復。
- Codex sandbox 內的 wrapper 子程序仍需 escalated execution，這是 sandbox 權限邊界，不是 npm 安裝問題。
- Gemini backend 的穩定用法是 `--lite --backend gemini`，不要加 `--progress`。
- Claude backend 的穩定用法是 `--lite --backend claude`。
