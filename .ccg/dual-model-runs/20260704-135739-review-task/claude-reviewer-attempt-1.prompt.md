ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# LINE RichMenu Shared Orchestrator Review Task

請以 reviewer 角色審查目前工作區 diff。

## Review 範圍

- 工作分支：Jesus_5.1.7.WorktreeRefactorRichMenu
- 目標：將 LINE RichMenu 共用能力抽離為可被未來 ASP.NET Core 產品重用的 shared orchestrator。
- 主要新增專案：LineMessagingProcessor.RichMenus、LineMessagingProcessor.RichMenus.Tests。
- 主要接線：LineMessagingProcessor.AspNetCore 的 AddLineRichMenus / AddLineRichMenuProvisioning。
- 產品端保留：ChurchReport CRM、UI、產品流程、既有 PushUtility / LineUtilityClass callsite。

## 請重點檢查

1. 是否符合 clean boundary：LineMessagingProcessor.RichMenus 不可依賴 ChurchReport、CRM、Controller、DbContext、IActionResult。
2. RichMenu provisioning / assignment / text trigger / orchestrator 是否資料流清楚、少特殊情況、不藏全域狀態。
3. DI 註冊是否穩定，不會再出現 ambiguous constructor。
4. 是否破壞既有 LINE push、reply、RichMenu 舊流程或 ChurchReport workflow。
5. 測試是否足以覆蓋 shared core 行為與產品邊界。
6. 是否有可維護性問題、命名問題、未來產品重用風險。

## 已跑過的本機驗證

- dotnet test LineMessagingProcessor.RichMenus\\LineMessagingProcessor.RichMenus.Tests.csproj：此路徑若不正確請忽略；實際測試專案為 LineMessagingProcessor.RichMenus.Tests。
- dotnet test LineMessagingProcessor.RichMenus.Tests\\LineMessagingProcessor.RichMenus.Tests.csproj -v minimal：13 passed。
- dotnet test LineMessagingProcessor.AspNetCore.Tests\\LineMessagingProcessor.AspNetCore.Tests.csproj -v minimal：3 passed。
- dotnet test LineMessagingProcessor.Tests\\LineMessagingProcessor.Tests.csproj -v minimal：33 passed。
- dotnet test ChurchReport.MemberInfo.Tests\\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LineSharedWorkflow|FullyQualifiedName~PushUtilityWorkflow" -v minimal：28 passed。
- dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false：0 errors。
- Boundary scan：RichMenus 無 ChurchReport / CRM / Controller / DbContext / IActionResult；Workflows 無 RichMenu workflow 殘留。
- Encoding check：changed text files UTF-8 without BOM + CRLF。
- Cleanup：worktree 內 bin / obj / artifacts 已清除。

## Diff stat

`	ext
 .../ChurchReport.MemberInfo.Tests.csproj           |   5 +-  .../LineSharedWorkflow/PushUtilityWorkflowTests.cs |   2 +  ChurchReport.sln                                   |  64 +++++++  ChurchReport/ChurchReport.csproj                   |  39 ++---  ChurchReport/Tools/LineUtilityClass.cs             |  86 ++++-----  ChurchReport/Tools/PushUtility.cs                  |  50 +++---  .../LineMessagingProcessor.AspNetCore.Tests.csproj |   3 +-  ...ingProcessorServiceCollectionExtensionsTests.cs |  33 ++++  .../LineMessagingProcessor.AspNetCore.csproj       |   3 +-  ...essagingProcessorServiceCollectionExtensions.cs |  63 ++++++-  .../LineRichMenuWorkflowTests.cs                   |  12 +-  .../ILineRichMenuWorkflow.cs                       |  11 +-  .../LineRichMenuCreateUploadAndLinkRequest.cs      |   7 +-  .../LineRichMenuDeleteLinkedRequest.cs             |   8 +-  .../LineRichMenuException.cs                       |   7 +-  .../LineRichMenuResult.cs                          |  15 +-  .../LineRichMenuWorkflow.cs                        |  45 ++---  .../LineMessagingProcessorClass.cs                 | 193 +++++++++++++--------  18 files changed, 434 insertions(+), 212 deletions(-)
`

## Changed files

`	ext
.ccg/tasks/ccg-dual-model-self-healing/requirements.md
.ccg/tasks/ccg-dual-model-self-healing/review.md
.ccg/tasks/ccg-dual-model-self-healing/task.json
.ccg/tasks/richmenu-shared-architecture-brainstorm/.turns.json
ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs
ChurchReport.sln
ChurchReport/ChurchReport.csproj
ChurchReport/Tools/LineUtilityClass.cs
ChurchReport/Tools/PushUtility.cs
docs/ccg-dual-model-health-permanent-fix.md
docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1
docs/scripts/Test-CcgDualModelHealth.ps1
docs/superpowers/reports/2026-07-04-line-richmenu-shared-orchestrator-implementation-report.md
LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj
LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs
LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj
LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs
LineMessagingProcessor.RichMenus.Tests/Actions/RichMenuActionFactoryTests.cs
LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs
LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs
LineMessagingProcessor.RichMenus.Tests/LineMessagingProcessor.RichMenus.Tests.csproj
LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs
LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs
LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs
LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs
LineMessagingProcessor.RichMenus.Tests/Support/RichMenuTestFactory.cs
LineMessagingProcessor.RichMenus.Tests/Triggers/LineRichMenuTextTriggerResolverTests.cs
LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs
LineMessagingProcessor.RichMenus/ILineRichMenuCatalog.cs
LineMessagingProcessor.RichMenus/ILineRichMenuIdCache.cs
LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs
LineMessagingProcessor.RichMenus/ILineRichMenuProvisioningWorkflow.cs
LineMessagingProcessor.RichMenus/ILineRichMenuTextTriggerResolver.cs
LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs
LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs
LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs
LineMessagingProcessor.RichMenus/IRichMenuExpirationSweepWorkflow.cs
LineMessagingProcessor.RichMenus/IRichMenuOrchestrator.cs
LineMessagingProcessor.RichMenus/IRichMenuPolicy.cs
LineMessagingProcessor.RichMenus/IRichMenuStateStore.cs
LineMessagingProcessor.RichMenus/LineMessagingProcessor.RichMenus.csproj
LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs
LineMessagingProcessor.RichMenus/LineRichMenuAliasNotFoundException.cs
LineMessagingProcessor.RichMenus/LineRichMenuAssignmentResult.cs
LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs
LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs
LineMessagingProcessor.RichMenus/LineRichMenuDefinition.cs
LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs
LineMessagingProcessor.RichMenus/LineRichMenuException.cs
LineMessagingProcessor.RichMenus/LineRichMenuFingerprint.cs
LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs
LineMessagingProcessor.RichMenus/LineRichMenuResult.cs
LineMessagingProcessor.RichMenus/LineRichMenuStatus.cs
LineMessagingProcessor.RichMenus/LineRichMenuSyncItem.cs
LineMessagingProcessor.RichMenus/LineRichMenuSyncOutcome.cs
LineMessagingProcessor.RichMenus/LineRichMenuSyncReport.cs
LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerOptions.cs
LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs
LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs
LineMessagingProcessor.RichMenus/RichMenuActionFactory.cs
LineMessagingProcessor.RichMenus/RichMenuContext.cs
LineMessagingProcessor.RichMenus/RichMenuDecision.cs
LineMessagingProcessor.RichMenus/RichMenuDecisionPriority.cs
LineMessagingProcessor.RichMenus/RichMenuExpirationSweepReport.cs
LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs
LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs
LineMessagingProcessor.RichMenus/RichMenuTextContext.cs
LineMessagingProcessor.RichMenus/RichMenuTextDecision.cs
LineMessagingProcessor.RichMenus/RichMenuUserState.cs
LineMessagingProcessor.RichMenus/StaticLineRichMenuCatalog.cs
LineMessagingProcessor/LineMessagingProcessorClass.cs
`

請輸出 Critical / Warning / Info 分級 review。Critical 必須是需要立即修復的問題。
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.