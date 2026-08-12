# Slice C continuation 根因基線

## 已驗證的舊 cycle 終態

歸檔終態為 `unreleasable-fail-closed`：fresh preflight 與 provision 均為 go，ExecuteFixture 產生 `child-process-failed / live-evidence-incomplete`，reconciliation 因 baseline-unprovable 停止，exact cleanup 為 go。舊 descriptor、ledger 與 fresh fixture 均已清除，且該 cycle 永遠不可重試。

## 初始假設（尚未作為修正結論）

1. child 在寫出 `live-evidence-incomplete` 後以 assertion 結束，parent 僅收到非零 exit code，可能遮蔽了可安全分類的 operation outcome。
2. DownloadListManager 會把呼叫端傳入的借用 `IOrganizationService` 寫入 factory 取得的共用 ToolUtility 欄位，違反 operation-local ownership。
3. `throw e;` 會重設 stack trace，使實際 CRM exception 的來源難以確認。

## 後續驗證方法

不讀取或輸出 CRM 原始例外。以離線 fake／test double 證實 service 不回寫、例外 stack 保留及 parent diagnostics 僅含固定分類；本機 gates 通過後才評估一次新的 CE cycle。
