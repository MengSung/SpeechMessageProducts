# P1-04 ToolUtility 型別盤點與抽取準入

日期：2026-11-05。正式指定資料夾共有 **62個原始檔**，先前暫記61不正確，已按實際列舉修正；不是擴大正式範圍。下表76列含partial多來源與巢狀型別。

## 判讀與完成界線

「可搬（先純化）」是候選，**尚未准入／搬移／驗收**。完成表列前置、每個待搬Operations的2～3代表golden（原實作先綠）及故障／隔離／資源測試，才git mv。「不可直接搬」是現況或連帶依賴未達前置，不代表永久排除P1；用最小純介面／host adapter排除障礙，不偷帶依賴。「不可搬（host）」保留原所有權，不拆partial跨assembly。

宣告索引只用於完整列舉，分類依建構、呼叫與故障／資源路徑；**正則命中不是安全審查通過證據**。逐方法語義與尚未測試細節明列前置，不宣稱已完成審查。借入SDK由host擁有，不保存HttpContext、Session、token或共用可變結果。

## 逐型別表

| 原檔 | 型別 | 判定 | 原因／必要前置 |
| --- | --- | --- | --- |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.ActivityAttachment.cs` | `ToolUtilityNameSpace.ToolUtilityClass` | 不可搬（host） | 同一ToolUtilityClass partial，持組態／連線並委派服務；全部留同一assembly。 |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Attribute.cs` | `ToolUtilityNameSpace.ToolUtilityClass` | 不可搬（host） | 同一ToolUtilityClass partial，持組態／連線並委派服務；全部留同一assembly。 |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Contact.cs` | `ToolUtilityNameSpace.ToolUtilityClass` | 不可搬（host） | 同一ToolUtilityClass partial，持組態／連線並委派服務；全部留同一assembly。 |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs` | `ToolUtilityNameSpace.ToolUtilityClass` | 不可搬（host） | 同一ToolUtilityClass partial，持組態／連線並委派服務；全部留同一assembly。 |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Entity.cs` | `ToolUtilityNameSpace.ToolUtilityClass` | 不可搬（host） | 同一ToolUtilityClass partial，持組態／連線並委派服務；全部留同一assembly。 |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs` | `ToolUtilityNameSpace.ToolUtilityClass` | 不可搬（host） | 同一ToolUtilityClass partial，持組態／連線並委派服務；全部留同一assembly。 |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.List.cs` | `ToolUtilityNameSpace.ToolUtilityClass` | 不可搬（host） | 同一ToolUtilityClass partial，持組態／連線並委派服務；全部留同一assembly。 |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Metadata.cs` | `ToolUtilityNameSpace.ToolUtilityClass` | 不可搬（host） | 同一ToolUtilityClass partial，持組態／連線並委派服務；全部留同一assembly。 |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Query1.cs` | `ToolUtilityNameSpace.ToolUtilityClass` | 不可搬（host） | 同一ToolUtilityClass partial，持組態／連線並委派服務；全部留同一assembly。 |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Query2.cs` | `ToolUtilityNameSpace.ToolUtilityClass` | 不可搬（host） | 同一ToolUtilityClass partial，持組態／連線並委派服務；全部留同一assembly。 |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Utility.cs` | `ToolUtilityNameSpace.ToolUtilityClass` | 不可搬（host） | 同一ToolUtilityClass partial，持組態／連線並委派服務；全部留同一assembly。 |
| `ToolUtility/ActivityOperations/ActivityService.cs` | `ToolUtilityNameSpace.ActivityOperations.ActivityService` | 不可直接搬 | 未用logger持有；直接寫入呼叫端ArrayList，ID字典覆寫，例外回顯原值／系統時間。 |
| `ToolUtility/ActivityOperations/IActivityService.cs` | `ToolUtilityNameSpace.ActivityOperations.IActivityService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/AppointmentOperations/AppointmentService.cs` | `ToolUtilityNameSpace.AppointmentOperations.AppointmentService` | 可搬（先純化） | SDK-only I/O；去除未用logger，日期文化／XML輸入先鎖golden再安全純化。 |
| `ToolUtility/AppointmentOperations/IAppointmentService.cs` | `ToolUtilityNameSpace.AppointmentOperations.IAppointmentService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/AttachmentOperations/AttachmentService.cs` | `ToolUtilityNameSpace.AttachmentOperations.AttachmentService` | 不可直接搬 | 未用logger持有；byte[]／base64缺容量契約，例外含ID及原始message。 |
| `ToolUtility/AttachmentOperations/IAttachmentService.cs` | `ToolUtilityNameSpace.AttachmentOperations.IAttachmentService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/AttributeOperations/AttributeServiceComposite.cs` | `ToolUtilityNameSpace.AttributeOperations.AttributeServiceComposite` | 不可直接搬 | 連帶九個服務反射載入Extensions logger，本檔無using不代表純Core。 |
| `ToolUtility/AttributeOperations/BoolAttributeService.cs` | `ToolUtilityNameSpace.AttributeOperations.BoolAttributeService` | 不可直接搬 | SafeLogError反射載入Extensions logger並保存object；先純轉換／host錯誤轉接分離。 |
| `ToolUtility/AttributeOperations/DateTimeAttributeService.cs` | `ToolUtilityNameSpace.AttributeOperations.DateTimeAttributeService` | 不可直接搬 | SafeLogError反射載入Extensions logger並保存object；先純轉換／host錯誤轉接分離。 |
| `ToolUtility/AttributeOperations/DoubleAttributeService.cs` | `ToolUtilityNameSpace.AttributeOperations.DoubleAttributeService` | 不可直接搬 | SafeLogError反射載入Extensions logger並保存object；先純轉換／host錯誤轉接分離。 |
| `ToolUtility/AttributeOperations/EntityAttributeUtilityService.cs` | `ToolUtilityNameSpace.AttributeOperations.EntityAttributeUtilityService` | 可搬（先純化） | 未用logger持有，例外回顯原message；先純化並保留真實Exception鏈。 |
| `ToolUtility/AttributeOperations/FloatAttributeService.cs` | `ToolUtilityNameSpace.AttributeOperations.FloatAttributeService` | 不可直接搬 | SafeLogError反射載入Extensions logger並保存object；先純轉換／host錯誤轉接分離。 |
| `ToolUtility/AttributeOperations/IAttributeService.cs` | `ToolUtilityNameSpace.AttributeOperations.IAttributeService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/AttributeOperations/IntAttributeService.cs` | `ToolUtilityNameSpace.AttributeOperations.IntAttributeService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/AttributeOperations/LookupAttributeService.cs` | `ToolUtilityNameSpace.AttributeOperations.LookupAttributeService` | 不可直接搬 | SafeLogError反射載入Extensions logger並保存object；先純轉換／host錯誤轉接分離。 |
| `ToolUtility/AttributeOperations/MoneyAttributeService.cs` | `ToolUtilityNameSpace.AttributeOperations.MoneyAttributeService` | 不可直接搬 | SafeLogError反射載入Extensions logger並保存object；先純轉換／host錯誤轉接分離。 |
| `ToolUtility/AttributeOperations/OptionSetAttributeService.cs` | `ToolUtilityNameSpace.AttributeOperations.OptionSetAttributeService` | 不可直接搬 | SafeLogError反射載入Extensions logger並保存object；先純轉換／host錯誤轉接分離。 |
| `ToolUtility/AttributeOperations/StringAttributeService.cs` | `ToolUtilityNameSpace.AttributeOperations.StringAttributeService` | 不可直接搬 | SafeLogError反射載入Extensions logger並保存object；先純轉換／host錯誤轉接分離。 |
| `ToolUtility/CollectionOperations/CollectionQueryService.cs` | `ToolUtilityNameSpace.CollectionOperations.CollectionQueryService` | 不可直接搬 | Diagnostics.LoggerInvoker host依賴，ID字典及分頁／取消／drain先驗證。 |
| `ToolUtility/CollectionOperations/ICollectionQueryService.cs` | `ToolUtilityNameSpace.CollectionOperations.ICollectionQueryService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/CollectionOperations/ICollectionQueryService.cs` | `ToolUtilityNameSpace.CollectionOperations.PagedResult<T>` | 不可直接搬 | 公開可變List／PagingCookie，只能是operation-owned DTO，不可當共用快照。 |
| `ToolUtility/ContactOperations/ContactService.cs` | `ToolUtilityNameSpace.ContactOperations.ContactService` | 不可直接搬 | Caching.ICrmCacheService及Diagnostics.LoggerInvoker；cache／連線留host。 |
| `ToolUtility/ContactOperations/IContactService.cs` | `ToolUtilityNameSpace.ContactOperations.IContactService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/EntityOperations/EntityAttributeHandler.cs` | `ToolUtilityNameSpace.EntityOperations.EntityAttributeHandler` | 不可直接搬 | static轉換catch Trace原值並有預設值路徑，須區分合法缺值及真正失敗。 |
| `ToolUtility/EntityOperations/EntityCrudService.cs` | `ToolUtilityNameSpace.EntityOperations.EntityCrudService` | 可搬（先純化） | SDK Create/Update/Delete；去除未用logger，host仍負責授權／冪等。 |
| `ToolUtility/EntityOperations/EntityOptimizedQueryService.cs` | `ToolUtilityNameSpace.EntityOperations.EntityOptimizedQueryService` | 不可直接搬 | 依Diagnostics.LoggerInvoker，ID字典、PagedResult及借入服務Dispose邊界先驗證。 |
| `ToolUtility/EntityOperations/EntityOptimizedQueryService.cs` | `ToolUtilityNameSpace.EntityOperations.PagedResult<T>` | 不可直接搬 | 公開可變List／PagingCookie，只能是operation-owned DTO，不可當共用快照。 |
| `ToolUtility/EntityOperations/EntityQueryService.cs` | `ToolUtilityNameSpace.EntityOperations.EntityQueryService` | 可搬（先純化） | 未用logger、允許未初始化服務、XML注入與Dispose狀態待測修；NoCache在此收斂。 |
| `ToolUtility/EntityOperations/EntityRepository.cs` | `ToolUtilityNameSpace.EntityOperations.IEntityRepository` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/EntityOperations/EntityRepository.cs` | `ToolUtilityNameSpace.EntityOperations.EntityRepository` | 不可直接搬 | Trace記錄實體／ID／Exception.Message；先安全診斷分層，保留原例外鏈。 |
| `ToolUtility/EntityOperations/IEntityCrudService.cs` | `ToolUtilityNameSpace.EntityOperations.IEntityCrudService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/EntityOperations/IEntityQueryService.cs` | `ToolUtilityNameSpace.EntityOperations.IEntityQueryService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/FeeOperations/FeeService.cs` | `ToolUtilityNameSpace.FeeOperations.FeeService` | 可搬（先純化） | 未用logger；兩有效方法有XML注入、兩空方法已有有效來源；先特性／修補後搬。 |
| `ToolUtility/FeeOperations/IFeeService.cs` | `ToolUtilityNameSpace.FeeOperations.IFeeService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/LessonsOperations/ILessonsService.cs` | `ToolUtilityNameSpace.LessonsOperations.ILessonsService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/LessonsOperations/LessonsService.cs` | `ToolUtilityNameSpace.LessonsOperations.LessonsService` | 可搬（先純化） | SDK-only；去除未用logger，日期文化及XML lookup安全組裝先測。 |
| `ToolUtility/ListOperations/IListService.cs` | `ToolUtilityNameSpace.ListOperations.IListService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/ListOperations/ListService.cs` | `ToolUtilityNameSpace.ListOperations.ListService` | 不可直接搬 | Task.Run／Delay批次與外部SDK借入，先驗取消／故障／排空及ID完整發布。 |
| `ToolUtility/ListOperations/MarketingListService.cs` | `ToolUtilityNameSpace.ListOperations.IMarketingListService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/ListOperations/MarketingListService.cs` | `ToolUtilityNameSpace.ListOperations.MarketingListService` | 不可直接搬 | 依IEntityRepository與SDK，Task.Run批次及連帶Repository待純化。 |
| `ToolUtility/MeetingStatisticsOperations/IMeetingStatisticsService.cs` | `ToolUtilityNameSpace.MeetingStatisticsOperations.IMeetingStatisticsService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/MeetingStatisticsOperations/MeetingStatisticsService.cs` | `ToolUtilityNameSpace.MeetingStatisticsOperations.MeetingStatisticsService` | 可搬（先純化） | SDK-only；去除未用logger，固定日期文化／lookup安全組裝，先2～3個golden。 |
| `ToolUtility/OwnerOperations/OwnerManagementService.cs` | `ToolUtilityNameSpace.OwnerOperations.OwnerManagementService` | 可搬（先純化） | SDK owner操作；去除未用logger，例外不得回顯ID／原message。 |
| `ToolUtility/QueryOperations/ComplexQueryService.cs` | `ToolUtilityNameSpace.QueryOperations.ComplexQueryService` | 不可直接搬 | 依Interfaces.ICrmClient而非純借入IOrganizationService，不搬host連線。 |
| `ToolUtility/QueryOperations/FetchXmlQueryService.cs` | `ToolUtilityNameSpace.QueryOperations.FetchXmlQueryService` | 不可直接搬 | 反射Extensions logger及未跳脫XML；認獻／收費可委派Fee單一實作。 |
| `ToolUtility/QueryOperations/IComplexQueryService.cs` | `ToolUtilityNameSpace.QueryOperations.IComplexQueryService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/QueryOperations/PresentRecordQueryService.cs` | `ToolUtilityNameSpace.QueryOperations.PresentRecordQueryService` | 不可直接搬 | 反射Extensions logger及FetchXML插入，先分離純查詢／host告警。 |
| `ToolUtility/QueryOperations/QueryService.cs` | `ToolUtilityNameSpace.QueryOperations.IQueryService` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/QueryOperations/QueryService.cs` | `ToolUtilityNameSpace.QueryOperations.QueryService` | 不可直接搬 | 依Repository、Trace原值與DateTime.Now，須處理時間來源／安全診斷。 |
| `ToolUtility/QueryOperations/RelationshipQueryService.cs` | `ToolUtilityNameSpace.QueryOperations.RelationshipQueryService` | 不可直接搬 | 反射Extensions logger與動態XML，實體／欄位／關係名也須結構驗證。 |
| `ToolUtility/Core/IToolUtilityCore.cs` | `ToolUtilityNameSpace.Core.IToolUtilityCore` | 可搬（純契約） | 簽章僅BCL／SDK，不持資源；必要DTO／文件及雙目標編譯仍須驗證。 |
| `ToolUtility/Core/ToolUtilityFacade.cs` | `ToolUtilityNameSpace.Core.ToolUtilityFacade` | 不可直接搬 | ConnectionOperations、logger、LINE與Lazy組合根；Metadata partial不可跨assembly拆。 |
| `ToolUtility/Core/ToolUtilityFacade.Metadata.cs` | `ToolUtilityNameSpace.Core.ToolUtilityFacade` | 不可直接搬 | ConnectionOperations、logger、LINE與Lazy組合根；Metadata partial不可跨assembly拆。 |
| `ToolUtility/Extensions/CrmAsyncExtensions.cs` | `ToolUtilityNameSpace.Extensions.CrmAsyncExtensions` | 不可直接搬 | SemaphoreSlim無確定性Dispose，Task.Run批次與dictionary覆寫ID；先有界／drain／衝突測試。 |
| `ToolUtility/Extensions/OrganizationServiceExtensions.cs` | `ToolUtilityNameSpace.Extensions.OrganizationServiceExtensions` | 可搬（先純化） | 只SDK Execute，但null service／request／response轉空結果須先釐清失敗契約。 |
| `ToolUtility/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns` | 可搬（先純化） | public static readonly string[]仍可變，先每次複本／不可變界線及caller mutation測試。 |
| `ToolUtility/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.Contact` | 可搬（先純化） | public static readonly string[]仍可變，先每次複本／不可變界線及caller mutation測試。 |
| `ToolUtility/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.List` | 可搬（先純化） | public static readonly string[]仍可變，先每次複本／不可變界線及caller mutation測試。 |
| `ToolUtility/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.PresentRecord` | 可搬（先純化） | public static readonly string[]仍可變，先每次複本／不可變界線及caller mutation測試。 |
| `ToolUtility/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.WeeklyReport` | 可搬（先純化） | public static readonly string[]仍可變，先每次複本／不可變界線及caller mutation測試。 |
| `ToolUtility/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.DedicationBooking` | 可搬（先純化） | public static readonly string[]仍可變，先每次複本／不可變界線及caller mutation測試。 |
| `ToolUtility/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.Fee` | 可搬（先純化） | public static readonly string[]仍可變，先每次複本／不可變界線及caller mutation測試。 |
| `ToolUtility/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.StorLessons` | 可搬（先純化） | public static readonly string[]仍可變，先每次複本／不可變界線及caller mutation測試。 |
| `ToolUtility/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.Account` | 可搬（先純化） | public static readonly string[]仍可變，先每次複本／不可變界線及caller mutation測試。 |
| `ToolUtility/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.Task` | 可搬（先純化） | public static readonly string[]仍可變，先每次複本／不可變界線及caller mutation測試。 |

## 必要連帶依賴與溯源

- Interfaces/IFetchXmlQueryService.cs實際namespace是ToolUtilityNameSpace.QueryOperations；IEntityAttributeUtilityService、IOwnerManagementService也位於Interfaces。對應服務准入時只搬必要純契約，ICrmClient、cache、LoggerInvoker與連線留host。
- ChurchReport七處名單呼叫經舊facade，SDK邊界是IOrganizationService；產品C#未找到ServiceClient／Proxy直接消費，停止條件目前未觸發，搬後重查。
- Facade的RetrieveDedicationBookingByFetchXml／RetrieveFeeByFetchXml走Fee空方法；另一組RetrieveDedicationBooking／RetrieveFee走有效FetchXmlQueryService。有效兩方法已有完整golden／原例外測試，可收斂單一Fee實作，不猜業務。
- 三個新Fee方法舊來源為父工作樹`.其他解決方案/LineApiWebhook/ToolUtility/Services/QueryService.cs`（GetPaidFee／GetNotYetPaidFee），以及ToolUtilityClass/ToolUtilityClass.FetchXmlAndString.cs（RetrieveFeeByFetchXmlOrderNumber）。前兩者storLessonName原未使用，不當權限；後者卡與ATM欄位OR，文字安全編碼，不照抄注入。RetrieveEntityNoCache來源是同來源Core/ToolUtilityFacade.cs，仍待實作。

## 證據與決策

Fee特性4/4、FetchXmlFee特性4/4、XML helper正反例6/6，合計14/14；`artifacts/p1-04-pre-extraction/p1-04-pre-extraction.trx`。helper測試不是產品功能RED。日期基準只局部固定InvariantCulture並finally還原，不設定DefaultThreadCurrentCulture。

雙模型分析run `.ccg/dual-model-runs/20261105-122655-integration-p1-04-extraction-analysis-analyzer`：dualModelComplete=true、quotaBlocked=false、degradedFallback=false，為摘要風險分析而非逐檔code review。採納先分類／溯源／特性／安全修補再搬。Claude推測Fee有效方法依賴ComplexQueryService不符實際直接SDK路徑，不列已證缺陷。模型主張必須修正式計畫只是建議；目前已找到有效來源且原計畫有安全抽取邊界，沒有改正式計畫或產Word修正報告。

## 尚待交付

候選純化、全部實際搬移Operations的特性、四方法各至少3案、Core雙目標strict、DI product隔離、兩目標測試／覆蓋率、架構／duplicates／ChurchReport回歸與雙模型code review。**本報告不等於P1-04完成。**
