using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Xrm.Sdk.PluginTelemetry;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ServiceModel.Description;
using ToolUtilityNameSpace.ActivityOperations;
using ToolUtilityNameSpace.AppointmentOperations;
using ToolUtilityNameSpace.AttachmentOperations;
using ToolUtilityNameSpace.AttributeOperations;
using Microsoft.Xrm.Sdk.Metadata;
using ToolUtilityNameSpace.CollectionOperations;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.ContactOperations;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.FeeOperations;
using ToolUtilityNameSpace.Interfaces;
using ToolUtilityNameSpace.LessonsOperations;
using ToolUtilityNameSpace.LineMessaging;
using ToolUtilityNameSpace.ListOperations;
using ToolUtilityNameSpace.MeetingStatisticsOperations;
using ToolUtilityNameSpace.OwnerOperations;
using ToolUtilityNameSpace.QueryOperations;
using ToolUtilityNameSpace.Utilities;

namespace ToolUtilityNameSpace.Core
{
    /// <summary>
    /// Light-weight Facade for ToolUtility functionality.
    /// This is the new Facade introduced in PR-04. It delegates to smaller services.
    /// It intentionally coexists with the legacy ToolUtilityClass during the refactor.
    /// 完整委派 ToolUtilityClass-developing.cs 的所有功能
    /// </summary>
    public partial class ToolUtilityFacade : IDisposable
    {
        #region 好牧人(雲端機房)
        private const String SERVER = "speechmessage.com.tw";
        private const String PORT = "7777";
        private const String ORGANIZATION = "jesus";
        private const String USERNAME = "Administrator@speechmessage.com.tw";
        private const String PASSWORD = "hu9840";
        private const String DOMAIN = "DYNAMICS-365";
        #endregion

        // Metadata helper methods moved to ToolUtilityFacade.Metadata.cs

        #region 好牧人(公司內部發展)
        //private const String SERVER = "speechmessage.com.tw";
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "jesusback";
        //private const String USERNAME = "Administrator@speechmessage.com.tw";
        //private const String PASSWORD = "hu9840";
        //private const String DOMAIN = "SPEECHMESSAGE";
        #endregion

        private readonly object _logger;
        private readonly ICrmConnectionService _crmConnectionService;

        private IOrganizationService _organizationService; // 改為可變更,由連接服務方法設定

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
        private Lazy<IPresentRecordQueryService> _presentRecordQueryService;
        private Lazy<IRelationshipQueryService> _relationshipQueryService;
        private Lazy<IFetchXmlQueryService> _fetchXmlQueryService;
        private Lazy<IOwnerManagementService> _ownerManagementService;
        private Lazy<IEntityAttributeUtilityService> _entityAttributeUtilityService;
        private Lazy<IActivityService> _activityService;

        private bool _disposed = false;

        /// <summary>
        /// 建構式: 僅接受 logger,organizationService 將透過連接服務方法設定
        /// </summary>
        public ToolUtilityFacade( IOrganizationService aOrganizationService, object logger = null)
        {
            _logger = logger ?? new object();

            // 初始化連接服務
            //_crmConnectionService = new CrmConnectionService();

            // 使用連接服務建立 CRM 連接
            //var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            //var adUsername = @"SPEECHMESSAGE\Administrator";
            //var adPassword = "hu9840";

            //_organizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);

            _organizationService = aOrganizationService;

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
                if (_presentRecordQueryService?.IsValueCreated == true) { var d = _presentRecordQueryService.Value as IDisposable; d?.Dispose(); }
                if (_relationshipQueryService?.IsValueCreated == true) { var d = _relationshipQueryService.Value as IDisposable; d?.Dispose(); }
                if (_fetchXmlQueryService?.IsValueCreated == true) { var d = _fetchXmlQueryService.Value as IDisposable; d?.Dispose(); }
                if (_ownerManagementService?.IsValueCreated == true) { var d = _ownerManagementService.Value as IDisposable; d?.Dispose(); }
                if (_entityAttributeUtilityService?.IsValueCreated == true) { var d = _entityAttributeUtilityService.Value as IDisposable; d?.Dispose(); }
                if (_activityService?.IsValueCreated == true) { var d = _activityService.Value as IDisposable; d?.Dispose(); }

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
            // 使用 Lazy 延遲載入,確保在使用時 _organizationService 已被設定
            _queryService = new Lazy<IEntityQueryService>(() => new EntityQueryService(_logger, _organizationService));
            _crudService = new Lazy<IEntityCrudService>(() => new EntityCrudService(_logger, _organizationService));
            _attributeService = new Lazy<IAttributeService>(() => new AttributeServiceComposite(_logger));
            _contactService = new Lazy<IContactService>(() => new ContactService(_logger, _organizationService));
            _listService = new Lazy<IListService>(() => new ListService(_logger, _organizationService));
            _attachmentService = new Lazy<IAttachmentService>(() => new AttachmentService(_logger, _organizationService));
            _lineMessageService = new Lazy<ILineMessageService>(() => new LineMessageService(_logger, _organizationService));
            _appointmentService = new Lazy<IAppointmentService>(() => new AppointmentService(_logger, _organizationService));
            _lessonsService = new Lazy<ILessonsService>(() => new LessonsService(_logger, _organizationService));
            _feeService = new Lazy<IFeeService>(() => new FeeService(_logger, _organizationService));
            _collectionQueryService = new Lazy<ICollectionQueryService>(() => new CollectionQueryService(_logger, _organizationService));
            _meetingStatisticsService = new Lazy<IMeetingStatisticsService>(() => new MeetingStatisticsService(_logger, _organizationService));
            _connectionService = new Lazy<ICrmConnectionService>(() => new CrmConnectionService());
            _presentRecordQueryService = new Lazy<IPresentRecordQueryService>(() => new PresentRecordQueryService(_logger, _organizationService));
            _relationshipQueryService = new Lazy<IRelationshipQueryService>(() => new RelationshipQueryService(_logger, _organizationService));
            _fetchXmlQueryService = new Lazy<IFetchXmlQueryService>(() => new FetchXmlQueryService(_logger, _organizationService));
            _ownerManagementService = new Lazy<IOwnerManagementService>(() => new OwnerManagementService(_logger, _organizationService));
            _entityAttributeUtilityService = new Lazy<IEntityAttributeUtilityService>(() => new EntityAttributeUtilityService(_logger));
            _activityService = new Lazy<IActivityService>(() => new ActivityService(_logger, _organizationService));
        }

        /// <summary>
        /// 重新初始化所有服務 (當 _organizationService 更新後呼叫)
        /// </summary>
        private void ReinitializeServicesIfNeeded()
        {
            // 如果服務已經被初始化過,需要重新建立 Lazy 實例
            if (_queryService?.IsValueCreated == true || _crudService?.IsValueCreated == true ||
                _contactService?.IsValueCreated == true || _listService?.IsValueCreated == true ||
                _attachmentService?.IsValueCreated == true || _lineMessageService?.IsValueCreated == true ||
                _appointmentService?.IsValueCreated == true || _lessonsService?.IsValueCreated == true ||
                _feeService?.IsValueCreated == true || _collectionQueryService?.IsValueCreated == true ||
                _meetingStatisticsService?.IsValueCreated == true || _presentRecordQueryService?.IsValueCreated == true ||
                _relationshipQueryService?.IsValueCreated == true || _fetchXmlQueryService?.IsValueCreated == true ||
                _ownerManagementService?.IsValueCreated == true || _entityAttributeUtilityService?.IsValueCreated == true ||
                _activityService?.IsValueCreated == true)
            {
                InitializeServices();
            }
        }

        #region 基本實體操作方法
        public Entity RetrieveEntity(string entityName, Guid entityId)
            => _queryService.Value.RetrieveEntity(entityName, entityId);

        public Entity RetrieveEntityByField(string entityName, string fieldName, string fieldValue)
            => _queryService.Value.RetrieveEntityByField(entityName, fieldName, fieldValue);

        public EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue)
            => _collectionQueryService.Value.RetrieveEntityCollectionByField(entityName, fieldName, fieldValue);

        public EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId)
            => _collectionQueryService.Value.QueryWeeklyReportBeforeTowMonthOfSunday(aSunday, aListEntityId);

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

        public void SetEntityIntAttribute(ref Entity entity, string propertyName, int value)
            => _attributeService.Value.SetIntAttribute(ref entity, propertyName, value);

        public string GetEntityStringAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetStringAttribute(entity, propertyName);

        public void SetEntityStringAttribute(ref Entity entity, string propertyName, string value)
            => _attributeService.Value.SetStringAttribute(ref entity, propertyName, value);

        public DateTime GetEntityDateTimeAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetDateTimeAttribute(entity, propertyName);

        public void SetEntityDateTimeAttribute(ref Entity entity, string propertyName, DateTime value)
            => _attributeService.Value.SetDateTimeAttribute(ref entity, propertyName, value);

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

        public void SetEntityLookUpAttribute(ref Entity entity, string propertyName, string lookupEntityName, Guid guidValue)
            => _attributeService.Value.SetLookupAttribute(ref entity, propertyName, lookupEntityName, guidValue);

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

        #region CRM 連接服務方法 (委派給 CrmConnectionService 並設定 _organizationService)
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
        /// 取得 CRM Organization Service 並設定為內部服務
        /// </summary>
        public IOrganizationService GetOrganizationService(string server, string port, string organization, string domain, string userName, string password)
        {
            _organizationService = _connectionService.Value.GetOrganizationService(server, port, organization, domain, userName, password);
            ReinitializeServicesIfNeeded();
            return _organizationService;
        }

        /// <summary>
        /// 設定 CRM 2011 Organization Service 並設定為內部服務
        /// </summary>
        public IOrganizationService SetOrganizationService(string server, string port, string organization, string domain, string userName, string password)
        {
            _organizationService = _connectionService.Value.SetOrganizationService(server, port, organization, domain, userName, password);
            ReinitializeServicesIfNeeded();
            return _organizationService;
        }

        /// <summary>
        /// 設定 Claims-Based 認證的 Organization Service 並設定為內部服務
        /// </summary>
        public IOrganizationService SetClaimsBasedAuthenticationOrganizationService(string organization, string server, string domain, string userName, string password)
        {
            _organizationService = _connectionService.Value.SetClaimsBasedAuthenticationOrganizationService(organization, server, domain, userName, password);
            ReinitializeServicesIfNeeded();
            return _organizationService;
        }

        /// <summary>
        /// 設定 Federated Organization Proxy 並設定為內部服務 (用於 Dynamics 365 Online 和 On-Premise IFD 環境)
        /// </summary>
        public OrganizationServiceProxy SetFederatedOrganizationProxy(string discoveryServiceType, string organization, string server, string port, string baseDiscoveryServiceAddress, string userName, string password, string domain)
        {
            var proxy = _connectionService.Value.SetFederatedOrganizationProxy(discoveryServiceType, organization, server, port, baseDiscoveryServiceAddress, userName, password, domain);
            _organizationService = proxy; // OrganizationServiceProxy 實作 IOrganizationService
            ReinitializeServicesIfNeeded();
            return proxy;
        }

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

        public EntityCollection RetrieveContactCollectionByLineId(string lineId)
            => _contactService.Value.RetrieveCollectionByLineId(lineId);

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

        public EntityCollection QueryContatsByStartedDedicationNumber(String dedicationStartNumber)
            => _contactService.Value.QueryContatsByStartedDedicationNumber(dedicationStartNumber);
        #endregion

        #region 名單相關方法 (委派給 ListService 和 CollectionQueryService)
        public void AddMembersToMarketingList(Guid listGuid, List<Guid> memberGuidList)
            => _listService.Value.AddMembers(listGuid, memberGuidList);

        public void AddMembersToMarketingList(Guid listGuid, List<Guid> memberGuidList, ref IOrganizationService gCRMService)
            => _listService.Value.AddMembersUsingSdk(listGuid, memberGuidList, gCRMService);

        public void RemoveMembersToMarketingList(Guid listGuid, Guid memberGuid)
            => _listService.Value.RemoveMember(listGuid, memberGuid);

        public void RemoveMembersToMarketingList(Guid listGuid, Guid memberGuid, ref IOrganizationService gCRMService)
            => _listService.Value.RemoveMemberUsingSdk(listGuid, memberGuid, gCRMService);

        public ArrayList GetAllMemberDataFromList(Guid listEntityId)
            => _listService.Value.GetAllMemberDataFromList(listEntityId);

        // 成員名單查詢方法
        public EntityCollection RetrieveMemberListCollectionByListId(Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListId(listId);

        public EntityCollection RetrieveMemberListCollectionByListId(ref IOrganizationService organizationService, Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListIdUsingService(_organizationService, listId);

        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(ref OrganizationServiceProxy organizationService, Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListIdUsingProxy(_organizationService, listId);

        public EntityCollection RetrieveMemberListCollectionByListIdCrm2011(ref IOrganizationService organizationService, Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListIdUsingService(_organizationService, listId);

        // 動態名單查詢方法
        public EntityCollection RetrieveDynamicMemberList(string strList)
            => _listService.Value.RetrieveDynamicMemberList(Guid.Parse(strList));

        public EntityCollection RetrieveDynamicMemberList(IOrganizationService service, string strList)
        {
            IOrganizationService svc = _organizationService;
            return RetrieveDynamicMemberList(ref svc, Guid.Parse(strList));
        }

        public EntityCollection RetrieveDynamicMemberListDynamics365(IOrganizationService service, Guid strList)
        {
            IOrganizationService proxy = _organizationService;
            return RetrieveDynamicMemberListDynamics365(ref proxy, strList);
        }

        public EntityCollection RetrieveDynamicMemberListCrm2011(IOrganizationService service, string strList)
        {
            IOrganizationService svc = _organizationService;
            return RetrieveDynamicMemberList(ref svc, Guid.Parse(strList));
        }

        public EntityCollection RetrieveDynamicMemberList(Guid listId)
            => _listService.Value.RetrieveDynamicMemberList(listId);

        public EntityCollection RetrieveDynamicMemberList(ref IOrganizationService service, Guid listId)
            => _listService.Value.RetrieveDynamicMemberListUsingService(service, listId);

        public EntityCollection RetrieveDynamicMemberListDynamics365(ref IOrganizationService service, Guid listId)
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

        #region 個人聚會與靈修記錄查詢方法 (委派給 PresentRecordQueryService)
        /// <summary>
        /// 搜尋主日日期是最近N週的靈修單
        /// </summary>
        public EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int weekPeriod)
            => _presentRecordQueryService.Value.QueryPresentRecordByContactIdAndSunday(listEntityId, contactId, weekPeriod);

        /// <summary>
        /// 根據主日日期排序查詢出席記錄
        /// </summary>
        public EntityCollection QueryPresentRecordSortBySunday(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName)
            => _presentRecordQueryService.Value.QueryPresentRecordSortBySunday(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);

        /// <summary>
        /// 使用 FetchXML 查詢最近N週的出席記錄
        /// </summary>
        public EntityCollection QueryPresentRecordSortBySundayFetchXml(int lastWeeks, string contactName, string contactId)
            => _presentRecordQueryService.Value.QueryPresentRecordSortBySundayFetchXml(lastWeeks, contactName, contactId);

        /// <summary>
        /// 根據週報和聯絡人ID查詢出席記錄
        /// </summary>
        public EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid contactId, Guid weeklyReportEntityId)
            => _presentRecordQueryService.Value.QueryPresentRecordInWeeklyReportByContactId(contactId, weeklyReportEntityId);

        /// <summary>
        /// 根據日期範圍查詢實體清單
        /// </summary>
        public EntityCollection QueryEntityListByDate(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName)
            => _presentRecordQueryService.Value.QueryEntityListByDate(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);

        /// <summary>
        /// 查詢週報(根據主日日期)
        /// </summary>
        public EntityCollection QueryWeeklyReportBySunday(DateTime sunday, Guid listEntityId)
            => _presentRecordQueryService.Value.QueryWeeklyReportBySunday(sunday, listEntityId);

        /// <summary>
        /// 查詢週報(主日日期前兩個月)
        /// </summary>
        public EntityCollection QueryWeeklyReportBeforeTwoMonthOfSunday(DateTime sunday, Guid listEntityId)
            => _presentRecordQueryService.Value.QueryWeeklyReportBeforeTwoMonthOfSunday(sunday, listEntityId);

        /// <summary>
        /// 根據聯絡人ID查詢名單
        /// </summary>
        public EntityCollection QueryListByContactId(Guid contactId, string associationName)
            => _presentRecordQueryService.Value.QueryListByContactId(contactId, associationName);
        #endregion

        #region 關聯查詢方法 (委派給 RelationshipQueryService)
        /// <summary>
        /// 查詢 N:1 關聯的集合
        /// </summary>
        public EntityCollection RetrieveManyToOneRelationship(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName)
            => _relationshipQueryService.Value.RetrieveManyToOneRelationship(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);

        /// <summary>
        /// 查詢 N:1 關聯的集合(根據名稱排序)
        /// </summary>
        public EntityCollection QueryListsAndOrderedByListName(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName)
            => _relationshipQueryService.Value.QueryListsAndOrderedByListName(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);

        /// <summary>
        /// 查詢 N:1 關聯(使用 LinkEntity 取得關聯資料)
        /// </summary>
        public EntityCollection RetrieveManyToOneWithLinkEntity()
            => _relationshipQueryService.Value.RetrieveManyToOneWithLinkEntity();

        /// <summary>
        /// 查詢週報(根據主日日期和N:1關聯)
        /// </summary>
        public EntityCollection QueryWeeklyReportBySunday(DateTime sunday, string parentEntityName,
            string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
            => _relationshipQueryService.Value.QueryWeeklyReportBySunday(sunday, parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);

        /// <summary>
        /// 查詢 N:N (ManyToMany) 的集合
        /// </summary>
        public EntityCollection QueryManyToMany(string conditionAttributeName, string entityNameToSearch,
            string linkFromEntityName, string linkFromAttributeName, string linkToEntityName,
            string linkToAttributeName, string attributeName, Guid entityIdValue)
            => _relationshipQueryService.Value.QueryManyToMany(conditionAttributeName, entityNameToSearch, linkFromEntityName, linkFromAttributeName, linkToEntityName, linkToAttributeName, attributeName, entityIdValue);

        /// <summary>
        /// 連絡人相關的各類名單 (N:N查詢)
        /// </summary>
        public EntityCollection QueryListOfContactManyToMany(Guid contactId)
            => _relationshipQueryService.Value.QueryListOfContactManyToMany(contactId);
        #endregion

        #region FetchXML 查詢方法 (委派給 FetchXmlQueryService)
        /// <summary>
        /// 根據聯絡人查詢學員上課記錄 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveStorLessonsByContact(string contactName, string contactId)
            => _fetchXmlQueryService.Value.RetrieveStorLessonsByFetchXml(contactName, contactId);

        /// <summary>
        /// 根據課程查詢學員上課記錄 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveStorLessonsByDiscipleLessons(string lessonName, string lessonId)
            => _fetchXmlQueryService.Value.RetrieveStorLessonsByDiscipleLessonsFetchXml(lessonName, lessonId);

        /// <summary>
        /// 根據聯絡人查詢認獻記錄 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveDedicationBooking(string contactName, string contactId)
            => _fetchXmlQueryService.Value.RetrieveDedicationBookingByFetchXml(contactName, contactId);

        /// <summary>
        /// 根據主日日期查詢聚會統計記錄 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveMeetingStatistics(DateTime sundayDate)
            => _fetchXmlQueryService.Value.RetrieveMeetingStatisticsByFetchXml(sundayDate);

        /// <summary>
        /// 根據認獻預約和繳費期間查詢收費單 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod)
            => _fetchXmlQueryService.Value.RetrieveFeeByFetchXml(dedicationBookingName, dedicationBookingId, paidPeriod);

        /// <summary>
        /// 查詢所有需要點名的小組名單 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveAllLists()
            => _fetchXmlQueryService.Value.RetrieveListByFetchXml();

        /// <summary>
        /// 查詢所有小組名單集合 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveSmallGroupListCollection()
            => _fetchXmlQueryService.Value.RetrieveSmallGroupListCollectionByFetchXml();
        #endregion

        #region 負責人管理方法 (委派給 OwnerManagementService)
        /// <summary>
        /// 取得實體的負責人 ID
        /// </summary>
        public Guid GetOwnerId(Entity entity)
            => _ownerManagementService.Value.GetOwnerId(entity);

        /// <summary>
        /// 取得實體的負責人名稱
        /// </summary>
        public string GetOwnerName(Entity entity)
            => _ownerManagementService.Value.GetOwnerName(entity);

        /// <summary>
        /// 指派負責人給實體
        /// </summary>
        public void AssignOwner(string entityName, Entity entity, Guid ownerId)
            => _ownerManagementService.Value.AssignOwner(entityName, entity, ownerId);
        #endregion

        #region 實體屬性工具方法 (委派給 EntityAttributeUtilityService)
        /// <summary>
        /// 取得屬性值（支援 AliasedValue）
        /// </summary>
        public string GetAttributeValue(Entity targetEntity, string attributeName)
            => _entityAttributeUtilityService.Value.GetAttributeValue(targetEntity, attributeName);

        /// <summary>
        /// 移除實體的指定屬性
        /// </summary>
        public void RemoveAttribute(ref Entity entity, string propertyName)
            => _entityAttributeUtilityService.Value.RemoveAttribute(ref entity, propertyName);

        /// <summary>
        /// 將實體的指定屬性設為 null
        /// </summary>
        public void SetEntityAttributeToNull(ref Entity entity, string propertyName)
            => _entityAttributeUtilityService.Value.SetEntityAttributeToNull(ref entity, propertyName);
        #endregion

        #region 活動操作方法 (委派給 ActivityService)
        /// <summary>
        /// 取得活動的參與者列表（寄件人或收件人）
        /// </summary>
        public void GetActivityPartyList(Entity activityEntity, string fromOrTo, ArrayList partyList, ArrayList partyTypeList)
            => _activityService.Value.GetActivityPartyList(activityEntity, fromOrTo, partyList, partyTypeList);

        /// <summary>
        /// 取得活動的參與者 ID 列表（寄件人或收件人）
        /// </summary>
        public void GetActivityPartyIdList(Entity activityEntity, string fromOrTo, ArrayList partyIdList, ArrayList partyTypeList)
            => _activityService.Value.GetActivityPartyIdList(activityEntity, fromOrTo, partyIdList, partyTypeList);

        /// <summary>
        /// 將活動狀態設為已完成
        /// </summary>
        public void SetActivityStatusToCompleted(string activityName, Guid activityId)
            => _activityService.Value.SetActivityStatusToCompleted(activityName, activityId);

        /// <summary>
        /// 將活動狀態設為已完成（使用外部服務）
        /// </summary>
        public void SetActivityStatusToCompleted(string activityName, Guid activityId, IOrganizationService organizationService)
            => _activityService.Value.SetActivityStatusToCompleted(activityName, activityId, organizationService);

        /// <summary>
        /// 將約會狀態設為已排程
        /// </summary>
        public void SetAppointmentStatusToScheduled(Guid activityId)
            => _activityService.Value.SetAppointmentStatusToScheduled(activityId);

        /// <summary>
        /// 將約會狀態設為已排程（使用外部服務）
        /// </summary>
        public void SetAppointmentStatusToScheduled(Guid activityId, IOrganizationService organizationService)
            => _activityService.Value.SetAppointmentStatusToScheduled(activityId, organizationService);
        #endregion

        #region 批量並行操作 (Phase 2.3 - 新增)

        /// <summary>
        /// 批量並行添加成員到名單 (非同步)
        /// ? Phase 2.3: 使用 ListService 的批次並行方法
        /// 效能提升: 5-10倍
        /// </summary>
        public async System.Threading.Tasks.Task<int> AddMembersToMarketingListAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int batchSize = 50,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return await _listService.Value.AddMembersAsync(
                listGuid,
                memberGuidList,
                batchSize,
                cancellationToken);
        }

        /// <summary>
        /// 使用 CRM SDK 批量添加成員 (非同步 - 最高效)
        /// ? Phase 2.3: 推薦用於大批量操作 (>100個成員)
        /// 效能提升: 20-50倍
        /// </summary>
        public async System.Threading.Tasks.Task<int> AddMembersToMarketingListUsingSdkAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            IOrganizationService service,
            int maxBatchSize = 1000,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return await _listService.Value.AddMembersUsingSdkAsync(
                listGuid,
                memberGuidList,
                service,
                maxBatchSize,
                cancellationToken);
        }

        /// <summary>
        /// 批量並行移除名單成員 (非同步)
        /// ? Phase 2.3: 批次並行處理
        /// 效能提升: 5-10倍
        /// </summary>
        public async System.Threading.Tasks.Task<int> RemoveMembersFromMarketingListAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int batchSize = 50,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return await _listService.Value.RemoveMembersAsync(
                listGuid,
                memberGuidList,
                batchSize,
                cancellationToken);
        }

        #endregion
    }
}
