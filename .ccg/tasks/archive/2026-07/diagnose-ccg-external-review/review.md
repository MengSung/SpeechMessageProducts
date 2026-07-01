# 外部 Gemini/Claude CCG Review 無法執行診斷

## 結論

外部 Gemini/Claude CCG review 無法執行不是因為 CCG 完全不存在，而是同時有兩個執行鏈問題：

1. `AGENTS.md` 內的 review 範本是 Bash 語法，包含 `<<'EOF'` heredoc、背景執行 `&`、`wait`。目前工作 shell 是 PowerShell，直接貼上會在 PowerShell 語法解析階段失敗。
2. 即使用 PowerShell here-string 改寫 stdin 輸入，`codeagent-wrapper.exe` 仍會失敗，因為它要呼叫的 backend 指令 `gemini` 與 `claude` 目前不在 PATH。

## 已確認存在的項目

- `$HOME` 是 `C:\Users\Administrator`。
- `C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe` 存在，且 `--help` 可以正常執行。
- CCG reviewer prompt 存在：
  - `C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md`
  - `C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md`

## 失敗證據

PowerShell 直接執行 Bash heredoc 範本時，錯誤為：

```text
重新導向運算子後面遺失檔案規格。
'<' 運算子保留供未來使用。
```

使用 PowerShell 原生 here-string 餵給 wrapper 後，wrapper 可以啟動，但 backend 找不到：

```text
gemini command not found in PATH
claude command not found in PATH
```

`Get-Command gemini`、`Get-Command claude`、`where.exe gemini`、`where.exe claude` 也都找不到命令。

## 次要現象

`cleanupOldLogs: ... Access is denied` 是 wrapper 清理暫存 log 時的權限訊息，不是主要阻斷點；真正讓 review 中止的是 backend command not found。

PowerShell 直接呼叫 `npm` 也會被 `npm.ps1` 的 ExecutionPolicy 擋住；若要查 npm 套件需用 `npm.cmd`。目前 `npm.cmd list -g --depth=0` 顯示全域 npm 套件是空的，因此 Gemini/Claude CLI 不像是已透過 npm 全域安裝。
