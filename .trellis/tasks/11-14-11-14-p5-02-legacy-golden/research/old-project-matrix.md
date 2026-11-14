# P5-02 舊方案盤點摘要

2026-11-14 13:00 Asia/Taipei (UTC+08:00)：以工作樹外核定來源 `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.其他解決方案\Dynamics365Line` 唯讀盤點第一層專案。排除 `.vs`、bin、obj、packages、backup、worktree；未複製來源、不輸出來源本文或秘密。

## 結果

- 實際第一層 `.csproj` 為 **37** 個，不是 05A 文字中的 36；差異保留，不以刪除或合併方式湊數。
- 30 個專案未命中簡單連外 API 指標；7 個專案命中 `HttpClient`、`WebRequest`、`WebClient`、`RestClient`、`Socket`、`TcpClient`、LINE／Bearer／token 相關字樣之一。此為盤點指標，不等於已證明可安全執行。
- 所有後續安全分類必須以 PluginHarness 的來源掃描與 Roslyn 編譯結果為準；命中不可攔截連外 API 時分類 `BlockedByNetworkApi`，不得執行舊程式。
- 盤點 JSON 為研究證據，含專案名稱、來源檔數與布林指標，不含來源內容、token、LINE ID 或個資。

## 逐案清單

| 專案 | csproj | C#檔數 | 連外指標 |
|---|---|---:|:---:|
| AllotPlugIn | AllotPlugIn.csproj | 4 | False |
| AllotTlsWorkflow | AllotTlsWorkflow.csproj | 8 | True |
| AllotWorkflow | AllotWorkflow.csproj | 5 | True |
| AppointmentPlugIn | AppointmentPlugIn.csproj | 1 | False |
| AppointmentSigning | AppointmentSigning.csproj | 2 | False |
| AppointmentSigningLibrary | AppointmentSigningLibrary.csproj | 3 | False |
| AppointmentWorkflow | AppointmentWorkflow.csproj | 1 | False |
| CalculateQrCodeScanNumberWorkflow | CalculateQrCodeScanNumberWorkflow.csproj | 3 | False |
| DiscipleLessonsPluIn | DiscipleLessonsPluIn.csproj | 3 | False |
| Dynamics365Line | Dynamics365Line.csproj | 0 | False |
| EquipmentWorkflow | EquipmentWorkflow.csproj | 3 | False |
| ExampleWorkflow | ExampleWorkflow.csproj | 3 | False |
| FeePushLineWorkflow | FeePushLineWorkflow.csproj | 3 | False |
| HappyBirthdayWorkflow | HappyBirthdayWorkflow.csproj | 5 | False |
| Line.Messaging | Line.Messaging.csproj | 135 | True |
| LineMessagePlugIn | LineMessagePlugIn.csproj | 27 | False |
| LineNotifyLibrary | Line.NotifyBot.csproj | 13 | True |
| LineUtility | LineUtility.csproj | 4 | True |
| NewPersonCareWorkflow | NewPersonCareWorkflow.csproj | 5 | False |
| NewPersonCareWorkflow-ForDebug | NewPersonCareWorkflow-ForDebug.csproj | 8 | False |
| PersonalQrCodeWorkflow | PersonalQrCodeWorkflow.csproj | 3 | False |
| PresentRecordLinePlugIn | PresentRecordLinePlugIn.csproj | 3 | False |
| QrCodeWorkflow | QrCodeWorkflow.csproj | 4 | True |
| RecursiveWorkflow | RecursiveWorkflow.csproj | 4 | False |
| StorLessonSigningLibrary | StorLessonSigningLibrary.csproj | 5 | False |
| StorLessonSigningWorkflow | StorLessonSigningWorkflow.csproj | 2 | False |
| StorLessonSignningPlugIn | StorLessonSignningPlugIn.csproj | 2 | False |
| SundayQrCodeWorkflow | SundayQrCodeWorkflow.csproj | 3 | False |
| TemplatePlugIn | TemplatePlugIn.csproj | 3 | False |
| ToolUtility | ToolUtility.csproj | 6 | True |
| Trace | Trace.csproj | 3 | False |
| TwoWingsPlugIn | TwoWingsPlugIn.csproj | 3 | False |
| WeeklyReportLinePlugIn | WeeklyReportLinePlugIn.csproj | 3 | False |
| WeeklyReportPlugIn | WeeklyReportPlugIn.csproj | 3 | False |
| WeeklyReportWorkflow | WeeklyReportWorkflow.csproj | 5 | False |
| WorkflowTemplate | WorkflowTemplate.csproj | 1 | False |
| YangMeillcStorLessonsPlugIn | YangMeillcStorLessonsPlugIn.csproj | 3 | False |

