```mermaid
classDiagram
    %% ==================== 控制器層 (藍色系) ====================
    
    class Controller {
        <<ASP.NET Core>>
    }
    
    class BaseChurchController {
        <<abstract>>
        #TOTAL_LEVEL: int
        #LEVEL_1 to LEVEL_5: int
        #LINE_ERROR_RECEIVER_ID: string
        #ToolUtility: ToolUtilityClass
        #InMemoryContext: InMemoryDataContextSmallGroup
        #PaymentService: IPayment
        +BaseChurchController(httpContextAccessor, memoryCache, paymentService)
        #HandleError(exception, methodName) IActionResult
        #SendLineErrorNotification(errorMessage) void
        #SetMultiGroupLayoutParameter() void
        #SetupBasicViewBag() void
        #SetupFeeDataListCount() void
        +Dispose() void
    }
    
    class AuthenticationController {
        +Login() Task~IActionResult~
        +ProcessLogin(galleryViewModel) Task~IActionResult~
        +LineIdLoginView(parameter) IActionResult
        +ProcessLineLogin() Task~IActionResult~
        +Logout() IActionResult
        +ForgotPassword() IActionResult
        +ResetPassword(email) Task~IActionResult~
        +ChangePassword(oldPassword, newPassword) Task~IActionResult~
        +CheckSession() IActionResult
        +ExtendSession() IActionResult
        -ValidateUserCredentials(viewModel) Tuple
        -RetrieveUserData(contactIdString, viewModel) Task
        -InitializeUserSession(loginContact, viewModel) void
        -SetupSystemData(loginContact, viewModel) void
        -DetermineDisplayViewType() string
        -SetupViewBagParameters(displayViewType) void
        -CreateLoginResponse(displayViewType, fullName, viewModel) IActionResult
    }
    
    class HomeController {
        +Index() IActionResult
        +IntegrateView() IActionResult
        +MultiGroupView() IActionResult
        +ChurchRoot() IActionResult
        +Login() IActionResult
        +LineIdLoginView() IActionResult
        +NewPerson() IActionResult
        +PersonalInfomationView() IActionResult
        +MaintainPersonInfomationView() IActionResult
        +EquipmentView() IActionResult
        +EquipmentContactView() IActionResult
        +EquipmentStorLessonsView() IActionResult
        +DedicationFeeAuditViewLine() IActionResult
        +QPayView() IActionResult
    }
    
    class PersonalController {
        +PersonalInfomationView() IActionResult
        +MaintainPersonInfomationView() IActionResult
        +PersonalReport() IActionResult
        +UpdatePersonalInfo(model) Task~IActionResult~
        +GetPersonalData() Task~IActionResult~
        -LoadPersonalInfomation() void
        -SavePersonalInfomation(model) bool
    }
    
    class NewPersonController {
        +NewPerson() IActionResult
        +CreateNewPerson(model) Task~IActionResult~
        +GetNewPersonList() Task~IActionResult~
        +UpdateNewPerson(model) Task~IActionResult~
        +DeleteNewPerson(id) Task~IActionResult~
        -ValidateNewPersonData(model) bool
        -SaveToDatabase(model) bool
    }
    
    class EquipmentController {
        +EquipmentView() IActionResult
        +EquipmentContactView() IActionResult
        +EquipmentStorLessonsView() IActionResult
        +GetEquipmentList() Task~IActionResult~
        +CreateEquipment(model) Task~IActionResult~
        +UpdateEquipment(model) Task~IActionResult~
        +DeleteEquipment(id) Task~IActionResult~
        -ValidateEquipmentData(model) bool
    }
    
    class DedicationAuditController {
        +DedicationFeeAuditViewWeb() IActionResult
        +DedicationFeeAuditViewLine() IActionResult
        +GetAuditList() Task~IActionResult~
        +ApproveAudit(id) Task~IActionResult~
        +RejectAudit(id, reason) Task~IActionResult~
        -ProcessAuditDecision(id, status) bool
    }
    
    class MyPayController {
        +PaymentResult() IActionResult
        +ProcessPayment(model) Task~IActionResult~
        +PaymentCallback(returnModel) Task~IActionResult~
        +QueryPaymentStatus(orderId) Task~IActionResult~
        -ValidatePaymentData(model) bool
        -CreatePaymentOrder(model) string
        -HandlePaymentResult(result) void
    }
    
    class QPayCardController {
        +QPayView() IActionResult
        +QPayLogin() IActionResult
        +ProcessQPay(model) Task~IActionResult~
        +QPayCallback(returnData) Task~IActionResult~
        -InitializeQPayService() void
        -ValidateQPayData(model) bool
    }
    
    class TSPGController {
        +ProcessTSPG(model) Task~IActionResult~
        +TSPGCallback(returnData) Task~IActionResult~
        +QueryTSPGStatus(orderId) Task~IActionResult~
        -ValidateTSPGData(model) bool
    }
    
    class SchedulerController {
        +GetSchedulerData() Task~IActionResult~
        +CreateAppointment(model) Task~IActionResult~
        +UpdateAppointment(model) Task~IActionResult~
        +DeleteAppointment(id) Task~IActionResult~
        -ValidateAppointmentData(model) bool
    }
    
    class PhoneBindingController {
        +BindPhone(model) Task~IActionResult~
        +UnbindPhone(phoneNumber) Task~IActionResult~
        +VerifyPhone(code) Task~IActionResult~
        -SendVerificationCode(phone) bool
        -ValidateVerificationCode(phone, code) bool
    }
    
    %% ==================== ViewModel 層 (綠色系) ====================
    
    class GalleryViewModel {
        +Images: IEnumerable~string~
        +Account: string
        +Password: string
        +FullName: string
        +Mobile: string
        +Address: string
        +NationId: string
    }
    
    class PersonalInfomationViewModel {
        +ContactId: string
        +FullName: string
        +Mobile: string
        +Email: string
        +Address: string
        +Birthday: DateTime
        +Gender: string
        +Status: string
    }
    
    class PersonFormViewModel {
        +FullName: string
        +Phone: string
        +Address: string
        +NationId: string
        +Email: string
        +Birthday: DateTime
    }
    
    class LineBindingViewModel {
        +Images: IEnumerable~string~
        +LineUserId: string
        +Account: string
        +Password: string
        +IsBinding: bool
    }
    
    class MyPayReturnModel {
        +OrderId: string
        +Amount: decimal
        +Status: string
        +Message: string
        +Timestamp: DateTime
        +HashCode: string
    }
    
    %% ==================== Models 層 (橙色系) ====================
    
    class InMemoryDataContextSmallGroup {
        -_memoryCache: IMemoryCache
        -m_ToolUtilityClass: ToolUtilityClass
        +m_ListManager: ListManager
        +m_SmallGroupDataList: SmallGroupDataList
        +m_WeeklyReportData: WeeklyReportData
        +m_NewPersonModel: NewPersonModel
        +m_PersonalInfomationModel: PersonalInfomationModel
        +m_HappyGroupDataManager: HappyGroupDataManager
        +m_ListManagementDataManager: ListManagementDataManager
        +m_EquipmentDataManager: EquipmentDataManager
        +m_FeeList: FeeList
        +m_LineBindingViewModel: LineBindingViewModel
        +m_AppointmentsListManager: AppointmentsListManager
        +m_QpayManager: QpayManager
        +m_PollManager: PollManager
        -m_ContextAccessor: HttpContextAccessor
        -m_HttpContext: HttpContext
        -m_Session: ISession
        -m_PaymentService: IPayment
        +InMemoryDataContextSmallGroup(contextAccessor, memoryCache, paymentService)
        +SetupSmallGroupData(fullName, account, password, selectDate, displayDateFlag) void
        +SaveChanges() void
    }
    
    class ListManager {
        +m_SelectDate: DateTime
        +LoginType: string
        +LoginFullName: string
        +ActiveListId: string
        +SchedulerView: string
        +DisplayNavigation: string
        +UserType: string
        +DedicationType: string
        +DedicationFlag: string
        +QrCodeId: string
        +m_Account: string
        +m_Password: string
        +InitialFlag: bool
        +m_Markers: List~MapData~
        +m_MultiGroupList: MultiGroupList
        +m_ListSmallGroupWeeklyReport: ListSmallGroupWeeklyReport
        +m_MultiGroupChartDataList: MultiGroupChartDataList
        +SetupListManager(account, password, selectDate) void
        +SetSelectDate(selectDate) void
        +GetDisplayViewType() string
        +SetupIntegrateData(listEntityId) void
        +GetMarkers() List~MapData~
    }
    
    class SmallGroupDataList {
        +m_SmallGroupData: SmallGroupData
        +m_NewPersonFollowUpData: SmallGroupData
        +m_AllMemberData: SmallGroupData
        +SetupContactIdString(contactIdString) void
        +LoadSmallGroupData() void
    }
    
    class NewPersonModel {
        +NewPersonList: List~NewPerson~
        +TotalCount: int
        +AddNewPerson(person) void
        +UpdateNewPerson(person) void
        +DeleteNewPerson(id) void
        +GetNewPersonById(id) NewPerson
    }
    
    class Member {
        +PresentRecordId: string
        +FullName: string
        +Phone: string
        +Address: string
        +Email: string
        +Status: string
        +Sunday: bool
        +SmallGroup: bool
        +Birthday: DateTime
        +Gender: string
    }
    
    class QpayManager {
        +LoginType: string
        +m_QpayModel: QpayModel
        +m_PaymentService: IPayment
        +QpayManager(paymentService)
        +SetQpayModel(loginContact) void
        +CreateOrder(customData) CreOrder
        +QueryOrder(orderId) QryOrder
        +MaintainOrder(orderId, action) OrderMaintain
    }
    
    class AppointmentsListManager {
        +m_Account: string
        +m_Password: string
        +m_LoginContact: Entity
        +UserType: string
        +AppointmentsList: List~Appointment~
        +SetupAppointmentList() void
        +AddAppointment(appointment) void
        +UpdateAppointment(appointment) void
        +DeleteAppointment(id) void
    }
    
    class FeeList {
        +FeeType: string
        +FeeDataList: List~FeeData~
        +SetupLessonList(account, password) void
        +AddFee(fee) void
        +UpdateFee(fee) void
        +DeleteFee(id) void
    }
    
    class HappyGroupDataManager {
        +HappyType: string
        +HappyGroupList: List~HappyGroup~
        +LoadHappyGroupData() void
        +UpdateHappyGroup(group) void
    }
    
    class PersonalInfomationModel {
        +m_LoginContact: Entity
        +FullName: string
        +Mobile: string
        +Email: string
        +Address: string
        +LoadPersonalInfo(contactId) void
        +UpdatePersonalInfo(model) bool
    }
    
    class EquipmentDataManager {
        +EquipmentList: List~Equipment~
        +TotalCount: int
        +LoadEquipmentData() void
        +AddEquipment(equipment) void
        +UpdateEquipment(equipment) void
        +DeleteEquipment(id) void
    }
    
    class ListManagementDataManager {
        +ListData: List~ListManagementData~
        +LoadListData() void
        +UpdateListData(data) void
    }
    
    class PollManager {
        +PollList: List~Poll~
        +LoadPollData() void
        +CreatePoll(poll) void
        +UpdatePoll(poll) void
        +DeletePoll(id) void
    }
    
    %% ==================== 工具層 (紫色系) ====================
    
    class ToolUtilityClass {
        -CRM_TYPE: string
        +m_Crm2011OrganizationService: IOrganizationService
        +m_OrganizationService: OrganizationServiceProxy
        +ToolUtilityClass()
        +ToolUtilityClass(discoveryServiceType)
        +RetrieveContactByAccountNumber(account, password) string
        +RetrieveEntityDynamics365(entityName, entityId) Entity
        +GetEntityStringAttribute(entity, attributeName) string
        +RetrieveContactEntityByLineUserId(lineUserId) Entity
        +TraceByLevel(totalLevel, level, message) void
        +Dispose() void
    }
    
    class MyPayToolkitWrapper {
        -myPayToolkit: MyPayToolkit
        +Initialize(config) void
        +CreateOrder(orderData) string
        +QueryOrder(orderId) OrderResult
        +VerifyHash(data, hash) bool
        +GenerateHash(data) string
    }
    
    class MyPayToolkit {
        +MerchantId: string
        +TerminalId: string
        +MerchantKey: string
        +ApiUrl: string
        +ProcessPayment(request) PaymentResult
        +QueryPayment(orderId) PaymentStatus
    }
    
    class QPayToolkit {
        +ShopNo: string
        +MerchantKey: string
        +ApiUrl: string
        +CreateOrder(orderData) string
        +QueryOrder(orderId) OrderStatus
        +CancelOrder(orderId) bool
    }
    
    class TspgToolkitWrapper {
        -tspgConfig: TSPGConfig
        +Initialize(config) void
        +ProcessPayment(data) PaymentResult
        +QueryTransaction(transId) TransactionStatus
    }
    
    class TSPGModels {
        +OrderData: OrderData
        +PaymentRequest: PaymentRequest
        +PaymentResponse: PaymentResponse
        +TransactionQuery: TransactionQuery
    }
    
    class TSPGStandardModels {
        +StandardOrderData: StandardOrderData
        +StandardPaymentRequest: StandardPaymentRequest
        +StandardPaymentResponse: StandardPaymentResponse
    }
    
    %% ==================== 服務層 (青色系) ====================
    
    class MyPayNotificationService {
        -httpClient: HttpClient
        +SendNotification(message) Task
        +SendLineNotification(userId, message) Task
        +SendEmailNotification(email, subject, body) Task
    }
    
    class MyPayStatusHelper {
        +GetStatusDescription(statusCode) string
        +IsSuccessStatus(statusCode) bool
        +IsFailureStatus(statusCode) bool
        +TranslateStatusCode(code) string
    }
    
    %% ==================== WebService 連接器 (粉色系) ====================
    
    class QPayProcessor {
        -qpayToolkit: QPayToolkit
        +ProcessOrder(orderData) OrderResult
        +QueryOrderStatus(orderId) OrderStatus
        +HandleCallback(callbackData) CallbackResult
        -ValidateCallback(data) bool
    }
    
    class PersonalInfomatioManager {
        -toolUtility: ToolUtilityClass
        +GetPersonalInfo(contactId) PersonalInfo
        +UpdatePersonalInfo(model) bool
        +CreatePersonalInfo(model) string
        -ValidatePersonalData(model) bool
    }
    
    class DownloadIntegrateData {
        -toolUtility: ToolUtilityClass
        +SetupIntegrateData(account, password, loginType, selectDate, listEntityId, weeklyReportEntityId, listSmallGroupWeeklyReport) void
        +DownloadSmallGroupData(listEntityId) SmallGroupData
        +DownloadWeeklyReport(weeklyReportEntityId) WeeklyReport
        -ProcessDownloadedData(data) void
    }
    
    class UploadIntegrateData {
        -toolUtility: ToolUtilityClass
        +UploadWeeklyReport(report) bool
        +UploadAttendanceData(attendance) bool
        +UploadNewPersonData(newPerson) bool
        -ValidateUploadData(data) bool
    }
    
    class NewPerson {
        -toolUtility: ToolUtilityClass
        +CreateNewPerson(model) string
        +UpdateNewPerson(model) bool
        +DeleteNewPerson(id) bool
        +GetNewPersonList(filter) List~NewPersonData~
    }
    
    class TransmitMemberInfomation {
        -toolUtility: ToolUtilityClass
        +TransmitToServer(memberData) bool
        +ReceiveFromServer(memberId) MemberData
        +SyncMemberData(localData, serverData) bool
        -CompareDataVersion(local, server) int
    }
    
    %% ==================== 介面 (黃色系) ====================
    
    class IPayment {
        <<interface>>
        +CreateOrder(customData, service) CreOrder
        +OrderCreate(req) CreOrder
        +OrderUnCapturedQuery(req) QryOrderUnCaptured
        +OrderMaintain(req) OrderMaintain
        +OrderQuery(req) QryOrder
        +OrderPayQuery(req) QryOrderPay
        +OrderPayQuery(req, hashCode) QryOrderPay
        +BillQuery(req) QryBill
        +AllotQuery(req) QryAllot
    }
    
    %% ==================== 外部實體 (灰色系) ====================
    
    class Entity {
        <<Dynamics 365>>
        +Id: Guid
        +Attributes: AttributeCollection
        +GetAttributeValue~T~(attributeName) T
        +SetAttributeValue(attributeName, value) void
    }
    
    class IOrganizationService {
        <<Dynamics 365>>
        +Create(entity) Guid
        +Update(entity) void
        +Delete(entityName, id) void
        +Retrieve(entityName, id, columnSet) Entity
        +RetrieveMultiple(query) EntityCollection
    }
    
    %% ==================== 繼承關係 ====================
    
    Controller <|-- BaseChurchController
    BaseChurchController <|-- AuthenticationController
    BaseChurchController <|-- HomeController
    BaseChurchController <|-- PersonalController
    BaseChurchController <|-- NewPersonController
    BaseChurchController <|-- EquipmentController
    BaseChurchController <|-- DedicationAuditController
    BaseChurchController <|-- MyPayController
    BaseChurchController <|-- QPayCardController
    BaseChurchController <|-- TSPGController
    BaseChurchController <|-- SchedulerController
    BaseChurchController <|-- PhoneBindingController
    
    %% ==================== 組合關係 ====================
    
    BaseChurchController *-- ToolUtilityClass : contains
    BaseChurchController *-- InMemoryDataContextSmallGroup : contains
    BaseChurchController ..> IPayment : uses
    
    InMemoryDataContextSmallGroup *-- ListManager : manages
    InMemoryDataContextSmallGroup *-- SmallGroupDataList : manages
    InMemoryDataContextSmallGroup *-- NewPersonModel : manages
    InMemoryDataContextSmallGroup *-- PersonalInfomationModel : manages
    InMemoryDataContextSmallGroup *-- HappyGroupDataManager : manages
    InMemoryDataContextSmallGroup *-- ListManagementDataManager : manages
    InMemoryDataContextSmallGroup *-- EquipmentDataManager : manages
    InMemoryDataContextSmallGroup *-- FeeList : manages
    InMemoryDataContextSmallGroup *-- LineBindingViewModel : manages
    InMemoryDataContextSmallGroup *-- AppointmentsListManager : manages
    InMemoryDataContextSmallGroup *-- QpayManager : manages
    InMemoryDataContextSmallGroup *-- PollManager : manages
    
    SmallGroupDataList *-- Member : contains
    ListManager *-- SmallGroupDataList : uses
    
    %% ==================== 依賴關係 ====================
    
    AuthenticationController ..> GalleryViewModel : uses
    AuthenticationController ..> Entity : uses
    
    PersonalController ..> PersonalInfomationViewModel : uses
    PersonalController ..> PersonFormViewModel : uses
    
    NewPersonController ..> NewPersonModel : uses
    
    MyPayController ..> MyPayReturnModel : uses
    MyPayController ..> MyPayToolkitWrapper : uses
    MyPayController ..> MyPayNotificationService : uses
    MyPayController ..> MyPayStatusHelper : uses
    
    QPayCardController ..> QPayToolkit : uses
    QPayCardController ..> QPayProcessor : uses
    
    TSPGController ..> TspgToolkitWrapper : uses
    TSPGController ..> TSPGModels : uses
    TSPGController ..> TSPGStandardModels : uses
    
    PhoneBindingController ..> LineBindingViewModel : uses
    
    ToolUtilityClass ..> Entity : retrieves
    ToolUtilityClass ..> IOrganizationService : uses
    
    QpayManager ..> IPayment : uses
    QpayManager ..> QPayToolkit : uses
    
    MyPayToolkitWrapper o-- MyPayToolkit : wraps
    
    QPayProcessor o-- QPayToolkit : uses
    PersonalInfomatioManager o-- ToolUtilityClass : uses
    DownloadIntegrateData o-- ToolUtilityClass : uses
    UploadIntegrateData o-- ToolUtilityClass : uses
    NewPerson o-- ToolUtilityClass : uses
    TransmitMemberInfomation o-- ToolUtilityClass : uses
    
    %% ==================== 樣式定義 ====================
    
    %% 控制器層 - 藍色系
    style Controller fill:#E3F2FD,stroke:#1976D2,stroke-width:3px,color:#000
    style BaseChurchController fill:#BBDEFB,stroke:#1976D2,stroke-width:3px,color:#000
    style AuthenticationController fill:#90CAF9,stroke:#1565C0,stroke-width:2px,color:#000
    style HomeController fill:#90CAF9,stroke:#1565C0,stroke-width:2px,color:#000
    style PersonalController fill:#90CAF9,stroke:#1565C0,stroke-width:2px,color:#000
    style NewPersonController fill:#90CAF9,stroke:#1565C0,stroke-width:2px,color:#000
    style EquipmentController fill:#90CAF9,stroke:#1565C0,stroke-width:2px,color:#000
    style DedicationAuditController fill:#90CAF9,stroke:#1565C0,stroke-width:2px,color:#000
    style MyPayController fill:#64B5F6,stroke:#0D47A1,stroke-width:2px,color:#000
    style QPayCardController fill:#64B5F6,stroke:#0D47A1,stroke-width:2px,color:#000
    style TSPGController fill:#64B5F6,stroke:#0D47A1,stroke-width:2px,color:#000
    style SchedulerController fill:#90CAF9,stroke:#1565C0,stroke-width:2px,color:#000
    style PhoneBindingController fill:#90CAF9,stroke:#1565C0,stroke-width:2px,color:#000
    
    %% ViewModel 層 - 綠色系
    style GalleryViewModel fill:#C8E6C9,stroke:#388E3C,stroke-width:2px,color:#000
    style PersonalInfomationViewModel fill:#C8E6C9,stroke:#388E3C,stroke-width:2px,color:#000
    style PersonFormViewModel fill:#C8E6C9,stroke:#388E3C,stroke-width:2px,color:#000
    style LineBindingViewModel fill:#C8E6C9,stroke:#388E3C,stroke-width:2px,color:#000
    style MyPayReturnModel fill:#A5D6A7,stroke:#2E7D32,stroke-width:2px,color:#000
    
    %% Models 層 - 橙色系
    style InMemoryDataContextSmallGroup fill:#FFE0B2,stroke:#E65100,stroke-width:3px,color:#000
    style ListManager fill:#FFCC80,stroke:#EF6C00,stroke-width:2px,color:#000
    style SmallGroupDataList fill:#FFCC80,stroke:#EF6C00,stroke-width:2px,color:#000
    style NewPersonModel fill:#FFCC80,stroke:#EF6C00,stroke-width:2px,color:#000
    style Member fill:#FFB74D,stroke:#F57C00,stroke-width:2px,color:#000
    style QpayManager fill:#FFA726,stroke:#E65100,stroke-width:2px,color:#000
    style AppointmentsListManager fill:#FFCC80,stroke:#EF6C00,stroke-width:2px,color:#000
    style FeeList fill:#FFCC80,stroke:#EF6C00,stroke-width:2px,color:#000
    style HappyGroupDataManager fill:#FFCC80,stroke:#EF6C00,stroke-width:2px,color:#000
    style PersonalInfomationModel fill:#FFCC80,stroke:#EF6C00,stroke-width:2px,color:#000
    style EquipmentDataManager fill:#FFCC80,stroke:#EF6C00,stroke-width:2px,color:#000
    style ListManagementDataManager fill:#FFCC80,stroke:#EF6C00,stroke-width:2px,color:#000
    style PollManager fill:#FFCC80,stroke:#EF6C00,stroke-width:2px,color:#000
    
    %% 工具層 - 紫色系
    style ToolUtilityClass fill:#E1BEE7,stroke:#7B1FA2,stroke-width:3px,color:#000
    style MyPayToolkitWrapper fill:#CE93D8,stroke:#6A1B9A,stroke-width:2px,color:#000
    style MyPayToolkit fill:#BA68C8,stroke:#4A148C,stroke-width:2px,color:#000
    style QPayToolkit fill:#BA68C8,stroke:#4A148C,stroke-width:2px,color:#000
    style TspgToolkitWrapper fill:#CE93D8,stroke:#6A1B9A,stroke-width:2px,color:#000
    style TSPGModels fill:#BA68C8,stroke:#4A148C,stroke-width:2px,color:#000
    style TSPGStandardModels fill:#BA68C8,stroke:#4A148C,stroke-width:2px,color:#000
    
    %% 服務層 - 青色系
    style MyPayNotificationService fill:#B2EBF2,stroke:#00838F,stroke-width:2px,color:#000
    style MyPayStatusHelper fill:#80DEEA,stroke:#006064,stroke-width:2px,color:#000
    
    %% WebService 連接器 - 粉色系
    style QPayProcessor fill:#F8BBD0,stroke:#C2185B,stroke-width:2px,color:#000
    style PersonalInfomatioManager fill:#F8BBD0,stroke:#C2185B,stroke-width:2px,color:#000
    style DownloadIntegrateData fill:#F48FB1,stroke:#AD1457,stroke-width:2px,color:#000
    style UploadIntegrateData fill:#F48FB1,stroke:#AD1457,stroke-width:2px,color:#000
    style NewPerson fill:#F8BBD0,stroke:#C2185B,stroke-width:2px,color:#000
    style TransmitMemberInfomation fill:#F48FB1,stroke:#AD1457,stroke-width:2px,color:#000
    
    %% 介面 - 黃色系
    style IPayment fill:#FFF9C4,stroke:#F57F17,stroke-width:3px,color:#000
    
    %% 外部實體 - 灰色系
    style Entity fill:#ECEFF1,stroke:#455A64,stroke-width:2px,color:#000
    style IOrganizationService fill:#CFD8DC,stroke:#263238,stroke-width:2px,color:#000
```