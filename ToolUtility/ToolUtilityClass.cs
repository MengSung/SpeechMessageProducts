using Line.Messaging.Webhooks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.SqlServer.Server;
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using PowerPlatform.Dataverse.Client;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceModel.Description;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Core;
using TraceNameSpace;
using static System.Net.WebRequestMethods;

namespace ToolUtilityNameSpace
{
    public class ToolUtilityClass
    {
        #region 資料區
        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365-9.0";

        String m_DiscoveryServiceType = "";

        public IOrganizationService m_Crm2011OrganizationService;
        private bool _disposed = false;

        // public OrganizationServiceProxy m_OrganizationProxy;
        public OrganizationServiceProxy m_OrganizationService;

        // 連接服務專責處理
        private readonly ICrmConnectionService _crmConnectionService;

        // 新架構的 Facade (用於委派複雜業務邏輯)
        private readonly ToolUtilityFacade _facade;

        #region Dynamics 365 新增組織修改區

        // 客製化
        #region 聖谷行道會(雲端機房)
        private const String SERVER = "speechmessage.com.tw";
        private const String PORT = "7777";
        private const String ORGANIZATION = "sunnyvalech";
        private const String USERNAME = "Administrator@speechmessage.com.tw";
        private const String PASSWORD = "hu9840";
        private const String DOMAIN = "DYNAMICS-365";
        #endregion

        #region 聖谷行道會(公司內部發展)
        //private const String SERVER = "speechmessage.com.tw";
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "sunnyvalechback";
        //private const String USERNAME = "Administrator@speechmessage.com.tw";
        //private const String PASSWORD = "hu9840";
        //private const String DOMAIN = "SPEECHMESSAGE";
        #endregion

        #region 僅供參考區塊
        //private String _discoveryServiceAddress = "https://tpehoc.speechmessage.com.tw/XRMServices/2011/Discovery.svc";
        private String BASE_DISCOVERY_SERVICE_ADDRESS = "/XRMServices/2011/Discovery.svc";
        //private String _organizationUniqueName = "tpehoc";
        // Provide your user name and password.
        //private String _userName = "administrator@speechmessage.com.tw";
        //private String _password = "hu9840";

        // Provide domain name for the On-Premises org.
        //private String _domain = "DYNAMICS-365";
        #endregion

        #endregion Class Level Members

        #region 有效截止日期
        private DateTime ExpireDate = new DateTime(2013, 3, 30);
        //private DateTime ExpireDate = new DateTime( 2012, 1, 28 );
        #endregion
        #region 常數參數
        #region 一般常數參數

        private const String FILTERED_PROJECT = ""; // 不過濾建案

        private const int EMPTY_VALUE = -999999999;

        // 是否真的執行 CRM 2011 的 新增、修改、刪除
        private const bool EXCUTION_FLAG = true;
        // 是否真的執行 追蹤Line訊息量
        private const bool EXCUTION_TRACE_LINE = true;

        #endregion
        #region 除錯用參數
        private const int TOTAL_LEVEL = 5;//改變這個值，就會改追蹤的階層，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        private const int LEVEL_1 = 1; // 比較容易被看到的，可能是比較大範圍的部分
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5; // 比較不會被看到的，可能是比較細節的部分
                                       // 如果 TRACE_LEVEL >= TRACE_LEVEL_GROUND 就會進行追蹤
                                       // 如果 TRACE_LEVEL < TRACE_LEVEL_GROUND 就不會進行追蹤
                                       //int TRACE_LEVEL = 5;
                                       //int TRACE_LEVEL_GROUND = 3;
        #endregion

        #endregion
        #region 追蹤專用變數
        private String m_TraceLogFile = "";
        private BugslayerTextWriterTraceListener m_Listener = new BugslayerTextWriterTraceListener();
        private FileStream m_XmlFileStream;
        private StreamWriter m_XmlFileStreamWriter;
        private const String TRACE_DIRECTOR = @"D:\除錯追蹤\" + "CHURCH_REPORT_TRACE.TXT";
        //private const String TRACE_DIRECTOR = @"C:\除錯追蹤\" + "TRACE.TXT";
        #endregion


        #endregion
        #region 建構式
        public ToolUtilityClass()
        {
            // 初始化連接服務
            _crmConnectionService = new CrmConnectionService();

            #region 追蹤專用變數
            m_TraceLogFile = TRACE_DIRECTOR;
            m_XmlFileStream = new FileStream(m_TraceLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            m_XmlFileStreamWriter = new StreamWriter(m_XmlFileStream, Encoding.GetEncoding("big5"));
            m_Listener = new BugslayerTextWriterTraceListener(m_XmlFileStreamWriter);

            Debug.AutoFlush = true;
            Debug.Listeners.Add(m_Listener);
            #endregion

            // 使用連接服務建立 CRM 連接
            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            var adUsername = @"SPEECHMESSAGE\Administrator";
            var adPassword = "hu9840";

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);

            // 初始化 Facade (不傳入 organizationService)
            _facade = new ToolUtilityFacade();
            // 透過 Facade 的連接服務方法設定 organizationService
            //_facade.SetOrganizationService(SERVER, PORT, ORGANIZATION, DOMAIN, adUsername, adPassword);

        }

        public ToolUtilityClass(String DiscoveryServiceType)
        {
            // 初始化連接服務
            _crmConnectionService = new CrmConnectionService();

            m_DiscoveryServiceType = DiscoveryServiceType;

            // 使用連接服務建立 CRM 連接
            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            var adUsername = @"Administrator@speechmessage.com.tw";
            var adPassword = "hu9840";

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);

            // 初始化 Facade (不傳入 organizationService)
            _facade = new ToolUtilityFacade();
            // 透過 Facade 的連接服務方法設定 organizationService
            //_facade.SetOrganizationService(SERVER, PORT, ORGANIZATION, DOMAIN, adUsername, adPassword);
        }

        public ToolUtilityClass(ref bool ValidFlag)
        {
            // 初始化連接服務
            _crmConnectionService = new CrmConnectionService();

            if (ExpireDate >= DateTime.Today)
            {
                ValidFlag = false;
            }
        }

        ~ToolUtilityClass()
        {
        }
        #endregion
        #region 解構式
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            // Free any unmanaged objects here.
            //this.m_OrganizationService.Dispose();

            _disposed = true;
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region 連接 CRM 2011 服務
        /// <summary>
        /// 取得 Windows 認證憑證 (委派給 CrmConnectionService)
        /// </summary>
        public ClientCredentials GetClientCredentials(String Domain, String UserName, String Password)
        {
            return _crmConnectionService.GetClientCredentials(Domain, UserName, Password);
        }

        /// <summary>
        /// 取得 CRM Organization Service (委派給 CrmConnectionService)
        /// </summary>
        public IOrganizationService GetOrganizationService(String Server, String Port, String Organization, String Domain, String UserName, String Password)
        {
            return _crmConnectionService.GetOrganizationService(Server, Port, Organization, Domain, UserName, Password);
        }

        /// <summary>
        /// 取得預設的 Windows 認證憑證 (委派給 CrmConnectionService)
        /// </summary>
        public ClientCredentials GetClientCredentials()
        {
            return _crmConnectionService.GetClientCredentials(DOMAIN, USERNAME, PASSWORD);
        }

        /// <summary>
        /// 設定 CRM 2011 Organization Service (委派給 CrmConnectionService)
        /// </summary>
        public IOrganizationService SetOrganizationService()
        {
            m_Crm2011OrganizationService = _crmConnectionService.SetOrganizationService(
                SERVER, PORT, ORGANIZATION, DOMAIN, USERNAME, PASSWORD);
            return m_Crm2011OrganizationService;
        }

        /// <summary>
        /// 設定 Claims-Based 認證的 Organization Service (委派給 CrmConnectionService)
        /// </summary>
        public IOrganizationService SetClaimsBasedAuthenticationOrganizationService()
        {
            m_Crm2011OrganizationService = _crmConnectionService.SetClaimsBasedAuthenticationOrganizationService(
                ORGANIZATION, SERVER, DOMAIN, USERNAME, PASSWORD);
            return m_Crm2011OrganizationService;
        }

        /// <summary>
        /// 除錯用的 Claims-Based 認證設定 (委派給 CrmConnectionService)
        /// </summary>
        public String SetClaimsBasedAuthenticationOrganizationService_DEBUG()
        {
            String DebugString = "";

            try
            {
                DebugString += "001 - 開始建立連接" + Environment.NewLine;

                m_Crm2011OrganizationService = _crmConnectionService.SetClaimsBasedAuthenticationOrganizationService(
                    ORGANIZATION, SERVER, DOMAIN, USERNAME, PASSWORD);

                DebugString += "002 - 連接成功建立" + Environment.NewLine;

                // 驗證連接
                if (_crmConnectionService.ValidateConnection(m_Crm2011OrganizationService))
                {
                    DebugString += "003 - 連接驗證成功" + Environment.NewLine;
                }
                else
                {
                    DebugString += "003 - 連接驗證失敗" + Environment.NewLine;
                }

                DebugString += "004 - 完成" + Environment.NewLine;
            }
            catch (Exception ex)
            {
                DebugString += $"ERROR - 連接失敗: {ex.Message}" + Environment.NewLine;
            }

            return DebugString;
        }
        #endregion

        #region 連接 Dynamics 365 服務
        /// <summary>
        /// 設定 Federated Organization Proxy (委派給 CrmConnectionService)
        /// 用於 Dynamics 365 Online 和 On-Premise IFD 環境
        /// </summary>
        public OrganizationServiceProxy SetFederatedOrganizationProxy(String DiscoveryServiceType)
        {
            m_OrganizationService = _crmConnectionService.SetFederatedOrganizationProxy(
                DiscoveryServiceType,
                ORGANIZATION,
                SERVER,
                PORT,
                BASE_DISCOVERY_SERVICE_ADDRESS,
                USERNAME,
                PASSWORD,
                DOMAIN);

            return m_OrganizationService;
        }

        /// <summary>
        /// 探索使用者所屬的組織 (委派給 CrmConnectionService)
        /// </summary>
        public OrganizationDetailCollection DiscoverOrganizations(IDiscoveryService service)
        {
            return _crmConnectionService.DiscoverOrganizations(service);
        }

        /// <summary>
        /// 在組織列表中尋找特定組織 (委派給 CrmConnectionService)
        /// </summary>
        public OrganizationDetail FindOrganization(string orgUniqueName, OrganizationDetail[] orgDetails)
        {
            return _crmConnectionService.FindOrganization(orgUniqueName, orgDetails);
        }

        #endregion
        #region 透過屬性取得實體
        #region 取得一般實體
        public Entity RetrieveEntityByField(String EntityName, String FieldName, String FieldValue)
        {
            try
            {
                // 委派給 Facade 處理
                return _facade.RetrieveEntityByField(EntityName, FieldName, FieldValue);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveEntityByField 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveEntityCollectionByField(String EntityName, String FieldName, String FieldValue)
        {
            try
            {
                // 委派給 Facade 處理
                return _facade.RetrieveEntityCollectionByField(EntityName, FieldName, FieldValue);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveEntityCollectionByField 錯誤: " + e.Message);
                throw;
            }
        }
        #endregion
        #region 取得聯絡人 - 委派到 Facade
        public String RetrieveContactByContactId(String ContactId)
            => _facade.RetrieveContactByContactId(ContactId);

        public Entity RetrieveContactByContactId(ref IOrganizationService aOrganizationService, String ContactId, ref int Count)
            => _facade.RetrieveContactByContactId(ref aOrganizationService, ContactId, ref Count);

        public String RetrieveContactByName(String ContactFullName)
            => _facade.RetrieveContactByName(ContactFullName);

        public Entity RetrieveContactEntityByName(String ContactFullName)
            => _facade.RetrieveContactEntityByName(ContactFullName);

        public Entity RetrieveContactByName(ref IOrganizationService aOrganizationService, String ContactFullName)
            => _facade.RetrieveContactByName(ref aOrganizationService, ContactFullName);

        public String RetrieveContactByName_ReturnString(ref IOrganizationService aOrganizationService, String ContactFullName)
            => _facade.RetrieveContactByName_ReturnString(ref aOrganizationService, ContactFullName);

        public EntityCollection RetrieveContactCollectionByName(String ContactFullName)
            => _facade.RetrieveContactCollectionByName(ContactFullName);

        public EntityCollection RetrieveContactCollectionByNationId(String ContactFullName)
            => _facade.RetrieveContactCollectionByNationId(ContactFullName);

        public Entity RetrieveContactByLineId(String LineId)
            => _facade.RetrieveContactByLineId(LineId);

        public String RetrieveContactByAccountNumber(String AccountNumber, String aPassword)
            => _facade.RetrieveContactByAccountNumber(AccountNumber, aPassword);

        public Entity DoesAccountExist(String AccountNumber)
            => _facade.DoesAccountExist(AccountNumber);

        public Entity RetrieveContactEntityByAccountNumber(String AccountNumber, String aPassword)
            => _facade.RetrieveContactEntityByAccountNumber(AccountNumber, aPassword);

        public Entity RetrieveContactEntityByLineUserId(String LineUserId)
            => _facade.RetrieveContactEntityByLineUserId(LineUserId);

        public Entity RetrieveContactEntityByFullNameAndMobileNumber(String FullName, String MobileNumber)
            => _facade.RetrieveContactEntityByFullNameAndMobileNumber(FullName, MobileNumber);

        public EntityCollection RetrieveContactEntityByFullNameCollection(String FullName)
            => _facade.RetrieveContactEntityByFullNameCollection(FullName);

        public EntityCollection QueryDediccationContatsByFetchXml(String DedicationNumber, String ContactName, String HomePhone, String Mobile, String NationId, String LastSixDigit)
            => _facade.QueryDediccationContatsByFetchXml(DedicationNumber, ContactName, HomePhone, Mobile, NationId, LastSixDigit);

        public EntityCollection QueryContatsByStartedDedicationNumber(String DedicationStartNumber)
            => _facade.QueryContatsByStartedDedicationNumber(DedicationStartNumber);
        #endregion
        #region 名單相關方法 - 委派到 Facade
        public EntityCollection RetrieveMemberListCollectionByListId(Guid aListId)
            => _facade.RetrieveMemberListCollectionByListId(aListId);

        public EntityCollection RetrieveMemberListCollectionByListId(ref IOrganizationService aOrganizationService, Guid aListId)
            => _facade.RetrieveMemberListCollectionByListId(ref aOrganizationService, aListId);

        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(ref OrganizationServiceProxy aOrganizationService, Guid aListId)
            => _facade.RetrieveMemberListCollectionByListIdDynamics365(ref aOrganizationService, aListId);

        public EntityCollection RetrieveMemberListCollectionByListIdCrm2011(ref IOrganizationService aOrganizationService, Guid aListId)
            => _facade.RetrieveMemberListCollectionByListIdCrm2011(ref aOrganizationService, aListId);

        public EntityCollection RetrieveDynamicMemberList(string strList)
            => _facade.RetrieveDynamicMemberList(strList);

        public EntityCollection RetrieveDynamicMemberList(IOrganizationService service, string strList)
            => _facade.RetrieveDynamicMemberList(service, strList);

        public EntityCollection RetrieveDynamicMemberListDynamics365(OrganizationServiceProxy service, string strList)
            => _facade.RetrieveDynamicMemberListDynamics365(service, strList);

        public EntityCollection RetrieveDynamicMemberListCrm2011(IOrganizationService service, string strList)
            => _facade.RetrieveDynamicMemberListCrm2011(service, strList);

        public EntityCollection RetrieveDynamicMemberList(Guid aListId)
            => _facade.RetrieveDynamicMemberList(aListId);

        public EntityCollection RetrieveDynamicMemberList(ref IOrganizationService service, Guid aListId)
            => _facade.RetrieveDynamicMemberList(ref service, aListId);

        public EntityCollection RetrieveDynamicMemberListDynamics365(ref OrganizationServiceProxy service, Guid aListId)
            => _facade.RetrieveDynamicMemberListDynamics365(ref service, aListId);

        public EntityCollection RetrieveDynamicMemberListCrm2011(ref IOrganizationService service, Guid aListId)
            => _facade.RetrieveDynamicMemberListCrm2011(ref service, aListId);
        #endregion
        #region 取得客戶(Account)組織 - 委派到 Facade
        public Guid RetrieveAccountCollectionByName(String AccountName)
            => _facade.RetrieveAccountCollectionByName(AccountName);
        #endregion
        #region 取得約會 - 委派到 Facade
        public EntityCollection RetrieveAppointmentsByDate(DateTime aSelectedDate)
            => _facade.RetrieveAppointmentsByDate(aSelectedDate);

        public EntityCollection RetrieveAppointmentsByFetchXml(DateTime StartDate, DateTime EndDate)
            => _facade.RetrieveAppointmentsByFetchXml(StartDate, EndDate);

        public EntityCollection RetrieveAppointmentsByFetchXml(String ContactName, String ContactId)
            => _facade.RetrieveAppointmentsByFetchXml(ContactName, ContactId);

        public EntityCollection RetrieveAppointmentsByFetchXmlAndScheduleType(DateTime StartDate, DateTime EndDate, String ScheduleType)
            => _facade.RetrieveAppointmentsByFetchXmlAndScheduleType(StartDate, EndDate, ScheduleType);
        #endregion
        #region 取得課程 - 委派到 Facade
        /// <summary>
        /// 特定連絡人已報名的課程
        /// </summary>
        public EntityCollection RetrieveEnrolledLessonsByFetchXml(DateTime StartDate, DateTime EndDate, String ContactName, String ContactId)
            => _facade.RetrieveEnrolledLessonsByFetchXml(StartDate, EndDate, ContactName, ContactId);

        public EntityCollection RetrieveLessonsByMonth(DateTime StartDate, DateTime EndDate)
            => _facade.RetrieveLessonsByMonth(StartDate, EndDate);
        #endregion
        #region 取得上課紀錄單 - 委派到 Facade
        /// <summary>
        /// 特定連絡人已報名的課程
        /// </summary>
        public EntityCollection RetrieveStorLessonsByFetchXml(String LessonName, String LessonId, String ContactName, String ContactId)
            => _facade.RetrieveStorLessonsByFetchXml(LessonName, LessonId, ContactName, ContactId);
        #endregion
        #region 取得工作 - 委派到 Facade
        /// <summary>
        /// 根據主旨查詢工作
        /// </summary>
        public EntityCollection RetrieveTaskByFetchXml(String Subject)
            => _facade.RetrieveTaskByFetchXml(Subject);
        #endregion
        #region 取得個人聚會與靈修記錄 - 委派到 Facade
        /// <summary>
        /// 根據週報和連絡人查詢出席記錄
        /// </summary>
        public EntityCollection RetrievePresentRecordByFetchXml(String WeeklyReportName, String WeeklyReportId, String ContactName, String ContactId)
            => _facade.RetrievePresentRecordByFetchXml(WeeklyReportName, WeeklyReportId, ContactName, ContactId);

        public EntityCollection RetrievePresentRecordByFetchXmlAndSundayDate(String ContactName, String ContactId, DateTime SundayDate)
            => _facade.RetrievePresentRecordByFetchXmlAndSundayDate(ContactName, ContactId, SundayDate);

        public EntityCollection RetrievePresentRecordByFetchXmlAndWeeklyReport(String ContactName, String ContactId, String WeeklyReportNmae, String WeeklyReportId)
            => _facade.RetrievePresentRecordByFetchXmlAndWeeklyReport(ContactName, ContactId, WeeklyReportNmae, WeeklyReportId);

        public EntityCollection RetrievePresentRecordByFetchXmlAndContainEpiredDate(String ContactName, String ContactId)
            => _facade.RetrievePresentRecordByFetchXmlAndContainEpiredDate(ContactName, ContactId);

        public EntityCollection RetrievePresentRecordByFetchXmlAndContact_SmallGroup_SundayDate(String ContactName, String ContactId, String SmallGroupName, String SmallGroupId, DateTime SundayDate)
            => _facade.RetrievePresentRecordByFetchXmlAndContact_SmallGroup_SundayDate(ContactName, ContactId, SmallGroupName, SmallGroupId, SundayDate);
        #endregion
        #region 取得名單 - 委派到 Facade
        public Entity RetrieveListEntityByName(String ListName)
            => _facade.RetrieveListEntityByName(ListName);

        public EntityCollection RetrieveListByFetchXmlContact(String ContactName)
            => _facade.RetrieveListByFetchXmlContact(ContactName);

        public EntityCollection RetrieveListByFetchXmlRacerLeader(String ContactName, String ContactId)
            => _facade.RetrieveListByFetchXmlRacerLeader(ContactName, ContactId);
        #endregion
        #region 取得收費單 - 委派到 Facade
        /// <summary>
        /// 根據連絡人查詢奉獻收費單
        /// </summary>
        public EntityCollection RetrieveDedicationFeeByFetchXml(String ContactName, String ContactId)
            => _facade.RetrieveDedicationFeeByFetchXml(ContactName, ContactId);

        public EntityCollection RetrieveDedicationFeeByDateFetchXml(String ContactName, String ContactId, DateTime StartDate, DateTime EndDate)
            => _facade.RetrieveDedicationFeeByDateFetchXml(ContactName, ContactId, StartDate, EndDate);
        #endregion
        #endregion
        #region 搜尋 N:1 的集合 - 委派到 Facade
        public EntityCollection RetrieveManyToOneCollection()
            => _facade.RetrieveManyToOneWithLinkEntity();

        public Entity QueryBloodReportByContactId(Guid ContactId)
        {
            // 特殊業務邏輯,暫時保留原始實現
            try
            {
                var query = new QueryExpression
                {
                    EntityName = "new_blood_report",
                    ColumnSet = new ColumnSet(true)
                };

                var filter = new FilterExpression(LogicalOperator.And);
                filter.AddCondition("new_blood_contact_relation", ConditionOperator.Equal, ContactId);
                query.Criteria = filter;
                query.AddOrder("createdon", OrderType.Descending);

                var retrieved = m_Crm2011OrganizationService.RetrieveMultiple(query);
                return retrieved.Entities.Count > 0 ? retrieved.Entities[0] : null;
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "QueryBloodReportByContactId 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection QueryPresentRecordByContactIdAndSunday(Guid aListEntityId, Guid ContactId, int MonthPeriod)
            => _facade.QueryPresentRecordByContactIdAndSunday(aListEntityId, ContactId, MonthPeriod);

        public EntityCollection RetrieveManyToOneRelationship(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.RetrieveManyToOneRelationship(ParentEntityName, ParentEntityIdName, ParentEntityId, AssociationName, ChildEntityName);

        public EntityCollection QueryPresentRecordSortBySunday(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.QueryPresentRecordSortBySunday(ParentEntityName, ParentEntityIdName, ParentEntityId, AssociationName, ChildEntityName);

        public EntityCollection QueryPresentRecordSortBySundayFetchXml(int LastWeeks, String ContactName, String ContactId)
            => _facade.QueryPresentRecordSortBySundayFetchXml(LastWeeks, ContactName, ContactId);

        public EntityCollection QueryPresentRecordSortBySunday_BACKUP(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.RetrieveManyToOneRelationship(ParentEntityName, ParentEntityIdName, ParentEntityId, AssociationName, ChildEntityName);

        public EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid aContactId, Guid aWeeklyReportEntityId)
            => _facade.QueryPresentRecordInWeeklyReportByContactId(aContactId, aWeeklyReportEntityId);

        public EntityCollection QueryEntityListByDate(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.QueryEntityListByDate(ParentEntityName, ParentEntityIdName, ParentEntityId, AssociationName, ChildEntityName);

        public EntityCollection RetrieveManyToOneRelationship()
            => _facade.RetrieveManyToOneWithLinkEntity();

        public EntityCollection QueryWeeklyReportBySunday(DateTime aSunday, String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.QueryWeeklyReportBySunday(aSunday, ParentEntityName, ParentEntityIdName, ParentEntityId, AssociationName, ChildEntityName);

        public EntityCollection QueryListsAndOrderedByListName(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.QueryListsAndOrderedByListName(ParentEntityName, ParentEntityIdName, ParentEntityId, AssociationName, ChildEntityName);

        public EntityCollection QueryListByContactId(Guid aContactId, String AssociationName)
            => _facade.QueryListByContactId(aContactId, AssociationName);

        public EntityCollection QueryWeeklyReportBySunday(DateTime aSunday, Guid aListEntityId)
            => _facade.QueryWeeklyReportBySunday(aSunday, aListEntityId);

        public EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId)
            => _facade.QueryWeeklyReportBeforeTwoMonthOfSunday(aSunday, aListEntityId);

        public Entity RetrieveContactCollectionByLineId(String LineId)
            => _facade.RetrieveContactByLineId(LineId);

        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(Guid aListId)
            => _facade.RetrieveMemberListCollectionByListId(aListId);

        public EntityCollection RetrieveDynamicMemberListDynamics365(Guid aListId)
            => _facade.RetrieveDynamicMemberList(aListId);
        #endregion
        #region 透過FetchXml取得實體或是集合 - 委派到 Facade
        public EntityCollection RetrieveStorLessonsByFetchXml(String ContactName, String ContactId)
            => _facade.RetrieveStorLessonsByContact(ContactName, ContactId);

        public EntityCollection RetrieveStorLessonsByDiscipleLessonsFetchXml(String LessonName, String LessonId)
            => _facade.RetrieveStorLessonsByDiscipleLessons(LessonName, LessonId);

        public EntityCollection RetrieveDedicationBookingByFetchXml(String ContactName, String ContactId)
            => _facade.RetrieveDedicationBooking(ContactName, ContactId);

        public EntityCollection RetrieveMeetingStatisticsByFetchXml(DateTime SundayDate)
            => _facade.RetrieveMeetingStatistics(SundayDate);

        public EntityCollection RetrieveFeeByFetchXml(String DedicationBookingName, String DedicationBookingId, String PaidPeriod)
            => _facade.RetrieveFee(DedicationBookingName, DedicationBookingId, PaidPeriod);

        public EntityCollection RetrieveListByFetchXml()
            => _facade.RetrieveAllLists();

        public EntityCollection RetrieveSmallGroupListCollectionByFetchXml()
            => _facade.RetrieveSmallGroupListCollection();
        #endregion
        #region 搜尋 N:N (ManyToMany) 的集合 - 委派到 Facade
        public EntityCollection QueryManyToMany(String ConditionAttributeName, String EntityNameToSearch,
            String LinkFromEntityName, String LinkFromAttributeName, String LinkToEntityName,
            String LinkToAttributeName, String AttributeName, Guid EntityIdValue)
            => _facade.QueryManyToMany(ConditionAttributeName, EntityNameToSearch, LinkFromEntityName,
                LinkFromAttributeName, LinkToEntityName, LinkToAttributeName, AttributeName, EntityIdValue);

        public EntityCollection QueryListOfContactManyToMany(Guid ContactId)
            => _facade.QueryListOfContactManyToMany(ContactId);

        public EntityCollection QueryEntityList(String ParentEntityName, String ParentEntityIdName,
            String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.QueryEntityListByDate(ParentEntityName, ParentEntityIdName, ParentEntityId,
                AssociationName, ChildEntityName);
        #endregion
        #region 實體操作區 - 委派到 Facade
        public Entity RetrieveEntity(String EntityName, Guid EntityId)
            => _facade.RetrieveEntity(EntityName, EntityId);

        public Entity RetrieveEntityDynamics365(String EntityName, Guid EntityId)
            => _facade.RetrieveEntity(EntityName, EntityId);

        public Entity RetrieveEntityCrm2011(String EntityName, Guid EntityId)
            => _facade.RetrieveEntity(EntityName, EntityId);

        public Guid CreateEntity(Entity aEntityTobeToCreate)
            => _facade.CreateEntity(aEntityTobeToCreate);

        public Guid CreateEntityDynamics365(ref OrganizationServiceProxy aOrganizationService, Entity aEntityTobeToCreate)
        {
            try
            {
                if (EXCUTION_FLAG == true)
                {
                    return aOrganizationService.Create(aEntityTobeToCreate);
                }
                return Guid.Empty;
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public Guid CreateEntityCrm2011(ref IOrganizationService aCrm2011OrganizationService, Entity aEntityTobeToCreate)
        {
            try
            {
                if (EXCUTION_FLAG == true)
                {
                    return aCrm2011OrganizationService.Create(aEntityTobeToCreate);
                }
                return Guid.Empty;
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public async Task<Guid> CreateEntityAsync(IOrganizationService aOrganizationService, Entity aEntityTobeToCreate)
        {
            try
            {
                if (EXCUTION_FLAG == true)
                {
                    return aOrganizationService.Create(aEntityTobeToCreate);
                }
                return Guid.Empty;
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public void UpdateEntity(ref Entity aEntityTobeUpdated)
            => _facade.UpdateEntity(aEntityTobeUpdated);

        public void UpdateEntity(Entity aEntityTobeUpdated)
            => _facade.UpdateEntity(aEntityTobeUpdated);

        public void UpdateEntity(ref IOrganizationService aOrganizationService, ref Entity aEntityTobeUpdated)
        {
            try
            {
                if (EXCUTION_FLAG == true)
                {
                    aOrganizationService.Update(aEntityTobeUpdated);
                }
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public void UpdateEntity(ref IOrganizationService aOrganizationService, Entity aEntityTobeUpdated)
        {
            try
            {
                if (EXCUTION_FLAG == true)
                {
                    aOrganizationService.Update(aEntityTobeUpdated);
                }
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public void UpdateEntityCrm2011(ref IOrganizationService aOrganizationService, ref Entity aEntityTobeUpdated)
        {
            try
            {
                if (EXCUTION_FLAG == true)
                {
                    aOrganizationService.Update(aEntityTobeUpdated);
                }
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public void UpdateEntityDynamics365(ref OrganizationServiceProxy aOrganizationService, ref Entity aEntityTobeUpdated)
        {
            try
            {
                if (EXCUTION_FLAG == true)
                {
                    aOrganizationService.Update(aEntityTobeUpdated);
                }
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public void UpdateEntityCrm2011(ref IOrganizationService aOrganizationService, Entity aEntityTobeUpdated)
        {
            try
            {
                if (EXCUTION_FLAG == true)
                {
                    aOrganizationService.Update(aEntityTobeUpdated);
                }
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public void UpdateEntityDynamics365(ref OrganizationServiceProxy aOrganizationService, Entity aEntityTobeUpdated)
        {
            try
            {
                if (EXCUTION_FLAG == true)
                {
                    aOrganizationService.Update(aEntityTobeUpdated);
                }
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public async Task UpdateEntityAsync(IOrganizationService aOrganizationService, Entity aEntityTobeUpdated)
        {
            try
            {
                if (EXCUTION_FLAG == true)
                {
                    aOrganizationService.Update(aEntityTobeUpdated);
                }
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public void DeleteEntity(ref IOrganizationService aOrganizationService, String aEntityName, Guid aEntityId)
        {
            try
            {
                if (EXCUTION_FLAG == true)
                {
                    aOrganizationService.Delete(aEntityName, aEntityId);
                }
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public void DeleteEntity(String aEntityName, Guid aEntityId)
            => _facade.DeleteEntity(aEntityName, aEntityId);

        public Guid GetEntityId(Entity aEntity)
            => aEntity.Id;
        #endregion
        #region 屬性操作區 - 委派到 Facade

        #region 布林屬性
        public bool GetEntityBoolAttribute(ref Entity aEntity, string PropertyName)
            => _facade.GetEntityBoolAttribute(aEntity, PropertyName);

        public bool GetEntityBoolAttribute(Entity aEntity, string PropertyName)
            => _facade.GetEntityBoolAttribute(aEntity, PropertyName);

        public void SetEntityBoolAttribute(ref Entity aEntity, string PropertyName, bool PropertyValue)
            => _facade.SetEntityBoolAttribute(ref aEntity, PropertyName, PropertyValue);

        public void SetEntityBoolAttributeToNull(ref Entity aEntity, string PropertyName)
        {
            if (aEntity.Attributes.Contains(PropertyName))
            {
                aEntity.Attributes[PropertyName] = null;
            }
            else
            {
                aEntity.Attributes.Add(PropertyName, null);
            }
        }
        #endregion

        #region 整數屬性
        public int GetEntityIntAttribute(ref Entity aEntity, string PropertyName)
            => _facade.GetEntityIntAttribute(aEntity, PropertyName);

        public int GetEntityIntAttribute(Entity aEntity, string PropertyName)
            => _facade.GetEntityIntAttribute(aEntity, PropertyName);

        public void SetEntityIntAttribute(ref Entity aEntity, string PropertyName, int PropertyValue)
        {
            if (aEntity.Attributes.Contains(PropertyName))
            {
                aEntity.Attributes[PropertyName] = PropertyValue;
            }
            else
            {
                aEntity.Attributes.Add(PropertyName, PropertyValue);
            }
        }

        public void SetEntityIntAttributeToNull(ref Entity aEntity, string PropertyName)
        {
            if (aEntity.Attributes.Contains(PropertyName))
            {
                aEntity.Attributes[PropertyName] = null;
            }
            else
            {
                aEntity.Attributes.Add(PropertyName, null);
            }
        }
        #endregion

        #region 浮點屬性
        public float GetEntityFloatAttribute(ref Entity aEntity, string PropertyName)
            => _facade.GetEntityFloatAttribute(aEntity, PropertyName);

        public float GetEntityFloatAttribute(Entity aEntity, string PropertyName)
            => _facade.GetEntityFloatAttribute(aEntity, PropertyName);

        public void SetEntityFloatAttribute(ref Entity aEntity, string PropertyName, float PropertyValue)
            => _facade.SetEntityFloatAttribute(ref aEntity, PropertyName, PropertyValue);

        public void SetEntityFloatAttribute(Entity aEntity, string PropertyName, float PropertyValue)
        {
            Entity tempEntity = aEntity;
            _facade.SetEntityFloatAttribute(ref tempEntity, PropertyName, PropertyValue);
        }

        public void SetEntityFloatAttributeToNull(Entity aEntity, string PropertyName)
            => _facade.SetEntityFloatAttributeToNull(aEntity, PropertyName);
        #endregion

        #region 金額屬性
        public Money GetEntityMoneyAttribute(ref Entity aEntity, string PropertyName)
            => _facade.GetEntityMoneyAttribute(aEntity, PropertyName);

        public Money GetEntityMoneyAttribute(Entity aEntity, string PropertyName)
            => _facade.GetEntityMoneyAttribute(aEntity, PropertyName);

        public void SetEntityMoneyAttribute(ref Entity aEntity, string PropertyName, Money PropertyValue)
        {
            if (PropertyValue.Value != -9999)
            {
                if (aEntity.Attributes.Contains(PropertyName))
                {
                    aEntity.Attributes[PropertyName] = PropertyValue;
                }
                else
                {
                    aEntity.Attributes.Add(PropertyName, PropertyValue);
                }
            }
        }

        public void SetEntityMoneyAttribute(Entity aEntity, string PropertyName, Money PropertyValue)
        {
            if (aEntity.Attributes.Contains(PropertyName))
            {
                aEntity.Attributes[PropertyName] = PropertyValue;
            }
            else
            {
                aEntity.Attributes.Add(PropertyName, PropertyValue);
            }
        }

        public void SetEntityMoneyAttributeToNull(Entity aEntity, string PropertyName)
        {
            Entity tempEntity = aEntity;
            _facade.SetEntityMoneyAttributeToNull(ref tempEntity, PropertyName);
        }
        #endregion

        #region 小數點屬性
        public Double GetEntityDoubleAttribute(ref Entity aEntity, string PropertyName)
            => _facade.GetEntityDoubleAttribute(aEntity, PropertyName);

        public Double GetEntityDoubleAttribute(Entity aEntity, string PropertyName)
            => _facade.GetEntityDoubleAttribute(aEntity, PropertyName);

        public void SetEntityDoubleAttribute(ref Entity aEntity, string PropertyName, Double PropertyValue)
            => _facade.SetEntityDoubleAttribute(ref aEntity, PropertyName, PropertyValue);

        public void SetEntityDoubleAttribute(Entity aEntity, string PropertyName, Double PropertyValue)
        {
            Entity tempEntity = aEntity;
            _facade.SetEntityDoubleAttribute(ref tempEntity, PropertyName, PropertyValue);
        }

        public void SetEntityDoubleAttributeToNull(Entity aEntity, string PropertyName)
            => _facade.SetEntityDoubleAttributeToNull(aEntity, PropertyName);
        #endregion

        #region 時間屬性
        public DateTime GetEntityDateTimeAttribute(ref Entity aEntity, string PropertyName)
            => _facade.GetEntityDateTimeAttribute(aEntity, PropertyName);

        public DateTime GetEntityDateTimeAttribute(Entity aEntity, string PropertyName)
            => _facade.GetEntityDateTimeAttribute(aEntity, PropertyName);

        public void SetEntityDateTimeAttribute(ref Entity aEntity, string PropertyName, DateTime PropertyValue)
        {
            if (aEntity.Attributes.Contains(PropertyName))
            {
                aEntity.Attributes[PropertyName] = PropertyValue;
            }
            else
            {
                aEntity.Attributes.Add(PropertyName, PropertyValue);
            }
        }

        public void SetEntityDateTimeAttributeToNull(ref Entity aEntity, string PropertyName)
            => _facade.SetEntityDateTimeAttributeToNull(ref aEntity, PropertyName);
        #endregion

        #region 文字屬性
        public String GetEntityStringAttribute(ref Entity aEntity, string PropertyName)
            => _facade.GetEntityStringAttribute(aEntity, PropertyName);

        public String GetEntityStringAttribute(Entity aEntity, string PropertyName)
            => _facade.GetEntityStringAttribute(aEntity, PropertyName);

        public void SetEntityStringAttribute(ref Entity aEntity, string PropertyName, String PropertyValue)
        {
            if (aEntity.Attributes.Contains(PropertyName))
            {
                aEntity.Attributes[PropertyName] = PropertyValue;
            }
            else
            {
                aEntity.Attributes.Add(PropertyName, PropertyValue);
            }
        }

        public void SetEntityStringAttribute(Entity aEntity, string PropertyName, String PropertyValue)
        {
            if (aEntity.Attributes.Contains(PropertyName))
            {
                aEntity.Attributes[PropertyName] = PropertyValue;
            }
            else
            {
                aEntity.Attributes.Add(PropertyName, PropertyValue);
            }
        }
        #endregion

        #region 選項屬性
        public int GetOptionSetAttribute(ref Entity aEntity, string PropertyName)
            => _facade.GetOptionSetAttribute(aEntity, PropertyName);

        public int GetOptionSetAttribute(Entity aEntity, string PropertyName)
            => _facade.GetOptionSetAttribute(aEntity, PropertyName);

        public void SetOptionSetAttribute(ref Entity aEntity, string PropertyName, int PropertyValue)
            => _facade.SetOptionSetAttribute(ref aEntity, PropertyName, PropertyValue);

        public void SetOptionSetAttribute(Entity aEntity, string PropertyName, int PropertyValue)
        {
            Entity tempEntity = aEntity;
            _facade.SetOptionSetAttribute(ref tempEntity, PropertyName, PropertyValue);
        }

        public void SetOptionSetAttributeNull(ref Entity aEntity, string PropertyName)
            => _facade.SetOptionSetAttributeNull(ref aEntity, PropertyName);

        public void SetOptionSetAttributeNull(Entity aEntity, string PropertyName)
        {
            Entity tempEntity = aEntity;
            _facade.SetOptionSetAttributeNull(ref tempEntity, PropertyName);
        }
        #endregion

        #region LookUp 屬性
        public Guid GetEntityLookupAttribute(ref Entity aEntity, string PropertyName)
            => _facade.GetEntityLookupAttribute(aEntity, PropertyName);

        public Guid GetEntityLookupAttribute(Entity aEntity, string PropertyName)
            => _facade.GetEntityLookupAttribute(aEntity, PropertyName);

        public String GetEntityLookupDisplayName(ref Entity aEntity, string PropertyName)
            => _facade.GetEntityLookupDisplayName(aEntity, PropertyName);

        public String GetEntityLookupDisplayName(Entity aEntity, string PropertyName)
            => _facade.GetEntityLookupDisplayName(aEntity, PropertyName);

        public void SetEntityLookUpAttribute(ref Entity aEntity, string PropertyName, String LookupEntityName, Guid GuidValue)
        {
            if (GuidValue != null && GuidValue != Guid.Empty)
            {
                EntityReference aEntityReference = new EntityReference(LookupEntityName, GuidValue);
                if (aEntity.Attributes.Contains(PropertyName))
                {
                    aEntity.Attributes[PropertyName] = aEntityReference;
                }
                else
                {
                    aEntity.Attributes.Add(PropertyName, aEntityReference);
                }
            }
        }

        public void SetEntityLookUpAttribute(Entity aEntity, string PropertyName, String LookupEntityName, Guid GuidValue)
        {
            if (GuidValue != null && GuidValue != Guid.Empty)
            {
                EntityReference aEntityReference = new EntityReference(LookupEntityName, GuidValue);
                if (aEntity.Attributes.Contains(PropertyName))
                {
                    aEntity.Attributes[PropertyName] = aEntityReference;
                }
                else
                {
                    aEntity.Attributes.Add(PropertyName, aEntityReference);
                }
            }
        }

        public void SetEntityLookUpAttribute(ref Entity aEntity, string PropertyName, ref EntityReference aEntityReference)
            => _facade.SetEntityLookUpAttribute(ref aEntity, PropertyName, ref aEntityReference);

        public void SetEntityLookUpToNull(ref Entity aEntity, string PropertyName)
            => _facade.SetEntityLookUpToNull(ref aEntity, PropertyName);
        #endregion

        #endregion
        #region 負責人管理
        public Guid GetOwnerId(Entity aEntity)
        {
            try
            {
                return aEntity.GetAttributeValue<EntityReference>("ownerid").Id;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void AssignOwner(String EntityName, Entity aEntity, Guid OwnerId)
        {
            try
            {
                AssignRequest assign = new AssignRequest
                {
                    Assignee = new EntityReference("systemuser", OwnerId),
                    Target = new EntityReference(EntityName, aEntity.Id)
                };

                // Execute the Request
                if (CRM_TYPE == "DYNAMICS365")
                {
                    this.m_OrganizationService.Execute(assign);
                }
                else
                {
                    this.m_Crm2011OrganizationService.Execute(assign);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public String GetOwnerName(Entity aEntity)
        {
            try
            {
                return aEntity.GetAttributeValue<EntityReference>("ownerid").Name;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 追蹤及統計用的Line訊息
        public void CreatePushLineMessage(string UserId, string Subject, string Message)
        {
            try
            {
                if (EXCUTION_TRACE_LINE == true)
                {
                    Entity aContact = RetrieveContactCollectionByLineId(UserId);

                    if (aContact != null)
                    {
                        Entity aEntity = new Entity("letter");
                        SetEntityStringAttribute(ref aEntity, "subject", Subject);
                        SetEntityStringAttribute(ref aEntity, "description", Message);
                        SetEntityStringAttribute(ref aEntity, "new_displayed_lineid", UserId);
                        SetEntityLookUpAttribute(ref aEntity, "regardingobjectid", "contact", aContact.Id);

                        SetEntityDateTimeAttribute(ref aEntity, "scheduledend", DateTime.Now);

                        //方向=>撥出
                        SetEntityBoolAttribute(ref aEntity, "directioncode", true);

                        //計數=>1
                        SetEntityIntAttribute(ref aEntity, "new_count", 1);

                        //設定訊息種類為文字 
                        SetOptionSetAttribute(ref aEntity, "new_message_category", 100000000);

                        Entity Fromparty = new Entity("activityparty");

                        Fromparty["partyid"] = new EntityReference("contact", aContact.Id);

                        aEntity["from"] = new Entity[] { Fromparty };
                        aEntity["to"] = new Entity[] { Fromparty };

                        // 新增Line訊息
                        this.CreateEntity(aEntity);

                        return;
                    }
                    else
                    {
                        return;
                    }
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public void CreatePushLineMessage(IList<string> To, string Subject, string Message)
        {
            try
            {
                if (EXCUTION_TRACE_LINE == true)
                {
                    foreach (String UserId in To)
                    {
                        Entity aContact = RetrieveContactCollectionByLineId(UserId);

                        if (aContact != null)
                        {
                            Entity aEntity = new Entity("letter");
                            SetEntityStringAttribute(ref aEntity, "subject", Subject);
                            SetEntityStringAttribute(ref aEntity, "description", Message);
                            SetEntityStringAttribute(ref aEntity, "new_displayed_lineid", UserId);
                            SetEntityLookUpAttribute(ref aEntity, "regardingobjectid", "contact", aContact.Id);

                            SetEntityDateTimeAttribute(ref aEntity, "scheduledend", DateTime.Now);

                            //方向=>撥出
                            SetEntityBoolAttribute(ref aEntity, "directioncode", true);

                            //計數=>1
                            SetEntityIntAttribute(ref aEntity, "new_count", 1);

                            //設定訊息種類為文字 
                            SetOptionSetAttribute(ref aEntity, "new_message_category", 100000000);

                            Entity Fromparty = new Entity("activityparty");

                            Fromparty["partyid"] = new EntityReference("contact", aContact.Id);

                            aEntity["from"] = new Entity[] { Fromparty };
                            aEntity["to"] = new Entity[] { Fromparty };

                            // 新增Line訊息
                            this.CreateEntity(aEntity);

                            return;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion
        #region 將連絡人加入或移除至名單

        //private readonly object m_MembersToMarketingListLocker = new object();
        public void AddMembersToMarketingList(Guid thisListGuid, List<Guid> memberGuidList, ref IOrganizationService gCRMService)
        {
            try
            {
                //lock (m_MembersToMarketingListLocker)
                //{
                AddListMembersListRequest orgServiceRequest = new AddListMembersListRequest();
                orgServiceRequest.ListId = thisListGuid;
                orgServiceRequest.MemberIds = memberGuidList.ToArray();
                gCRMService.Execute(orgServiceRequest);
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void RemoveMembersToMarketingList(Guid aListGuid, Guid MemberGuid, ref IOrganizationService gCRMService)
        {
            try
            {
                //lock (m_MembersToMarketingListLocker)
                //{
                RemoveMemberListRequest orgServiceRequest = new RemoveMemberListRequest();
                orgServiceRequest.ListId = aListGuid;
                orgServiceRequest.EntityId = MemberGuid;
                gCRMService.Execute(orgServiceRequest);
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void AddMembersToMarketingList(Guid thisListGuid, List<Guid> memberGuidList)
        {
            try
            {
                //lock (m_MembersToMarketingListLocker)
                //{
                AddListMembersListRequest orgServiceRequest = new AddListMembersListRequest();
                orgServiceRequest.ListId = thisListGuid;
                orgServiceRequest.MemberIds = memberGuidList.ToArray();
                if (CRM_TYPE == "DYNAMICS365")
                {
                    this.m_OrganizationService.Execute(orgServiceRequest);
                }
                else
                {
                    this.m_Crm2011OrganizationService.Execute(orgServiceRequest);
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void RemoveMembersToMarketingList(Guid aListGuid, Guid MemberGuid)
        {
            try
            {
                //lock (m_MembersToMarketingListLocker)
                //{
                RemoveMemberListRequest orgServiceRequest = new RemoveMemberListRequest();
                orgServiceRequest.ListId = aListGuid;
                orgServiceRequest.EntityId = MemberGuid;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    this.m_OrganizationService.Execute(orgServiceRequest);
                }
                else
                {
                    this.m_Crm2011OrganizationService.Execute(orgServiceRequest);
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public ArrayList GetAllMemberDataFromList(Guid ListEntityId)
        {
            #region // 處理每個小組名單
            //搜尋名單的組員
            //EntityCollection Contacts = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntityId.ToString(), "new_cell_list_contact", "contact");

            Entity ListEntity = this.RetrieveEntity("list", ListEntityId);

            bool ListType = this.GetEntityBoolAttribute(ListEntity, "type");
            EntityCollection MemberCollection;
            if (ListType == false)
            {
                // 靜態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.RetrieveMemberListCollectionByListIdDynamics365(ref this.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            else
            {
                // 動態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.RetrieveDynamicMemberListDynamics365(ref this.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.RetrieveDynamicMemberListCrm2011(ref this.m_Crm2011OrganizationService, ListEntityId);
                }
            }

            int PresentRecordIdCounter = 0;
            ArrayList MemberEntityIdList = new ArrayList();
            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                // 每個組員
                Entity ContactEntity;

                if (ListType == false)
                {
                    // 靜態名單
                    MemberEntityIdList.Add(((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                    //ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                }
                else
                {
                    // 動態名單
                    MemberEntityIdList.Add((Guid)MemberEntity.Attributes["contactid"]);
                    //ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
                }

            }
            #endregion

            return MemberEntityIdList;
        }

        #endregion
        #region 活動相關的收件人或寄件人
        public void GetActivityPartyList(Entity ActivityEntity, String FromOrTo, ArrayList aFromOrToList, ArrayList aFromOrToTypeList)
        {
            try
            {
                EntityCollection aFromEntityCollection = ActivityEntity.GetAttributeValue<EntityCollection>(FromOrTo);

                for (int i = 0; i < aFromEntityCollection.Entities.Count; i++)
                {
                    #region 取得活動寄送者
                    EntityReference aFromOrToEntityReference = (EntityReference)aFromEntityCollection.Entities[i]["partyid"];

                    Guid aFromOrToEntityId = aFromOrToEntityReference.Id;

                    String EntityName = aFromOrToEntityReference.LogicalName;

                    aFromOrToTypeList.Add(EntityName);

                    Entity aRetrievedFromOrToEntity = this.RetrieveEntity(EntityName, aFromOrToEntityId);

                    aFromOrToList.Add(aRetrievedFromOrToEntity);


                    #endregion
                }

                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void GetActivityPartyIdList(Entity ActivityEntity, String FromOrTo, ArrayList aFromOrToIdList, ArrayList aFromOrToTypeList)
        {
            try
            {
                EntityCollection aFromEntityCollection = ActivityEntity.GetAttributeValue<EntityCollection>(FromOrTo);

                if (aFromEntityCollection != null)
                {
                    for (int i = 0; i < aFromEntityCollection.Entities.Count; i++)
                    {
                        #region 取得活動寄送者
                        EntityReference aFromOrToEntityReference = (EntityReference)aFromEntityCollection.Entities[i]["partyid"];

                        aFromOrToTypeList.Add(aFromOrToEntityReference.LogicalName);

                        aFromOrToIdList.Add(aFromOrToEntityReference.Id);


                        #endregion
                    }
                }

                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        public void SetActivityStatusToCompleted(String ActivityName, Guid aActivityId)
        {
            try
            {
                // Create the request object.
                SetStateRequest aSetStateActivityRequest = new SetStateRequest();

                // Set the properties of the request object.
                aSetStateActivityRequest.State = new OptionSetValue(1);
                //aSetStatePhoneActivityRequest.Status = new OptionSetValue(2);
                aSetStateActivityRequest.Status = new OptionSetValue(4);

                // EntityId is the GUID of the account whose state is being changed.
                EntityReference EntityMoniker = new EntityReference(ActivityName, aActivityId);
                aSetStateActivityRequest.EntityMoniker = EntityMoniker;

                // Execute the request.
                SetStateResponse StateSetResponse;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    StateSetResponse = (SetStateResponse)this.m_OrganizationService.Execute(aSetStateActivityRequest);
                }
                else
                {
                    StateSetResponse = (SetStateResponse)this.m_Crm2011OrganizationService.Execute(aSetStateActivityRequest);
                }

                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetAppointmentStatusToScheduled(Guid aActivityId)
        {
            try
            {
                // Create the request object.
                SetStateRequest aSetStateActivityRequest = new SetStateRequest();

                // Set the properties of the request object.
                aSetStateActivityRequest.State = new OptionSetValue(3);
                //aSetStatePhoneActivityRequest.Status = new OptionSetValue(2);
                aSetStateActivityRequest.Status = new OptionSetValue(5);

                // EntityId is the GUID of the account whose state is being changed.
                EntityReference EntityMoniker = new EntityReference("appointment", aActivityId);
                aSetStateActivityRequest.EntityMoniker = EntityMoniker;

                // Execute the request.

                SetStateResponse StateSetResponse;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    StateSetResponse = (SetStateResponse)this.m_OrganizationService.Execute(aSetStateActivityRequest);
                }
                else
                {
                    StateSetResponse = (SetStateResponse)this.m_Crm2011OrganizationService.Execute(aSetStateActivityRequest);
                }

                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        #endregion
        #region 處理附加檔
        public EntityCollection DownloadAnAttachment(ref IOrganizationService aCrmService, Guid AnEntityId)
        {
            try
            {
                #region How to download attachment from activitymimeattachment record
                #region 建立 Condidtion

                ConditionExpression ContactConditionPrincipal = new ConditionExpression();

                // Set the ConditionExpressions properties so that the condition is true when the 
                // ownerid of the account equals the principalId.
                ContactConditionPrincipal.AttributeName = "objectid";
                ContactConditionPrincipal.Operator = ConditionOperator.Equal;

                ContactConditionPrincipal.Values.Add(AnEntityId.ToString());


                #endregion
                #region 建立 Filter
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(ContactConditionPrincipal);
                #endregion
                #region 建立 QueryExpression
                QueryExpression query = new QueryExpression();
                //query.EntityName = EntityName.activitymimeattachment.ToString();
                //queryPrincipal.EntityName = @"new_blood_report";
                query.EntityName = "annotation";
                query.ColumnSet.AllColumns = true;

                query.Criteria = filter;
                #endregion
                #region 建立 Retrieve Multiple Request

                return aCrmService.RetrieveMultiple(query);
                //BusinessEntityCollection AnnotationsCollection = aCrmService.RetrieveMultiple(query);
                //return AnnotationsCollection;
                #endregion
                #region 執行搜尋後驗證而已

                //foreach (annotation Annotation in attachments.BusinessEntities)
                //{
                //Guid attachid = Annotation.annotationid.Value;
                // Retrieve the activitymimeattachment record.
                //annotation AnnotationAttchment = (annotation)aCrmService.Retrieve(EntityName.annotation.ToString(), attachid, new AllColumns());
                // Download the attachment in the current execution folder.
                //using (FileStream fileStream = new FileStream(@"D:\客戶資料\" + AnnotationAttchment.filename, FileMode.OpenOrCreate))
                //{
                //byte[] fileContent = new UTF8Encoding(true).GetBytes(AnnotationAttchment.documentbody);
                //byte[] fileContent = Convert.FromBase64String(AnnotationAttchment.documentbody);

                //fileStream.Write(fileContent, 0, fileContent.Length);
                //}
                //}
                #endregion
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        public void UploadAnAttachment(ref IOrganizationService aCrmService, String EntityName, String Subject, String NoteText, String FileName, String MimeType, byte[] DocumentBody, Guid ToBeAttachedEntityId)
        {
            try
            {
                #region 附加一個檔案到實體裡

                #region Mime Type
                //jpg->image / jpeg
                //doc->application / octet - stream
                //docx->application / octet - stream
                //pdf->application / pdf
                //msg->application / octet - stream
                //htm->text / html
                //png->image / png
                //gif->image / png
                //xls->application / octet - stream


                //case ".3dm": mimeType = "x-world/x-3dmf"; break;
                //case ".3dmf": mimeType = "x-world/x-3dmf"; break;
                //case ".a": mimeType = "application/octet-stream"; break;
                //case ".aab": mimeType = "application/x-authorware-bin"; break;
                //case ".aam": mimeType = "application/x-authorware-map"; break;
                //case ".aas": mimeType = "application/x-authorware-seg"; break;
                //case ".abc": mimeType = "text/vnd.abc"; break;
                //case ".acgi": mimeType = "text/html"; break;
                //case ".afl": mimeType = "video/animaflex"; break;
                //case ".ai": mimeType = "application/postscript"; break;
                //case ".aif": mimeType = "audio/aiff"; break;
                //case ".aifc": mimeType = "audio/aiff"; break;
                //case ".aiff": mimeType = "audio/aiff"; break;
                //case ".aim": mimeType = "application/x-aim"; break;
                //case ".aip": mimeType = "text/x-audiosoft-intra"; break;
                //case ".ani": mimeType = "application/x-navi-animation"; break;
                //case ".aos": mimeType = "application/x-nokia-9000-communicator-add-on-software"; break;
                //case ".aps": mimeType = "application/mime"; break;
                //case ".arc": mimeType = "application/octet-stream"; break;
                //case ".arj": mimeType = "application/arj"; break;
                //case ".art": mimeType = "image/x-jg"; break;
                //case ".asf": mimeType = "video/x-ms-asf"; break;
                //case ".asm": mimeType = "text/x-asm"; break;
                //case ".asp": mimeType = "text/asp"; break;
                //case ".asx": mimeType = "video/x-ms-asf"; break;
                //case ".au": mimeType = "audio/basic"; break;
                //case ".avi": mimeType = "video/avi"; break;
                //case ".avs": mimeType = "video/avs-video"; break;
                //case ".bcpio": mimeType = "application/x-bcpio"; break;
                //case ".bin": mimeType = "application/octet-stream"; break;
                //case ".bm": mimeType = "image/bmp"; break;
                //case ".bmp": mimeType = "image/bmp"; break;
                //case ".boo": mimeType = "application/book"; break;
                //case ".book": mimeType = "application/book"; break;
                //case ".boz": mimeType = "application/x-bzip2"; break;
                //case ".bsh": mimeType = "application/x-bsh"; break;
                //case ".bz": mimeType = "application/x-bzip"; break;
                //case ".bz2": mimeType = "application/x-bzip2"; break;
                //case ".c": mimeType = "text/plain"; break;
                //case ".c++": mimeType = "text/plain"; break;
                //case ".cat": mimeType = "application/vnd.ms-pki.seccat"; break;
                //case ".cc": mimeType = "text/plain"; break;
                //case ".ccad": mimeType = "application/clariscad"; break;
                //case ".cco": mimeType = "application/x-cocoa"; break;
                //case ".cdf": mimeType = "application/cdf"; break;
                //case ".cer": mimeType = "application/pkix-cert"; break;
                //case ".cha": mimeType = "application/x-chat"; break;
                //case ".chat": mimeType = "application/x-chat"; break;
                //case ".class": mimeType = "application/java"; break;
                //case ".com": mimeType = "application/octet-stream"; break;
                //case ".conf": mimeType = "text/plain"; break;
                //case ".cpio": mimeType = "application/x-cpio"; break;
                //case ".cpp": mimeType = "text/x-c"; break;
                //case ".cpt": mimeType = "application/x-cpt"; break;
                //case ".crl": mimeType = "application/pkcs-crl"; break;
                //case ".crt": mimeType = "application/pkix-cert"; break;
                //case ".csh": mimeType = "application/x-csh"; break;
                //case ".css": mimeType = "text/css"; break;
                //case ".cxx": mimeType = "text/plain"; break;
                //case ".dcr": mimeType = "application/x-director"; break;
                //case ".deepv": mimeType = "application/x-deepv"; break;
                //case ".def": mimeType = "text/plain"; break;
                //case ".der": mimeType = "application/x-x509-ca-cert"; break;
                //case ".dif": mimeType = "video/x-dv"; break;
                //case ".dir": mimeType = "application/x-director"; break;
                //case ".dl": mimeType = "video/dl"; break;
                //case ".doc": mimeType = "application/msword"; break;
                //case ".dot": mimeType = "application/msword"; break;
                //case ".dp": mimeType = "application/commonground"; break;
                //case ".drw": mimeType = "application/drafting"; break;
                //case ".dump": mimeType = "application/octet-stream"; break;
                //case ".dv": mimeType = "video/x-dv"; break;
                //case ".dvi": mimeType = "application/x-dvi"; break;
                //case ".dwf": mimeType = "model/vnd.dwf"; break;
                //case ".dwg": mimeType = "image/vnd.dwg"; break;
                //case ".dxf": mimeType = "image/vnd.dwg"; break;
                //case ".dxr": mimeType = "application/x-director"; break;
                //case ".el": mimeType = "text/x-script.elisp"; break;
                //case ".elc": mimeType = "application/x-elc"; break;
                //case ".env": mimeType = "application/x-envoy"; break;
                //case ".eps": mimeType = "application/postscript"; break;
                //case ".es": mimeType = "application/x-esrehber"; break;
                //case ".etx": mimeType = "text/x-setext"; break;
                //case ".evy": mimeType = "application/envoy"; break;
                //case ".exe": mimeType = "application/octet-stream"; break;
                //case ".f": mimeType = "text/plain"; break;
                //case ".f77": mimeType = "text/x-fortran"; break;
                //case ".f90": mimeType = "text/plain"; break;
                //case ".fdf": mimeType = "application/vnd.fdf"; break;
                //case ".fif": mimeType = "image/fif"; break;
                //case ".fli": mimeType = "video/fli"; break;
                //case ".flo": mimeType = "image/florian"; break;
                //case ".flx": mimeType = "text/vnd.fmi.flexstor"; break;
                //case ".fmf": mimeType = "video/x-atomic3d-feature"; break;
                //case ".for": mimeType = "text/x-fortran"; break;
                //case ".fpx": mimeType = "image/vnd.fpx"; break;
                //case ".frl": mimeType = "application/freeloader"; break;
                //case ".funk": mimeType = "audio/make"; break;
                //case ".g": mimeType = "text/plain"; break;
                //case ".g3": mimeType = "image/g3fax"; break;
                //case ".gif": mimeType = "image/gif"; break;
                //case ".gl": mimeType = "video/gl"; break;
                //case ".gsd": mimeType = "audio/x-gsm"; break;
                //case ".gsm": mimeType = "audio/x-gsm"; break;
                //case ".gsp": mimeType = "application/x-gsp"; break;
                //case ".gss": mimeType = "application/x-gss"; break;
                //case ".gtar": mimeType = "application/x-gtar"; break;
                //case ".gz": mimeType = "application/x-gzip"; break;
                //case ".gzip": mimeType = "application/x-gzip"; break;
                //case ".h": mimeType = "text/plain"; break;
                //case ".hdf": mimeType = "application/x-hdf"; break;
                //case ".help": mimeType = "application/x-helpfile"; break;
                //case ".hgl": mimeType = "application/vnd.hp-hpgl"; break;
                //case ".hh": mimeType = "text/plain"; break;
                //case ".hlb": mimeType = "text/x-script"; break;
                //case ".hlp": mimeType = "application/hlp"; break;
                //case ".hpg": mimeType = "application/vnd.hp-hpgl"; break;
                //case ".hpgl": mimeType = "application/vnd.hp-hpgl"; break;
                //case ".hqx": mimeType = "application/binhex"; break;
                //case ".hta": mimeType = "application/hta"; break;
                //case ".htc": mimeType = "text/x-component"; break;
                //case ".htm": mimeType = "text/html"; break;
                //case ".html": mimeType = "text/html"; break;
                //case ".htmls": mimeType = "text/html"; break;
                //case ".htt": mimeType = "text/webviewhtml"; break;
                //case ".htx": mimeType = "text/html"; break;
                //case ".ice": mimeType = "x-conference/x-cooltalk"; break;
                //case ".ico": mimeType = "image/x-icon"; break;
                //case ".idc": mimeType = "text/plain"; break;
                //case ".ief": mimeType = "image/ief"; break;
                //case ".iefs": mimeType = "image/ief"; break;
                //case ".iges": mimeType = "application/iges"; break;
                //case ".igs": mimeType = "application/iges"; break;
                //case ".ima": mimeType = "application/x-ima"; break;
                //case ".imap": mimeType = "application/x-httpd-imap"; break;
                //case ".inf": mimeType = "application/inf"; break;
                //case ".ins": mimeType = "application/x-internett-signup"; break;
                //case ".ip": mimeType = "application/x-ip2"; break;
                //case ".isu": mimeType = "video/x-isvideo"; break;
                //case ".it": mimeType = "audio/it"; break;
                //case ".iv": mimeType = "application/x-inventor"; break;
                //case ".ivr": mimeType = "i-world/i-vrml"; break;
                //case ".ivy": mimeType = "application/x-livescreen"; break;
                //case ".jam": mimeType = "audio/x-jam"; break;
                //case ".jav": mimeType = "text/plain"; break;
                //case ".java": mimeType = "text/plain"; break;
                //case ".jcm": mimeType = "application/x-java-commerce"; break;
                //case ".jfif": mimeType = "image/jpeg"; break;
                //case ".jfif-tbnl": mimeType = "image/jpeg"; break;
                //case ".jpe": mimeType = "image/jpeg"; break;
                //case ".jpeg": mimeType = "image/jpeg"; break;
                //case ".jpg": mimeType = "image/jpeg"; break;
                //case ".jps": mimeType = "image/x-jps"; break;
                //case ".js": mimeType = "application/x-javascript"; break;
                //case ".jut": mimeType = "image/jutvision"; break;
                //case ".kar": mimeType = "audio/midi"; break;
                //case ".ksh": mimeType = "application/x-ksh"; break;
                //case ".la": mimeType = "audio/nspaudio"; break;
                //case ".lam": mimeType = "audio/x-liveaudio"; break;
                //case ".latex": mimeType = "application/x-latex"; break;
                //case ".lha": mimeType = "application/octet-stream"; break;
                //case ".lhx": mimeType = "application/octet-stream"; break;
                //case ".list": mimeType = "text/plain"; break;
                //case ".lma": mimeType = "audio/nspaudio"; break;
                //case ".log": mimeType = "text/plain"; break;
                //case ".lsp": mimeType = "application/x-lisp"; break;
                //case ".lst": mimeType = "text/plain"; break;
                //case ".lsx": mimeType = "text/x-la-asf"; break;
                //case ".ltx": mimeType = "application/x-latex"; break;
                //case ".lzh": mimeType = "application/octet-stream"; break;
                //case ".lzx": mimeType = "application/octet-stream"; break;
                //case ".m": mimeType = "text/plain"; break;
                //case ".m1v": mimeType = "video/mpeg"; break;
                //case ".m2a": mimeType = "audio/mpeg"; break;
                //case ".m2v": mimeType = "video/mpeg"; break;
                //case ".m3u": mimeType = "audio/x-mpequrl"; break;
                //case ".man": mimeType = "application/x-troff-man"; break;
                //case ".map": mimeType = "application/x-navimap"; break;
                //case ".mar": mimeType = "text/plain"; break;
                //case ".mbd": mimeType = "application/mbedlet"; break;
                //case ".mc$": mimeType = "application/x-magic-cap-package-1.0"; break;
                //case ".mcd": mimeType = "application/mcad"; break;
                //case ".mcf": mimeType = "text/mcf"; break;
                //case ".mcp": mimeType = "application/netmc"; break;
                //case ".me": mimeType = "application/x-troff-me"; break;
                //case ".mht": mimeType = "message/rfc822"; break;
                //case ".mhtml": mimeType = "message/rfc822"; break;
                //case ".mid": mimeType = "audio/midi"; break;
                //case ".midi": mimeType = "audio/midi"; break;
                //case ".mif": mimeType = "application/x-mif"; break;
                //case ".mime": mimeType = "message/rfc822"; break;
                //case ".mjf": mimeType = "audio/x-vnd.audioexplosion.mjuicemediafile"; break;
                //case ".mjpg": mimeType = "video/x-motion-jpeg"; break;
                //case ".mm": mimeType = "application/base64"; break;
                //case ".mme": mimeType = "application/base64"; break;
                //case ".mod": mimeType = "audio/mod"; break;
                //case ".moov": mimeType = "video/quicktime"; break;
                //case ".mov": mimeType = "video/quicktime"; break;
                //case ".movie": mimeType = "video/x-sgi-movie"; break;
                //case ".mp2": mimeType = "audio/mpeg"; break;
                //case ".mp3": mimeType = "audio/mpeg"; break;
                //case ".mpa": mimeType = "audio/mpeg"; break;
                //case ".mpc": mimeType = "application/x-project"; break;
                //case ".mpe": mimeType = "video/mpeg"; break;
                //case ".mpeg": mimeType = "video/mpeg"; break;
                //case ".mpg": mimeType = "video/mpeg"; break;
                //case ".mpga": mimeType = "audio/mpeg"; break;
                //case ".mpp": mimeType = "application/vnd.ms-project"; break;
                //case ".mpt": mimeType = "application/vnd.ms-project"; break;
                //case ".mpv": mimeType = "application/vnd.ms-project"; break;
                //case ".mpx": mimeType = "application/vnd.ms-project"; break;
                //case ".mrc": mimeType = "application/marc"; break;
                //case ".ms": mimeType = "application/x-troff-ms"; break;
                //case ".mv": mimeType = "video/x-sgi-movie"; break;
                //case ".my": mimeType = "audio/make"; break;
                //case ".mzz": mimeType = "application/x-vnd.audioexplosion.mzz"; break;
                //case ".nap": mimeType = "image/naplps"; break;
                //case ".naplps": mimeType = "image/naplps"; break;
                //case ".nc": mimeType = "application/x-netcdf"; break;
                //case ".ncm": mimeType = "application/vnd.nokia.configuration-message"; break;
                //case ".nif": mimeType = "image/x-niff"; break;
                //case ".niff": mimeType = "image/x-niff"; break;
                //case ".nix": mimeType = "application/x-mix-transfer"; break;
                //case ".nsc": mimeType = "application/x-conference"; break;
                //case ".nvd": mimeType = "application/x-navidoc"; break;
                //case ".o": mimeType = "application/octet-stream"; break;
                //case ".oda": mimeType = "application/oda"; break;
                //case ".omc": mimeType = "application/x-omc"; break;
                //case ".omcd": mimeType = "application/x-omcdatamaker"; break;
                //case ".omcr": mimeType = "application/x-omcregerator"; break;
                //case ".p": mimeType = "text/x-pascal"; break;
                //case ".p10": mimeType = "application/pkcs10"; break;
                //case ".p12": mimeType = "application/pkcs-12"; break;
                //case ".p7a": mimeType = "application/x-pkcs7-signature"; break;
                //case ".p7c": mimeType = "application/pkcs7-mime"; break;
                //case ".p7m": mimeType = "application/pkcs7-mime"; break;
                //case ".p7r": mimeType = "application/x-pkcs7-certreqresp"; break;
                //case ".p7s": mimeType = "application/pkcs7-signature"; break;
                //case ".part": mimeType = "application/pro_eng"; break;
                //case ".pas": mimeType = "text/pascal"; break;
                //case ".pbm": mimeType = "image/x-portable-bitmap"; break;
                //case ".pcl": mimeType = "application/vnd.hp-pcl"; break;
                //case ".pct": mimeType = "image/x-pict"; break;
                //case ".pcx": mimeType = "image/x-pcx"; break;
                //case ".pdb": mimeType = "chemical/x-pdb"; break;
                //case ".pdf": mimeType = "application/pdf"; break;
                //case ".pfunk": mimeType = "audio/make"; break;
                //case ".pgm": mimeType = "image/x-portable-greymap"; break;
                //case ".pic": mimeType = "image/pict"; break;
                //case ".pict": mimeType = "image/pict"; break;
                //case ".pkg": mimeType = "application/x-newton-compatible-pkg"; break;
                //case ".pko": mimeType = "application/vnd.ms-pki.pko"; break;
                //case ".pl": mimeType = "text/plain"; break;
                //case ".plx": mimeType = "application/x-pixclscript"; break;
                //case ".pm": mimeType = "image/x-xpixmap"; break;
                //case ".pm4": mimeType = "application/x-pagemaker"; break;
                //case ".pm5": mimeType = "application/x-pagemaker"; break;
                //case ".png": mimeType = "image/png"; break;
                //case ".pnm": mimeType = "application/x-portable-anymap"; break;
                //case ".pot": mimeType = "application/vnd.ms-powerpoint"; break;
                //case ".pov": mimeType = "model/x-pov"; break;
                //case ".ppa": mimeType = "application/vnd.ms-powerpoint"; break;
                //case ".ppm": mimeType = "image/x-portable-pixmap"; break;
                //case ".pps": mimeType = "application/vnd.ms-powerpoint"; break;
                //case ".ppt": mimeType = "application/vnd.ms-powerpoint"; break;
                //case ".ppz": mimeType = "application/vnd.ms-powerpoint"; break;
                //case ".pre": mimeType = "application/x-freelance"; break;
                //case ".prt": mimeType = "application/pro_eng"; break;
                //case ".ps": mimeType = "application/postscript"; break;
                //case ".psd": mimeType = "application/octet-stream"; break;
                //case ".pvu": mimeType = "paleovu/x-pv"; break;
                //case ".pwz": mimeType = "application/vnd.ms-powerpoint"; break;
                //case ".py": mimeType = "text/x-script.phyton"; break;
                //case ".pyc": mimeType = "applicaiton/x-bytecode.python"; break;
                //case ".qcp": mimeType = "audio/vnd.qcelp"; break;
                //case ".qd3": mimeType = "x-world/x-3dmf"; break;
                //case ".qd3d": mimeType = "x-world/x-3dmf"; break;
                //case ".qif": mimeType = "image/x-quicktime"; break;
                //case ".qt": mimeType = "video/quicktime"; break;
                //case ".qtc": mimeType = "video/x-qtc"; break;
                //case ".qti": mimeType = "image/x-quicktime"; break;
                //case ".qtif": mimeType = "image/x-quicktime"; break;
                //case ".ra": mimeType = "audio/x-pn-realaudio"; break;
                //case ".ram": mimeType = "audio/x-pn-realaudio"; break;
                //case ".ras": mimeType = "application/x-cmu-raster"; break;
                //case ".rast": mimeType = "image/cmu-raster"; break;
                //case ".rexx": mimeType = "text/x-script.rexx"; break;
                //case ".rf": mimeType = "image/vnd.rn-realflash"; break;
                //case ".rgb": mimeType = "image/x-rgb"; break;
                //case ".rm": mimeType = "application/vnd.rn-realmedia"; break;
                //case ".rmi": mimeType = "audio/mid"; break;
                //case ".rmm": mimeType = "audio/x-pn-realaudio"; break;
                //case ".rmp": mimeType = "audio/x-pn-realaudio"; break;
                //case ".rng": mimeType = "application/ringing-tones"; break;
                //case ".rnx": mimeType = "application/vnd.rn-realplayer"; break;
                //case ".roff": mimeType = "application/x-troff"; break;
                //case ".rp": mimeType = "image/vnd.rn-realpix"; break;
                //case ".rpm": mimeType = "audio/x-pn-realaudio-plugin"; break;
                //case ".rt": mimeType = "text/richtext"; break;
                //case ".rtf": mimeType = "text/richtext"; break;
                //case ".rtx": mimeType = "text/richtext"; break;
                //case ".rv": mimeType = "video/vnd.rn-realvideo"; break;
                //case ".s": mimeType = "text/x-asm"; break;
                //case ".s3m": mimeType = "audio/s3m"; break;
                //case ".saveme": mimeType = "application/octet-stream"; break;
                //case ".sbk": mimeType = "application/x-tbook"; break;
                //case ".scm": mimeType = "application/x-lotusscreencam"; break;
                //case ".sdml": mimeType = "text/plain"; break;
                //case ".sdp": mimeType = "application/sdp"; break;
                //case ".sdr": mimeType = "application/sounder"; break;
                //case ".sea": mimeType = "application/sea"; break;
                //case ".set": mimeType = "application/set"; break;
                //case ".sgm": mimeType = "text/sgml"; break;
                //case ".sgml": mimeType = "text/sgml"; break;
                //case ".sh": mimeType = "application/x-sh"; break;
                //case ".shar": mimeType = "application/x-shar"; break;
                //case ".shtml": mimeType = "text/html"; break;
                //case ".sid": mimeType = "audio/x-psid"; break;
                //case ".sit": mimeType = "application/x-sit"; break;
                //case ".skd": mimeType = "application/x-koan"; break;
                //case ".skm": mimeType = "application/x-koan"; break;
                //case ".skp": mimeType = "application/x-koan"; break;
                //case ".skt": mimeType = "application/x-koan"; break;
                //case ".sl": mimeType = "application/x-seelogo"; break;
                //case ".smi": mimeType = "application/smil"; break;
                //case ".smil": mimeType = "application/smil"; break;
                //case ".snd": mimeType = "audio/basic"; break;
                //case ".sol": mimeType = "application/solids"; break;
                //case ".spc": mimeType = "text/x-speech"; break;
                //case ".spl": mimeType = "application/futuresplash"; break;
                //case ".spr": mimeType = "application/x-sprite"; break;
                //case ".sprite": mimeType = "application/x-sprite"; break;
                //case ".src": mimeType = "application/x-wais-source"; break;
                //case ".ssi": mimeType = "text/x-server-parsed-html"; break;
                //case ".ssm": mimeType = "application/streamingmedia"; break;
                //case ".sst": mimeType = "application/vnd.ms-pki.certstore"; break;
                //case ".step": mimeType = "application/step"; break;
                //case ".stl": mimeType = "application/sla"; break;
                //case ".stp": mimeType = "application/step"; break;
                //case ".sv4cpio": mimeType = "application/x-sv4cpio"; break;
                //case ".sv4crc": mimeType = "application/x-sv4crc"; break;
                //case ".svf": mimeType = "image/vnd.dwg"; break;
                //case ".svr": mimeType = "application/x-world"; break;
                //case ".swf": mimeType = "application/x-shockwave-flash"; break;
                //case ".t": mimeType = "application/x-troff"; break;
                //case ".talk": mimeType = "text/x-speech"; break;
                //case ".tar": mimeType = "application/x-tar"; break;
                //case ".tbk": mimeType = "application/toolbook"; break;
                //case ".tcl": mimeType = "application/x-tcl"; break;
                //case ".tcsh": mimeType = "text/x-script.tcsh"; break;
                //case ".tex": mimeType = "application/x-tex"; break;
                //case ".texi": mimeType = "application/x-texinfo"; break;
                //case ".texinfo": mimeType = "application/x-texinfo"; break;
                //case ".text": mimeType = "text/plain"; break;
                //case ".tgz": mimeType = "application/x-compressed"; break;
                //case ".tif": mimeType = "image/tiff"; break;
                //case ".tiff": mimeType = "image/tiff"; break;
                //case ".tr": mimeType = "application/x-troff"; break;
                //case ".tsi": mimeType = "audio/tsp-audio"; break;
                //case ".tsp": mimeType = "application/dsptype"; break;
                //case ".tsv": mimeType = "text/tab-separated-values"; break;
                //case ".turbot": mimeType = "image/florian"; break;
                //case ".txt": mimeType = "text/plain"; break;
                //case ".uil": mimeType = "text/x-uil"; break;
                //case ".uni": mimeType = "text/uri-list"; break;
                //case ".unis": mimeType = "text/uri-list"; break;
                //case ".unv": mimeType = "application/i-deas"; break;
                //case ".uri": mimeType = "text/uri-list"; break;
                //case ".uris": mimeType = "text/uri-list"; break;
                //case ".ustar": mimeType = "application/x-ustar"; break;
                //case ".uu": mimeType = "application/octet-stream"; break;
                //case ".uue": mimeType = "text/x-uuencode"; break;
                //case ".vcd": mimeType = "application/x-cdlink"; break;
                //case ".vcs": mimeType = "text/x-vcalendar"; break;
                //case ".vda": mimeType = "application/vda"; break;
                //case ".vdo": mimeType = "video/vdo"; break;
                //case ".vew": mimeType = "application/groupwise"; break;
                //case ".viv": mimeType = "video/vivo"; break;
                //case ".vivo": mimeType = "video/vivo"; break;
                //case ".vmd": mimeType = "application/vocaltec-media-desc"; break;
                //case ".vmf": mimeType = "application/vocaltec-media-file"; break;
                //case ".voc": mimeType = "audio/voc"; break;
                //case ".vos": mimeType = "video/vosaic"; break;
                //case ".vox": mimeType = "audio/voxware"; break;
                //case ".vqe": mimeType = "audio/x-twinvq-plugin"; break;
                //case ".vqf": mimeType = "audio/x-twinvq"; break;
                //case ".vql": mimeType = "audio/x-twinvq-plugin"; break;
                //case ".vrml": mimeType = "application/x-vrml"; break;
                //case ".vrt": mimeType = "x-world/x-vrt"; break;
                //case ".vsd": mimeType = "application/x-visio"; break;
                //case ".vst": mimeType = "application/x-visio"; break;
                //case ".vsw": mimeType = "application/x-visio"; break;
                //case ".w60": mimeType = "application/wordperfect6.0"; break;
                //case ".w61": mimeType = "application/wordperfect6.1"; break;
                //case ".w6w": mimeType = "application/msword"; break;
                //case ".wav": mimeType = "audio/wav"; break;
                //case ".wb1": mimeType = "application/x-qpro"; break;
                //case ".wbmp": mimeType = "image/vnd.wap.wbmp"; break;
                //case ".web": mimeType = "application/vnd.xara"; break;
                //case ".wiz": mimeType = "application/msword"; break;
                //case ".wk1": mimeType = "application/x-123"; break;
                //case ".wmf": mimeType = "windows/metafile"; break;
                //case ".wml": mimeType = "text/vnd.wap.wml"; break;
                //case ".wmlc": mimeType = "application/vnd.wap.wmlc"; break;
                //case ".wmls": mimeType = "text/vnd.wap.wmlscript"; break;
                //case ".wmlsc": mimeType = "application/vnd.wap.wmlscriptc"; break;
                //case ".word": mimeType = "application/msword"; break;
                //case ".wp": mimeType = "application/wordperfect"; break;
                //case ".wp5": mimeType = "application/wordperfect"; break;
                //case ".wp6": mimeType = "application/wordperfect"; break;
                //case ".wpd": mimeType = "application/wordperfect"; break;
                //case ".wq1": mimeType = "application/x-lotus"; break;
                //case ".wri": mimeType = "application/mswrite"; break;
                //case ".wrl": mimeType = "application/x-world"; break;
                //case ".wrz": mimeType = "x-world/x-vrml"; break;
                //case ".wsc": mimeType = "text/scriplet"; break;
                //case ".wsrc": mimeType = "application/x-wais-source"; break;
                //case ".wtk": mimeType = "application/x-wintalk"; break;
                //case ".xbm": mimeType = "image/x-xbitmap"; break;
                //case ".xdr": mimeType = "video/x-amt-demorun"; break;
                //case ".xgz": mimeType = "xgl/drawing"; break;
                //case ".xif": mimeType = "image/vnd.xiff"; break;
                //case ".xl": mimeType = "application/excel"; break;
                //case ".xla": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlb": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlc": mimeType = "application/vnd.ms-excel"; break;
                //case ".xld": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlk": mimeType = "application/vnd.ms-excel"; break;
                //case ".xll": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlm": mimeType = "application/vnd.ms-excel"; break;
                //case ".xls": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlt": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlv": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlw": mimeType = "application/vnd.ms-excel"; break;
                //case ".xm": mimeType = "audio/xm"; break;
                //case ".xml": mimeType = "application/xml"; break;
                //case ".xmz": mimeType = "xgl/movie"; break;
                //case ".xpix": mimeType = "application/x-vnd.ls-xpix"; break;
                //case ".xpm": mimeType = "image/xpm"; break;
                //case ".x-png": mimeType = "image/png"; break;
                //case ".xsr": mimeType = "video/x-amt-showrun"; break;
                //case ".xwd": mimeType = "image/x-xwd"; break;
                //case ".xyz": mimeType = "chemical/x-pdb"; break;
                //case ".z": mimeType = "application/x-compressed"; break;
                //case ".zip": mimeType = "application/zip"; break;
                //case ".zoo": mimeType = "application/octet-stream"; break;
                //case ".zsh": mimeType = "text/x-script.zsh"; break;
                //default: mimeType = "application/octet-stream"; break;
                #endregion
                //string strMessage =”this is a demo”;
                //byte[] filename = Encoding.ASCII.GetBytes(strMessage);
                //string encodedData = System.Convert.ToBase64String(filename);
                //Entity Annotation = new Entity(“annotation”);
                //Annotation.Attributes["objectid"] = new EntityReference(“EntityName”, GUID);
                //Annotation.Attributes["objecttypecode"] = “EntityNAME”;
                //Annotation.Attributes["subject"] = “Demo”;
                //Annotation.Attributes["documentbody"] = encodedData;
                //Annotation.Attributes["mimetype"] = @”text / plain”;
                //Annotation.Attributes["notetext"] = “Sample attachment.”;
                //Annotation.Attributes["filename"] = “Demo.txt”;
                //Service.Create(Annotation);


                Entity aAnnotationEntity = new Entity("annotation");
                aAnnotationEntity.Attributes["objectid"] = new EntityReference(EntityName, ToBeAttachedEntityId);
                aAnnotationEntity.Attributes["objecttypecode"] = EntityName;
                aAnnotationEntity.Attributes["subject"] = Subject;
                aAnnotationEntity.Attributes["notetext"] = NoteText;

                string aEncodedData = System.Convert.ToBase64String(DocumentBody);

                aAnnotationEntity.Attributes["documentbody"] = aEncodedData;
                aAnnotationEntity.Attributes["mimetype"] = MimeType;
                aAnnotationEntity.Attributes["filename"] = FileName;

                CreateEntity(aAnnotationEntity);
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        #endregion
        #region 處理字串
        static public void DeleteLastComma(ref String StringToProcess)
        {
            try
            {
                // 去掉最後一個逗號
                int Length = StringToProcess.LastIndexOf("，");
                if (Length > 0)
                {
                    StringToProcess = StringToProcess.Substring(0, Length);
                }
            }
            catch (System.Exception e)
            {
                //String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        static public void DeleteLastChar(ref String StringToProcess)
        {
            try
            {
                // 去掉最後一個逗號
                int Length = StringToProcess.Length;
                if (Length > 0)
                {
                    StringToProcess = StringToProcess.Substring(0, Length - 1);
                }
            }
            catch (System.Exception e)
            {
                //String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        static public String DeletePresentRate(String StringToProcess)
        {
            try
            {
                String SpotKeyString = "-主日出席率:";

                int StartPosition = StringToProcess.IndexOf(SpotKeyString);

                String LeftString = "";
                if (StartPosition > 0)
                {
                    LeftString = StringToProcess.Substring(0, StartPosition);
                    return LeftString;
                }
                else
                {
                    return StringToProcess;
                }
            }
            catch (System.Exception e)
            {
                //String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public String TrimPresentRate(String StringToProcess)
        {
            try
            {
                String SpotKeyString = "-主日出席率:";

                int StartPosition = StringToProcess.IndexOf(SpotKeyString);

                String LeftString = "";
                if (StartPosition > 0)
                {
                    LeftString = StringToProcess.Substring(0, StartPosition);
                    return LeftString;
                }
                else
                {
                    return StringToProcess;
                }
            }
            catch (System.Exception e)
            {
                //String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #region 過濾出數字字串
        public String FilterDigit(String aFilteredString)
        {
            try
            {
                //lock (m_OtherLocker)
                {
                    Regex DigitsOnly = new Regex(@"[^\d]");

                    return DigitsOnly.Replace(aFilteredString, "");
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion

        #endregion
        #region 除錯追蹤區
        public void TraceByLevel(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
        {
            try
            {
                if (TotalLevel >= QualifiedLevel)
                {
                    Debug.WriteLine("Time            =" + DateTime.Now.ToString() + Environment.NewLine);
                    Debug.WriteLine("StringToProcess =" + StringToProcess + Environment.NewLine);
                    StackTrace aStackTraceNextLevel = new StackTrace(new StackFrame(1, true));
                    Debug.WriteLine("StackTrace      =" + aStackTraceNextLevel.ToString() + Environment.NewLine);
                    Debug.WriteLine("================================================================== " + Environment.NewLine);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        static public void TraceByLevelStatic(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
        {
            try
            {
                if (TotalLevel >= QualifiedLevel)
                {
                    Debug.WriteLine("Time            =" + DateTime.Now.ToString() + Environment.NewLine);
                    Debug.WriteLine("StringToProcess =" + StringToProcess + Environment.NewLine);
                    StackTrace aStackTraceNextLevel = new StackTrace(new StackFrame(1, true));
                    Debug.WriteLine("StackTrace      =" + aStackTraceNextLevel.ToString() + Environment.NewLine);
                    Debug.WriteLine("================================================================== " + Environment.NewLine);
                }
            }
            catch (System.Exception e)
            {
                //String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
    }
}
