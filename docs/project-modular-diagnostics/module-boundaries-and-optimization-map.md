# SpeechMessageProducts 模組邊界與獨立診斷優化地圖

## 1. 文件目的

本文件把整個版本庫切分為可管理、可追蹤、可獨立分析與診斷的模組，
並建立唯一主要擁有權規則，避免同一個檔案同時落入兩個優化範圍。

這份地圖不代表目前程式碼已經完全模組化。它同時描述：

1. 已具有實體專案邊界的共享元件。
2. 仍位於 `SpeechMessageProducts.ChurchReport` 主站中的垂直業務能力。
3. 必須跨業務能力治理，但不得吸收業務邏輯的橫切平台能力。
4. 尚無法安全判定單一職責的舊有混合檔案隔離區。

後續每個模組都必須依序進行「分析 -> 診斷 -> 核准 -> 優化」。
本文件本身不授權修改產品程式碼。

## 2. 分類範圍與不可破壞的規則

### 2.1 分類範圍

- 分類母集合是 `git ls-files` 可見的版本控制檔案。
- 新增但尚未追蹤、且預定提交的檔案，也必須在提交前套用相同規則。
- `bin/`、`obj/`、`.vs/`、測試結果、套件快取、發佈輸出等衍生檔案不列入模組擁有權。
- 目前解決方案包含 18 個專案，但版本庫另有未加入解決方案的專案、測試、工具與歷史資料；本文件一併涵蓋。

### 2.2 唯一主要擁有者

- 每個檔案只能有一個 `Primary Owner`。
- 只有本文件列出的 35 個葉節點 ID 可以成為 `Primary Owner`。
- F01、F03、F05、B04、B06、X02、X04、X05 只是管理領域名稱，
  不能直接建立整包優化工作項目。
- 主要擁有者負責該檔案的分析結論、缺陷判定、修改、測試與回滾。
- 其他模組可以是 `Dependency` 或 `Consumer`，可以閱讀與呼叫，但不能因為依賴關係宣稱共同擁有。
- 若一個優化需要修改兩個主要擁有者的檔案，必須拆成兩個模組工作項目，再建立一個整合驗證項目。
- 不允許以「所有 Controller」、「所有 Model」、「所有 CRM 程式碼」或「所有測試」作為優化模組。

### 2.3 擁有權判定順序

每個路徑由下列規則由上往下判定，第一個命中者即為唯一主要擁有者，
後續規則停止評估：

1. 本文件列出的單檔或測試例外。
2. F02、F03A/F03B/F03Q、F04、F05A/F05B、F06-F09、X02Q 的實體專案路徑。
3. ChurchReport 垂直業務葉節點 B01-B03、B04A-B04C、B05、
   B06A-B06C、B07 的明確路徑或檔名。
4. ChurchReport 橫切平台葉節點 X01、X02A-X02C、X03、
   X04A-X04B 的明確路徑或檔名。
5. `SpeechMessageProducts.ChurchReport/**` 尚未命中的檔案全部歸 X05Q。
6. 版本庫中其他尚未命中的建置與 solution 檔歸 F01A，代理與工作流檔歸 F01B，
   一般文件、工具與歷史資料歸 F01C，共用測試容器治理檔歸 F01D。

這個順序同時提供完整覆蓋與唯一結果。F03Q、X02Q、X05Q 是隔離節點，
只能執行分析、責任證明、拆分與重新分類，不得直接進行整包優化。

## 3. 版本庫拓撲

```text
SpeechMessageProducts.sln
|
+-- SpeechMessageProducts.ChurchReport
|   +-- ToolUtility
|   |   +-- PowerPlatform.Dataverse.Client
|   |   `-- Line.Messaging
|   +-- Line.Messaging
|   +-- LineMessagingProcessor
|   +-- LineMessagingProcessor.Workflows
|   +-- LineMessagingProcessor.RichMenus
|   +-- LineMessagingProcessor.AspNetCore
|   +-- SpeechMessage.Payments
|   +-- SpeechMessage.Payments.Workflows
|   `-- SpeechMessage.Payments.AspNetCore
|
+-- reusable library test projects
`-- ChurchReport.MemberInfo.Tests
```

`SpeechMessageProducts.ChurchReport` 是 ASP.NET Core 主站與 Composition Root。
LINE、付款、CRM 操作與 Dataverse 連線已有不同程度的實體拆分；ChurchReport
本身多數業務流程仍橫跨 Controllers、Models、Services、Tools、
WebServiceConnector、Views 與靜態資產。

## 4. 實體專案的唯一生命週期擁有者

「生命週期擁有者」負責專案檔、目標框架、套件、建置與是否納入 solution。
測試專案內的個別測試檔仍依第 8 節跟隨受測模組。

| 實體專案或專案族 | Solution 狀態 | 生命週期擁有者 | 說明 |
|---|---:|---|---|
| `SpeechMessageProducts.sln` | 已納入 | F01A | 解決方案拓撲與建置矩陣 |
| `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj` | 已納入 | X01 | 主站 Composition Root |
| `PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj` | 已納入 | F02 | Dataverse 連線與 SDK 基礎 |
| `PowerPlatform.Dataverse.Client/NSspi/NSspi.csproj` | 未納入 | F02 | Dataverse 驗證相依元件 |
| `ToolUtility/ToolUtility.csproj` | 已納入 | F03A | CRM 操作函式庫專案生命週期；內含 F03B/F03Q 檔案例外 |
| `ToolUtility.Tests/ToolUtility.Tests.csproj` | 未納入 | F01D | 測試容器目前為 net8.0，受測專案為 net10.0，執行閘門未成立 |
| `Line.Messaging/Line.Messaging.csproj` | 已納入 | F04 | LINE Messaging SDK |
| `Line.Messaging/Line.Messaging_Net10.csproj` | 未納入 | F04 | 與 canonical project 同為 net10.0 的重複/歷史替代定義，非目標框架變體 |
| `Line.Messaging.Tests/Line.Messaging.Tests.csproj` | 已納入 | F04 | F04 測試容器 |
| `LineMessagingProcessor/LineMessagingProcessor.csproj` | 已納入 | F05A | LINE 相容處理器核心 |
| `LineMessagingProcessor/LineMessagingProcessor_Net10.csproj` | 未納入 | F05A | 不完整的重複/歷史替代定義；缺少 canonical project 的 LINE project reference |
| `LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj` | 已納入 | F05A | F05A 測試 |
| `LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj` | 已納入 | F05B | 依賴 F05A、F06、F07 的 ASP.NET Core 組裝 adapter |
| `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj` | 已納入 | F05B | F05B 測試 |
| `LineMessagingProcessor.Workflows/LineMessagingProcessor.Workflows.csproj` | 已納入 | F06 | LINE 通知與回覆工作流 |
| `LineMessagingProcessor.Workflows.Tests/LineMessagingProcessor.Workflows.Tests.csproj` | 已納入 | F06 | F06 測試 |
| `LineMessagingProcessor.RichMenus/LineMessagingProcessor.RichMenus.csproj` | 已納入 | F07 | RichMenu 引擎 |
| `LineMessagingProcessor.RichMenus.Tests/LineMessagingProcessor.RichMenus.Tests.csproj` | 已納入 | F07 | F07 測試 |
| `SpeechMessage.Payments/SpeechMessage.Payments.csproj` | 已納入 | F08 | 付款供應商核心 |
| `LinePayCSharp/LinePayCSharp.csproj` | 未納入 | F08 | 舊有 LINE Pay 供應商實作 |
| `SpeechMessage.Payments.Tests/SpeechMessage.Payments.Tests.csproj` | 已納入 | F08 | F08/F09 共用測試容器，專案生命週期由 F08 管理 |
| `SpeechMessage.Payments.Workflows/SpeechMessage.Payments.Workflows.csproj` | 已納入 | F09 | 供應商中立付款工作流 |
| `SpeechMessage.Payments.AspNetCore/SpeechMessage.Payments.AspNetCore.csproj` | 已納入 | F09 | 付款 HTTP 宿主介面 |
| `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj` | 已納入 | F01D | ChurchReport 共用整合測試容器 |
| `Trace/Trace.csproj` | 未納入 | X02Q | 歷史追蹤實作隔離 |
| `Trace/Trace_Fixed.csproj` | 未納入 | X02Q | 歷史追蹤替代定義 |
| `Trace/Trace_Net10.csproj` | 未納入 | X02Q | 歷史追蹤替代定義 |
| `.ccg/tasks/**/docx-generator/*.csproj` | 工具專案 | F01C | 文件產生工具，不屬產品執行路徑 |

`ChurchReport.Tests` 目前只有測試原始檔，沒有可見的 `.csproj`，因此不視為獨立實體專案；
其中的效能測試檔實際測試 ToolUtility collection query，歸 F03A。

## 5. 35 個可直接派工的葉節點

上層名稱只用於看板分組。分析、診斷、優化、測試與回滾一律以本節的葉節點
ID 為最小單位。`Q` 結尾的節點是隔離節點，只能分析、拆分與重新分類。

### 5.1 Shared Foundation

| 葉節點 | 模組 | 主要職責與擁有內容 | 明確排除 | 主要依賴 |
|---|---|---|---|---|
| F01A | Solution、Build 與 CI 治理 | solution、根目錄 build 規則、Git 規則、CI、專案納管與 canonical project 決策 | 代理工作流、一般文件、產品程式 | 無 |
| F01B | AI Agent 與開發工作流治理 | `.agents`、`.ccg`、`.claude`、`.codex`、`.gemini`、`.opencode`、`.serena`、`.trellis` | 產品程式、一般產品文件 | F01A |
| F01C | 文件、工具與歷史資料 | 根目錄 `docs`、`tools`、`scratch`、`openspec`、教學產物、非執行圖片 | 專案內產品文件、測試 harness | F01A |
| F01D | 共用測試容器治理 | ChurchReport 共用 test csproj、測試 SDK/target framework、共享 fixture 與 sanity gate | 測試案例的業務內容 | F01A |
| F02 | Dataverse 連線基礎 | `PowerPlatform.Dataverse.Client/**`，包含 NSspi、驗證、連線、傳輸、SDK 包裝 | ChurchReport 查詢與業務規則 | 外部 Dataverse/CRM |
| F03A | CRM 操作函式庫 | `ToolUtility/**` 中非 F03B/F03Q 的 CRM CRUD、query、attribute、attachment、list、connection 程式 | LINE adapter、混合 facade、ChurchReport 業務規則 | F02 |
| F03B | ToolUtility LINE Adapter | `ToolUtility/LineMessaging/**`、`PushUtility.cs`、`ToolUtilityPartials/ToolUtilityClass.Line.cs` 及相應測試 | CRM facade、ChurchReport LINE 整合 | F04、F03A |
| F03Q | ToolUtility 混合 Facade 隔離 | `ToolUtility/Core/ToolUtilityFacade.cs` 等同時持有 CRM 與 LINE 狀態的混合檔案 | 不得整包優化；只允許拆分後移交 F03A/F03B | F02、F04 |
| F04 | LINE Messaging SDK | `Line.Messaging/**`、LINE API model、serialization、HTTP、錯誤與 retry contract | 收件人業務判定、ChurchReport 綁定與通知 | 外部 LINE API |
| F05A | LINE Processor Core | `LineMessagingProcessor/**` 及 core tests，processor API 與相容層 | ASP.NET DI、通知 workflow、RichMenu | F04 |
| F05B | LINE ASP.NET Core Composition Adapter | `LineMessagingProcessor.AspNetCore/**` 及測試，DI 註冊與 host 組裝 | Processor、workflow、RichMenu 的內部邏輯 | F05A、F06、F07 |
| F06 | LINE 通知與回覆工作流 | `LineMessagingProcessor.Workflows/**` 及測試，message factory、recipient validation、result normalization | ChurchReport CRM/profile 查詢、RichMenu | F04、F05A |
| F07 | LINE RichMenu 引擎 | `LineMessagingProcessor.RichMenus/**` 及測試，catalog、provisioning、assignment、trigger、expiry | ChurchReport 舊 catalog 與 user lookup | F04、F05A |
| F08 | 付款供應商核心 | `SpeechMessage.Payments/**`、`LinePayCSharp/**`、provider protocol、HTTP、signature/crypto、callback parsing | MVC、CRM、奉獻規則、LINE 通知 | 外部付款供應商 |
| F09 | 可重用付款工作流與宿主 Adapter | `SpeechMessage.Payments.Workflows/**`、`SpeechMessage.Payments.AspNetCore/**`，中立 order、acknowledgement、HTTP mapping、post-payment contract | ChurchReport route、session、CRM 寫入與通知 | F08 |

### 5.2 ChurchReport 垂直業務能力

| 葉節點 | 模組 | 主要職責 | 明確排除 | 主要依賴 |
|---|---|---|---|---|
| B01 | 身分、登入、Session 與存取控制 | 登入、LINE Login/OAuth、電話綁定、claims、session、authorization、return URL | 一般 LINE 推播、會員 CRUD | F03A、F04-F06、X01、X04A |
| B02 | 會員、聯絡人、個人資料與新朋友 | 會員範圍、個人資料、頭像、新朋友、follow-up、會員 UI | 登入框架、小組、付款 | F03A、B01、X03 |
| B03 | 小組、層級與週報 | 小組 CRUD/指派、牧區層級、週報、整合檢視、小組專用 cache policy | 通用 cache、QR、會員主檔 | F03A、B01、B02、X02A、X03 |
| B04A | 出席與 Present Record | 出席紀錄下載/上傳、present-record service 與模型 | 預約、設備、排程、QR | F03A、B01、B02 |
| B04B | 預約與設備 | 預約、設備借用、課程、設備狀態與相關 UI | 出席、排程、QR | F03A、B01、B02、X03 |
| B04C | 排程與 QR | Scheduler API/UI、個人/小組/主日 QR 產生與操作 | 小組主檔、LINE transport、出席資料主檔 | F03A、F06、B01-B04B、X03 |
| B05 | 奉獻與產品付款流程 | 奉獻輸入/稽核、payment session、host adapter、callback、CRM 寫入、付款後通知 | provider protocol、費用主檔、通用 LINE transport | F03A、F08、F09、B01、B06B、B07 |
| B06A | 清單與參照資料 | ListManagement、option metadata、map/list reference data | 費用維護、奉獻交易、教會登錄流程 | F03A、B01、X02A、X03 |
| B06B | 費用管理 | FeeManagement、fee/lesson/present fee 主檔與 UI | 奉獻付款交易、provider callback | F03A、B01、B06A、X03 |
| B06C | 教會層級與 Register | church hierarchy、register、qualification 與相關 reference flow | 小組週報、費用交易 | F03A、B01、B02、B06A |
| B07 | ChurchReport 專用 LINE 整合 | 綁定/管理員通知、profile adapter、push/reply facade、舊 RichMenu catalog | LINE SDK、通用 workflow、B01 OAuth、B05 payment decision | F04-F07、B01、B02、B05 |

### 5.3 Cross-Cutting Platform

| 葉節點 | 模組 | 主要職責 | 明確排除 | 主要依賴 |
|---|---|---|---|---|
| X01 | 主站組裝、Middleware、Routes 與 Lifetime | Program、Startup、主站 csproj、DI、route、非業務 middleware、host startup | 業務流程、監控實作、設定值 | 所有執行模組 |
| X02A | 共用 Cache 基礎 | cache interface/implementation、cache key、容量/expiry 基礎規則 | 小組專用 cache policy、logging、profiling | F03A、X01 |
| X02B | Observability、Health 與 Logging | logger provider、session monitoring、diagnostics endpoint、health/operational signal | request profiling、業務 KPI、legacy Trace | X01、X04A |
| X02C | Performance Profiling | request/startup profiler、timing filter/middleware、threshold、perf parser/monitor | cache correctness、logging provider、業務效能決策 | F02、F03A、X01 |
| X02Q | Legacy Trace 隔離 | `Trace/**` 三個未納入 solution 的歷史 project | 不得直接優化；先證明用途、consumer 與 canonical project | F01A |
| X03 | 共用 Web UI 與靜態資產平台 | shared layout/component、vendor CSS/JS、DevExtreme、Bootstrap、跨多業務前端 utility | 業務頁面與單一業務專用 asset | X01、各 B 模組 |
| X04A | Runtime Configuration 與 Secrets | appsettings、web.config、environment override、secret injection、startup validation | publish script、NuGet source、業務程式內決策 | X01 |
| X04B | Deployment 與 Package Sources | publish script、launch settings、NuGet source、部署可重現性 | runtime secret 值、業務功能 | F01A、X04A |
| X05Q | ChurchReport Legacy Boundary 隔離 | 尚未安全判定單一責任的主站混合檔案 | 不得整包優化；只允許 responsibility proof、拆分與重新分類 | 視檔案而定 |

### 5.4 ToolUtility 專案內部例外

`ToolUtility/**` 的專案預設擁有者是 F03A，但下列規則優先：

- F03B 擁有 `ToolUtility/LineMessaging/**`、`ToolUtility/PushUtility.cs`、
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs` 與
  `ToolUtility.Tests/LineMessaging/**`。
- F03Q 擁有 `ToolUtility/Core/ToolUtilityFacade.cs`，因為它同時持有 CRM service
  與 `ILineMessageService` 狀態，無法在未拆分前獨立優化。
- `ToolUtility/ToolUtility.csproj` 的生命週期歸 F03A，但其中對
  `Line.Messaging` 的 project reference 是 F03B 的建置需求。
- `ToolUtility.Tests/ToolUtility.Tests.csproj` 目前 target `net8.0`，受測
  `ToolUtility` target `net10.0`，且測試專案未納入 solution。F03A/F03B
  在 F01A/F01D 修復這個 gate 前只能進行分析與診斷。

## 6. ChurchReport 唯一路徑擁有權

### 6.1 B01 身分、登入、Session 與存取控制

主要擁有：

- `Controllers/AuthenticationController/**`
- `Controllers/PhoneBindingController.cs`
- `Models/Authentication/**`
- `Services/Authentication/**`
- `Security/**`
- `Filters/GlobalAuthorizationFilter.cs`
- `Middleware/IdentityAuditCleanupService.cs`
- `Middleware/IdentityAuditMiddleware.cs`
- `Middleware/MiniAppDetectionMiddleware.cs`
- `Middleware/SessionValidationMiddleware.cs`
- `SessionAttribute.cs`
- `Views/Authentication/**`
- `Views/Shared/_Login*.cshtml`
- `wwwroot/css/LineIdLoginView.css`
- `wwwroot/css/LineLiffView.css`
- `wwwroot/css/Login.css`
- `wwwroot/css/mini-app-safe-area.css`
- `wwwroot/js/Login.js`
- `wwwroot/js/LineIdLoginView.js`

不擁有：

- LINE HTTP 與訊息模型，歸 F04。
- LINE 通用通知工作流，歸 F06。
- ChurchReport push/reply/profile adapter，歸 B07。
- Middleware 註冊與執行順序，歸 X01。

### 6.2 B02 會員、聯絡人、個人資料與新朋友

主要擁有：

- `Controllers/MemberInfoController.cs`
- `Controllers/PersonalController*.cs`
- `Controllers/NewPersonController.cs`
- `Services/Contact/**`
- `Services/ContactAvatar/**`
- `Services/FollowUp/**`
- `Services/MemberInfo/**`
- `ViewComponents/MemberInfoNavViewComponent.cs`
- `ViewModels/MemberInfo*.cs`
- `ViewModels/Personal*.cs`
- `ViewModels/PersonFormViewModel.cs`
- `Models/ContactMember.cs`
- `Models/Member.cs`
- `Models/NewPersonModel.cs`
- `Models/PersonalInfomationModel.cs`
- `Models/CrmTransmitModule/**`
- `WebServiceConnector/NewPerson.cs`
- `WebServiceConnector/PersonalInfomatioManager.cs`
- `WebServiceConnector/Converters/FollowUpConverter.cs`
- `WebServiceConnector/DownloadIntegrateData.FollowUp.cs`
- `WebServiceConnector/DownloadIntegrateData.Members.cs`
- `WebServiceConnector/UploadIntegrateData.Contact.cs`
- `WebServiceConnector/UploadIntegrateData.FollowUp.cs`
- `Views/MemberInfo/**`
- `Views/NewPerson/**`
- `Views/Personal/**`
- `Views/Shared/_MemberInfoDetailPopupHost.cshtml`
- `Views/Shared/Components/MemberInfoNav/**`
- `wwwroot/css/Gallery.css`
- `wwwroot/css/NewPerson.css`

不擁有：

- 會員是否登入與 session 建立，歸 B01。
- 會員的 LINE profile transport 與通知 facade，歸 B07。
- 通用 CRM 操作，歸 F03A。

### 6.3 B03 小組、層級與週報

主要擁有：

- `Controllers/SmallGroupController/**`
- `Controllers/SmallGroupReportController.cs`
- `Controllers/ApiControllers/AssignSmallGroupController.cs`
- `Controllers/ApiControllers/ShepherdMethodLookupController.cs`
- `Controllers/ApiControllers/SpiritLeaderLookupController.cs`
- `Services/Caching/ISmallGroupCacheManager.cs`
- `Services/Caching/SmallGroupCacheManager.cs`
- `Tools/WeeklyReportProcessor.cs`
- `WebServiceConnector/DownloadHappyGroup.cs`
- `WebServiceConnector/DownloadIntegrateData.Core.cs`
- `WebServiceConnector/DownloadIntegrateData.Identity.cs`
- `WebServiceConnector/DownloadIntegrateData.Setup.cs`
- `WebServiceConnector/HappyGroupUtility.cs`
- `WebServiceConnector/UploadIntegrateData.Assignment.cs`
- `WebServiceConnector/UploadIntegrateData.AsyncWrapper.cs`
- `WebServiceConnector/UploadIntegrateData.Converters.cs`
- `WebServiceConnector/UploadIntegrateData.Core.cs`
- `WebServiceConnector/UploadIntegrateData.HappyGroup.cs`
- `WebServiceConnector/UploadIntegrateData.WeeklyReport.cs`
- `WebServiceConnector/WeeklyReportManager.cs`
- 小組與週報模型：`AreaLeader.cs`、`BestRecord.cs`、`ChartData*.cs`、
  `ChurchRoot.cs`、`ClassName.cs`、`ContextDictionary.cs`、
  `HappyGroup*.cs`、`InMemoryDataContextSmallGroup.cs`、`ListSmallGroupWeeklyReport.cs`、
  `MultiGroup*.cs`、`RaceLeader.cs`、`SameNameElement.cs`、
  `ShepherdMethod*.cs`、`SmallGroup*.cs`、`SpiritLeader*.cs`、
  `WeeklyReport*.cs`
- `ViewModels/WeeklyReportViewModel.cs`
- 下列 Views/Home 小組與週報頁面：
  `_GeneralGroup*`、`_HappyGroup*`、`_IndividualReport*`、`_WeeklyReportJournal.cshtml`、
  `ChurchRoot.cshtml`、`DetailGrid.cshtml`、`HappyGroup.cshtml`、
  `HappyGroupWeeklyReport.cshtml`、`IntegrateView*.cshtml`、`MultiGroupView.cshtml`、
  `SmallGroupMemberList.cshtml`、`SmallGroupReportView.cshtml`、`WeeklyReport.cshtml`
- `wwwroot/css/ReportChart.css`
- `wwwroot/css/SmallGroupReportController.css`
- `wwwroot/css/WeeklyReport.css`

不擁有：

- 通用 cache engine 與效能監控，分別歸 X02A-X02C。
- QR 產生與 QR controller，歸 B04C。
- 使用者登入與授權，歸 B01。

### 6.4 B04A 出席與 Present Record

主要擁有：

- `Services/PresentRecord/**`
- `WebServiceConnector/DownloadIntegrateData.PresentRecord.cs`
- `WebServiceConnector/UploadIntegrateData.PresentRecord.cs`
- `Views/Home/PresentFeeListView.cshtml`
- 僅承載 present-record 的 DTO、mapping 與測試。

不擁有預約、設備、排程、QR、通用 CRM API 或 Fee master data。

### 6.5 B04B 預約與設備

主要擁有：

- `Controllers/AppointmentController.cs`
- `Controllers/EquipmentController.cs`
- `WebServiceConnector/AppointmentsDownUpLoader.cs`
- `WebServiceConnector/DownloadEquipment.cs`
- `WebServiceConnector/EquipmentStatusCalculator.cs`
- `Models/Appointment.cs`
- `Models/AppointmentsListManager.cs`
- `Models/EquipmenSmallGroup.cs`
- `Models/EquipmentContact.cs`
- `Models/EquipmentDataManager.cs`
- `Models/EquipmentRootClass.cs`
- `Models/EquipmentStorLessons.cs`
- `Models/InMemoryAppointmentsDataContext.cs`
- `Models/Lesson.cs`
- `Views/Equipment/**`

不擁有出席紀錄、排程 controller、QR 產生或共用前端套件。

### 6.6 B04C 排程與 QR

主要擁有：

- `Controllers/QrCodeController.cs`
- `Controllers/SchedulerController.cs`
- `Controllers/ApiControllers/SchedulerDataController.cs`
- `Services/SundayCalculator.cs`
- `Services/WeeklyScheduleSettings.cs`
- `Tools/PersonalQrCodeUtility.cs`
- `Tools/QrCodeUtility.cs`
- `Tools/SmallGroupQrCodeUtility.cs`
- `Tools/SundayQrCodeUtility.cs`
- `Models/HolidayClass.cs`
- `Models/PollManager.cs`
- `Models/PollModel.cs`
- `Views/QrCode/**`
- `Views/Home/Scheduler.cshtml`
- `Views/Home/SchedulerView.cshtml`
- `wwwroot/css/Scheduler.css`

不擁有 LINE transport、小組主檔、出席資料主檔或預約/設備流程。

### 6.7 B05 奉獻、費用付款與產品付款流程

主要擁有：

- `Controllers/DedicationController.cs`
- `Controllers/DedicationAuditController.cs`
- `Controllers/DonationPaymentLoginController.cs`
- `Controllers/MyPayController.cs`
- `Controllers/PaymentReturnController.cs`
- `Controllers/TSPGController.cs`
- `Payments/**`
- `Services/Donation/**`
- `Services/DonationBookingService.cs`
- `Services/DonationContactCreationService.cs`
- `Services/DonationContactService.cs`
- `Services/DonationCreditCardProfileService.cs`
- `Services/DonationDedicationFeeFormService.cs`
- `Services/DonationFeeQueryService.cs`
- `Services/DonationKeyInDedicationService.cs`
- `Services/DonationLoginContactService.cs`
- `Services/DonationPaymentFormBuilder.cs`
- `Services/DonationPaymentModelAssembler.cs`
- `Services/DonationPaymentSubmissionService.cs`
- `Services/PaymentCallbackLogger.cs`
- `Services/PaymentCrmService.cs`
- `Services/PaymentFeeTypeHelper.cs`
- `Services/PaymentMessageBuilder.cs`
- `Services/PaymentNotificationService.cs`
- `Tools/DonationFeePaymentProcessor.cs`
- `Tools/DonationPaymentDebugLogger.cs`
- `Tools/DonationPaymentResultHelper.cs`
- `Tools/RecurringDonationPaymentProcessor.cs`
- `WebServiceConnector/DedicationInfo.cs`
- `WebServiceConnector/DonationPaymentProcessor/**`
- 奉獻與付款模型：`CreditCard.cs`、`DedicationBooking.cs`、`DedicationFee.cs`、
  `DedicationInfoModel.cs`、`DedicationModel.cs`、`DonationPaymentFormModel.cs`、
  `DonationPaymentManager.cs`、`PayPage.cs`、`PayPageResponse.cs`、`ProductItem.cs`
- `Views/Dedication/**`
- `Views/DedicationAudit/**`
- `Views/MyPay/**`
- `Views/PaymentReturn/**`
- 付款相關 Views/Home：`DedicationInofView.cshtml`、`DedicationView.cshtml`、
  `DonationPaymentLogin.cshtml`、`PaymentError.cshtml`、`PaymentSuccess.cshtml`
- `wwwroot/css/arch-dedication.css`
- `wwwroot/css/DonationPaymentView.css`
- `wwwroot/js/ActionButtonCentering.js`

不擁有：

- 付款供應商 HTTP、簽章與 callback protocol，歸 F08。
- 中立付款工作流與 ASP.NET adapter，歸 F09。
- Fee/List 主檔維護，歸 B06A/B06B。
- LINE push/reply facade，歸 B07；B05 只擁有「何時通知與通知內容」。

### 6.8 B06A 清單與參照資料

主要擁有：

- `Controllers/ListManagementController.cs`
- `Services/ListManagement/**`
- `Services/OptionSetMetadataService.cs`
- `Utilities/OptionSetConverter.cs`
- `WebServiceConnector/ChurchListDataProcessor.cs`
- `WebServiceConnector/DownloadListManager.cs`
- `Models/ListManagementDataManager.cs`
- `Models/ListManager.cs`
- `Models/MapData.cs`
- `Models/MapDataList.cs`
- `Views/Home/ListManagement.cshtml`
- `Views/Home/ListManagementDistrictPastor.cshtml`

不擁有：

- 奉獻交易、付款 session 與 callback，歸 B05。
- 小組階層與週報，歸 B03。
- Fee master data，歸 B06B。
- 通用 CRM 操作，歸 F03A。

### 6.9 B06B 費用管理

主要擁有：

- `Controllers/FeeManagementController.cs`
- `WebServiceConnector/FeeDownUpLoader.cs`
- `Models/Fee.cs`
- `Models/FeeList.cs`
- `Views/FeeManagement/**`
- `Views/Home/FeeManagerView.cshtml`
- `Views/Home/FeeView.cshtml`
- `wwwroot/js/FeeDataGridAjax.js`

`Ajax.js`、`DataGridAjax.js`、`DropDownBox.js`、`LoadPanel.js` 與 `SelectDate.js`
被多個業務能力共同使用，因此歸 X03，不由 B06B 擁有。

### 6.10 B06C 教會層級與 Register

主要擁有：

- `Models/RegisterManager.cs`
- `WebServiceConnector/RegisterConnector.cs`
- `Views/Home/Register.cshtml`
- `Views/Home/QualificationView.cshtml`

若後續 caller/callee map 證明其他 church hierarchy 檔案只服務此流程，
由 X05Q 經 responsibility proof 後移交，不依檔名直接推定。

### 6.11 B07 ChurchReport 專用 LINE 整合

主要擁有：

- `Services/ChurchReportLineAdminNotificationService.cs`
- `Services/ChurchReportLineBindingNotificationService.cs`
- `Services/IChurchReportLineBindingNotificationService.cs`
- `Tools/ChurchReportLegacyRichMenuCatalog.cs`
- `Tools/LineUtilityClass.cs`
- `Tools/PushUtility.cs`
- `Tools/ReplyUtility.cs`
- `WebServiceConnector/LineBindingUtility.cs`
- `WebServiceConnector/LineNotifyUtility.cs`
- `Views/Home/BindingResultView.cshtml`

不擁有：

- B01 的登入、OAuth 與 session 決策。
- B05 的付款結果與通知內容決策。
- F04-F07 的 SDK、處理器、工作流與 RichMenu 引擎。

### 6.12 X01 主站組裝、Middleware、Routes 與 Lifetime

主要擁有：

- `SpeechMessageProducts.ChurchReport.csproj`
- `Program.cs`
- `Startup.cs`
- `Startup.Caching.cs`
- `Extensions/ArrayPoolExtensions.cs`
- `Extensions/AsyncEnumerableExtensions.cs`
- `Filters/StrictNoCacheFilter.cs`
- `Middleware/StaticRequestPathHelper.cs`
- `Middleware/WebCacheDeceptionMiddleware.cs`
- `Scripts/Migrate-ControllerSplit-Phase1.ps1`
- `Scripts/Update-ViewRoutes*.ps1`

X01 可以註冊其他模組的服務，但不取得其業務實作擁有權。DI lifetime、
middleware order 與 route compatibility 由 X01 驗證。

### 6.13 X02A 共用 Cache 基礎

主要擁有：

- `Services/Caching/CacheKeys.cs`
- `Services/Caching/CacheService.cs`
- `Services/Caching/ICacheService.cs`

不擁有：

`Services/Caching/ISmallGroupCacheManager.cs` 與 `SmallGroupCacheManager.cs`
屬 B03，因為它們承載小組專用 cache policy，不屬 X02A。

### 6.14 X02B Observability、Health 與 Logging

主要擁有：

- `Logging/**`
- `Controllers/DiagnosticsController.cs`
- `Middleware/SessionMonitoringMiddleware.cs`
- `Services/Monitoring/**`

X02B 的獨立驗證必須包含 logger output、health/diagnostic response、
hosted service start/stop 與敏感資料遮罩。

### 6.15 X02C Performance Profiling

主要擁有：

- `Diagnostics/**`
- `Controllers/PerformanceController.cs`
- `Filters/PerfTimingActionFilter.cs`
- `Middleware/PerformanceMonitoringMiddleware.cs`
- `Middleware/PerfProfilingMiddleware.cs`
- `Services/Performance/**`
- `Tools/CachePerformanceMonitor.cs`
- `Tools/parse-perf-log.ps1`

`ChurchReport.Tests/PerformanceTests/CollectionQueryServiceAsyncTests.cs`
實際測試 F03A 的 `CollectionQueryService`，不屬 X02C。

### 6.16 X02Q Legacy Trace 隔離

主要擁有 `Trace/**`。目前三個 project 均未納入 solution，且沒有已證明的
runtime consumer。只允許：

1. 確認是否仍被載入或參考。
2. 決定 canonical project。
3. 建立可執行測試後移交 X02B，或核准淘汰。

### 6.17 X03 共用 Web UI 與靜態資產平台

主要擁有：

- `Views/_ViewImports.cshtml`
- `Views/_ViewStart.cshtml`
- `Views/Shared/_Layout.cshtml`
- `Views/Home/_LoadingPanelPartial.cshtml`
- `Views/Home/_LoadPanelComponent.cshtml`
- `Views/Home/_ToastComponents*.cshtml`
- `Views/Home/_UploadButtonPartial.cshtml`
- `wwwroot/lib/**`
- `wwwroot/assets/**`
- `wwwroot/css/devextreme/**`
- `wwwroot/js/devextreme/**`
- `wwwroot/css/chinese-support.css`
- `wwwroot/css/MasterDetail.css`
- `wwwroot/css/Site*.css`
- `wwwroot/js/Ajax.js`
- `wwwroot/js/DataGridAjax.js`
- `wwwroot/js/DropDownBox.js`
- `wwwroot/js/LoadPanel.js`
- `wwwroot/js/SelectDate.js`
- `wwwroot/favicon.ico`
- `wwwroot/_references.js`
- `.bowerrc`
- `bower.json`
- `Filters/ThemeViewDataFilter.cs`
- `Services/Theme/**`

業務模組擁有其頁面與專用 CSS/JS。X03 只擁有共用 UI contract 與 vendor
資產，因此 UI 優化不會把所有業務功能綁成單一工作項目。

`wwwroot/js/TreeView.js` 沒有找到 view 引用，暫歸 X05Q；不得因位於
`wwwroot/js` 就推定為共用資產。

本機路徑驗證結果：

- `wwwroot/css` 根目錄只有 16 個業務/共用 CSS，沒有 `dx.*`、CLDR 或其他
  DevExtreme vendor 檔。
- `wwwroot/js` 根目錄只有本節與 B01/B05/B06B/X05Q 已列出的 10 個 custom script。
- DevExtreme vendor 檔實際位於 `wwwroot/css/devextreme/**` 140 個檔案與
  `wwwroot/js/devextreme/**` 161 個檔案，已由 X03 wildcard 完整涵蓋。

### 6.18 X04A Runtime Configuration 與 Secrets

主要擁有：

- `appsettings.json`
- `appsettings.Development.json`
- `appsettings.Production.json`
- `web.config`

X04A 管理設定 schema、環境覆寫、secret injection 與 startup validation。
各業務模組只擁有設定需求與驗證契約，不直接擁有設定檔。

### 6.19 X04B Deployment 與 Package Sources

主要擁有：

- `Properties/launchSettings.json`
- `DotNetPublish/**`
- `DotNetPublish-*.bat`
- `Tools/verify-release-noperf.ps1`
- 主站 `NuGet.config` 與 `NuGet.config.bak`

X04B 管理 package source、publish profile、部署腳本與部署 smoke。

### 6.20 X05Q Legacy Boundary Quarantine

下列檔案目前具有明顯混合職責，或從名稱與位置無法證明只有一個業務能力，
因此先由 X05Q 唯一擁有：

- `Controllers/BaseChurchController.cs`
- `Controllers/HomeController.cs`
- `Domain/Constants/CommitmentConstants.cs`
- `Extensions/ListManagerCacheExtensions.cs`
- `Services/Navigation/**`
- `Models/IInMemoryDataContext.cs`
- `Tools/ServiceRequest.cs`
- `WebServiceConnector/UploadData.cs`
- `WebServiceConnector/WebServiceConnector.cs`
- `Views/Home/DisplayErrorView.cshtml`
- `Views/Home/NewPersonFollowUpView.cshtml`
- `Views/Home/VisitorCard.cshtml`
- `wwwroot/test-api.html`
- `wwwroot/js/TreeView.js`
- `SpeechMessageProducts.ChurchReport/文件/**`
- 所有其他尚未命中業務葉節點或 X01、X02A-X02C、X03、X04A-X04B 的
  `SpeechMessageProducts.ChurchReport/**` 版本控制檔案

X05Q 不是一個可以整包優化的模組。它只能執行：

1. 單檔責任分析。
2. 建立呼叫者與資料流證據。
3. 將已證明單一職責的檔案移交給對應業務或平台葉節點。
4. 將仍混合的檔案拆分後，分別移交。

未完成責任證明前，不允許把 X05Q 檔案直接歸給名稱最接近的業務模組。

## 7. F01 版本庫治理葉節點

除第 4 至 6 節已明確歸屬的檔案外，治理檔案依下列葉節點收斂：

- F01A：`SpeechMessageProducts.sln`、`.editorconfig`、`.gitattributes`、
  `.gitignore`、`.github/**` 與未被其他葉節點認領的 root build metadata。
- F01B：`.agents/**`、`.ccg/**`、`.claude/**`、`.codex/**`、
  `.gemini/**`、`.opencode/**`、`.serena/**`、`.trellis/**`。
- F01C：根目錄 `README.md`、`AGENTS.md`、`docs/**`、`tools/**`、
  `scratch/**`、`openspec/**`、教學文件、分析報告、圖片與歷史產物。
- F01D：ChurchReport 共用 test csproj、shared test fixture、test SDK/version
  決策與 `SanityTest.cs`。
- 各實體產品專案內的文件隨該產品專案擁有者，不回收至 F01C。

F01A-F01D 只管理各自生命週期，不得以治理名義取得產品程式碼或業務測試
內容的修改權。

## 8. 測試檔案唯一擁有權

### 8.1 基本規則

- 測試跟隨「被驗證的主體」，不跟隨測試專案名稱。
- 測試專案 `.csproj` 的生命週期擁有者依第 4 節。
- 一個整合測試若驗證多個模組，主要擁有者是測試中主導業務結果的模組；
  其他模組列為測試依賴。
- 無法判定主導業務結果的 ChurchReport 整合測試暫歸 X05Q，不歸 F01D。

### 8.2 明確測試歸屬

| 測試路徑 | 主要擁有者 |
|---|---|
| `ToolUtility.Tests/ToolUtility.Tests.csproj` | F01D |
| `ToolUtility.Tests/LineMessaging/**` | F03B |
| `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs` | F03Q |
| `ToolUtility.Tests/**` | F03A，優先順序低於前三條 |
| `Line.Messaging.Tests/**` | F04 |
| `Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs` | F05A，優先於上一條 |
| `LineMessagingProcessor.Tests/**` | F05A |
| `LineMessagingProcessor.AspNetCore.Tests/**` | F05B |
| `LineMessagingProcessor.Workflows.Tests/**` | F06 |
| `LineMessagingProcessor.RichMenus.Tests/**` | F07 |
| `SpeechMessage.Payments.Tests/Workflows/**` | F09 |
| `SpeechMessage.Payments.Tests/**` | F08，優先順序低於上一條 |
| `ChurchReport.Tests/PerformanceTests/CollectionQueryServiceAsyncTests.cs` | F03A；目前沒有 test csproj，不能作為可執行 gate |
| `ChurchReport.MemberInfo.Tests/Security/**` | B01 |
| `ChurchReport.MemberInfo.Tests/Payments/PushUtilityTests.cs` | B07 |
| `ChurchReport.MemberInfo.Tests/Payments/PaymentAcknowledgementResultMapperTests.cs` | F09 |
| `ChurchReport.MemberInfo.Tests/Payments/PaymentHttpRequestMapperTests.cs` | F09 |
| `ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentWorkflowTests.cs` | F09 |
| `ChurchReport.MemberInfo.Tests/Payments/PaymentWorkflowResultMapperTests.cs` | F09 |
| `ChurchReport.MemberInfo.Tests/Payments/**` | B05 |
| `ChurchReport.MemberInfo.Tests/DefaultAvatarSvgTests.cs` | B02 |
| `ChurchReport.MemberInfo.Tests/MemberInfo*.cs` | B02 |
| `ChurchReport.MemberInfo.Tests/DonationNavigationAccessResolverTests.cs` | B05 |
| `ChurchReport.MemberInfo.Tests/PaymentNotificationRetryKeyTests.cs` | B05 |
| `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PaymentNotificationServiceWorkflowTests.cs` | B05 |
| `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/**` | B07，優先順序低於上一條 |
| `ChurchReport.MemberInfo.Tests/ReplyUtilityGroupRoomProfileAdapterTests.cs` | B07 |
| `ChurchReport.MemberInfo.Tests/StaticRequestPathHelperTests.cs` | X01 |
| `ChurchReport.MemberInfo.Tests/SanityTest.cs` | F01D |
| 其他 `ChurchReport.MemberInfo.Tests/**/*.cs` | X05Q |

新增測試時，PR 或任務必須同時填入受測葉節點 ID。未填者不得以「共用測試」
名義進入 F01D。

## 9. 共享契約與跨模組邊界

### 9.1 共享契約的唯一擁有者

| 契約 | 擁有者 | 消費者 |
|---|---|---|
| Dataverse/CRM 連線 client | F02 | F03A、F03Q、X02C |
| CRM CRUD/query API | F03A | B01-B07、X02A-X02C |
| ToolUtility LINE adapter contract | F03B | F03Q 與 legacy consumers |
| LINE HTTP/model contract | F04 | F03B、F03Q、F05A-F07、B01、B07 |
| LINE processor interface | F05A | F05B、F06、F07、B07 |
| LINE ASP.NET Core registration contract | F05B | X01 |
| LINE notification result/workflow | F06 | F05B、B04C、B05、B07 |
| RichMenu workflow contract | F07 | F05B、B07、X01 |
| Payment provider contract | F08 | F09、B05 |
| Payment host/workflow contract | F09 | B05、X01 |
| Authentication/session contract | B01 | B02-B07、X01 |
| Member/contact identity contract | B02 | B03、B04A-B04C、B05、B07 |
| Attendance contract | B04A | B04C |
| Reference/list contract | B06A | B05、B06B、B06C |
| Fee master data contract | B06B | B05 |
| Host lifetime與route contract | X01 | 所有主站模組 |
| Cache infrastructure | X02A | 所有主站模組 |
| Logging/health contract | X02B | 所有主站模組 |
| Profiling contract | X02C | 所有主站模組 |
| Shared layout/vendor asset contract | X03 | B01-B07 |
| Runtime configuration schema | X04A | 所有執行模組 |
| Deployment/package source contract | X04B | F01A、X01 |

契約擁有者修改 public API、DTO、設定 schema、route 或 callback 行為時，
必須提供相容性說明；消費者只負責驗證自己的使用方式，不得複製契約。

### 9.2 實體 ProjectReference 圖

本圖的箭頭定義為 `consumer project -> referenced project`：

```text
SpeechMessageProducts.ChurchReport
  -> ToolUtility
  -> Line.Messaging
  -> LineMessagingProcessor
  -> LineMessagingProcessor.AspNetCore
  -> LineMessagingProcessor.Workflows
  -> LineMessagingProcessor.RichMenus
  -> SpeechMessage.Payments
  -> SpeechMessage.Payments.AspNetCore
  -> SpeechMessage.Payments.Workflows

ToolUtility -> PowerPlatform.Dataverse.Client
ToolUtility -> Line.Messaging
LineMessagingProcessor -> Line.Messaging
LineMessagingProcessor.Workflows -> LineMessagingProcessor + Line.Messaging
LineMessagingProcessor.RichMenus -> LineMessagingProcessor + Line.Messaging
LineMessagingProcessor.AspNetCore
  -> LineMessagingProcessor
  -> LineMessagingProcessor.Workflows
  -> LineMessagingProcessor.RichMenus
  -> Line.Messaging
SpeechMessage.Payments.Workflows -> SpeechMessage.Payments
SpeechMessage.Payments.AspNetCore
  -> SpeechMessage.Payments
  -> SpeechMessage.Payments.Workflows
```

### 9.3 契約供應方向

本圖的箭頭定義為 `contract provider => consumer`，與 9.2 的 project reference
箭頭語意不同：

```text
F02 => F03A/F03Q
F04 => F03B/F03Q/F05A
F05A => F05B/F06/F07/B07
F06/F07 => F05B/B07
F08 => F09 => B05

B01 => B02-B07
B02 => B03/B04A-B04C/B05/B07
B04A => B04C
B06A => B06B/B06C/B05
B06B => B05
B07 transport => B05 notification use case

F/B/X platform modules => X01 composition
X02A-X02C/X03/X04A provide platform capability
F03Q/X02Q/X05Q => no stable contract
```

### 9.4 強制 Consumer Gate Matrix

契約擁有者有變更時，至少觸發下列 consumer gate。Gate 未建立或不能執行時，
該模組只允許分析與診斷，不允許宣告優化完成。

| 契約擁有者 | 必跑 provider gate | 必跑 consumer gate |
|---|---|---|
| F02 | Dataverse client build/tests | F03A/F03Q compile、主站 compile |
| F03A | ToolUtility build、CRM tests | B01-B06C 相關 host tests、主站 compile |
| F03B | LINE adapter tests | F03Q compile、所有仍使用 ToolUtility LINE API 的 consumer compile |
| F04 | LINE SDK build/tests | F03B、F05A-F07、F05B、主站 compile/tests |
| F05A | Processor tests | F05B、F06、F07 tests 與 B07 compile/tests |
| F06 | Workflow tests | F05B tests、B04C/B05/B07 相關 tests |
| F07 | RichMenu tests | F05B tests、B07 tests |
| F08 | Payment provider tests | F09 tests、B05 host payment tests |
| F09 | Workflow/AspNetCore tests | B05 controller/adapter/callback tests |
| B01 | Security/session tests | B02-B07 route/auth smoke |
| B02 | Member tests | B03/B04A-B04C/B05/B07 compile與整合 tests |
| B04A | Attendance tests | B04C scheduler/QR integration |
| B06A/B06B | List/Fee tests | B05 payment form與callback integration |
| X01 | Host build、DI resolution、route snapshot | 所有關鍵 user-flow smoke |
| X02A-X02C | 各自 component tests | 至少一個代表性 B 模組 load/smoke |
| X03 | browser/shared asset tests | 受影響 B 模組 browser workflow |
| X04A/X04B | config/deployment validation | host startup與deployment smoke |

若程式碼發現反向依賴或 gate 需要跨越未列出的消費者，必須在診斷報告標記，
先更新本矩陣，再申請優化。

## 10. 各模組的獨立診斷與優化邊界

每個模組開始前建立自己的檔案 manifest，並交付：

1. `analysis.md`
   - 唯一擁有檔案清單。
   - public/internal contract。
   - 直接依賴與直接消費者。
   - request、data、CRM、LINE、payment 的資料流。
   - 現有測試、未測路徑與執行基線。
2. `diagnosis.md`
   - 已驗證問題與純假設分開。
   - Critical、High、Medium、Low 嚴重度。
   - 每個發現包含檔案、行號、重現方式、影響與責任模組。
3. `optimization-plan.md`
   - 僅包含該主要擁有者的檔案。
   - 目標指標、相容性、測試命令與回滾點。
   - 跨模組需求以獨立工作項目連結，不直接混入。
4. 優化實作
   - 一個模組一組可回滾 commit。
   - 修改 public contract 時加 consumer validation gate。
5. `before-after.md`
   - 測試、效能、資源、錯誤率或可維護性證據。

### 10.1 優化准入狀態

「有測試檔」不等於「已有可執行 gate」。每個葉節點在第一次分析時必須記錄
實際命令與結果，未達准入條件前只能停在 analysis/diagnosis：

| 狀態 | 葉節點 | 可進行工作 |
|---|---|---|
| 有專屬測試候選，仍需建立綠色 baseline | F04、F05A、F05B、F06-F09、B01、B02、B05、B07 | analysis、diagnosis；baseline 綠色後才可申請 optimization |
| 已知 gate 阻塞 | F02、F03A、F03B、B03、B04A-B04C、B06A-B06C、X01、X02A-X02C、X03、X04A-X04B | analysis、diagnosis、補 gate；不得宣告優化完成 |
| 治理葉節點 | F01A-F01D | 只做各自治理範圍；每個變更必須可獨立回滾 |
| 隔離葉節點 | F03Q、X02Q、X05Q | responsibility proof、拆分、移交或核准淘汰 |

已知 gate 阻塞證據：

- `ToolUtility.Tests` 未納入 solution，target `net8.0`，受測 `ToolUtility`
  target `net10.0`。
- `ChurchReport.Tests/PerformanceTests/CollectionQueryServiceAsyncTests.cs`
  沒有 test project，且測試主體是 F03A，不是 profiling 平台。
- B03、B04A-B04C、B06A-B06C 沒有可直接歸屬的現有測試套件。
- X01、X02A-X02C、X03、X04A-X04B 尚未定義完整的 route、DI、component、
  browser、config 或 deployment baseline 命令。

任何葉節點進入 optimization 前，必須同時具備：

1. 可重複執行且目前為綠色的 provider baseline。
2. 第 9.4 節要求的 consumer gate。
3. 明確的修改檔案清單。
4. 可回滾 commit 或同等回滾點。

### 10.2 每類模組的診斷重點

| 模組類型 | 必做診斷 | 最低驗證 |
|---|---|---|
| F02、F03A/F03B、F04-F09 實體函式庫 | public API、target framework、HTTP/client lifetime、錯誤分類、重試、同步阻塞、consumer leakage | 專案 build、單元測試、contract fixture、主要 consumer compile |
| B01 | auth/session/claims/authorization、跨使用者隔離、OAuth state | security tests、route integration、同時使用者測試 |
| B02-B07 葉節點 | 垂直資料流、CRM query shape、狀態隔離、重複實作、controller/service 大小 | 模組測試、CRM/LINE/payment fake、主要 UI workflow |
| X01 | DI lifetime、middleware order、route、startup cost、service resolution | host smoke、route snapshot、DI resolution |
| X02A | cache key/limit、memory、expiry、eviction | cache component tests、負載基線 |
| X02B | logging cost、health accuracy、hosted service lifecycle、資料遮罩 | logger/health/component tests |
| X02C | metric accuracy、profiling overhead、threshold、timing scope | profiling tests、before/after resource snapshot |
| X03 | view contract、vendor 重複、payload、cache、accessibility、client error | browser workflow、asset budget |
| X04A | secret、環境覆寫、設定 schema、startup validation | secret scan、config/startup validation |
| X04B | package source、publish script、部署可重現性 | package restore、deployment smoke |
| F03Q/X02Q/X05Q | 責任發現與移交，不做整包效能優化 | caller/callee map、移交前後 ownership check |

## 11. 跨模組變更協定

1. **分析可以跨界閱讀**：為了建立資料流，可以讀取依賴與消費者。
2. **修改不能跨界混寫**：一個模組工作項目只修改自己的主要擁有檔案。
3. **契約變更分兩階段**：
   - 契約擁有者修改契約並提供相容層。
   - 消費者在各自工作項目完成遷移。
4. **整合驗證獨立管理**：跨模組 E2E、負載或部署驗證不取得來源檔案擁有權。
5. **Critical 問題可以阻擋下游**：例如 F02 連線安全、B01 session 隔離、
   F08 callback 驗證、X04A secret 問題。
6. **隔離節點先拆責任再優化**：禁止以「Legacy cleanup」一次修改多個不相干流程。

## 12. 建議依賴執行層

以下層次只描述技術依賴順序，不代表 Issue 的優化優先級或全域 Wave
歸屬。全域 Wave 必須另外依產品風險橫向選取。

### Dependency Layer 0：建立可信基線

- F01A-F01D 版本庫、solution、build/test 與工作流可見性。
- X04A runtime config/secret 與 X04B 部署治理。
- X01 DI、route、middleware 與 lifetime 地圖。
- X02A cache、X02B observability、X02C profiling 基線。

### Dependency Layer 1：共享基礎

- F02 -> F03A；F03B 與 F03Q 先盤點 LINE/CRM 混合責任。
- F04 -> F05A -> F06/F07 -> F05B。
- F08 -> F09。

### Dependency Layer 2：身分與核心資料

- B01。
- B02。
- B03、B06A、B06B、B06C，可在各自 gate 建立後分開進行。

### Dependency Layer 3：整合業務流程

- B07。
- B04A -> B04C；B04B 可獨立進行。
- B05。

### Dependency Layer 4：使用者體驗與整體驗證

- 各 B 模組自己的頁面與前端資產。
- X03 共用 layout、vendor 資產與全站前端效能。
- 跨模組 E2E、負載、記憶體、socket、授權與部署驗證。
- 逐批處理 F03Q、X02Q、X05Q，將混合責任移交到正式葉節點。

## 13. 管理看板最低欄位

每個模組工作項目至少記錄：

- Module ID。
- Stage：analysis、diagnosis、optimization-planning、implementation、validation。
- Primary owner paths。
- Dependencies 與 consumers。
- Baseline command/result。
- Findings 數量與最高嚴重度。
- Approved scope。
- Cross-module contract changes。
- Rollback point。
- Review status。

同一時間可以平行處理沒有共享修改檔案、沒有未決契約變更的模組。
有依賴順序的模組必須依第 12 節波次與實際發現調整。

## 14. 分類完整性檢查

每次新增、移動或拆分檔案後，必須檢查：

- [ ] 每個版本控制檔案依第 2.3 節得到且只得到一個主要擁有者。
- [ ] 每個 `.csproj` 在第 4 節具有一個生命週期擁有者。
- [ ] 每個測試檔依第 8 節跟隨受測模組。
- [ ] 依賴者沒有被誤寫成共同擁有者。
- [ ] ChurchReport 未命中檔案全部進入 X05Q，而不是失去歸屬。
- [ ] 非 ChurchReport 未命中檔案依類型進入 F01A-F01D，而不是失去歸屬。
- [ ] F03Q、X02Q、X05Q 新增項目附上無法判定責任的原因與預定釐清方式。
- [ ] 跨模組變更已拆成模組工作項目與整合驗證項目。

## 15. 已知限制

- 本分類保證管理上的唯一擁有權，不代表現有 namespace、project reference
  或 runtime dependency 已經符合邊界。
- B01-B03、B04A-B04C、B05、B06A-B06C、B07 仍是主站內的邏輯模組。
  開始診斷時必須從本文件產生實際
  manifest，不能只依資料夾名稱推斷。
- F03Q、X02Q、X05Q 是為了完整覆蓋而設的隔離邊界。隔離內容越多，
  表示實際架構越需要責任拆分；它們不能被視為穩定共用層。
- 第三方靜態資產目前大量直接提交在 `wwwroot`，由 X03 管理其版本、
  重複與供應鏈風險；業務模組只管理自己的使用方式。
- `LinePayCSharp`、`Trace`、Net10 變體與其他未納入 solution 的專案，
  必須由 F01A 建置治理確認保留、遷移或淘汰，但其程式內容仍由第 4 節指定
  的產品模組擁有。

## 16. 審查狀態

唯讀 subagent 已完成實際 repository 對照審查。初稿結論為「不可直接作為
獨立優化控制圖」，提出 2 個 Critical 與 9 個 Warning。已採納並修訂：

1. 將 `LineMessagingProcessor` core 與 `LineMessagingProcessor.AspNetCore`
   adapter 拆為 F05A/F05B。
2. 將 ToolUtility 的 CRM、LINE adapter 與混合 facade 拆為
   F03A/F03B/F03Q。
3. 修正 `ToolUtility.Tests` 未納入 solution、net8.0 對 net10.0 的
   gate blocker。
4. 將 B04 拆為出席、預約設備、排程 QR；將 B06 拆為清單參照、
   費用管理、教會層級。
5. 將 X02 拆為 cache、observability/logging、profiling 與 legacy Trace。
6. 將 F01 與 X04 拆為可獨立回滾的治理葉節點。
7. 修正 `MapData.cs`/`MapDataList.cs` 為 B06A 唯一擁有。
8. 將直接測試 `SpeechMessage.Payments.AspNetCore/Workflows` 的四個 host
   test 轉歸 F09。
9. 將沒有 test project 的 `CollectionQueryServiceAsyncTests.cs` 轉歸 F03A，
   並標記為不可執行 gate。
10. 修正 Login、Dedication、Fee 與跨業務 JavaScript 的擁有權。
11. 將 `_Net10.csproj` 描述改為重複/歷史替代定義，不再誤稱 framework variant。
12. 分離 ProjectReference 圖與契約供應圖，新增強制 consumer gate matrix。
13. 對沒有獨立可執行 gate 的葉節點明確禁止直接進入 optimization。
14. 依 CCG review 將 `Payments/PushUtilityTests.cs` 轉歸 B07，並將
    `ToolUtilityFacadeIntegrationTests.cs` 轉歸 F03Q。

CCG 外部 review 狀態：

- Claude 完成並產出可用結果。
- Gemini 因 provider quota/billing 403「餘額不足」未產出結果。
- 本次是核准的 degraded single-model fallback，不是完整雙模型 review。
- Claude 對 DevExtreme root asset 的 Critical 經本機逐檔核對為 false positive：
  vendor 檔均位於既有 `css/devextreme/**`、`js/devextreme/**` 規則內。

修訂後的管理結論：

- 35 個葉節點均可獨立建立分析與診斷工作項目。
- F03Q、X02Q、X05Q 只能分析、拆分、移交或淘汰，不能整包優化。
- 第 10.1 節標記為 gate 阻塞的葉節點，必須先建立可執行 baseline、
  consumer gate 與 rollback point，才可申請優化。
- 完成上述准入條件後，本分類可用於職責分明、界線清楚的獨立診斷與優化管理。
