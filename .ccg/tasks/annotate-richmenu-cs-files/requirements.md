# Requirements: Add detailed comments to RichMenu C# files

User request: 將所有關於RICHMENU的.CS檔案加入詳細完整深入的註解。

Scope:
- Worktree branch: Jesus_5.1.7.WorktreeRichMenuAddComment
- Add detailed, complete, maintainability-focused comments to all C# files whose filename or path is RichMenu-related.
- Preserve runtime behavior; this is intended as a documentation-only change.
- Prefer XML documentation comments for public/internal types and members.
- Add inline comments only where control flow, policy decisions, LINE API sequencing, idempotency, expiration handling, trigger matching, or test intent would otherwise be hard to understand.
- Avoid noisy comments that merely restate obvious syntax.

Files:
- LineMessagingProcessor.RichMenus/ILineRichMenuCatalog.cs
- LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs
- LineMessagingProcessor.RichMenus/ILineRichMenuIdCache.cs
- LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs
- LineMessagingProcessor.RichMenus/StaticLineRichMenuCatalog.cs
- LineMessagingProcessor.RichMenus/RichMenuUserState.cs
- LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs
- LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs
- LineMessagingProcessor.RichMenus/RichMenuExpirationSweepReport.cs
- LineMessagingProcessor.RichMenus/RichMenuDecisionPriority.cs
- LineMessagingProcessor.RichMenus/RichMenuDecision.cs
- LineMessagingProcessor.RichMenus/RichMenuContext.cs
- LineMessagingProcessor.RichMenus/RichMenuActionFactory.cs
- LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs
- LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs
- LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerPolicy.cs
- LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerOptions.cs
- LineMessagingProcessor.RichMenus/LineRichMenuSyncReport.cs
- LineMessagingProcessor.RichMenus/LineRichMenuSyncOutcome.cs
- LineMessagingProcessor.RichMenus/LineRichMenuSyncItem.cs
- LineMessagingProcessor.RichMenus/LineRichMenuStatus.cs
- LineMessagingProcessor.RichMenus/LineRichMenuResult.cs
- LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs
- LineMessagingProcessor.RichMenus/LineRichMenuFingerprint.cs
- LineMessagingProcessor.RichMenus/LineRichMenuException.cs
- LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs
- LineMessagingProcessor.RichMenus/LineRichMenuDefinition.cs
- LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs
- LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs
- LineMessagingProcessor.RichMenus/LineRichMenuAssignmentResult.cs
- LineMessagingProcessor.RichMenus/LineRichMenuAliasNotFoundException.cs
- LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs
- LineMessagingProcessor.RichMenus/IRichMenuStateStore.cs
- LineMessagingProcessor.RichMenus/IRichMenuPolicy.cs
- LineMessagingProcessor.RichMenus/IRichMenuOrchestrator.cs
- LineMessagingProcessor.RichMenus/IRichMenuExpirationSweepWorkflow.cs
- LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs
- LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs
- LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs
- LineMessagingProcessor.RichMenus/ILineRichMenuTextTriggerResolver.cs
- LineMessagingProcessor.RichMenus/ILineRichMenuProvisioningWorkflow.cs
- Line.Messaging/Messages/RichMenu/RichMenuBulkRequest.cs
- Line.Messaging/Messages/RichMenu/RichMenuBatchOperation.cs
- Line.Messaging/Messages/RichMenu/RichMenuAlias.cs
- Line.Messaging/Messages/RichMenu/RichMenu.cs
- Line.Messaging/Messages/RichMenu/ResponseRichMenu.cs
- Line.Messaging/Messages/RichMenu/ActionArea.cs
- ChurchReport/Tools/ChurchReportLegacyRichMenuCatalog.cs
- Line.Messaging/Messages/Action/RichMenuSwitchTemplateAction.cs
- LineMessagingProcessor.RichMenus.Tests/Triggers/LineRichMenuTextTriggerResolverTests.cs
- LineMessagingProcessor.RichMenus.Tests/Support/RichMenuTestFactory.cs
- LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs
- LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs
- LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs
- LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuExpirationSweepWorkflowTests.cs
- LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs
- LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs
- LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs
- LineMessagingProcessor.RichMenus.Tests/Actions/RichMenuActionFactoryTests.cs


Acceptance criteria:
- RichMenu-related .cs files have meaningful detailed comments.
- No behavior changes are introduced.
- Build/tests for affected projects are attempted after edits.
