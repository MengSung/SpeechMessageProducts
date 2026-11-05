# P1-04 ToolUtility 型別盤點與抽取準入

日期：2026-11-05。正式指定資料夾共有 **62個原始檔**，先前暫記61不正確，已按實際列舉修正；不是擴大正式範圍。下表76列含partial多來源與巢狀型別。

本表是抽取前的來源路徑與準入盤點，不表示全部仍在原目錄。最新搬移進度見文末及
`wp-reports/P1-04-core-extraction.md`；不得以原盤點的歷史「待實作」文字蓋過後續實際證據。

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
| `ToolUtility.Core/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns` | 已純化並抽取 | 私有固定模板加公開複本 getter；21組完整golden、caller mutation及並行測試通過。 |
| `ToolUtility.Core/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.Contact` | 已純化並抽取 | 欄位陣列由getter逐次複製；保留來源namespace及既有欄位／順序。 |
| `ToolUtility.Core/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.List` | 已純化並抽取 | 欄位陣列由getter逐次複製；保留來源namespace及既有欄位／順序。 |
| `ToolUtility.Core/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.PresentRecord` | 已純化並抽取 | 欄位陣列由getter逐次複製；保留來源namespace及既有欄位／順序。 |
| `ToolUtility.Core/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.WeeklyReport` | 已純化並抽取 | 欄位陣列由getter逐次複製；保留來源namespace及既有欄位／順序。 |
| `ToolUtility.Core/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.DedicationBooking` | 已純化並抽取 | 欄位陣列由getter逐次複製；保留來源namespace及既有欄位／順序。 |
| `ToolUtility.Core/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.Fee` | 已純化並抽取 | 欄位陣列由getter逐次複製；保留來源namespace及既有欄位／順序。 |
| `ToolUtility.Core/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.StorLessons` | 已純化並抽取 | 欄位陣列由getter逐次複製；保留來源namespace及既有欄位／順序。 |
| `ToolUtility.Core/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.Account` | 已純化並抽取 | 欄位陣列由getter逐次複製；保留來源namespace及既有欄位／順序。 |
| `ToolUtility.Core/Constants/CrmEntityColumns.cs` | `ToolUtilityNameSpace.Constants.CrmEntityColumns.Task` | 已純化並抽取 | 欄位陣列由getter逐次複製；保留來源namespace及既有欄位／順序。 |

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

## 抽取後狀態更新（2026-11-05）

| 原範圍 | 最新位置與進度 |
| --- | --- |
| FeeService／IFeeService | 已 git mv 至 Core/FeeOperations；七入口安全組裝與四方法中三個 Fee API 已實作，舊來源及完整頁面證據已補 |
| EntityQueryService／IEntityQueryService | 已 git mv 至 Core/EntityOperations；RetrieveEntityNoCache 已實作，通用 Fetch 根實體在 I/O 前辨識，Dispose 不關借入 SDK |
| AppointmentService／IAppointmentService | 已 git mv 至 Core/AppointmentOperations；先綠 golden，再安全紅綠，最後搬移 |
| LessonsService／ILessonsService | 已 git mv 至 Core/LessonsOperations；三查詢完整 golden、lookup／日期／ID／故障／隔離證據 |
| MeetingStatisticsService／IMeetingStatisticsService | 已 git mv 至 Core/MeetingStatisticsOperations；六查詢完整 golden，保留全部舊欄位／狀態／提示值 |
| 新共同內部 helper | Core/DataOperations/SdkDataQuery.cs；只建局部 XML／日期與驗頁，不建立連線、通知器或快取 |
| EntityCrud／Owner | 已抽至 Core；7 個搬前特性及安全紅綠、1,000 次 Create＋Assign 並行，Delete 精確例外及 35 處 caller 補證已納入本輪複審 |
| EntityAttributeUtility／OrganizationServiceExtensions | 三檔已 git mv 至 Core；12 搬前特性、安全 28 紅／3 綠→修後搬前 81 綠；最終雙宿主 314/314、395/395，覆蓋率 99.36%／100%，最終 reviewer 20261105-185904 雙模型無 Critical／High／Warning；完整方案 2056/2056 |
| 其他純契約及 Operations | 仍依逐型別準入處理；不能把未用 logger、Trace 或可移除的反射診斷依賴當作永久 host 排除理由，也不擴張到其他階段業務重寫 |
| CrmEntityColumns | 已移至 `ToolUtility.Core/Constants/`；private static templates 加 caller-owned clone getters，完整欄位清單 golden 及 21 組 mutation／並行契約共 23 案通過 |
| 不可直接搬的既有型別 | 仍依原表逐型別判斷；必須釐清 host 依賴及最小抽取邊界，不帶入反射 logger、cache、連線或背景工作 |

不同 ID 的同名記錄在所有已搬移單頁保持完整；重複／空 ID／跨實體候選拒絕。
本輪不更改正式 P1 計畫，沒有觸發 Word 修正報告需求。


## Repository切片更新（2026-11-05）

`EntityRepository`／`IEntityRepository` 已同檔git mv至Core，先12特性綠、安全30紅／4綠、
overload2紅、修後搬前含六個真實caller98綠。最新雙宿主363/363、457/457，
Core99.42%／100%、strict0警告／錯誤；完整方案2167/2167、ChurchReport623/623。
I18nCheck抓到13個舊中文診斷常值後已改固定英文，Core CJK=0，不放寬baseline。
195337末輪雙模型完整、無quota降級，前兩Warning已關閉、無新增阻斷缺陷。
詳細契約、測試與本地提交歸屬見 `wp-reports/P1-04-repository.md`。
Activity／Attachment、其餘屬性／Collection／Contact／List／Query及必要純契約尚待處理；
整個P1-04與P1仍未完成，主P1維持2/12。
