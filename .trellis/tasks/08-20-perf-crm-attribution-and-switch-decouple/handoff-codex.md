Active task: .trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple

工作樹：D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree
分支：feat/dataverse-scoped-connection（目前乾淨，HEAD = 537430bc）

## 先讀，再動手

依序讀完這三份，它們是本次的契約：

1. .trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/prd.md
2. .trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/design.md
3. .trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/implement.md

照 implement.md 的 Step 1 → 2 → 3 → 4 → 5 順序執行，逐項打勾。

## 兩件事，一句話版本

**問題 1（核心）**：Trace.log 裡 84 行 `[Perf]` 全部是 `crm{n=0,ms=0}`，沒有一行例外。
原因是 `ToolUtilityClass.Core.cs:94-98` 先把 service 存進
`m_Crm2011OrganizationService`，再用它建 `_facade`；`TimedToolUtilityProvider`
事後只換掉了那個欄位，而 `_facade` 早已捕獲未裝飾的原始參考。
`ToolUtilityPartials/` 內 `_facade.` 用了 158 次，`m_Crm2011OrganizationService.` 只用 1 次
——裝飾器裝錯地方了。修法是把裝飾移到 DI 解析點（design.md 一）。

**問題 2**：`Startup.cs:158` 讓 `SessionDiagnosticsSwitch` 綁在
`DiagnosticsTrace:Enabled` 上，而 Development 是 `true`。
於是「開 Trace.log」和「開 51 行 Session 診斷」是同一個開關，
B1 的降噪在實際環境從未生效（Trace.log 12,115 行中 10,673 行、88% 是這些噪音）。
修法是拆出獨立的 `SessionVerbose`，預設 false（design.md 二）。

## 不要做的事

- 不要碰 `SpeechMessageProducts.ChurchReport/Tools/Analyze-ChurchReportTraces.ps1`。
  它已定版，SHA-256 必須維持
  C131E43EB048B8904DF51CDFD601407E6286B0DC61E45949D52C21A292D7302B，
  且**必須保留 UTF-8 BOM**——該檔含 185 行繁體中文，
  移除 BOM 會讓 Windows PowerShell 5.1 以 cp950 解碼而全部變成亂碼。
  之前那條「UTF-8 without BOM」規則只適用於 .cs，不適用於這個 .ps1。
- 不要改 `ensureMin` 在登入路徑同步等待的行為（另案）。
- 不要處理 `CHURCH_REPORT_TRACE.TXT` 從未產生的問題（待釐清）。
- 不要順手修 `Line.Messaging/LineMessagingClient.cs:840` 的 CS1572 等範圍外警告。
- 不要 commit。做完交由使用者確認。
- 不要把任何 `.ccg/dual-model-runs/` 的暫存審查產物加進 commit
  （上一批 de4c1710 誤納 19 個，事後才用 537430bc 移除）。

## 實跑驗收（Step 4，不可跳過）

程式改完**必須實跑一次**收 trace，這是 AC-1 / AC-2 唯一的證據來源。
關鍵前置，順序不可顛倒：

1. 確認應用程式行程沒在跑
2. 把 `D:\除錯追蹤\Trace.log` 和 `dataverse-trace.jsonl` **改名移走，不要清空**
   （兩者是 Append 模式；現有那份混了 6 次啟動，整檔行數是無意義的累積量）
3. 先暖機 CRM 端點再啟動——已知冷啟動約 4.0 秒、暖機後約 0.05 秒，
   不暖機會把冷啟動誤記成程式問題
4. 啟動 → 登入 → 操作幾個會查 CRM 的頁面 → **正常關閉**（不可直接砍行程，
   否則驗不到 flush 與 `Cleaning up trace listener`）
5. 跑分析器產生新的 ChurchReport-Trace-Report.md
6. 填完 implement.md Step 4.9 的整張數字表 + 4.10 的三筆逐請求交叉比對

## 回報要求

- Step 4.9 / 4.10 的**原始數字**必須全部附上。取不到就寫「未取得」，**不得估算**。
- 明確標示每條 AC 是通過 / 未通過 / 未取得。
- 任何 AC 沒過就直接寫沒過。**不得調整門檻或改寫驗收條件來湊過。**
- 若 `[Perf] crm.n` 與 JSONL `crmCount` 有系統性落差，如實回報落差幅度，
  並說明是否存在不經 `IOrganizationService` 的 CRM 路徑
  （design.md 已把這列為已知限制，不是失敗）。
- 若 Gemini / Claude reviewer 跑失敗，附實際 stderr，
  **不得宣稱「雙模型審查通過」**。已知既有故障：runner 以空值傳入
  `--setting-sources` 導致 `claude` exit 1，屬工具問題、非本任務範圍。

## 完成定義

implement.md 的 Step 1–5 全部打勾，且報告內含 Step 4.9 完整數字表。
未實跑收 trace = 未完成，不接受只有「build 成功、測試通過」的回報。
