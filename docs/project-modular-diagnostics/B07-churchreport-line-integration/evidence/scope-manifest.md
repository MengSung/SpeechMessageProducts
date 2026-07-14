# B07 Scope Manifest - ChurchReport LINE Integration

## Scope
- Workspace: docs/project-modular-diagnostics/B07-churchreport-line-integration/
- Nested agent count: 0
- B07 owns ChurchReport-specific LINE binding/admin notification, profile adapter, push/reply facade, and legacy RichMenu catalog.
- B07 excludes LINE SDK internals, generic workflow internals, B01 OAuth/session/login, B02 member master data, and B05 payment decision logic except as dependency/consumer context.

## Boundary Evidence
- docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:475-494 lists B07 owner files and exclusions.
- B01 explicitly excludes ChurchReport push/reply/profile adapter.
- B05 explicitly excludes LINE push/reply facade; B05 owns only when/what payment notification content is sent.

## Primary Owner Files Inspected Read-Only
- SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs
- SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs
- SpeechMessageProducts.ChurchReport/Services/IChurchReportLineBindingNotificationService.cs
- SpeechMessageProducts.ChurchReport/Tools/ChurchReportLegacyRichMenuCatalog.cs
- SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs
- SpeechMessageProducts.ChurchReport/Tools/PushUtility.cs
- SpeechMessageProducts.ChurchReport/Tools/ReplyUtility.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/LineBindingUtility.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs
- SpeechMessageProducts.ChurchReport/Views/Home/BindingResultView.cshtml

## Dependency Context
- F04 LINE HTTP/model contract is dependency only.
- F05A processor interface is dependency only.
- F06 notification/reply workflow is dependency only.
- F07 RichMenu workflow is dependency only.
- B01, B02, and B05 are consumer/dependency context only.

## Test Context
- ChurchReport.MemberInfo.Tests/Payments/PushUtilityTests.cs maps to B07.
- ChurchReport.MemberInfo.Tests/ReplyUtilityGroupRoomProfileAdapterTests.cs maps to B07.
- ChurchReport.MemberInfo.Tests/LineSharedWorkflow/** maps to B07 unless superseded by B05 payment workflow ownership.