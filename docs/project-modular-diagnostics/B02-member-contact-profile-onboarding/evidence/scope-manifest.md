# B02 Scope Manifest

Module: B02
Workspace: docs/project-modular-diagnostics/B02-member-contact-profile-onboarding/
Mode: DIAGNOSIS_ONLY
Branch/worktree: 1.0.0.1.EvenVersion at D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion
Product code write status: read-only

## Primary Owner Files Confirmed

- SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs
- SpeechMessageProducts.ChurchReport/Controllers/NewPersonController.cs
- SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs
- SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs
- SpeechMessageProducts.ChurchReport/Services/Contact/IContactService.cs
- SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs
- SpeechMessageProducts.ChurchReport/Services/ContactAvatar/ContactAvatarUrl.cs
- SpeechMessageProducts.ChurchReport/Services/ContactAvatar/DefaultAvatarSvg.cs
- SpeechMessageProducts.ChurchReport/Services/FollowUp/IFollowUpService.cs
- SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoAccess.cs
- SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs
- SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs
- SpeechMessageProducts.ChurchReport/ViewComponents/MemberInfoNavViewComponent.cs
- SpeechMessageProducts.ChurchReport/ViewModels/MemberInfoDetailViewModel.cs
- SpeechMessageProducts.ChurchReport/ViewModels/MemberInfoListRowViewModel.cs
- SpeechMessageProducts.ChurchReport/ViewModels/MemberInfoRecordRows.cs
- SpeechMessageProducts.ChurchReport/ViewModels/PersonalInfomationViewModel.cs
- SpeechMessageProducts.ChurchReport/ViewModels/PersonalReportViewModel.cs
- SpeechMessageProducts.ChurchReport/ViewModels/PersonFormViewModel.cs
- SpeechMessageProducts.ChurchReport/Models/ContactMember.cs
- SpeechMessageProducts.ChurchReport/Models/Member.cs
- SpeechMessageProducts.ChurchReport/Models/NewPersonModel.cs
- SpeechMessageProducts.ChurchReport/Models/PersonalInfomationModel.cs
- SpeechMessageProducts.ChurchReport/Models/CrmTransmitModule/TransmitMemberInfomation.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/PersonalInfomatioManager.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/Converters/FollowUpConverter.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.FollowUp.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Members.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Contact.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.FollowUp.cs
- SpeechMessageProducts.ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml
- SpeechMessageProducts.ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml
- SpeechMessageProducts.ChurchReport/Views/NewPerson/NewPerson.cshtml
- SpeechMessageProducts.ChurchReport/Views/Personal/MaintainPersonInfomationView.cshtml
- SpeechMessageProducts.ChurchReport/Views/Personal/MaintainPersonInfomationView_Clean.cshtml
- SpeechMessageProducts.ChurchReport/Views/Personal/PersonalInfomationView.cshtml
- SpeechMessageProducts.ChurchReport/Views/Personal/PersonalInfomationView_fix.cshtml
- SpeechMessageProducts.ChurchReport/Views/Personal/PersonalInfomationViewWithImage.cshtml
- SpeechMessageProducts.ChurchReport/Views/Personal/PersonalReport.cshtml
- SpeechMessageProducts.ChurchReport/Views/Shared/_MemberInfoDetailPopupHost.cshtml
- SpeechMessageProducts.ChurchReport/Views/Shared/Components/MemberInfoNav/Default.cshtml
- SpeechMessageProducts.ChurchReport/wwwroot/css/Gallery.css
- SpeechMessageProducts.ChurchReport/wwwroot/css/NewPerson.css
- ChurchReport.MemberInfo.Tests/DefaultAvatarSvgTests.cs
- ChurchReport.MemberInfo.Tests/MemberInfoAccessResolverTests.cs
- ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardTests.cs

## Missing Or Empty Scope Notes

- `SpeechMessageProducts.ChurchReport/ViewModels/PersonFormViewModel.cs` is present.
- `SpeechMessageProducts.ChurchReport/Models/Member.cs` is present.
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Members.cs` is present.
- `SpeechMessageProducts.ChurchReport/Views/Personal/MaintainPersonInfomationView_Clean.cshtml` exists with 0 lines; treated as a legacy/empty owner file and not used as evidence.

## Dependencies Recorded, Not Edited

- B01 identity/session/access control: `BaseChurchController`, `GlobalAuthorizationFilter`, session and cookie identity.
- F03A CRM operations: `ToolUtility`, CRM retrieve/update, option metadata retrieval.
- B07 LINE profile transport: LINE picture URL/profile data stored on contact and used as avatar fallback.
- X01 host MVC filters and routes: global authorization/cache filters and possible anti-forgery rollout point.
- X02A/X02B cache/observability: shared cache and job status could support optimization.
- X03 shared UI: Razor views, DevExtreme grids, shared popup host, CSS.

## Consumers Recorded, Not Edited

- B03 small-group hierarchy/reporting consumes member/contact identity and group membership.
- B04A-B04C attendance, appointment/equipment, and scheduling consume contact identity.
- B05 donation/product/payment consumes contact identity and profile fields.
- B07 ChurchReport LINE integration consumes contact LINE/profile data.

## Evidence Commands Used

- `rg --files` with normalized paths to confirm B02 owner files.
- `rg -n` over B02 controllers, views, services, and connector files for `ContactId`, `CanViewContact`, `HttpPost`, anti-forgery markers, cache, CRM, and `Task.Run`.
- Read-only line inspections with `Get-Content`; no build, restore, test, package, formatting, codegen, or migration command was run.
