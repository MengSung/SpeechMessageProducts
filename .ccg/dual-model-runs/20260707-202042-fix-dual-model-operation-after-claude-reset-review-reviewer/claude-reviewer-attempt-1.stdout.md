## Review：CCG Dual-Model Runner 修復（`Invoke-CcgDualModelWithSelfHealing.ps1` / `Test-CcgDualModelHealth.ps1`）

已用 `git diff` 檢視兩支腳本的變更（另外兩個變更檔 `.turns.json` 為任務狀態快照、`ccg-external-review-thinking-guide.md` 為說明文件更新，僅用於確認本次修復的設計意圖，非審查範圍）。此文件也證實了 shim 的設計初衷：`codeagent-wrapper` 未提供 Claude model 旗標，因此 wrapper 啟動 `claude` 時會落到使用者全域預設模型（可能導致 "Fable 5 limit"），修復方式是「process-only」的 `claude.cmd` shim，且明確要求不可寫入 User PATH——程式碼確實遵守了這點（`$toolPathEntries`／User PATH 更新未包含 shim 目錄，只有 process-scope `$env:Path` 用 `-Prepend` 加入），這部分實作正確。

---

### 🔴 Critical

**共用暫存目錄的 shim 檔案有競爭寫入風險**
- 檔案：`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:121`（呼叫處）、`New-CcgClaudeModelShim` 定義於 `:80-114`；`docs/scripts/Test-CcgDualModelHealth.ps1:318`、定義於 `:53-87`
- 問題：兩支腳本都把 shim 寫到**同一個固定路徑** `%TEMP%\ccg-claude-model-shim\claude.cmd`，且用 `[System.IO.File]::WriteAllText(...)` 非原子方式寫入（截斷→寫入→關閉）。從 `.ccg/dual-model-runs/` 底下大量緊鄰時間戳的 `*-analyzer` / `*-reviewer` 目錄可以看到，這個系統本來就會（或很容易）並行執行多個 `Invoke-CcgDualModelWithSelfHealing.ps1`／`Test-CcgDualModelHealth.ps1` 進程。若兩個進程同時各自呼叫 `New-CcgClaudeModelShim` 覆寫同一個檔案，另一個並行進程（透過 PATH 解析呼叫 `claude.cmd`，即 shim 本身）有機會在寫入視窗期間讀到被截斷／空白的批次檔，導致該次 `claude` 呼叫整個失敗（而不只是誤判 quota），並被歸類為看似無關的 `no-usable-output` / `backend-exit-N`，反而混淆了「provider quota/session block」的分類誠實性。
- 修法：改成每個 run/PID 專屬子目錄（例如 `ccg-claude-model-shim-$PID` 或 `-$runId`），或至少改成「寫到暫存檔名 → `Move-Item -Force` 原子改名」。

---

### 🟡 Warning

1. **直接探測（direct quota probe）的 diagnostic 內容在非 quota 情境下遺失**
   - 檔案：`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:614-624`
   - 修復前：`$diagnostic = $directProbe.Output` 是不論是否為 quota block 都會設定；修復後只在 `$directProbe.QuotaBlocked` 為真時才寫入 `$diagnostic`。而後面補值邏輯（`:629-631`）也只在 `$quotaBlocked` 為真時才用 `Get-ShortDiagnostic` 補齊。結果是：Claude 因**非** quota 原因失敗（真正的 toolchain／驗證問題）時，`summary.json` 裡該 backend 的 `diagnostic` 欄位會是 `$null`，即使直接探測其實已經跑過、拿到有用輸出。原始 stdout/stderr 檔案仍有保留（`stdoutPath`/`stderrPath`），所以不是資訊全失，但 summary.json 本身的可讀性/可診斷性下降，不利於快速判斷「是本機工具鏈問題還是 provider 問題」。
   - 建議：非 quota 分支也把 `$directProbe.Output`（或至少 `Get-ShortDiagnostic` 處理過的版本）寫回 `$diagnostic`。

2. **`Get-ShortDiagnostic` 的關鍵字清單與正式 quota 判斷 pattern 不一致，可能抓不到真正相關的那一行**
   - 檔案：`Invoke-CcgDualModelWithSelfHealing.ps1:373-401`（`:389` 的 priority regex）；`Test-CcgDualModelHealth.ps1:275-287`（`:279` 的 priority regex）
   - 兩處的「優先行」regex 都**缺少** `Test-QuotaBlockedText`/`$quotaBlockedPattern`（`Invoke...ps1:299`、`Test-Ccg...ps1:204`）裡已涵蓋的關鍵字，例如 `session limit`、`rate limit`、單獨的 `429`、以及中文餘額不足字樣（`\u4f59\u989d\u4e0d\u8db3` 等）。當某個 backend 是因為這些詞被判定為 quota-blocked，但輸出文字裡沒出現 `quota|billing|payment|required|4xx status`，`Get-ShortDiagnostic` 就會落到「取前幾行原始輸出」的 fallback，很可能顯示的是無關的啟動 log，而不是真正命中 quota 判斷的那一行，削弱摘要的「誠實可讀性」。
   - 建議：兩處優先行 regex 直接重用 `$quotaBlockedPattern`（或共用同一個 pattern 常數），避免兩份關鍵字清單各自維護、逐漸漂移。

3. **`payment required.*(quota|balance|billing)` 這條新 pattern 只能同行匹配，容易漏判**
   - 檔案：`Invoke-CcgDualModelWithSelfHealing.ps1:299`、`Test-CcgDualModelHealth.ps1:204`
   - `.NET regex` 的 `.` 預設不跨行（沒有加 `(?s)`/singleline），所以 `payment required` 與 `quota|balance|billing` 必須出現在**同一行**才會命中。實務上 provider 回傳的多行 JSON／CLI 錯誤訊息常把狀態碼說明與原因拆在不同行，這條新增規則很可能無法涵蓋它原本要涵蓋的真實 HTTP 402 情境，等於新增了但形同虛設。
   - 建議：拆成兩個獨立條件（單獨比對 `payment required` / `402` 即視為 quota-blocked），或改用允許跨行的比對方式。

---

### 🟢 Info

1. **`Resolve-CcgRealClaudeCommand` 只查兩個寫死路徑，不像同檔案其他地方用 `Resolve-ExecutablePath`（含 `Get-Command` PATH 搜尋）當備援**（`Invoke-CcgDualModelWithSelfHealing.ps1:67-78`、`Test-CcgDualModelHealth.ps1:40-51`）。若真實 `claude.cmd` 裝在其他位置，`New-CcgClaudeModelShim` 會靜默回傳 `$null`、不建立 shim、也沒有任何警告記錄，行為上會悄悄退回「不注入 model」的舊行為。可接受，但建議至少在 `summary`/`notes` 中記一筆「shim 未建立」的原因。

2. **`CLAUDE_MODEL_SHIM` 只被寫入環境變數與 summary，本次 diff 範圍內沒有任何程式碼實際讀取它**——真正生效的機制是「shim 目錄被 prepend 進 `$env:Path`」，讓 wrapper 透過 PATH 查找 `claude`/`claude.cmd` 時命中 shim。`CLAUDE_MODEL_SHIM`/`CLAUDE_REAL_COMMAND` 目前純粹是給人看的除錯資訊；若 `codeagent-wrapper.exe` 本身有機會不遵循一般 PATHEXT 搜尋順序（未知其內部實作），shim 對 wrapper 呼叫路徑就不會生效。建議在下一輪驗證時，實際跑一次帶 `-RunHealthBackendSmoke` 的健康檢查，確認 wrapper 啟動的 `claude` 真的吃到了 `--model`（例如暫時讓 shim 多寫一行 log 檔驗證）。

3. 編碼／換行部分沒有問題：`*.ps1` 在 `.gitattributes` 中強制 `eol=crlf`，heredoc 內容落地後仍是 CRLF，`claude.cmd` 內容純 ASCII，UTF-8(no BOM) 寫檔不會造成 cmd.exe 解析問題；PowerShell 5.1 相容性（`::new()`、if/else 作為運算式賦值、`ProcessStartInfo.Environment` 索引寫入）與既有程式碼風格一致，未見新增相容性問題。

---

### 結論
本次修復方向正確（process-only shim、quota/billing pattern 擴充、gemini 也補上 direct probe、failureReason 分類），且確實遵守文件中「不可寫入 User PATH」的限制。主要風險集中在**共用暫存 shim 檔案的併發寫入競爭**（Critical，建議優先修），以及**quota 診斷字串在部分情境下不夠準確／遺失**（3 項 Warning，建議修但非阻斷）。未發現會導致「假成功」（把真正失敗誤報為 `ok`/`degradedFallback`）的邏輯錯誤——`degradedFallback` 只在 `-AllowSingleModelWhenQuotaBlocked` 且另一 backend 確實 `ok`（含 `producedOutput`）時才會設 `$true`，符合任務要求的誠實回退語意。

---
SESSION_ID: 0f476969-d923-4fb4-97cc-c976f09749d2
