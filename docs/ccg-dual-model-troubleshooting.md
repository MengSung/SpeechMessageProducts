# CCG 雙模型（Gemini + Claude）故障排除手冊

> 2026-07-02 完整修復後整理。**下次雙模型又出問題時，先讀這份，不要從零診斷。**
> 歷史教訓：同樣的問題群在 2026-06-26 ～ 07-02 之間反覆「修好又壞」了至少 4 次，
> 因為每次只修了五個根因中的一個，且只在「環境剛好正常」的 session 驗證。

## ⚡ 快速分診表（先對症狀，再看對應章節）

| 你看到的錯誤訊息（精確特徵） | 根因 | 修法 |
|---|---|---|
| `gemini/claude command not found in PATH`（wrapper 有啟動） | R1 Codex 沙箱 | 檢查 `~/.codex/config.toml` 有 `[windows] sandbox = "elevated"` |
| `cleanupOldLogs: ... path resolution failed: Access is denied.` | R1 Codex 沙箱 | 同上 |
| `Assertion failed: !(handle->flags & UV_HANDLE_CLOSING)`（gemini，有 Session-ID 後崩潰） | R2 libuv 崩潰 | 加 `--lite`，或確認 `CODEAGENT_LITE_MODE=true` 有進環境 |
| 中文 prompt 變成 `????`；reviewer 回覆「這像 prompt injection」拒答 | R3 ASCII 管線 | PowerShell 先 `$OutputEncoding = [Text.Encoding]::UTF8`（profile 應已自動做） |
| `Gemini CLI is not running in a trusted directory`（exit 55） | R4 目錄信任 | 環境變數 `GEMINI_CLI_TRUST_WORKSPACE=true`（模板已內嵌前綴） |
| `Not logged in · Please run /login` / `No API key available`（claude） | R5 認證失效 | `claude auth login --claudeai`，然後 `claude auth status` 確認 `loggedIn=true` |
| gemini 回覆變成 Trellis 工作流程說明而不是審查結果 | 專案 `.gemini` hooks 蓋掉 reviewer prompt | 從暫存目錄跑 gemini 審查，不要用專案目錄當 workdir |
| 健康測試叫模型「請只回答 OK」被拒 | 不是故障 | 測試 prompt 要問真實小問題，reviewer 會拒絕無實質內容的蓋章請求 |

30 秒健康檢查（任一新 PowerShell 執行）：

```powershell
"LITE=$env:CODEAGENT_LITE_MODE TRUST=$env:GEMINI_CLI_TRUST_WORKSPACE ENC=$($OutputEncoding.EncodingName)"
claude auth status   # 期望 loggedIn=true
gemini --version; claude --version
```

三個值應為 `LITE=true TRUST=true ENC=Unicode (UTF-8)`。若是空的 → profile 沒載入（見 F2 的 OneDrive 陷阱）。

## 五大根因（R1–R5）

**R1 — Codex 桌面版非提升沙箱**：碰不到 AppData 與 %TEMP%，npm shims（gemini.cmd/claude.cmd 都在 `%APPDATA%\npm`）完全無法解析 → 歷次記錄裡所有「command not found in PATH」都是這個，不是真的沒安裝。修復 = codex config `[windows] sandbox = "elevated"`。

**R2 — wrapper Web UI 模式下 gemini 崩潰**：非 `--lite` 時 wrapper 開 Web UI/轉接 stdout，gemini（Node）在 stdio 重導環境觸發 libuv console-handle assertion。修復 = 一律 lite 模式。

**R3 — PowerShell 5.1 管線編碼**：`$OutputEncoding` 預設 US-ASCII，管線餵給原生執行檔的中文全變 `?`。Git Bash（heredoc）不受影響；只有 PowerShell 管線中招。

**R4 — gemini headless 目錄信任**：headless 模式對未信任目錄直接 fatal exit 55。**已實測：`~/.gemini/trusted-folders.json` 在 headless 完全不生效（連精確路徑都被拒），只認 `GEMINI_CLI_TRUST_WORKSPACE=true` 或 `--skip-trust`。**

**R5 — claude 認證**：`~/.claude/.credentials.json` 的 accessToken/refreshToken 曾變空字串（2026-07-01，一次性事件，成因不明，疑似多程序併發刷新）。

## 永久修復清單（F1–F5，多層防護，缺一層仍有他層）

| 層 | 位置 | 內容 |
|---|---|---|
| F1 HKCU 環境變數 | 登錄檔 User 層 | `CODEAGENT_LITE_MODE=true`、`GEMINI_CLI_TRUST_WORKSPACE=true`（新啟動的 GUI/process 生效；已在跑的舊 process 拿不到） |
| F2 PowerShell profile | `C:\Users\Administrator\OneDrive\文件\WindowsPowerShell\profile.ps1` | `$OutputEncoding=UTF8` + 兩個 env var。**陷阱：Documents 被 OneDrive 重導到「文件」，寫到原生 `C:\Users\Administrator\Documents\...` 不會被載入** |
| F3 Git Bash profile | `~/.bashrc`（由 `~/.bash_profile` 委派） | export 同樣兩個 env var；Claude Code Bash tool 的快照由此初始化 |
| F4 CCG 模板 | `~/.claude/commands/ccg/*.md` + `~/.claude/.ccg/engine/**.md`（22 檔 46 處） | 呼叫改為 `GEMINI_CLI_TRUST_WORKSPACE=true .../codeagent-wrapper.exe --progress --lite --backend ...` — 舊環境快照的 session 照模板跑也安全 |
| F5 Codex 沙箱 | `~/.codex/config.toml` | `[windows] sandbox = "elevated"` |

wrapper 本身：`~/.claude/bin/codeagent-wrapper.exe`（v5.11.1），exit 127 = backend 指令找不到、124 = 逾時、55 = gemini 目錄信任。

## 🚫 死路清單（實測無效，下次不要再試，省 token）

1. `~/.gemini/trusted-folders.json` — headless 模式不讀（讀了原始碼+實測兩種路徑格式確認）。
2. Machine PATH / HKLM 修改 — Claude Code 的 shell 非提升，`Requested registry access is not allowed`；而且對 R1（沙箱連檔案都拒讀）根本無效。
3. 把 profile 寫到原生 `Documents\WindowsPowerShell\` — OneDrive 重導後 PowerShell 不讀那裡。
4. 對已在跑的 session 設 HKCU 環境變數就期待生效 — 舊 process 的環境是啟動時快照，必須靠 F2/F3/F4 層補。
5. 健康測試用「請只回答 OK」 — Claude reviewer 會視為 injection/蓋章請求拒答，浪費一輪。

## 殘餘風險（外部因素，無法根絕）與 runbook

- claude OAuth 失效 → `claude auth login --claudeai`。
- Gemini API 暫時性 500/配額 → 重試；不是設定問題。
- **升級 `ccg-workflow` npm 套件會重生模板 → F4 消失**，但 F1–F3 仍涵蓋所有情境；如要恢復 F4，批次把 `--progress --backend` 換成 `GEMINI_CLI_TRUST_WORKSPACE=true ... --progress --lite --backend`。

## 標準驗證程序（修完必跑，缺一不可）

1. 盤點：上面的「30 秒健康檢查」。
2. **全新 shell** 測試（不是在目前 session 直接跑！歷史上的假陽性都來自「目前 session 環境剛好正常」）：

```powershell
# 存成 UTF-8 的 task.txt（問真實問題），然後：
powershell.exe -File test.ps1   # test.ps1 內容：讀 task.txt → 管線餵 wrapper --progress --backend gemini/claude
```

3. 驗收標準：無 `Web UI:` 行（lite 生效）、中文完整往返、exit 0、兩個 backend 都要測。
4. 最壞情境：從 Codex session 或舊 Claude session 用 CCG 模板原樣跑一次。

## 修復歷程時間線（供考古）

- 06-26 ~ 06-29：wrapper 缺失 → CLI 缺失，多次審查被迫「僅本機驗證」。
- 07-01 晚：裝了 gemini-cli/claude-code CLI；claude token 空字串 → auth login 修復；當晚雙模型都完成過正式審查（詳見 `.ccg/tasks/payment-module-extraction/review.md`）。
- 07-02 早：Codex session 再踩 R1/R2/R4 →「又壞了」。當日完成 R1–R5 全根因分析與 F1–F5 多層修復，最壞情境驗證通過。
- 對應 Claude memory：`ccg-dual-model-review-working.md`（Claude session 會自動載入摘要）。
