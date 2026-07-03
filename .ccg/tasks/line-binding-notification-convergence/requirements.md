# LINE Binding Notification Convergence

## Requirement

SmallGroupController.LineLogin still creates LineMessagingProcessorClass directly for LINE binding prompts. Move that product flow behind a ChurchReport service so controllers do not construct shared LINE processors and future ASP.NET Core products can reuse the product-agnostic processor/workflow pattern.

## Acceptance Criteria

- SmallGroupController.LineLogin no longer calls new LineMessagingProcessorClass or NotifyLineBinding directly.
- ChurchReport-specific binding URL and message text stay in ChurchReport.
- Shared LINE projects remain product-agnostic.
- Existing redirect behavior for fullName ending with `(Line)` is preserved.
- Tests cover URL/message composition and controller delegation where practical.
