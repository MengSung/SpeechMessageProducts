# 修復 CCG Gemini/Claude reviewer backend 啟動問題

## 使用者問題

CCG 外部 reviewer backend 再次出現：

- `gemini command not found in PATH`
- `claude command not found in PATH`
- PATH 內殘留不存在的 `C:\Users\Administrator\AppData\Roaming\Claude\claude-code\2.1.92`
- `C:\Users\Administrator\AppData\Roaming\npm` 沒有 `claude` 或 `gemini` shim

## 驗收標準

- Gemini CLI 可以被執行並回報版本。
- Claude CLI 可以被執行並回報版本。
- `codeagent-wrapper` 可以分別用 `--backend gemini` 與 `--backend claude` 成功啟動 reviewer role。
- 若一般 wrapper 模式在 Windows 下仍有已知崩潰，需記錄可工作的穩定呼叫方式。
- 結論需清楚區分 PATH、PowerShell ExecutionPolicy、wrapper 模式三種不同問題。
