# F07 Scope Manifest

Module: F07 LINE RichMenu Engine
Mode: DIAGNOSIS_ONLY
Workspace: `docs/project-modular-diagnostics/F07-line-richmenu-engine/`
Nested agent count: 0

## Required Workflow/Map Reads

Required by the lead interrupt:

- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`
- `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`

Read attempt result in this checkout:

- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`: MISSING.
- `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`: MISSING.

Impact:

- The module map/workflow could not be read from disk.
- The F07 scope below is derived from the active task prompt plus direct repository inspection.
- This is recorded as a workflow-context blocker, not as permission to broaden write scope.

Batch baseline context:

- `C:\Users\Administrator\AppData\Local\Temp\module-diagnostics-batch-07-baseline.txt` was present.
- That baseline listed both missing diagnostics documents and the F07 workspace artifacts as expected untracked batch files, but the files themselves were not present in this checkout before this subagent created the F07 workspace.

## Active Task Context Availability

- Provided active task: `.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization`.
- In this checkout, `.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/` was not present when inspected.
- `.ccg/spec/` was not present.
- Trellis session/phase/package context and Trellis backend/guide indexes were read.

## Authoritative Scope Used

Because the workflow/map files were unavailable, the user prompt is the authoritative boundary source for this run.

Own:

- `LineMessagingProcessor.RichMenus/**`
- `LineMessagingProcessor.RichMenus.Tests/**`

Own F07 responsibilities:

- RichMenu catalog contracts.
- RichMenu provisioning.
- RichMenu assignment.
- Text trigger resolution/policy.
- Expiry sweep/state/cache owned by this project.
- RichMenu workflow contracts.

Read-only dependencies:

- F04 `Line.Messaging/**`
- F05A `LineMessagingProcessor/**`

Read-only consumers:

- F05B `LineMessagingProcessor.AspNetCore/**`
- B07 ChurchReport LINE integration files.
- X01 host composition if needed.

Excluded:

- ChurchReport legacy RichMenu catalog and user lookup decisions.
- General LINE SDK transport.
- Processor core internals.
- ASP.NET DI registration internals beyond exposure/lifetime evidence.
- Product-side CRM/profile decisions.

## Owned Files Inspected

Owned source files:

- `LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs`
- `LineMessagingProcessor.RichMenus/ILineRichMenuCatalog.cs`
- `LineMessagingProcessor.RichMenus/ILineRichMenuIdCache.cs`
- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs`
- `LineMessagingProcessor.RichMenus/ILineRichMenuProvisioningWorkflow.cs`
- `LineMessagingProcessor.RichMenus/ILineRichMenuTextTriggerResolver.cs`
- `LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs`
- `LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs`
- `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs`
- `LineMessagingProcessor.RichMenus/IRichMenuExpirationSweepWorkflow.cs`
- `LineMessagingProcessor.RichMenus/IRichMenuOrchestrator.cs`
- `LineMessagingProcessor.RichMenus/IRichMenuPolicy.cs`
- `LineMessagingProcessor.RichMenus/IRichMenuStateStore.cs`
- `LineMessagingProcessor.RichMenus/LineMessagingProcessor.RichMenus.csproj`
- `LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuAliasNotFoundException.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentResult.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuDefinition.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuException.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuFingerprint.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuResult.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuStatus.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuSyncItem.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuSyncOutcome.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuSyncReport.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerOptions.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerPolicy.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs`
- `LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs`
- `LineMessagingProcessor.RichMenus/RichMenuActionFactory.cs`
- `LineMessagingProcessor.RichMenus/RichMenuContext.cs`
- `LineMessagingProcessor.RichMenus/RichMenuDecision.cs`
- `LineMessagingProcessor.RichMenus/RichMenuDecisionPriority.cs`
- `LineMessagingProcessor.RichMenus/RichMenuExpirationSweepReport.cs`
- `LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs`
- `LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs`
- `LineMessagingProcessor.RichMenus/RichMenuUserState.cs`
- `LineMessagingProcessor.RichMenus/StaticLineRichMenuCatalog.cs`

Owned test files:

- `LineMessagingProcessor.RichMenus.Tests/Actions/RichMenuActionFactoryTests.cs`
- `LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs`
- `LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs`
- `LineMessagingProcessor.RichMenus.Tests/LineMessagingProcessor.RichMenus.Tests.csproj`
- `LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs`
- `LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuExpirationSweepWorkflowTests.cs`
- `LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs`
- `LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs`
- `LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs`
- `LineMessagingProcessor.RichMenus.Tests/Support/RichMenuTestFactory.cs`
- `LineMessagingProcessor.RichMenus.Tests/Triggers/LineRichMenuTextTriggerResolverTests.cs`

## Read-Only Evidence Files Inspected

- `LineMessagingProcessor/LineMessagingProcessorClass.cs`
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs`
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessorOptions.cs`
- `Line.Messaging/ILineMessagingClient.cs`
- `Line.Messaging/LineMessagingClient.cs`
- `SpeechMessageProducts.ChurchReport/Tools/PushUtility.cs`
- `SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs`
- `SpeechMessageProducts.ChurchReport/Tools/ChurchReportLegacyRichMenuCatalog.cs`
- `SpeechMessageProducts.ChurchReport/Startup.cs`

## Local Command Boundaries

Commands used were read/search/list/status operations plus directory creation for allowed output paths:

- `python ./.trellis/scripts/get_context.py`
- `python ./.trellis/scripts/get_context.py --mode phase`
- `python ./.trellis/scripts/get_context.py --mode packages`
- `Get-Content`
- `Get-ChildItem`
- `Select-String`
- `Get-Process`
- `rg --files`
- `rg -n`
- `git status --short`
- `New-Item -ItemType Directory -Force` for the allowed F07 diagnostics workspace and `.ccg/dual-model-runs`.

Forbidden commands were not run:

- No `dotnet restore`.
- No `dotnet build`.
- No `dotnet test`.
- No package restore.
- No code generation.
- No formatting.
- No migrations.
- No benchmarks.
- No coverage.
