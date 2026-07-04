## RichMenu Shared Orchestrator Final Post-Fix Code Review

### 1. Critical findings
**No Critical findings.**

驗證項目：
- `dotnet build LineMessagingProcessor.RichMenus/LineMessagingProcessor.RichMenus.csproj`：0 警告、0 錯誤。
- 四個 `.cs` diff（`LineRichMenuProvisioningWorkflow.cs`、`LineRichMenuWorkflow.cs`、`RichMenuExpirationSweepWorkflow.cs`、`RichMenuOrchestrator.cs`）逐行核對後，**全部改動僅為新增 XML `<remarks>` 與行內註解**，沒有任何邏輯、簽章或控制流程變更，因此不會有 DI ambiguity 或啟動失敗風險。
- `grep` 掃描 `LineMessagingProcessor.RichMenus/*.cs`：無 `ChurchReport`、`DbContext`、`IActionResult`、`SpeechMessage.Payments` 等產品依賴殘留。
- 舊路徑掃描：`HandleTextAsync`、`RichMenuTextContext`、`RichMenuTextDecision` 在共用層皆無殘留。
- `Start-CcgDualModelRun.ps1`、`Invoke-CcgDualModelWithSelfHealing.ps1` 皆以 `System.Management.Automation.Language.Parser` 解析，語法錯誤數為 0。
- exit code 邏輯核對：`ok=true` → exit 0；`degradedFallback=true` → exit 0；`quotaBlocked=true`（且無 fallback）→ exit 3；其餘 → exit 2。`Start-CcgDualModelRun.ps1` 執行後會讀 `summary.json` 並印出 `full dual-model success` / `DEGRADED FALLBACK` / `quota/session state` 三種明確狀態，不會把 degraded fallback 誤報成完整成功。

### 2. Warning findings

**W1 — `Start-CcgDualModelRun.ps1` 用 `LastWriteTime` 猜測 run 目錄，而非確定性比對**
`docs/scripts/Start-CcgDualModelRun.ps1:125-128` 用「`$resolvedOutputDirectory` 底下含 `summary.json` 且 `LastWriteTime` 最新的資料夾」來找剛剛跑完的 run，而不是用它自己算出的 `$timestamp` + `$safeTitle` + `$Role` 去精確比對 `Invoke-CcgDualModelWithSelfHealing.ps1` 產生的 `$runId`（該 runner 內部用自己的時間戳 + task 檔名重新組字串，兩者時間戳不同，見下方 W2）。多個 CCG run 前後緊接執行、或有人手動整理/複製舊 run 資料夾時，可能撈到錯誤的 `summary.json`，讓「印出的狀態」與「這次真正執行的結果」對不上。屬於可維護性風險，不影響底層 runner 本身的 exit code 正確性。
建議：讓 `Start-CcgDualModelRun.ps1` 直接比對包含它自己算出的 `taskFile` basename 的資料夾，或改讓 runner 把 `runDirectory` 路徑印到一行可解析的 stdout（如 `RUN_DIRECTORY=...`）供上層腳本擷取。

**W2 — 產生的 run 資料夾名稱有雙重時間戳，可讀性差**
實際驗證資料夾如 `.ccg/dual-model-runs/20260704-192048-20260704-192048-ccg-auto-recovery-entrypoint-smoke-reviewer/`：前段 `20260704-192048` 來自 `Invoke-CcgDualModelWithSelfHealing.ps1` 自己的 `$runId = (Get-Date)... + "-" + GetFileNameWithoutExtension($TaskFile)`，而 `$TaskFile` 本身檔名又已經含有 `Start-CcgDualModelRun.ps1` 產生的時間戳，兩層疊加造成命名冗長、難以人工瀏覽。與 W1 為同一根因（兩個腳本各自生成時間戳，沒有共用一個 run id）。
建議：讓 `Start-CcgDualModelRun.ps1` 產生 task 檔名時不含時間戳（只用 `safeTitle-Role`），時間戳統一交給底層 runner 的 `$runId` 產生；或反過來讓 runner 接受上層傳入的 run id。

**W3 — `.trellis` 思考指南未列出 exit code 3**
`.trellis/spec/guides/ccg-external-review-thinking-guide.md` 的「Mandatory Recovery Loop」與「Quick Trigger」只提到 `exit code 2`（本機工具鏈問題）與 `quotaBlocked=true`，沒有像 `docs/ccg-dual-model-health-permanent-fix.md:156-161` 一樣明確列出 `exit code 3 = quota/session 阻擋且無可用 fallback`。目前不算誤導（文字仍正確描述 quotaBlocked 語意），但兩份文件對 exit code 的顆粒度不一致，未來若有人只看 `.trellis` guide、憑 exit code 分流（例如指令碼化的 CI 判斷），可能誤把 exit 3 當成「還沒定義」的狀況去走 exit 2 的「修本機工具鏈」流程。
建議：在 `.trellis` guide 的 exit code 條列中補上 `exit code 3` 對應說明，與 `docs/ccg-dual-model-health-permanent-fix.md` 保持一致顆粒度。

其餘先前 Warning 已確認修正：
- AGENTS.md 與 `.trellis` guide 現在對 Standing Fallback Policy 文字一致（「已核准 quota/session fallback，但需標示為 degraded fallback，不可稱 full dual-model success」）。
- `RichMenuOrchestrator.cs` 註解已把具體未來產品情境（角色、租戶、會員狀態、案件階段、文字觸發、臨時活動狀態）改成抽象通用描述，未再列出任何具體產品業務語意。
- `Start-CcgDualModelRun.ps1` 執行後會主動讀 `summary.json` 並印出三態狀態文字，緩解「exit 0 混淆 full success 與 degraded fallback」的原始 Warning。

### 3. Info findings
- `.ccg/dual-model-runs/` 下大量本輪驗證產生的 run 資料夾（`20260704-1914xx`、`20260704-1920xx` 等）目前是 untracked。專案先前已有把這類 run 產出物提交進 git 的慣例（如 `20260704-135739-review-task/` 等已被 track），屬於既有 dogfooding 紀錄慣例，非本次改動的缺口，但建議在提交前確認哪些 run 資料夾確實要保留、避免過多雷同 smoke-test 產出物堆積。
- `LineRichMenuWorkflow.cs` 新增的 `<remarks>` 清楚說明它與 `LineRichMenuProvisioningWorkflow` 的差異（單次操作 vs. catalog 共用），對後續要決定用哪個 workflow 的開發者很有幫助，命名與段落結構清楚。
- `RichMenuExpirationSweepWorkflow.cs` 註解把「有 PreviousMenuKey → 還原」「無 → 解除綁定」的行為講清楚，且明確標註目前 report 只統計筆數、未來若要逐筆錯誤需擴充 report item——這類「目前只做到這裡、未來可以怎麼擴充」的註解在保母級文件中定位得宜。

### 4. 是否建議合併/提交
**建議合併/提交。** 沒有 Critical 問題，Warning 均為腳本本身可維護性與文件顆粒度的非阻斷性問題（W1/W2 不影響目前單一循序執行場景下的正確性，W3 只是文件顆粒度落差），可在下一輪或後續小修正中處理，不需要卡住這次 merge。

---
SESSION_ID: f4118f0e-b225-4c89-9e63-de68a5c12de7
