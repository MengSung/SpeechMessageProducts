## 分析完成

已逐行核對 `DynamicsProfileRuntimeManager.cs`、`DynamicsProfileRuntime.cs`、`MultiProfileRuntimeTests.cs` 與 spec 文件，並將完整分析輸出到 `.ccg/dual-model-runs/20260729-163834-dynamics-multi-profile-runtime-drain-recovery-analyzer/claude-analyzer-attempt-1.stdout.md`（此目錄已有對應的 Gemini 分析結果，供雙模型比對）。

**核心結論**：

- **Root cause 確認**：`ReplaceCoreAsync` 用「是否拋出例外」而非「Runtime 實際 `State`」來決定要不要清除 `slot.Draining`，導致 `previous.DrainAndDisposeAsync(...)` 一旦拋錯，`slot.Draining = null` 那行永遠不會執行，Alias 永久失去 Replace 能力。
- **五項修正假設全部同意**，並補了一個關鍵落地細節與一個額外發現：
  - 假設 3（用 `runtime.State == Disposed` 而非例外判斷清除）是整組修正中風險最高、最容易寫錯的一步——若簡化成「catch 就清除」會導致未 Disposed 的孤兒 Runtime 洩漏，違反硬約束。
  - **額外發現（Warning）**：現有程式碼尾端排空呼叫用的是裸 `callerCancellationToken` 而非 `linkedCts.Token`，導致 Manager Shutdown 無法提前中止排空等待（有界但延遲），修正時新增的「Factory 前等待 pending Draining」步驟必須避免複製同一個 token 錯誤。
- 兩個必要 RED 測試都指出了一個容易寫錯的細節：**第二次 Replace 成功後的新 Generation 編號不是直覺的 +1**（狀態 1 是 3、狀態 2 也是 3，因為第一次 Replace 在失敗前已經把新 Generation 發布成 Active），若測試斷言假設錯誤會產生假陽性。
- 完整的 Critical/Warning/Info 分級、最小安全實作步驟（9 步）與競態案例已寫入報告檔案。

分析文件較長（約 9000 字），已存檔而非全文貼出；如需要我可以把報告重點以更精簡的形式直接貼在對話中，或針對其中某一節深入展開。

---
SESSION_ID: 49424f8e-60b2-4c98-a8f3-d72aa37da09b
