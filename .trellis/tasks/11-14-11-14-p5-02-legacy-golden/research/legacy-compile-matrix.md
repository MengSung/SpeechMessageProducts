# P5-02b 舊入口只編譯矩陣

## 文件目的與證據範圍

本文件由同目錄 `legacy-compile-matrix.json` 的實際編譯候選整理，供後續模組依 D5-3 分流。工具只讀核定外部 Dynamics365Line 舊根，沒有執行任何舊入口、連線 D365／LINE 或複製舊來源。Generated 僅代表編譯成功，不代表安全執行、黃金資料或實機驗收。本次沒有 Generated，不得據此宣稱 P5-02 整體完成。

## 執行方式與限制

呼叫 `LegacyGolden.PluginHarness.CompileMatrix.Create(legacyRoot)`；此 API 與 Program／HarnessEngine 無相依，不經執行流程。每個入口呼叫正式 `LegacyProjectCompiler.Compile`，using 結束立即 Dispose 清零輸出位元組。結果只保留三態、固定診斷代碼與 basename，不含來源、token、LINE ID 或絕對路徑。輸出先經現有 `OutputSecretScan.Validate`。

本次以 Windows PowerShell 反射正式 Debug/net48 EXE API；反射主機僅將 EXE 輸出目錄相依 DLL 作為 AssemblyResolve 候選，避免 PowerShell 缺少 EXE binding redirects 導致誤報。只載入受信任工具及 metadata 相依，不載入舊產物。主機退出即釋放事件解析器，不建立常駐程序。早期主機失敗候選已由正式 API 重跑結果取代。建置時另一代理仍修改 compiler／runner；本表是下述時間的實際快照，後續 compiler 修復應重新產生，不能沿用為最終驗收。

UTC 執行時間：`2026-11-14T07:32:41.4232595+00:00`。集合驗證：`true`。

## 數量與實際差異

- 第一層實際 37 個專案目錄；排除 `Dynamics365Line/Dynamics365Line.csproj` 零 C# 方案殼後，核定 36 個恰好覆蓋一次。殼只有設定、csproj 與 bin/obj，bin/obj 不作為產品來源。
- 29 個入口專案、11 個 IPlugin、18 個 CodeActivity 與 05A F11 數字一致，沒有數量矛盾。此分類來自唯讀來源宣告盤點；不是 29 個編譯成功的語意證明。
- 來源有入口宣告不表示 csproj 有納入來源：`RecursiveWorkflow`、`WorkflowTemplate` 的現行 Compile 閉包為零，因此正式 compiler 回 `NoReadableCSharpSource`，沒有假造入口或把來源掃描替代 csproj。
- 計畫簡寫修正為實際名稱：`HappyBirthdayWorkflowNamespace.HappyBirthdayWorkflowPlugIn`、`RecursiveWorkflowNameSpace.RecursiveWorkflowPlugIn`、`WeeklyReportWorkflowNameSpace.WeeklyReportWorkflow`。
- `NewPersonCareWorkflow` 與 `NewPersonCareWorkflow-ForDebug` 使用相同完整型別；以專案＋型別識別，兩者均保留。`LineNotifyLibrary` 的實際 csproj 是 `Line.NotifyBot.csproj`。
- 正式 compiler 只回入口專案第一個可辨識入口；失敗列的 `entryTypeActual` 為 null。manifest 名單不宣稱語意編譯已確認所有型別，也不宣稱來源沒有額外入口。

狀態總計：Generated **0**、CompileFailed **29**、BlockedByNetworkApi **0**。安全拒絕屬 CompileFailed，不能將所有拒絕改稱網路阻擋；本次沒有進入舊入口執行。

## 29 個入口逐列結果

| 專案 | 預期實際完整入口 | 種類 | 編譯狀態 | 固定診斷 |
| --- | --- | --- | --- | --- |
| AllotPlugIn | AllotPlugIn.AllotPlugIn | IPlugin | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| AllotTlsWorkflow | AllotTlsWorkflow.AllotTlsWorkflowClass | CodeActivity | CompileFailed | HttpTransportApiRefused:AllotApiClient.cs, CustomHttpHandlerRefused:AllotApiClient.cs, ExecutionEscapeRefused:AllotDebugTrace.cs, ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| AllotWorkflow | AllotWorkflow.AllotWorkflowClass | CodeActivity | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| AppointmentPlugIn | AppointmentPlugIn.AppointmentPlugIn | IPlugin | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| AppointmentSigning | AppointmentSigning.AppointmentSigningWorkflow | CodeActivity | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| AppointmentWorkflow | AppointmentWorkflow.AppointmentWorkflowPlugin | CodeActivity | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| CalculateQrCodeScanNumberWorkflow | CalculateQrCodeScanNumberWorkflow.CalculateQrCodeScanNumberWorkflow | CodeActivity | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| DiscipleLessonsPluIn | DiscipleLessonsPluIn.DiscipleLessonsPluIn | IPlugin | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| EquipmentWorkflow | EquipmentWorkflow.EquipmentWorkflowClass | CodeActivity | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| ExampleWorkflow | ExampleWorkflow.ExampleWorkflowClass | CodeActivity | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| FeePushLineWorkflow | FeePushLineWorkflow.FeePushLineWorkflowClass | CodeActivity | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| HappyBirthdayWorkflow | HappyBirthdayWorkflowNamespace.HappyBirthdayWorkflowPlugIn | CodeActivity | CompileFailed | ExternalExecutionApiRefused:ToolUtilityClass.cs |
| LineMessagePlugIn | LineMessagePlugIn.LineMessagePlugIn | IPlugin | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| NewPersonCareWorkflow | NewPersonCareWorkflow.NewPersonCareWorkflowClass | CodeActivity | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| NewPersonCareWorkflow-ForDebug | NewPersonCareWorkflow.NewPersonCareWorkflowClass | CodeActivity | CompileFailed | ExternalExecutionApiRefused:BSUStackTrace.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs |
| PersonalQrCodeWorkflow | PersonalQrCodeWorkflow.PersonalQrCodeWorkflowClass | CodeActivity | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| PresentRecordLinePlugIn | PresentRecordLinePlugIn.PresentRecordLinePlugIn | IPlugin | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| QrCodeWorkflow | QrCodeWorkflow.QrCodeWorkflow | CodeActivity | CompileFailed | ExternalExecutionApiRefused:QrCodeUtility.cs, UnsafeExecutionApiRefused:QrCodeUtility.cs, ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| RecursiveWorkflow | RecursiveWorkflowNameSpace.RecursiveWorkflowPlugIn | CodeActivity | CompileFailed | NoReadableCSharpSource |
| StorLessonSigningWorkflow | StorLessonSigningWorkflow.StorLessonWorkflowPlugin | CodeActivity | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| StorLessonSignningPlugIn | StorLessonSignningPlugIn.StorLessonSignningPlugIn | IPlugin | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| SundayQrCodeWorkflow | SundayQrCodeWorkflow.QrCodeWorkflow | CodeActivity | CompileFailed | ExternalExecutionApiRefused:SundayQrCodeUtility.cs, UnsafeExecutionApiRefused:SundayQrCodeUtility.cs, ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| TemplatePlugIn | TemplatePlugIn.TemplatePlugIn | IPlugin | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| TwoWingsPlugIn | TwoWingsPlugIn.TwoWingsPlugIn | IPlugin | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| WeeklyReportLinePlugIn | WeeklyReportLinePlugIn.WeeklyReportLinePlugIn | IPlugin | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| WeeklyReportPlugIn | WeeklyReportPlugIn.WeeklyReportPlugIn | IPlugin | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |
| WeeklyReportWorkflow | WeeklyReportWorkflowNameSpace.WeeklyReportWorkflow | CodeActivity | CompileFailed | ExternalExecutionApiRefused:ToolUtilityClass.cs |
| WorkflowTemplate | WorkflowTemplate.WorkflowTemplatePlugin | CodeActivity | CompileFailed | NoReadableCSharpSource |
| YangMeillcStorLessonsPlugIn | YangMeillcStorLessonsPlugIn.YangMeillcStorLessonsPlugIn | IPlugin | CompileFailed | ExternalExecutionApiRefused:QPayToolkit.cs, ExecutionEscapeRefused:QPayToolkit.cs, ExternalExecutionApiRefused:ToolUtilityClass.cs, ExternalExecutionApiRefused:BSUStackTrace.cs |

## 全部 36 個專案去向

| 專案 | 矩陣角色 |
| --- | --- |
| AllotPlugIn | EntryProject |
| AllotTlsWorkflow | EntryProject |
| AllotWorkflow | EntryProject |
| AppointmentPlugIn | EntryProject |
| AppointmentSigning | EntryProject |
| AppointmentSigningLibrary | NonEntryLibrary |
| AppointmentWorkflow | EntryProject |
| CalculateQrCodeScanNumberWorkflow | EntryProject |
| DiscipleLessonsPluIn | EntryProject |
| EquipmentWorkflow | EntryProject |
| ExampleWorkflow | EntryProject |
| FeePushLineWorkflow | EntryProject |
| HappyBirthdayWorkflow | EntryProject |
| Line.Messaging | NonEntryLibrary |
| LineMessagePlugIn | EntryProject |
| LineNotifyLibrary | NonEntryLibrary |
| LineUtility | NonEntryLibrary |
| NewPersonCareWorkflow | EntryProject |
| NewPersonCareWorkflow-ForDebug | EntryProject |
| PersonalQrCodeWorkflow | EntryProject |
| PresentRecordLinePlugIn | EntryProject |
| QrCodeWorkflow | EntryProject |
| RecursiveWorkflow | EntryProject |
| StorLessonSigningLibrary | NonEntryLibrary |
| StorLessonSigningWorkflow | EntryProject |
| StorLessonSignningPlugIn | EntryProject |
| SundayQrCodeWorkflow | EntryProject |
| TemplatePlugIn | EntryProject |
| ToolUtility | NonEntryLibrary |
| Trace | NonEntryLibrary |
| TwoWingsPlugIn | EntryProject |
| WeeklyReportLinePlugIn | EntryProject |
| WeeklyReportPlugIn | EntryProject |
| WeeklyReportWorkflow | EntryProject |
| WorkflowTemplate | EntryProject |
| YangMeillcStorLessonsPlugIn | EntryProject |

## 驗證與交接

`CompileMatrixTests` 保護精確 36 專案、29 唯一入口專案與複合身份、11/18、方案殼排除、同名型別保留、未核定根完整拒絕及呼叫端修改隔離。測試不依賴舊根，不執行舊入口。真實 29 列只經正式 compiler 編譯 API，全部失敗原因保留於上表；後續需由 compiler owner 修復或依 D5-3 提供閱讀對照，不能降低攔截保護來增加成功數。

JSON 是無註解的機器證據：schemaVersion／mode／generatedMeaning 說明格式與編譯語意；runUtc 是本次時間；inventoryValid／diagnostics 是集合門檻；expectedCounts 是計畫數字、manifestCounts 是明確清單數字；projects 記錄 36 個去向，excludedShell 明記殼原因；entrypoints 保留入口專案／相對 csproj／預期型別／編譯確認型別／種類／三態／安全診斷；statusCounts 只能合計 entrypoints。失敗列不能填入猜測的 actual 型別。此研究任務目錄非產品 JSON，不修改範圍外伴隨清單。

本次驗證結果：

- `dotnet build SpeechMessage.Dynamics.Plugins.Tests/SpeechMessage.Dynamics.Plugins.Tests.csproj --no-restore -v minimal`：通過，零警告、零錯誤。
- `dotnet test SpeechMessage.Dynamics.Plugins.Tests/SpeechMessage.Dynamics.Plugins.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~CompileMatrixTests -v minimal`：四項通過，零失敗。
- 四個交付檔案的嚴格 UTF-8／無 BOM／CRLF／final CRLF 位元組檢查及矩陣集合、三態、秘密樣式檢查：通過。
- `python tools/RepoQuality/check_file_documentation.py`：未通過；1522 檔中四個非本子任務所有檔案仍有 CrlfRequired：LegacyRestTransport.cs、OutputSecretScan.cs、ScenarioRunner.cs、TransportInterception.cs。未改動這些檔案，交由其 owner 修正；本次兩個 C# 檔沒有守門錯誤。
- `git diff --check`（限定本次四檔）：通過。此子任務不執行整個測試專案，不作 commit／push；外部審查與最終 compiler 修復後重跑由主代理統籌。
