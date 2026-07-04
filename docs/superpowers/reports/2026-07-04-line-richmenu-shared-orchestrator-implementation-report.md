# LINE RichMenu Shared Orchestrator 實作報告

## 目標

本階段將 LINE RichMenu 從一般 LINE workflow 中獨立成可重用的共用核心，放在 `LineMessagingProcessor.RichMenus`。設計目標是讓未來 ASP.NET Core 產品可以直接引用 RichMenu 共用模組，並在各產品內只實作自己的選單 catalog、使用者分群 policy、儲存層與畫面流程。

核心原則：

- RichMenu 共用專案只處理 LINE RichMenu API 與可重用流程。
- ChurchReport 的 CRM、Controller、View、資料庫與產品規則仍留在 ChurchReport。
- 未來產品只需要提供產品自己的 catalog / policy / state store，不需要重寫建立、上傳、連結、解除連結、文字觸發與到期清理流程。
- 程式邊界要清楚，避免特殊情況散落在各處，符合易管理與低耦合的重構方向。

## 專案分層

- `Line.Messaging`：底層 LINE Messaging API SDK。
- `LineMessagingProcessor`：既有 LINE processor wrapper，提供底層 LINE API 呼叫能力。
- `LineMessagingProcessor.RichMenus`：新增的 RichMenu 共用核心，包含 workflow、catalog contract、assignment、provisioning、cache、state store、文字觸發、orchestrator、到期清理與 action helper。
- `LineMessagingProcessor.AspNetCore`：ASP.NET Core DI 註冊入口，提供 `AddLineRichMenus(...)` 與 `AddLineRichMenuProvisioning<TCatalog>()`。
- `ChurchReport`：只保留 ChurchReport 專用接入，例如 Controller、產品 catalog、產品 policy、CRM/畫面流程與維護功能。

## 本階段已完成的主要修改

### 1. 新增 RichMenu 共用專案

新增 `LineMessagingProcessor.RichMenus`，集中管理 RichMenu 共用能力：

- `ILineRichMenuProcessor` / `LineMessagingProcessorRichMenuAdapter`：把 RichMenu 共用核心與既有 `LineMessagingProcessorClass` 隔離，核心只依賴抽象介面。
- `ILineRichMenuWorkflow` / `LineRichMenuWorkflow`：統一處理建立 RichMenu、上傳 PNG、連結使用者、解除連結與刪除遠端選單。
- `LineRichMenuDefinition` / `ILineRichMenuCatalog` / `StaticLineRichMenuCatalog`：讓各產品以 catalog 方式提供選單定義。
- `ILineRichMenuIdCache` / `InMemoryLineRichMenuIdCache`：快取 menu key、alias、richMenuId、fingerprint，避免上層散落 provider id 對照邏輯。
- `ILineRichMenuProvisioningWorkflow` / `LineRichMenuProvisioningWorkflow`：負責同步 catalog 到 LINE，包含 fingerprint 判斷與 alias 維護。
- `ILineRichMenuAssignmentWorkflow` / `LineRichMenuAssignmentWorkflow`：負責依 menu key 指派或解除使用者 RichMenu。
- `IRichMenuPolicy` / `IRichMenuOrchestrator` / `RichMenuOrchestrator`：讓產品以 policy 決定使用者應套用哪個選單，避免在產品端寫大量 if/else。
- `ILineRichMenuTextTriggerResolver` / `LineRichMenuTextTriggerResolver`：支援依使用者輸入文字切換 RichMenu。
- `IRichMenuStateStore` / `InMemoryRichMenuStateStore`：提供目前的共用狀態儲存抽象，未來產品可替換為資料庫或 Redis。
- `IRichMenuExpirationSweepWorkflow` / `RichMenuExpirationSweepWorkflow`：支援到期選單還原或清理。
- `RichMenuActionFactory`：集中建立 LINE RichMenu action，避免各產品重複手寫 action 結構。

### 2. RichMenu workflow 從舊 Workflows 專案移出

原本 RichMenu 相關 workflow 位於 `LineMessagingProcessor.Workflows`，本階段已搬到 `LineMessagingProcessor.RichMenus`，並確認舊 `LineMessagingProcessor.Workflows` 與 `LineMessagingProcessor.Workflows.Tests` 不再殘留 RichMenu workflow 型別。

這樣做的原因是 RichMenu 的生命週期、catalog、assignment、文字觸發與到期清理會持續擴張，若繼續放在一般 workflow 專案會讓責任混在一起。獨立後的邊界更清楚，也方便未來產品只引用 RichMenu 能力。

### 3. ASP.NET Core DI 註冊拆分

`LineMessagingProcessor.AspNetCore` 現在提供兩層註冊：

- `AddLineRichMenus(...)`：註冊產品中立的 RichMenu 共用服務，不要求產品一定要先提供 catalog。
- `AddLineRichMenuProvisioning<TCatalog>()`：在產品需要 provisioning 時，再註冊產品自己的 catalog 與 provisioning workflow。

這個拆分可避免未來產品只想用 assignment、text trigger 或 orchestrator 時，卻因為尚未註冊 catalog 而在 `ValidateOnBuild` 失敗。

另外，`IRichMenuOrchestrator` 已改為 explicit factory 註冊，明確指定使用 policy-based constructor，避免 Microsoft DI 因為 `RichMenuOrchestrator` 有多個 public constructor 而發生建構子歧義。

### 4. 移除不必要專案相依

`LineMessagingProcessor.RichMenus.csproj` 已移除對 `LineMessagingProcessor.Workflows` 的專案參考。RichMenu 共用核心目前只保留必要相依：

- `Line.Messaging`
- `LineMessagingProcessor`

這使 RichMenu 專案不再依附舊 workflow 專案，邊界更乾淨。

### 5. ChurchReport 接入方式

ChurchReport 仍保留自己的產品流程，只接上共用 LINE / RichMenu 能力：

- `ChurchReport/Tools/LineUtilityClass.cs`
- `ChurchReport/Tools/PushUtility.cs`
- `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs`

ChurchReport 的 CRM、付款、奉獻、Controller 與畫面邏輯沒有被放入 RichMenu 共用核心。這符合「共用核心只做共用能力，產品規則留在產品」的邊界。

## 對未來產品整合的幫助

未來如建設公司維修系統、協會會員系統、發票收款系統等 ASP.NET Core 產品，需要 RichMenu 時可採用以下模式：

1. 引用 `Line.Messaging`、`LineMessagingProcessor`、`LineMessagingProcessor.RichMenus`、`LineMessagingProcessor.AspNetCore`。
2. 在 ASP.NET Core DI 中呼叫 `AddLineMessagingProcessor(...)` 與 `AddLineRichMenus(...)`。
3. 由產品實作自己的 `ILineRichMenuCatalog`，定義產品需要的 RichMenu。
4. 由產品實作自己的 `IRichMenuPolicy`，依角色、狀態、文字輸入或業務資料決定要套用哪個 RichMenu。
5. 若需要持久化狀態，替換 `IRichMenuStateStore`，例如改成資料庫或 Redis。
6. 若需要啟動時同步選單，呼叫 `ILineRichMenuProvisioningWorkflow`。
7. 若需要處理文字觸發，呼叫 `IRichMenuOrchestrator.HandleTextAsync(...)` 或使用 `ILineRichMenuTextTriggerResolver`。
8. 若需要到期還原或清理，排程呼叫 `IRichMenuExpirationSweepWorkflow`。

這讓未來產品不必重寫 LINE RichMenu API 細節，只需要填入產品自己的規則與資料來源。

## 邊界驗證

已完成下列邊界檢查：

- `LineMessagingProcessor.RichMenus` 沒有 `ChurchReport`、`Microsoft.Xrm`、`IOrganizationService`、`DbContext`、`Controller`、`IActionResult` 等產品或框架耦合字串。
- `LineMessagingProcessor.Workflows` 與 `LineMessagingProcessor.Workflows.Tests` 不再殘留 RichMenu workflow 型別。
- `LineMessagingProcessor.RichMenus.csproj` 不再引用 `LineMessagingProcessor.Workflows`。
- `LineMessagingProcessor.RichMenus` 與測試檔已清除亂碼註解，並確認為 UTF-8 without BOM。

## 驗證結果

已執行並通過：

- `dotnet build .\LineMessagingProcessor.RichMenus\LineMessagingProcessor.RichMenus.csproj -v minimal -p:UseSharedCompilation=false`
- `dotnet test .\LineMessagingProcessor.RichMenus.Tests\LineMessagingProcessor.RichMenus.Tests.csproj -v minimal -p:UseSharedCompilation=false`：13 passed。
- `dotnet test .\LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj -v minimal -p:UseSharedCompilation=false`：3 passed。
- `dotnet test .\LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal -p:UseSharedCompilation=false`：33 passed。
- `dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LineSharedWorkflow|FullyQualifiedName~PushUtilityWorkflow" -v minimal -p:UseSharedCompilation=false`：28 passed。
- `dotnet build .\ChurchReport\ChurchReport.csproj -v minimal -p:UseSharedCompilation=false`：build succeeded，0 warning，0 error。

備註：`ChurchReport.MemberInfo.Tests` 在建置時仍有既有 xUnit analyzer warning `MemberInfoScopeGuardTests.cs`，本階段未修改該測試，且不影響 RichMenu 抽離驗證。

## CCG 雙模型自我修復補強

本階段也補強 CCG Gemini + Claude 雙模型分析 / REVIEW 的穩定性：

- 新增 `docs/scripts/Test-CcgDualModelHealth.ps1`。
- 新增 `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`。
- 新增 `docs/ccg-dual-model-health-permanent-fix.md`。
- 更新全域 CCG analyze / review 模板，使未來雙模型分析與 review 預設先執行健康檢查、自動修復可修復的本機問題，然後再重試 Gemini + Claude。

若 Claude 或 Gemini 是 provider 額度、session limit、登入或外部服務中斷，runner 會明確標示 `quotaBlocked=true`，不會誤判成本機工具壞掉，也不會假裝雙模型 review 成功。

## 後續建議

- 若要正式 merge，建議再跑一次 CCG Gemini + Claude review；若 Claude 額度不足，可記錄 quota blocker，並以 Gemini + 本機驗證結果先推進。
- 下一階段可補 ChurchReport 實際 RichMenu catalog 與產品 policy，將管理者、一般使用者、付款、維修、會員等角色切換規則逐步接上。
- 若未來產品需要跨機器或跨站台共用狀態，建議新增資料庫或 Redis 版 `IRichMenuStateStore`。