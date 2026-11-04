# P0-13 安全數字診斷補修審查結果

**審查範圍**：僅讀取 `DeploymentCli.cs`、`DeploymentContractTests.cs`、`Program.cs`、`deployment-contract.md`、驗收文件；未執行測試、未連線、未讀取設定或金鑰。

## 結論：無 Critical／Warning，僅 2 項 Info

### 核心邏輯逐項核對（均通過）

1. **report 先行、dispose 在前**：`using var lease` 宣告於外層 `try` 內，C# 編譯器會在該宣告點展開成內層 `try/finally`，涵蓋至外層 `try` 結尾。例外往外傳播時，內層 `finally`（`lease.Dispose()`／`writeLock.Dispose()`）先執行，才會被外層 `catch (Exception exception)` 捕捉並呼叫 `report(exception)`。`Cli_DataverseFault_ShowsOnlyCodesAndReportsOriginalOnce` 中 `disposedBeforeReport = connector.DisposeCount == 1` 在 `report` 回呼內斷言成立，證實此順序（`DeploymentCli.cs:94-101,166-180`）。
2. **只讀 ErrorCode、有界迭代**：`WriteDataverseFaultCodes`（`DeploymentCli.cs:190-212`）只存取 `fault.Detail`／`current.ErrorCode`／`current.InnerFault`，未觸碰 `Message`、`Reason`、`TraceText`、`ErrorDetails`、`Data`。迴圈條件 `index < 4 && current is not null`，無論是否循環，最多執行 4 次，不需要 HashSet，與規格一致。
3. **格式正確**：`ErrorCode` 是 `Int32`，`.ToString("X8")` 直接作用在 32 位元，不會因提升為 Int64 產生符號延伸；`int.MinValue→0x80000000`、`0→0x00000000`、`1→0x00000001`、`int.MaxValue→0x7FFFFFFF`，與測試期待的四碼逐一相符。
4. **獨立 try/catch、不妨礙原回報**：`WriteDataverseFaultCodes` 外層有自己的 `try/catch` 吞掉任何例外；呼叫時機在 `report(exception)`（含其自身失敗分支 `WriteFixedError`）之後，兩者互不影響，`reporterFails=true` 案例驗證了「回報故障仍可見數字碼」。
5. **測試為真紅後綠、非假綠燈**：`Logs_DataverseFault_NeverContainSecretValues` 透過反射呼叫真實 `Program.RunCommand`，走完整的 `ExceptionDiagnostics`／`Exception.log` 路徑，斷言共用日誌與 stdout 皆不含 `Fixture.SecretValue`，同時仍能看到 `Dataverse fault code[0]`，防止「跳過回報換取數字碼」或「隱藏診斷換取無秘密」兩種取巧寫法。
6. **缺失 Detail 安全失敗**：`FaultException<OrganizationServiceFault>(null!, ...)` 時 `current is not null` 一開始即為 false，不輸出任何 `Dataverse fault code[`，不丟空參考例外，原例外仍上報、租約仍釋放。
7. **未觸及宣告排除範圍**：全差異只動到 `DeploymentCli.cs` 的一個新私有方法＋一行呼叫、測試檔新增 4 個方法（6 個案例）、規格文件與驗收文件；未改 SDK 寫入、連線、重試、驗證、Plugin 或清單程式碼，符合「只補診斷」的授權邊界。
8. **文件未失實宣稱**：驗收文件明確寫「這只補足診斷，尚未修復或驗證伺服器端拒絕原因」、「不能排除部分寫入」、範例碼標註「並非本次伺服器實際代碼」，狀態表也如實列出 verify／smoke 仍未驗證。未發現「數字診斷修補＝實機成功」的失實宣稱。

### Info（非缺陷，僅供參考）

- **`[file: SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs:718-743]`**　`Cli_DataverseFault_NestedOrCyclicChain_IsBounded(cyclic: true)` 中，循環指回的是第 4 層（`fourth.InnerFault = first`），但迴圈在輸出第 4 筆後即因 `index==4` 終止，從未真正走到被循環覆寫的那個 `InnerFault`。因此 `cyclic=true` 與 `cyclic=false` 兩個案例目前對「是否真的走訪循環邊」沒有差異、只是重複驗證同一個硬上限。不是缺陷（硬上限本身已確定性阻止任何循環造成無界走訪），只是測試對「循環」情境的覆蓋比命名顯示的弱一些；若要更貼合測試說明，可將循環點改到較淺層（例如第 2 層指回第 1 層）以真正證明提早出現的循環也不受影響，但目前寫法不影響既有 52 案例的正確性或可信度。
- **`[file: 文件/規劃設計書/實作計畫-3.3/驗收/P0_Plugin骨架佈署步驟.md]`**　格式示例 `Dataverse fault code[0]: 0x80040220` 旁已加註「此處僅為格式示例，並非本次伺服器實際代碼」，用意良好；僅提醒後續若使用者貼上真實 apply 輸出的數字碼時，文件需同步更新為實際值，避免範例碼被誤讀為已知根因（目前文字已有此風險意識，無需改動）。

## 總結

本次補修在唯讀審查下**未發現 Critical 或 Warning 等級問題**：秘密遮罩、租約釋放順序、例外責任邊界、迭代上限、測試真實性（含真實 `Program.RunCommand`／`Exception.log` 驗證）與文件狀態誠實性均通過檢查。可依使用者既有流程（重新 plan 審核後 apply）繼續，Codex 不需連線即可視為此次補修審查完成。

---
SESSION_ID: 26ab5428-5941-4fa7-98ff-09335c23d61c
