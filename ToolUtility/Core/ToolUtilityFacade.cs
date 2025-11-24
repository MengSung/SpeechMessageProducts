using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.ListOperations;
using ToolUtilityNameSpace.AttachmentOperations;
using ToolUtilityNameSpace.LineMessaging;
using ToolUtilityNameSpace.ContactOperations;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtilityNameSpace.Interfaces;
using ToolUtilityNameSpace.Utilities;
using ToolUtilityNameSpace.AppointmentOperations;
using ToolUtilityNameSpace.LessonsOperations;
using ToolUtilityNameSpace.FeeOperations;
using ToolUtilityNameSpace.CollectionOperations;
using ToolUtilityNameSpace.MeetingStatisticsOperations;
using ToolUtilityNameSpace.ConnectionOperations;
using System.ServiceModel.Description;

namespace ToolUtilityNameSpace.Core
{
    /// <summary>
    /// Light-weight Facade for ToolUtility functionality.
    /// This is the new Facade introduced in PR-04. It delegates to smaller services.
    /// It intentionally coexists with the legacy ToolUtilityClass during the refactor.
    /// 完整委派 ToolUtilityClass-developing.cs 的所有功能
    /// </summary>
    public class ToolUtilityFacade : IDisposable
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        private Lazy<IEntityQueryService> _queryService;
        private Lazy<IEntityCrudService> _crudService;
        private Lazy<IAttributeService> _attributeService;
        private Lazy<IContactService> _contactService;
        private Lazy<IListService> _listService;
        private Lazy<IAttachmentService> _attachmentService;
        private Lazy<ILineMessageService> _lineMessageService;
        private Lazy<IAppointmentService> _appointmentService;
        private Lazy<ILessonsService> _lessonsService;
        private Lazy<IFeeService> _feeService;
        private Lazy<ICollectionQueryService> _collectionQueryService;
        private Lazy<IMeetingStatisticsService> _meetingStatisticsService;
        private Lazy<ICrmConnectionService> _connectionService;

        private bool _disposed = false;

        public ToolUtilityFacade(object logger = null, IOrganizationService organizationService = null)
        {
            _logger = logger ?? new object();
            _organizationService = organizationService;
            InitializeServices();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                if (_queryService?.IsValueCreated == true) { var d = _queryService.Value as IDisposable; d?.Dispose(); }
                if (_crudService?.IsValueCreated == true) { var d = _crudService.Value as IDisposable; d?.Dispose(); }
                if (_attributeService?.IsValueCreated == true) { var d = _attributeService.Value as IDisposable; d?.Dispose(); }
                if (_contactService?.IsValueCreated == true) { var d = _contactService.Value as IDisposable; d?.Dispose(); }
                if (_listService?.IsValueCreated == true) { var d = _listService.Value as IDisposable; d?.Dispose(); }
                if (_attachmentService?.IsValueCreated == true) { var d = _attachmentService.Value as IDisposable; d?.Dispose(); }
                if (_lineMessageService?.IsValueCreated == true) { var d = _lineMessageService.Value as IDisposable; d?.Dispose(); }
                if (_appointmentService?.IsValueCreated == true) { (_appointmentService.Value as IDisposable)?.Dispose(); }
                if (_lessonsService?.IsValueCreated == true) { var d = _lessonsService.Value as IDisposable; d?.Dispose(); }
                if (_feeService?.IsValueCreated == true) { var d = _feeService.Value as IDisposable; d?.Dispose(); }
                if (_meetingStatisticsService?.IsValueCreated == true) { var d = _meetingStatisticsService.Value as IDisposable; d?.Dispose(); }
                if (_connectionService?.IsValueCreated == true) { var d = _connectionService.Value as IDisposable; d?.Dispose(); }

                (_organizationService as IDisposable)?.Dispose();
            }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void InitializeServices()
        {
            _queryService = new Lazy<IEntityQueryService>(() => new EntityQueryService(_logger, _organizationService));
            _crudService = new Lazy<IEntityCrudService>(() => new EntityCrudService(_logger, _organizationService));
            _attributeService = new Lazy<IAttributeService>(() => new AttributeServiceComposite(_logger));
            _contactService = new Lazy<IContactService>(() => new ContactService(_logger, _queryService.Value));
            _listService = new Lazy<IListService>(() => new ListService(_logger, _queryService.Value, _organizationService));
            _attachmentService = new Lazy<IAttachmentService>(() => new AttachmentService(_logger, _organizationService));
            _lineMessageService = new Lazy<ILineMessageService>(() => new LineMessageService(_logger, _crudService.Value));
            _appointmentService = new Lazy<IAppointmentService>(() => new AppointmentService(_logger, _queryService.Value));
            _lessonsService = new Lazy<ILessonsService>(() => new LessonsService(_logger, _queryService.Value));
            _feeService = new Lazy<IFeeService>(() => new FeeService(_logger, _queryService.Value));
            _collectionQueryService = new Lazy<ICollectionQueryService>(() => new CollectionQueryService(_logger, _queryService.Value));
            _meetingStatisticsService = new Lazy<IMeetingStatisticsService>(() => new MeetingStatisticsService(_logger, _queryService.Value));
            _connectionService = new Lazy<ICrmConnectionService>(() => new CrmConnectionService());
        }

        #region 基本實體操作方法
        public Entity RetrieveEntity(string entityName, Guid entityId)
            => _queryService.Value.RetrieveEntity(entityName, entityId);

        public Entity RetrieveEntityByField(string entityName, string fieldName, string fieldValue)
            => _queryService.Value.RetrieveEntityByField(entityName, fieldName, fieldValue);

        public EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue)
            => _collectionQueryService.Value.RetrieveEntityCollectionByField(entityName, fieldName, fieldValue);

        public Guid CreateEntity(Entity entityToCreate)
            => _crudService.Value.CreateEntity(entityToCreate);

        public void UpdateEntity(Entity entityToUpdate)
            => _crudService.Value.UpdateEntity(entityToUpdate);

        public void DeleteEntity(string entityName, Guid entityId)
            => _crudService.Value.DeleteEntity(entityName, entityId);
        #endregion

        #region 屬性操作方法
        public bool GetEntityBoolAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetBoolAttribute(entity, propertyName);

        public void SetEntityBoolAttribute(ref Entity entity, string propertyName, bool propertyValue)
            => _attributeService.Value.SetBoolAttribute(ref entity, propertyName, propertyValue);

        public int GetEntityIntAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetIntAttribute(entity, propertyName);

        public string GetEntityStringAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetStringAttribute(entity, propertyName);

        public DateTime GetEntityDateTimeAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetDateTimeAttribute(entity, propertyName);

        public Money GetEntityMoneyAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetMoneyAttribute(entity, propertyName);

        public Guid GetEntityLookupAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetLookupAttribute(entity, propertyName);

        public void SetEntityMoneyAttributeToNull(ref Entity entity, string propertyName)
            => _attributeService.Value.SetMoneyAttributeToNull(ref entity, propertyName);

        public void SetEntityDateTimeAttributeToNull(ref Entity entity, string propertyName)
            => _attributeService.Value.SetDateTimeAttributeToNull(ref entity, propertyName);

        public void SetEntityLookUpAttribute(ref Entity entity, string propertyName, ref EntityReference entityReference)
            => _attributeService.Value.SetLookupAttribute(ref entity, propertyName, ref entityReference);

        public void SetEntityLookUpToNull(ref Entity entity, string propertyName)
            => _attributeService.Value.SetLookupToNull(ref entity, propertyName);

        public string GetEntityLookupDisplayName(Entity entity, string propertyName)
            => _attributeService.Value.GetLookupDisplayName(entity, propertyName);

        public int GetOptionSetAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetOptionSetAttribute(entity, propertyName);

        public void SetOptionSetAttribute(ref Entity entity, string propertyName, int value)
            => _attributeService.Value.SetOptionSetAttribute(ref entity, propertyName, value);

        public void SetOptionSetAttributeNull(ref Entity entity, string propertyName)
            => _attributeService.Value.SetOptionSetAttributeNull(ref entity, propertyName);

        public float GetEntityFloatAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetFloatAttribute(entity, propertyName);

        public void SetEntityFloatAttribute(ref Entity entity, string propertyName, float value)
            => _attributeService.Value.SetFloatAttribute(ref entity, propertyName, value);

        public void SetEntityFloatAttributeToNull(Entity entity, string propertyName)
            => _attributeService.Value.SetFloatAttributeToNull(entity, propertyName);

        public double GetEntityDoubleAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetDoubleAttribute(entity, propertyName);

        public void SetEntityDoubleAttribute(ref Entity entity, string propertyName, double value)
            => _attributeService.Value.SetDoubleAttribute(ref entity, propertyName, value);

        public void SetEntityDoubleAttributeToNull(Entity entity, string propertyName)
            => _attributeService.Value.SetDoubleAttributeToNull(entity, propertyName);
        #endregion

        #region CRM 連接服務方法 (委派給 CrmConnectionService)
        /// <summary>
        /// 取得 Windows 認證憑證
        /// </summary>
        public ClientCredentials GetClientCredentials(string domain, string userName, string password)
            => _connectionService.Value.GetClientCredentials(domain, userName, password);

        /// <summary>
        /// 取得預設認證憑證
        /// </summary>
        public ClientCredentials GetClientCredentials()
            => _connectionService.Value.GetClientCredentials();

        /// <summary>
        /// 取得 CRM Organization Service
        /// </summary>
        public IOrganizationService GetOrganizationService(string server, string port, string organization, string domain, string userName, string password)
            => _connectionService.Value.GetOrganizationService(server, port, organization, domain, userName, password);

        /// <summary>
        /// 設定 CRM 2011 Organization Service
        /// </summary>
        public IOrganizationService SetOrganizationService(string server, string port, string organization, string domain, string userName, string password)
            => _connectionService.Value.SetOrganizationService(server, port, organization, domain, userName, password);

        /// <summary>
        /// 設定 Claims-Based 認證的 Organization Service
        /// </summary>
        public IOrganizationService SetClaimsBasedAuthenticationOrganizationService(string organization, string server, string domain, string userName, string password)
            => _connectionService.Value.SetClaimsBasedAuthenticationOrganizationService(organization, server, domain, userName, password);

        /// <summary>
        /// 設定 Federated Organization Proxy (用於 Dynamics 365 Online 和 On-Premise IFD 環境)
        /// </summary>
        public OrganizationServiceProxy SetFederatedOrganizationProxy(string discoveryServiceType, string organization, string server, string port, string baseDiscoveryServiceAddress, string userName, string password, string domain)
            => _connectionService.Value.SetFederatedOrganizationProxy(discoveryServiceType, organization, server, port, baseDiscoveryServiceAddress, userName, password, domain);

        /// <summary>
        /// 探索使用者所屬的組織
        /// </summary>
        public OrganizationDetailCollection DiscoverOrganizations(IDiscoveryService service)
            => _connectionService.Value.DiscoverOrganizations(service);

        /// <summary>
        /// 在組織列表中尋找特定組織
        /// </summary>
        public OrganizationDetail FindOrganization(string orgUniqueName, OrganizationDetail[] orgDetails)
            => _connectionService.Value.FindOrganization(orgUniqueName, orgDetails);
        #endregion

        #region 聯絡人相關方法 (委派給 ContactService)
        public Entity RetrieveContactByLineId(string lineId)
            => _contactService.Value.RetrieveByLineId(lineId);

        public EntityCollection RetrieveContactCollectionByName(string contactFullName)
            => _contactService.Value.RetrieveCollectionByName(contactFullName);

        public string RetrieveContactByContactId(string contactId)
            => _contactService.Value.GetContactInfoByContactId(contactId);

        public Entity RetrieveContactByContactId(ref IOrganizationService organizationService, string contactId, ref int count)
            => _contactService.Value.RetrieveByContactId(organizationService, contactId, ref count);

        public string RetrieveContactByName(string contactFullName)
            => _contactService.Value.GetContactInfoByFullName(contactFullName);

        public Entity RetrieveContactEntityByName(string contactFullName)
            => _contactService.Value.RetrieveByFullName(contactFullName);

        public Entity RetrieveContactByName(ref IOrganizationService organizationService, string contactFullName)
            => _contactService.Value.RetrieveByFullName(organizationService, contactFullName);

        public string RetrieveContactByName_ReturnString(ref IOrganizationService organizationService, string contactFullName)
            => _contactService.Value.GetContactInfoByFullName(organizationService, contactFullName);

        public EntityCollection RetrieveContactCollectionByNationId(string nationId)
            => _contactService.Value.RetrieveCollectionByNationId(nationId);

        public Entity RetrieveContactByLineId_Entity(string lineId)
            => _contactService.Value.RetrieveByLineId(lineId);

        public string RetrieveContactByAccountNumber(string accountNumber, string password)
        {
            var result = _contactService.Value.RetrieveByAccountNumber(accountNumber, password);
            if (result != null)
            {
                return result.Id.ToString();
            }
            return "帳號或密碼錯誤";
        }

        public Entity DoesAccountExist(string accountNumber)
            => _contactService.Value.RetrieveAccountEntity(accountNumber);

        public Entity RetrieveContactEntityByAccountNumber(string accountNumber, string password)
            => _contactService.Value.RetrieveByAccountNumber(accountNumber, password);

        public Entity RetrieveContactEntityByLineUserId(string lineUserId)
            => _contactService.Value.RetrieveByLineId(lineUserId);

        public Entity RetrieveContactEntityByFullNameAndMobileNumber(string fullName, string mobileNumber)
            => _contactService.Value.RetrieveByFullNameAndMobile(fullName, mobileNumber);

        public EntityCollection RetrieveContactEntityByFullNameCollection(string fullName)
            => _contactService.Value.RetrieveCollectionByFullName(fullName);

        public EntityCollection QueryDediccationContatsByFetchXml(string dedicationNumber, string contactName, string homePhone, string mobile, string nationId, string lastSixDigit)
            => _contactService.Value.QueryDediccationContatsByFetchXml(dedicationNumber, contactName, homePhone, mobile, nationId, lastSixDigit);

        public EntityCollection QueryContatsByStartedDedicationNumber(string dedicationStartNumber)
            => _contactService.Value.QueryContatsByStartedDedicationNumber(dedicationStartNumber);
        #endregion

        #region 名單相關方法 (委派給 ListService 和 CollectionQueryService)
        public void AddMembersToMarketingList(Guid listGuid, List<Guid> memberGuidList)
            => _listService.Value.AddMembers(listGuid, memberGuidList);

        public void RemoveMembersToMarketingList(Guid listGuid, Guid memberGuid)
            => _listService.Value.RemoveMember(listGuid, memberGuid);

        // 成員名單查詢方法
        public EntityCollection RetrieveMemberListCollectionByListId(Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListId(listId);

        public EntityCollection RetrieveMemberListCollectionByListId(ref IOrganizationService organizationService, Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListIdUsingService(organizationService, listId);

        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(ref OrganizationServiceProxy organizationService, Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListIdUsingProxy(organizationService, listId);

        public EntityCollection RetrieveMemberListCollectionByListIdCrm2011(ref IOrganizationService organizationService, Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListIdUsingService(organizationService, listId);

        // 動態名單查詢方法
        public EntityCollection RetrieveDynamicMemberList(string strList)
            => _listService.Value.RetrieveDynamicMemberList(Guid.Parse(strList));

        public EntityCollection RetrieveDynamicMemberList(IOrganizationService service, string strList)
        {
            IOrganizationService svc = service;
            return RetrieveDynamicMemberList(ref svc, Guid.Parse(strList));
        }

        public EntityCollection RetrieveDynamicMemberListDynamics365(OrganizationServiceProxy service, string strList)
        {
            OrganizationServiceProxy proxy = service;
            return RetrieveDynamicMemberListDynamics365(ref proxy, Guid.Parse(strList));
        }

        public EntityCollection RetrieveDynamicMemberListCrm2011(IOrganizationService service, string strList)
        {
            IOrganizationService svc = service;
            return RetrieveDynamicMemberList(ref svc, Guid.Parse(strList));
        }

        public EntityCollection RetrieveDynamicMemberList(Guid listId)
            => _listService.Value.RetrieveDynamicMemberList(listId);

        public EntityCollection RetrieveDynamicMemberList(ref IOrganizationService service, Guid listId)
            => _listService.Value.RetrieveDynamicMemberListUsingService(service, listId);

        public EntityCollection RetrieveDynamicMemberListDynamics365(ref OrganizationServiceProxy service, Guid listId)
            => _listService.Value.RetrieveDynamicMemberListUsingProxy(service, listId);

        public EntityCollection RetrieveDynamicMemberListCrm2011(ref IOrganizationService service, Guid listId)
            => _listService.Value.RetrieveDynamicMemberListUsingService(service, listId);
        #endregion

        #region 客戶(Account)組織方法
        public Guid RetrieveAccountCollectionByName(string accountName)
            => _queryService.Value.RetrieveAccountByName(accountName);
        #endregion

        #region 約會相關方法 (委派給 AppointmentService - 修正方法名稱)
        public EntityCollection RetrieveAppointmentsByDate(DateTime selectedDate)
            => _appointmentService.Value.RetrieveByDate(selectedDate);

        public EntityCollection RetrieveAppointmentsByFetchXml(DateTime startDate, DateTime endDate)
            => _appointmentService.Value.RetrieveByDateRange(startDate, endDate);

        public EntityCollection RetrieveAppointmentsByFetchXml(string contactName, string contactId)
            => _appointmentService.Value.RetrieveByContactWithinYear(contactName, contactId);

        public EntityCollection RetrieveAppointmentsByFetchXmlAndScheduleType(DateTime startDate, DateTime endDate, string scheduleType)
            => _appointmentService.Value.RetrieveByDateRangeAndScheduleType(startDate, endDate, scheduleType);
        #endregion

        #region 課程相關方法 (委派給 LessonsService - 修正方法名稱)
        public EntityCollection RetrieveEnrolledLessonsByFetchXml(DateTime startDate, DateTime endDate, string contactName, string contactId)
            => _lessonsService.Value.RetrieveEnrolledLessons(startDate, endDate, contactName, contactId);

        public EntityCollection RetrieveLessonsByMonth(DateTime startDate, DateTime endDate)
            => _lessonsService.Value.RetrieveLessonsByMonth(startDate, endDate);

        public EntityCollection RetrieveStorLessonsByFetchXml(string lessonName, string lessonId, string contactName, string contactId)
            => _lessonsService.Value.RetrieveStorLessons(lessonName, lessonId, contactName, contactId);
        #endregion

        #region 工作相關方法
        public EntityCollection RetrieveTaskByFetchXml(string subject)
            => _queryService.Value.RetrieveTaskBySubject(subject);
        #endregion

        #region 個人聚會與靈修記錄、收費單 - 暫時保留為未實作
        // 注意: 以下方法需要檢查實際服務介面的方法簽名
        // 暫時保留為註解,在 ToolUtilityClass-developing.cs 中直接實作
        #endregion

        #region 收費單相關方法 (委派給 FeeService)
        public EntityCollection RetrieveDedicationBookingByFetchXml(string contactName, string contactId)
            => _feeService.Value.RetrieveDedicationBooking(contactName, contactId);

        public EntityCollection RetrieveFeeByFetchXml(string dedicationBookingName, string dedicationBookingId, string paidPeriod)
            => _feeService.Value.RetrieveFee(dedicationBookingName, dedicationBookingId, paidPeriod);
        #endregion

        #region Line 訊息相關方法
        public void CreatePushLineMessage(string userId, string subject, string message)
            => _lineMessageService.Value.CreatePushMessage(userId, subject, message);
        #endregion

        #region 附件相關方法
        public EntityCollection DownloadAnAttachment(ref IOrganizationService crmService, Guid entityId)
            => _attachmentService.Value.DownloadAttachment(ref crmService, entityId);

        public void UploadAnAttachment(ref IOrganizationService crmService, string entityName, string subject, string noteText, string fileName, string mimeType, byte[] documentBody, Guid toBeAttachedEntityId)
            => _attachmentService.Value.UploadAttachment(ref crmService, entityName, subject, noteText, fileName, mimeType, documentBody, toBeAttachedEntityId);
        #endregion

        #region 字串工具方法
        public static void DeleteLastComma(ref string stringToProcess)
            => StringUtility.DeleteLastComma(ref stringToProcess);

        public string FilterDigit(string filteredString)
            => StringUtility.FilterDigit(filteredString);
        #endregion

        #region 除錯追蹤方法
        public void TraceByLevel(int totalLevel, int qualifiedLevel, string stringToProcess)
        {
            TraceUtility.TraceByLevel(_logger, totalLevel, qualifiedLevel, stringToProcess);
        }

        public static void TraceByLevelStatic(int totalLevel, int qualifiedLevel, string stringToProcess)
        {
            TraceUtility.TraceByLevel(null, totalLevel, qualifiedLevel, stringToProcess);
        }
        #endregion

        #region 個人聚會與靈修記錄方法 (委派給 MeetingStatisticsService)
        public EntityCollection RetrievePresentRecordByFetchXml(string weeklyReportName, string weeklyReportId, string contactName, string contactId)
            => _meetingStatisticsService.Value.RetrieveByWeeklyReportAndContact(weeklyReportName, weeklyReportId, contactName, contactId);

        public EntityCollection RetrievePresentRecordByFetchXmlAndSundayDate(string contactName, string contactId, DateTime sundayDate)
            => _meetingStatisticsService.Value.RetrieveBySundayDateAndContact(contactName, contactId, sundayDate);

        public EntityCollection RetrievePresentRecordByFetchXmlAndWeeklyReport(string contactName, string contactId, string weeklyReportName, string weeklyReportId)
            => _meetingStatisticsService.Value.RetrieveByWeeklyReportAndContactAlt(contactName, contactId, weeklyReportName, weeklyReportId);

        public EntityCollection RetrievePresentRecordByFetchXmlAndContainEpiredDate(string contactName, string contactId)
            => _meetingStatisticsService.Value.RetrieveWithExpiredDateByContact(contactName, contactId);

        public EntityCollection RetrievePresentRecordByFetchXmlAndContact_SmallGroup_SundayDate(string contactName, string contactId, string smallGroupName, string smallGroupId, DateTime sundayDate)
            => _meetingStatisticsService.Value.RetrieveByContactSmallGroupAndSundayDate(contactName, contactId, smallGroupName, smallGroupId, sundayDate);
        #endregion

        #region 名單查詢方法 (委派給 ListService)
        public Entity RetrieveListEntityByName(string listName)
            => _listService.Value.RetrieveListEntityByName(listName);

        public EntityCollection RetrieveListByFetchXmlContact(string contactName)
            => _listService.Value.RetrieveListByContact(contactName);

        public EntityCollection RetrieveListByFetchXmlRacerLeader(string contactName, string contactId)
            => _listService.Value.RetrieveListByRacerLeader(contactName, contactId);
        #endregion

        #region 收費單查詢方法 (委派給 FeeService)
        public EntityCollection RetrieveDedicationFeeByFetchXml(string contactName, string contactId)
            => _feeService.Value.RetrieveDedicationFee(contactName, contactId);

        public EntityCollection RetrieveDedicationFeeByDateFetchXml(string contactName, string contactId, DateTime startDate, DateTime endDate)
            => _feeService.Value.RetrieveDedicationFeeByDateRange(contactName, contactId, startDate, endDate);
        #endregion
    }
}
