# LineUtilityClass Best-Effort SDK Message Convergence

## Requirements

- Continue converging existing ChurchReport LINE call sites toward reusable shared LINE workflow/processor modules.
- Keep ChurchReport CRM, ToolUtility, template setup, and product-specific message statistics inside ChurchReport.
- Keep shared LINE projects product-agnostic.
- Do not remove the legacy `LineUtilityClass` API in this slice.
- Route safe user push methods through `ILineNotificationWorkflow.SendAsync(...)` when workflow is injected.
- Preserve legacy fallback behavior when workflow is not injected.
- Do not change reply, multicast, rich menu, CRM sender parsing, or synchronous legacy fire-and-forget methods in this slice.

## Acceptance Criteria

- `LineUtilityClass` has a constructor path that accepts `ILineNotificationWorkflow` without breaking the existing constructor.
- Safe push methods for text, SDK message list, image, video, audio, location, sticker, template, flex, confirm, and imagemap can route through the shared workflow.
- Workflow routing is centralized in one helper.
- Tests prove workflow routing for representative message types and legacy fallback for an existing constructor path.
- `LineMessagingProcessor.Workflows.Tests` pass.
- `ChurchReport.sln` builds.