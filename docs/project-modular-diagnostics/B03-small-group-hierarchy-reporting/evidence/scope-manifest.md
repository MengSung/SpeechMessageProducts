# B03 Scope Manifest

Status: LOCAL_DIAGNOSIS_COMPLETE_CCG_PENDING
Module: B03
Workspace: docs/project-modular-diagnostics/B03-small-group-hierarchy-reporting/
Mode: DIAGNOSIS_ONLY
Target worktree: D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion
Branch observed: 1.0.0.1.EvenVersion

## Map Contract

Authoritative map: docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md.

- B03 summary/dependencies: module-boundaries-and-optimization-map.md:153.
- B03 primary owner paths: module-boundaries-and-optimization-map.md:266-304.
- B03 exclusions: X02A-X02C cache/profiling, B04C QR, B01 login/auth at module-boundaries-and-optimization-map.md:306-310.
- Gate state: B03 is gate-blocked at module-boundaries-and-optimization-map.md:878 and has no directly attributable existing test suite at module-boundaries-and-optimization-map.md:888.

## Confirmed Primary Owner Files

- SpeechMessageProducts.ChurchReport/Models/AreaLeader.cs
- SpeechMessageProducts.ChurchReport/Models/BestRecord.cs
- SpeechMessageProducts.ChurchReport/Models/ChartData.cs
- SpeechMessageProducts.ChurchReport/Models/ChartDataList.cs
- SpeechMessageProducts.ChurchReport/Models/ChurchRoot.cs
- SpeechMessageProducts.ChurchReport/Models/ClassName.cs
- SpeechMessageProducts.ChurchReport/Models/ContextDictionary.cs
- SpeechMessageProducts.ChurchReport/Models/HappyGroupDataManager.cs
- SpeechMessageProducts.ChurchReport/Models/HappyGroupListClass.cs
- SpeechMessageProducts.ChurchReport/Models/HappyGroupWeeklyReport.cs
- SpeechMessageProducts.ChurchReport/Models/HappyGroupWeeklyReportListClass.cs
- SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs
- SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs
- SpeechMessageProducts.ChurchReport/Models/MultiGroupChartData.cs
- SpeechMessageProducts.ChurchReport/Models/MultiGroupChartDataList.cs
- SpeechMessageProducts.ChurchReport/Models/MultiGroupList.cs
- SpeechMessageProducts.ChurchReport/Models/RaceLeader.cs
- SpeechMessageProducts.ChurchReport/Models/SameNameElement.cs
- SpeechMessageProducts.ChurchReport/Models/ShepherdMethod.cs
- SpeechMessageProducts.ChurchReport/Models/ShepherdMethods.cs
- SpeechMessageProducts.ChurchReport/Models/SmallGroup.cs
- SpeechMessageProducts.ChurchReport/Models/SmallGroupData.cs
- SpeechMessageProducts.ChurchReport/Models/SmallGroupDataList.cs
- SpeechMessageProducts.ChurchReport/Models/SmallGroupReportData.cs
- SpeechMessageProducts.ChurchReport/Models/SpiritLeader.cs
- SpeechMessageProducts.ChurchReport/Models/WeeklyReportData.cs
- SpeechMessageProducts.ChurchReport/Models/WeeklyReportRecord.cs
- SpeechMessageProducts.ChurchReport/Views/Home/_GeneralGroupGrids.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/_GeneralGroupUploadButton.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/_HappyGroupGrid.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/_HappyGroupUploadButton.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/_HappyGroupWeekSelection.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/_IndividualReportGrid.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/_IndividualReportUploadButton.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/_WeeklyReportJournal.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/ChurchRoot.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/DetailGrid.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/HappyGroup.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/HappyGroupWeeklyReport.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView_Clean.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/MultiGroupView.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/SmallGroupMemberList.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/SmallGroupReportView.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/WeeklyReport.cshtml
- SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/AssignSmallGroupController.cs
- SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/ShepherdMethodLookupController.cs
- SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SpiritLeaderLookupController.cs
- SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Cache.cs
- SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Core.cs
- SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs
- SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.DataApi.cs
- SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Date.cs
- SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.IntegrateView.cs
- SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs
- SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.MultiGroupView.cs
- SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs
- SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.ViewBag.cs
- SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupReportController.cs
- SpeechMessageProducts.ChurchReport/Services/Caching/ISmallGroupCacheManager.cs
- SpeechMessageProducts.ChurchReport/Services/Caching/SmallGroupCacheManager.cs
- SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs
- SpeechMessageProducts.ChurchReport/ViewModels/WeeklyReportViewModel.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadHappyGroup.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Core.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Identity.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Setup.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/HappyGroupUtility.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Assignment.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.AsyncWrapper.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Contact.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Converters.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Core.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.FollowUp.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.HappyGroup.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.WeeklyReport.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/WeeklyReportManager.cs
- SpeechMessageProducts.ChurchReport/wwwroot/css/ReportChart.css
- SpeechMessageProducts.ChurchReport/wwwroot/css/SmallGroupReportController.css
- SpeechMessageProducts.ChurchReport/wwwroot/css/WeeklyReport.css

## Dependencies And Consumers To Record, Not Edit

- F03A CRM operations: ToolUtility/Dataverse query and write helpers used by weekly-report and upload flows.
- B01 auth/session: global authorization and session fallback protect routes; B03 relies on session-scoped state.
- B02 member/contact identity: B03 reads and mutates member/contact/list relationships.
- B04/B06 flows: attendance, QR, reference-list, and church-hierarchy flows consume or influence small-group data.
- X02A cache: B03 owns small-group cache policy; shared cache engine/lifetime remains X02A.
- X03 shared UI: B03 owns its business pages/assets; shared layout/vendor/component infrastructure remains X03.

## Tests And Gate State

No restore/build/test/codegen/format/migration/package install or generated-output command was run. B03 remains diagnosis-only until a module validation gate exists.

## Write Scope

Allowed writes for this pass are this B03 workspace and B03-prefixed `.ccg/dual-model-runs/**` artifacts only. Product code is read-only.
