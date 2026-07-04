ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# LINE RichMenu Shared Orchestrator CCG Review Task

請以 reviewer 角色審查目前 worktree 的 RichMenu 共用化重構變更。

## 工作範圍

- 工作分支：`Jesus_5.1.7.WorktreeRefactorRichMenu`
- 主要目標：把 LINE RichMenu 能力抽離成未來 ASP.NET Core 產品可共用的核心專案。
- 新增共用專案：`LineMessagingProcessor.RichMenus`
- 新增測試專案：`LineMessagingProcessor.RichMenus.Tests`
- ASP.NET Core 註冊入口：`LineMessagingProcessor.AspNetCore`
- ChurchReport 只應保留產品端流程與既有呼叫點，不應進入 RichMenu shared core。

## 本輪已修正的 review 重點

1. `LineRichMenuProvisioningWorkflow` 不再重複開啟 PNG stream，也不再透過 `.GetAwaiter().GetResult()` 做 sync-over-async。
2. `LineRichMenuFingerprint.BuildName(...)` 改為接收已讀取的 `byte[]` 或已計算的 fingerprint，讓 provisioning 資料流清楚。
3. `RichMenuOrchestrator` 收斂成單一 public constructor，文字觸發改走 `LineRichMenuTextTriggerPolicy : IRichMenuPolicy`。
4. `RichMenuOrchestrator` 不再保留 concrete-only `HandleTextAsync` 分支；所有 RichMenu 決策統一走 policy pipeline。
5. `PushUtility` / `LineUtilityClass` 的 RichMenu 成功回傳字串從亂碼修成清楚的 `"成功"`。
6. `RichMenuTextContext` / `RichMenuTextDecision` 已移除，避免保留舊的特殊路徑模型。

## 請重點審查

### Critical

- 是否仍有 DI ambiguous constructor 或 service registration 風險。
- `LineMessagingProcessor.RichMenus` 是否誤引用 ChurchReport、CRM、Controller、DbContext、IActionResult 等產品相依。
- RichMenu provisioning 是否仍可能重複讀圖、同步等待 async、或使用錯誤 fingerprint 名稱。
- 文字觸發、角色政策、期限政策等未來產品規則是否能統一經過 policy pipeline，不需要再新增特殊分支。
- ChurchReport 既有 LINE push/reply/payment notification workflow 是否被破壞。

### Warning

- 新增 shared core 的抽象是否過度或不足。
- cache/state store 的預設 in-memory 實作是否清楚標示為可替換，而不是永久儲存。
- 測試是否能覆蓋 provisioning、assignment、text trigger、DI registration、boundary。
- 是否還有使用者可見亂碼字串或舊 API 殘留。

### Info

- 可讀性、命名、註解是否有助於未來產品整合。
- 是否符合「少特殊情況、資料流清楚、不藏全域狀態、一個東西只做一件事」。

## 已執行驗證

- `dotnet test LineMessagingProcessor.RichMenus.Tests\LineMessagingProcessor.RichMenus.Tests.csproj -v minimal`
  - 通過：13
- `dotnet test LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj -v minimal`
  - 通過：3
- `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal`
  - 通過：33
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LineSharedWorkflow|FullyQualifiedName~PushUtilityWorkflow" -v minimal`
  - 通過：28
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false`
  - 成功：0 errors
- Boundary scan:
  - `LineMessagingProcessor.RichMenus` 無 ChurchReport / CRM / Controller / DbContext / IActionResult 相依。
  - `LineMessagingProcessor.Workflows` 無 RichMenu workflow 殘留。
- Encoding check:
  - changed text files 已檢查 UTF-8 without BOM + CRLF。
- Cleanup:
  - 已清除 worktree 內 `bin/`、`obj/`、`artifacts/`。

## 輸出格式

請輸出：

1. Critical / Warning / Info 分級 findings。
2. 每個 finding 請附檔案與具體原因。
3. 若沒有 Critical，請明確寫出「未發現 Critical」。
4. 若有建議修正，請說明最小修正方案，不要建議大範圍重寫。

</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.