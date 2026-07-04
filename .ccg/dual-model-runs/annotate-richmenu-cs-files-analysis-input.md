We need to add detailed, complete, maintainability-focused comments to all RichMenu-related C# files in this repository.

This is a documentation-only change. Please analyze the scope and provide guidance before implementation.

Repository branch: Jesus_5.1.7.WorktreeRichMenuAddComment
Files in scope:
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


Please output:
1. Commenting strategy for production RichMenu workflow files.
2. Commenting strategy for LINE Messaging DTO/action files.
3. Commenting strategy for test/support files.
4. Any risks where comments could accidentally mislead maintainers.
5. Suggested verification after edits.
