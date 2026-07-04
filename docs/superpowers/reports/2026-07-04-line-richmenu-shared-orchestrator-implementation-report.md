# LINE RichMenu Shared Orchestrator 實作報告

## 目標

本階段將 LINE RichMenu 從一般 LINE workflow 中獨立成可重用的共用核心，放在 `LineMessagingProcessor.RichMenus`。設計目標是讓未來 ASP.NET Core 產品可以直接引用 RichMenu 共用模組，並在各產品內只實作自己的選單 catalog、使用者分群 policy、狀態儲存與畫面流程。

核心原則：

- RichMenu 共用專案只處理 LINE RichMenu API、catalog、同步、指派、文字觸發、orchestrator 與到期還原。
- ChurchReport 的 CRM、付款、奉獻、Controller、View 與產品流程不放入共用核心。
- 未來產品只需要填入自己的 `ILineRichMenuCatalog`、`IRichMenuPolicy`、必要時替換 `IRichMenuStateStore`。
- 程式保持單一責任、低特殊情況、資料流清楚、不藏全域狀態，符合易維護與 Linus 風格原則。

## 專案分層

- `Line.Messaging`：LINE Messaging API SDK 與 RichMenu DTO / action 模型。
- `LineMessagingProcessor`：既有 LINE processor wrapper，補齊 RichMenu API 包裝。
- `LineMessagingProcessor.RichMenus`：新增 RichMenu 共用核心，包含 workflow、catalog contract、assignment、provisioning、cache、state store、文字觸發、orchestrator、到期清理與 action helper。
- `LineMessagingProcessor.AspNetCore`：ASP.NET Core DI 註冊入口，提供 `AddLineRichMenus(...)` 與 `AddLineRichMenuProvisioning<TCatalog>()`。
- `ChurchReport`：只保留 ChurchReport 的產品流程，呼叫共用 RichMenu 能力，不把產品規則塞進共用核心。

## 本階段已完成

### 1. 新增 RichMenu 共用專案

新增 `LineMessagingProcessor.RichMenus`，集中管理 RichMenu 共用能力：

- `ILineRichMenuProcessor` / `LineMessagingProcessorRichMenuAdapter`：隔離共用核心與既有 `LineMessagingProcessorClass`。
- `ILineRichMenuWorkflow` / `LineRichMenuWorkflow`：處理建立 RichMenu、上傳 PNG、連結使用者、解除連結與刪除遠端選單。
- `LineRichMenuDefinition` / `ILineRichMenuCatalog` / `StaticLineRichMenuCatalog`：讓產品以 catalog 方式提供選單定義。
- `ILineRichMenuIdCache` / `InMemoryLineRichMenuIdCache`：快取 menu key 與 richMenuId，採 copy-on-write snapshot，避免清空期間的競態。
- `ILineRichMenuProvisioningWorkflow` / `LineRichMenuProvisioningWorkflow`：同步 catalog 到 LINE，包含 fingerprint 判斷、alias 維護、default 設定與逐項失敗報告。
- `ILineRichMenuAssignmentWorkflow` / `LineRichMenuAssignmentWorkflow`：依 menu key 指派或解除使用者 RichMenu。
- `IRichMenuPolicy` / `IRichMenuOrchestrator` / `RichMenuOrchestrator`：讓產品用 policy 決定使用者應套用哪個選單。
- `ILineRichMenuTextTriggerResolver` / `LineRichMenuTextTriggerResolver`：支援依使用者輸入文字切換 RichMenu。
- `IRichMenuStateStore` / `InMemoryRichMenuStateStore`：提供可替換的使用者 RichMenu 狀態儲存抽象。
- `IRichMenuExpirationSweepWorkflow` / `RichMenuExpirationSweepWorkflow`：支援到期選單還原或清理。
- `RichMenuActionFactory`：集中建立 LINE RichMenu action，避免產品端重複手寫 action 結構。

### 2. RichMenu workflow 從舊 Workflows 專案移出

原本 RichMenu workflow 位於 `LineMessagingProcessor.Workflows`，本階段已搬到 `LineMessagingProcessor.RichMenus`。`LineMessagingProcessor.Workflows` 與 `LineMessagingProcessor.Workflows.Tests` 不再保留 RichMenu workflow 型別，讓一般通知 workflow 與 RichMenu 生命週期分開管理。

### 3. Processor 補齊 RichMenu API 包裝

`LineMessagingProcessorClass` 已補齊共用 RichMenu 核心需要的 API 包裝，例如：

- `GetRichMenuListAsync()`
- `SetDefaultRichMenuAsync(...)`
- `GetDefaultRichMenuIdAsync()` / `CancelDefaultRichMenuAsync()`
- `GetRichMenuIdOfUserAsync(...)`
- RichMenu alias CRUD 與 alias list 查詢

共用核心透過 `ILineRichMenuProcessor` 呼叫這些能力，不直接依賴產品專案或畫面流程。

### 4. ASP.NET Core DI 註冊拆分

`LineMessagingProcessor.AspNetCore` 提供：

- `AddLineMessagingProcessor(...)`：註冊 LINE client、processor、notification workflow、reply workflow，並預設註冊 RichMenu 共用服務。
- `AddLineRichMenus(...)`：註冊產品中立 RichMenu 服務，不要求產品一定要提供 catalog。
- `AddLineRichMenuProvisioning<TCatalog>()`：產品需要 provisioning 時才註冊自己的 catalog 與 provisioning workflow。

`IRichMenuOrchestrator` 使用 explicit factory 註冊，避免 DI 因多個 public constructor 產生建構子歧義。文字觸發設定也已支援後續呼叫覆蓋，避免先註冊預設值後無法更新。

### 5. Assignment 工作流補強

`LineRichMenuAssignmentWorkflow` 已補齊 spec 要求與未來產品需要的行為：

- `AssignAsync(...)`：回傳標準結果，適合可容忍失敗或要自行呈現錯誤的流程。
- `AssignOrThrowAsync(...)`：失敗時拋 `LineRichMenuException`，適合必要流程。
- `UnassignAsync(...)` / `UnassignOrThrowAsync(...)`：解除使用者 RichMenu 綁定。
- cache miss 時，若有 catalog，會依 catalog 重新計算 fingerprint 版名稱，從 LINE 線上 RichMenu 清單找回 richMenuId，並回填 cache。

這個 fallback 很重要：未來產品重啟或 in-memory cache 清空後，不會因為 cache 暫時空掉就完全無法指派，只要 LINE 上已完成 provisioning，就能重新解析。

### 6. Provisioning 工作流補強

`LineRichMenuProvisioningWorkflow` 已改成逐定義同步：

- 每個 RichMenu definition 獨立處理。
- 單一定義建立、上傳、alias、default 設定失敗時，記錄 `LineRichMenuSyncOutcome.Failed` 與 `ErrorMessage`。
- 失敗不會中斷整批同步，後續 definition 仍會繼續處理。

這符合 spec 的「整體報告呈現、單一定義失敗不中斷其餘同步」要求，也讓管理端能一次看到完整同步結果。

### 7. ChurchReport 最小接入

ChurchReport 仍保留自己的產品流程，只接上共用 LINE / RichMenu 能力：

- `ChurchReport/Tools/LineUtilityClass.cs`
- `ChurchReport/Tools/PushUtility.cs`
- `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs`

ChurchReport 的 CRM、付款、奉獻、Controller 與畫面邏輯沒有進入 `LineMessagingProcessor.RichMenus`。

## 對未來產品整合的幫助

未來如建設公司維修系統、協會會員系統、發票收款系統等 ASP.NET Core 產品，需要 RichMenu 時可採用：

1. 引用 `Line.Messaging`、`LineMessagingProcessor`、`LineMessagingProcessor.RichMenus`、`LineMessagingProcessor.AspNetCore`。
2. 在 DI 中呼叫 `AddLineMessagingProcessor(...)` 與 `AddLineRichMenus(...)`。
3. 由產品實作自己的 `ILineRichMenuCatalog`，定義不同角色或狀態需要的 RichMenu。
4. 由產品實作自己的 `IRichMenuPolicy`，依角色、狀態、文字輸入或業務資料決定要套用哪個 RichMenu。
5. 若需要持久化狀態，替換 `IRichMenuStateStore`，例如資料庫或 Redis。
6. 若需要啟動時同步選單，呼叫 `ILineRichMenuProvisioningWorkflow.SyncAsync(...)`。
7. 若需要文字觸發切換，呼叫 `IRichMenuOrchestrator.ApplyAsync(...)` 或使用 `ILineRichMenuTextTriggerResolver`。
8. 若需要到期還原或清理，排程呼叫 `IRichMenuExpirationSweepWorkflow.SweepAsync(...)`。

這讓未來產品不必重寫 LINE RichMenu API 細節，只需要提供產品自己的選單、規則與狀態儲存。

## 邊界驗證

已完成下列邊界檢查：

- `LineMessagingProcessor.RichMenus` 沒有 `ChurchReport`、`Microsoft.Xrm`、`IOrganizationService`、`DbContext`、`Controller`、`IActionResult`、`CRM` 等產品或框架耦合字串。
- `LineMessagingProcessor.Workflows` 與 `LineMessagingProcessor.Workflows.Tests` 不再殘留 RichMenu workflow 型別。
- `LineMessagingProcessor.RichMenus.csproj` 不再引用 `LineMessagingProcessor.Workflows`。
- `LineMessagingProcessor.RichMenus` 與測試檔可用 strict UTF-8 解碼。
- `LineMessagingProcessor.RichMenus` 與測試未發現 `GetAwaiter().GetResult()` 或 `.Wait(` 同步阻塞模式。

## 最新驗證結果

已於 `Jesus_5.1.7.WorktreeRefactorRichMenu` worktree 執行：

- `dotnet build .\LineMessagingProcessor.RichMenus\LineMessagingProcessor.RichMenus.csproj -v minimal -p:UseSharedCompilation=false`：成功，0 warning，0 error。
- `dotnet test .\LineMessagingProcessor.RichMenus.Tests\LineMessagingProcessor.RichMenus.Tests.csproj -v minimal -p:UseSharedCompilation=false`：16 passed。
- `dotnet test .\LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj -v minimal -p:UseSharedCompilation=false`：4 passed。
- `dotnet test .\LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal -p:UseSharedCompilation=false`：33 passed。
- `dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LineSharedWorkflow|FullyQualifiedName~PushUtilityWorkflow" -v minimal -p:UseSharedCompilation=false`：28 passed。
- `dotnet build .\ChurchReport\ChurchReport.csproj -v minimal -p:UseSharedCompilation=false`：成功，0 warning，0 error。

備註：`ChurchReport.MemberInfo.Tests` 建置時仍有既有 xUnit analyzer warning `MemberInfoScopeGuardTests.cs`，本階段未修改該測試，且不影響 RichMenu 抽離驗證。

## CCG 雙模型自我修復補強

本階段也補強 CCG Gemini + Claude 雙模型 analysis / review 流程：

- `docs/scripts/Test-CcgDualModelHealth.ps1`
- `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`
- `docs/ccg-dual-model-health-permanent-fix.md`
- `.trellis/spec/guides/ccg-external-review-thinking-guide.md`
- `AGENTS.md`

未來 CCG analyze / review 應先走自我修復 runner。runner 會修 PATH/env、設定 UTF-8、確認 wrapper 與 CLI，並區分本機可修復問題與 provider quota/session limit。若 provider 額度阻塞，會標記 `quotaBlocked=true`，不可誤報為雙模型 review 成功。

## 後續建議

- 若要正式 merge，建議再跑一次完整 CCG Gemini + Claude review；若 provider 額度不足，記錄 quota blocker，不要假裝雙模型成功。
- 下一階段可補 ChurchReport 實際 RichMenu catalog 與產品 policy，將管理者、一般使用者、付款、維修、會員等角色切換規則逐步接上。
- 若未來產品需要跨機器或跨站台共用狀態，建議新增資料庫或 Redis 版 `IRichMenuStateStore`。
## 2026-07-04 收尾更新：Assignment 例外邊界修正

本次針對外部 review 指出的 RichMenu assignment 例外邊界問題完成修正：

- `LineRichMenuAssignmentWorkflow` 將原本較寬鬆的 `TryMapException` 收斂為 `TryMapProviderException`。
- 共用層只將 LINE / provider 可預期外部錯誤轉成 `LineRichMenuAssignmentResult`：
  - `LineResponseException` → `ProviderRejected`
  - `HttpRequestException` → `ProviderUnavailable`
  - 非呼叫端主動取消的 `TaskCanceledException` → `ProviderUnavailable` / timeout
- 未知程式錯誤不再被包成 `UnexpectedError`，而是直接往外拋，避免遮住真正 bug。
- 新增 assignment / unassignment 不吞掉未知 processor exception 的回歸測試。

### 收尾驗證

已重新執行下列驗證：

- `LineMessagingProcessor.RichMenus.Tests`：30 passed。
- `LineMessagingProcessor.AspNetCore.Tests`：4 passed。
- `LineMessagingProcessor.Tests`：33 passed。
- `ChurchReport.MemberInfo.Tests` LINE / RichMenu focused filter：31 passed。
- `ChurchReport\ChurchReport.csproj` build：0 warnings / 0 errors。
- `ChurchReport.sln` build：0 warnings / 0 errors。
- `LineMessagingProcessor.RichMenus` product boundary scan：passed。
- touched files UTF-8 / mojibake scan：passed。
- CCG self-healing review：Gemini PASS；Claude 被 provider session limit 擋住，runner 正確分類為 `quotaBlocked=true`，非本機工具鏈問題。

### 對未來產品整合的意義

這次修正讓 RichMenu 共用核心維持清楚邊界：產品可以用標準 result 處理 LINE provider 錯誤，但程式錯誤不會被靜默吞掉。未來建設公司維修系統、協會會員系統、發票收款系統接入此模組時，比較容易在測試與監控中發現真正的 integration bug，而不是被統一包成模糊失敗結果。
