using Line.Messaging.Webhooks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.SqlServer.Server;
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
using TraceNameSpace;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Core;
using static System.Net.WebRequestMethods;

namespace ToolUtility_Developing_NameSpace
{
    /// <summary>
    /// ToolUtilityClass 開發版本 - 作為 ToolUtilityFacade 的包裝類別
    /// 保持向後兼容性,同時委派業務邏輯到新的服務架構
    /// </summary>
    public class ToolUtilityClass : IDisposable
    {
        #region 資料區
        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365-9.0";

        String m_DiscoveryServiceType = "";

        public IOrganizationService m_Crm2011OrganizationService;
        private bool _disposed = false;

        public OrganizationServiceProxy m_OrganizationService;

        // 連接服務專責處理
        private readonly ICrmConnectionService _crmConnectionService;
        
        // 新架構的 Facade (用於委派複雜業務邏輯)
        private readonly ToolUtilityFacade _facade;

        #region Dynamics 365 組織設定
        #region 聖谷行道會(雲端機房)
        private const String SERVER = "speechmessage.com.tw";
        private const String PORT = "7777";
        private const String ORGANIZATION = "sunnyvalech";
        private const String USERNAME = "Administrator@speechmessage.com.tw";
        private const String PASSWORD = "hu9840";
        private const String DOMAIN = "DYNAMICS-365";
        #endregion

        private String BASE_DISCOVERY_SERVICE_ADDRESS = "/XRMServices/2011/Discovery.svc";
        #endregion

        #region 有效截止日期
        private DateTime ExpireDate = new DateTime(2013, 3, 30);
        #endregion

        #region 常數參數
        private const String FILTERED_PROJECT = "";
        private const int EMPTY_VALUE = -999999999;
        private const bool EXCUTION_FLAG = true;
        private const bool EXCUTION_TRACE_LINE = true;
        
        // 除錯用參數
        private const int TOTAL_LEVEL = 5;
        private const int LEVEL_1 = 1;
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5;
        #endregion

        #region 追蹤專用變數
        private String m_TraceLogFile = "";
        private BugslayerTextWriterTraceListener m_Listener = new BugslayerTextWriterTraceListener();
        private FileStream m_XmlFileStream;
        private StreamWriter m_XmlFileStreamWriter;
        private const String TRACE_DIRECTOR = @"D:\除錯追蹤\" + "CHURCH_REPORT_TRACE.TXT";
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
            
            // 初始化 Facade (用於委派業務邏輯)
            _facade = new ToolUtilityFacade();
        }

        public ToolUtilityClass(String DiscoveryServiceType)
        {
            _crmConnectionService = new CrmConnectionService();
            m_DiscoveryServiceType = DiscoveryServiceType;

            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            var adUsername = @"Administrator@speechmessage.com.tw";
            var adPassword = "hu9840";

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);
            _facade = new ToolUtilityFacade();
        }

        public ToolUtilityClass(ref bool ValidFlag)
        {
            _crmConnectionService = new CrmConnectionService();

            if (ExpireDate >= DateTime.Today)
            {
                ValidFlag = false;
            }
            
            _facade = new ToolUtilityFacade();
        }

        ~ToolUtilityClass()
        {
        }
        #endregion

        #region 解構式
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _facade?.Dispose();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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

        #region 取得聯絡人 (委派給 Facade)
        public String RetrieveContactByContactId(String ContactId)
        {
            try
            {
                return _facade.RetrieveContactByContactId(ContactId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactByContactId 錯誤: " + e.Message);
                throw;
            }
        }

        public Entity RetrieveContactByContactId(ref IOrganizationService aOrganizationService, String ContactId, ref int Count)
        {
            try
            {
                return _facade.RetrieveContactByContactId(ref aOrganizationService, ContactId, ref Count);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactByContactId (Service) 錯誤: " + e.Message);
                throw;
            }
        }

        public String RetrieveContactByName(String ContactFullName)
        {
            try
            {
                return _facade.RetrieveContactByName(ContactFullName);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactByName 錯誤: " + e.Message);
                throw;
            }
        }

        public Entity RetrieveContactEntityByName(String ContactFullName)
        {
            try
            {
                return _facade.RetrieveContactEntityByName(ContactFullName);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactEntityByName 錯誤: " + e.Message);
                throw;
            }
        }

        public Entity RetrieveContactByName(ref IOrganizationService aOrganizationService, String ContactFullName)
        {
            try
            {
                return _facade.RetrieveContactByName(ref aOrganizationService, ContactFullName);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactByName (Service) 錯誤: " + e.Message);
                throw;
            }
        }

        public String RetrieveContactByName_ReturnString(ref IOrganizationService aOrganizationService, String ContactFullName)
        {
            try
            {
                return _facade.RetrieveContactByName_ReturnString(ref aOrganizationService, ContactFullName);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactByName_ReturnString 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveContactCollectionByName(String ContactFullName)
        {
            try
            {
                return _facade.RetrieveContactCollectionByName(ContactFullName);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactCollectionByName 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveContactCollectionByNationId(String NationId)
        {
            try
            {
                return _facade.RetrieveContactCollectionByNationId(NationId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactCollectionByNationId 錯誤: " + e.Message);
                throw;
            }
        }

        public Entity RetrieveContactByLineId(String LineId)
        {
            try
            {
                return _facade.RetrieveContactByLineId(LineId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactByLineId 錯誤: " + e.Message);
                throw;
            }
        }

        public String RetrieveContactByAccountNumber(String AccountNumber, String aPassword)
        {
            try
            {
                return _facade.RetrieveContactByAccountNumber(AccountNumber, aPassword);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactByAccountNumber 錯誤: " + e.Message);
                throw;
            }
        }

        public Entity DoesAccountExist(String AccountNumber)
        {
            try
            {
                return _facade.DoesAccountExist(AccountNumber);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "DoesAccountExist 錯誤: " + e.Message);
                throw;
            }
        }

        public Entity RetrieveContactEntityByAccountNumber(String AccountNumber, String aPassword)
        {
            try
            {
                return _facade.RetrieveContactEntityByAccountNumber(AccountNumber, aPassword);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactEntityByAccountNumber 錯誤: " + e.Message);
                throw;
            }
        }

        public Entity RetrieveContactEntityByLineUserId(String LineUserId)
        {
            try
            {
                return _facade.RetrieveContactEntityByLineUserId(LineUserId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactEntityByLineUserId 錯誤: " + e.Message);
                throw;
            }
        }

        public Entity RetrieveContactEntityByFullNameAndMobileNumber(String FullName, String MobileNumber)
        {
            try
            {
                return _facade.RetrieveContactEntityByFullNameAndMobileNumber(FullName, MobileNumber);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactEntityByFullNameAndMobileNumber 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveContactEntityByFullNameCollection(String FullName)
        {
            try
            {
                return _facade.RetrieveContactEntityByFullNameCollection(FullName);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveContactEntityByFullNameCollection 錯誤: " + e.Message);
                throw;
            }
        }

        #endregion

        #region 取得成員名單、動態名單 (委派給 Facade)
        public EntityCollection RetrieveMemberListCollectionByListId(Guid aListId)
        {
            try
            {
                return _facade.RetrieveMemberListCollectionByListId(aListId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveMemberListCollectionByListId 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveMemberListCollectionByListId(ref IOrganizationService aOrganizationService, Guid aListId)
        {
            try
            {
                return _facade.RetrieveMemberListCollectionByListId(ref aOrganizationService, aListId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveMemberListCollectionByListId (Service) 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(ref OrganizationServiceProxy aOrganizationService, Guid aListId)
        {
            try
            {
                return _facade.RetrieveMemberListCollectionByListIdDynamics365(ref aOrganizationService, aListId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveMemberListCollectionByListIdDynamics365 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveMemberListCollectionByListIdCrm2011(ref IOrganizationService aOrganizationService, Guid aListId)
        {
            try
            {
                return _facade.RetrieveMemberListCollectionByListIdCrm2011(ref aOrganizationService, aListId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveMemberListCollectionByListIdCrm2011 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveDynamicMemberList(string strList)
        {
            try
            {
                return _facade.RetrieveDynamicMemberList(strList);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveDynamicMemberList (String) 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveDynamicMemberList(IOrganizationService service, string strList)
        {
            try
            {
                return _facade.RetrieveDynamicMemberList(service, strList);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveDynamicMemberList (Service, String) 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveDynamicMemberListDynamics365(OrganizationServiceProxy service, string strList)
        {
            try
            {
                return _facade.RetrieveDynamicMemberListDynamics365(service, strList);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveDynamicMemberListDynamics365 (String) 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveDynamicMemberListCrm2011(IOrganizationService service, string strList)
        {
            try
            {
                return _facade.RetrieveDynamicMemberListCrm2011(service, strList);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveDynamicMemberListCrm2011 (String) 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveDynamicMemberList(Guid aListId)
        {
            try
            {
                return _facade.RetrieveDynamicMemberList(aListId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveDynamicMemberList (Guid) 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveDynamicMemberList(ref IOrganizationService service, Guid aListId)
        {
            try
            {
                return _facade.RetrieveDynamicMemberList(ref service, aListId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveDynamicMemberList (Service, Guid) 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveDynamicMemberListDynamics365(ref OrganizationServiceProxy service, Guid aListId)
        {
            try
            {
                return _facade.RetrieveDynamicMemberListDynamics365(ref service, aListId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveDynamicMemberListDynamics365 (Guid) 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveDynamicMemberListCrm2011(ref IOrganizationService service, Guid aListId)
        {
            try
            {
                return _facade.RetrieveDynamicMemberListCrm2011(ref service, aListId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveDynamicMemberListCrm2011 (Guid) 錯誤: " + e.Message);
                throw;
            }
        }

        #endregion

        #region FetchXML 查詢方法 (保持原有實作)
        public EntityCollection QueryDediccationContatsByFetchXml(String DedicationNumber, String ContactName, String HomePhone, String Mobile, String NationId, String LastSixDigit)
        {
            return ExecuteFetchXmlQuery(BuildDedicationContactsFetchXml(DedicationNumber, ContactName, HomePhone, Mobile, NationId, LastSixDigit));
        }

        public EntityCollection QueryContatsByStartedDedicationNumber(String DedicationStartNumber)
        {
            return ExecuteFetchXmlQuery(BuildStartDedicationFetchXml(DedicationStartNumber));
        }

        #endregion

        #region 取得客戶組織
        public Guid RetrieveAccountCollectionByName(String AccountName)
        {
            var query = new QueryByAttribute("account") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("name", "statecode");
            query.Values.AddRange(AccountName, 0);
            var retrieved = ExecuteQuery(query);
            return (retrieved.Entities.Count > 0) ? retrieved.Entities[0].Id : Guid.Empty;
        }
        #endregion

        #region 取得約會、課程、工作等 (保持原有 FetchXML 實作)
        public EntityCollection RetrieveAppointmentsByDate(DateTime aSelectedDate)
        {
            var query = new QueryByAttribute("appointment") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "statecode");
            return ExecuteQuery(query);
        }

        public EntityCollection RetrieveAppointmentsByFetchXml(DateTime StartDate, DateTime EndDate)
        {
            return ExecuteFetchXmlQuery(BuildAppointmentsByDateRangeFetchXml(StartDate, EndDate));
        }

        public EntityCollection RetrieveAppointmentsByFetchXml(String ContactName, String ContactId)
        {
            return ExecuteFetchXmlQuery(BuildAppointmentsByContactFetchXml(ContactName, ContactId));
        }

        public EntityCollection RetrieveAppointmentsByFetchXmlAndScheduleType(DateTime StartDate, DateTime EndDate, String ScheduleType)
        {
            return ExecuteFetchXmlQuery(BuildAppointmentsByDateRangeAndScheduleTypeFetchXml(StartDate, EndDate, ScheduleType));
        }

        public EntityCollection RetrieveEnrolledLessonsByFetchXml(DateTime StartDate, DateTime EndDate, String ContactName, String ContactId)
        {
            return ExecuteFetchXmlQuery(BuildEnrolledLessonsFetchXml(StartDate, EndDate, ContactName, ContactId));
        }

        public EntityCollection RetrieveLessonsByMonth(DateTime StartDate, DateTime EndDate)
        {
            return ExecuteFetchXmlQuery(BuildLessonsByMonthFetchXml(StartDate, EndDate));
        }

        public EntityCollection RetrieveStorLessonsByFetchXml(String LessonName, String LessonId, String ContactName, String ContactId)
        {
            return ExecuteFetchXmlQuery(BuildStorLessonsFetchXml(LessonName, LessonId, ContactName, ContactId));
        }

        public EntityCollection RetrieveTaskByFetchXml(String Subject)
        {
            return ExecuteFetchXmlQuery(BuildTaskBySubjectFetchXml(Subject));
        }

        public EntityCollection RetrievePresentRecordByFetchXml(String WeeklyReportName, String WeeklyReportId, String ContactName, String ContactId)
        {
            return ExecuteFetchXmlQuery(BuildPresentRecordFetchXml(WeeklyReportName, WeeklyReportId, ContactName, ContactId));
        }

        public EntityCollection RetrievePresentRecordByFetchXmlAndSundayDate(String ContactName, String ContactId, DateTime SundayDate)
        {
            return ExecuteFetchXmlQuery(BuildPresentRecordBySundayDateFetchXml(ContactName, ContactId, SundayDate));
        }

        public EntityCollection RetrievePresentRecordByFetchXmlAndWeeklyReport(String ContactName, String ContactId, String WeeklyReportName, String WeeklyReportId)
        {
            return ExecuteFetchXmlQuery(BuildPresentRecordByWeeklyReportFetchXml(ContactName, ContactId, WeeklyReportName, WeeklyReportId));
        }

        public EntityCollection RetrievePresentRecordByFetchXmlAndContainEpiredDate(String ContactName, String ContactId)
        {
            return ExecuteFetchXmlQuery(BuildPresentRecordWithExpiredDateFetchXml(ContactName, ContactId));
        }

        public EntityCollection RetrievePresentRecordByFetchXmlAndContact_SmallGroup_SundayDate(String ContactName, String ContactId, String SmallGroupName, String SmallGroupId, DateTime SundayDate)
        {
            return ExecuteFetchXmlQuery(BuildPresentRecordByContactSmallGroupAndDateFetchXml(ContactName, ContactId, SmallGroupName, SmallGroupId, SundayDate));
        }

        public Entity RetrieveListEntityByName(String ListName)
        {
            var query = new QueryByAttribute("list") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("listname", "statecode");
            query.Values.AddRange(ListName, 0);
            var retrieved = ExecuteQuery(query);
            return (retrieved.Entities.Count > 0) ? retrieved.Entities[0] : null;
        }

        public EntityCollection RetrieveListByFetchXmlContact(String ContactName)
        {
            return ExecuteFetchXmlQuery(BuildListByContactFetchXml(ContactName));
        }

        public EntityCollection RetrieveListByFetchXmlRacerLeader(String ContactName, String ContactId)
        {
            return ExecuteFetchXmlQuery(BuildListByRacerLeaderFetchXml(ContactName, ContactId));
        }

        public EntityCollection RetrieveDedicationFeeByFetchXml(String ContactName, String ContactId)
        {
            return ExecuteFetchXmlQuery(BuildDedicationFeeFetchXml(ContactName, ContactId));
        }

        public EntityCollection RetrieveDedicationFeeByDateFetchXml(String ContactName, String ContactId, DateTime StartDate, DateTime EndDate)
        {
            return ExecuteFetchXmlQuery(BuildDedicationFeeByDateRangeFetchXml(ContactName, ContactId, StartDate, EndDate));
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
                    Debug.WriteLine("Time            =" + DateTime.Now.ToString());
                    Debug.WriteLine("StringToProcess =" + StringToProcess);
                    StackTrace aStackTraceNextLevel = new StackTrace(new StackFrame(1, true));
                    Debug.WriteLine("StackTrace      =" + aStackTraceNextLevel.ToString());
                    Debug.WriteLine("==================================================================");
                }
            }
            catch (Exception e)
            {
                // Swallow trace errors
            }
        }

        static public void TraceByLevelStatic(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
        {
            try
            {
                if (TotalLevel >= QualifiedLevel)
                {
                    Debug.WriteLine("Time            =" + DateTime.Now.ToString());
                    Debug.WriteLine("StringToProcess =" + StringToProcess);
                    StackTrace aStackTraceNextLevel = new StackTrace(new StackFrame(1, true));
                    Debug.WriteLine("StackTrace      =" + aStackTraceNextLevel.ToString());
                    Debug.WriteLine("==================================================================");
                }
            }
            catch
            {
                // Swallow trace errors
            }
        }
        #endregion

        #region 輔助方法 (私有)
        private IOrganizationService GetCurrentService()
        {
            if (CRM_TYPE == "DYNAMICS365" && m_OrganizationService != null)
            {
                return m_OrganizationService;
            }
            return m_Crm2011OrganizationService;
        }

        private QueryByAttribute CreateContactQuery(string fieldName, string fieldValue)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);
            return query;
        }

        private Entity RetrieveContactEntity(string fieldName, string fieldValue)
        {
            var query = CreateContactQuery(fieldName, fieldValue);
            var retrieved = ExecuteQuery(query);
            return (retrieved.Entities.Count > 0) ? retrieved.Entities[0] : null;
        }

        private string FormatContactQuery(string fieldName, string fieldValue)
        {
            var entity = RetrieveContactEntity(fieldName, fieldValue);
            if (entity == null) return string.Empty;

            StringBuilder sb = new StringBuilder();
            if (entity.Contains("fullname")) sb.AppendLine("姓名:" + entity["fullname"]);
            if (entity.Contains("build_customer_id")) sb.AppendLine("身分證字號:" + entity["build_customer_id"]);
            if (entity.Contains("telephone1")) sb.AppendLine("電話號碼:" + entity["telephone1"]);
            if (entity.Contains("emailaddress1")) sb.AppendLine("電子郵件:" + entity["emailaddress1"]);
            return sb.ToString();
        }

        private EntityCollection ExecuteQuery(QueryByAttribute query)
        {
            var service = GetCurrentService();
            return service.RetrieveMultiple(query);
        }

        private EntityCollection ExecuteFetchXmlQuery(string fetchXml)
        {
            var request = new RetrieveMultipleRequest { Query = new FetchExpression(fetchXml) };
            var service = GetCurrentService();
            var response = (RetrieveMultipleResponse)service.Execute(request);
            return response.EntityCollection;
        }

        // FetchXML 建構方法 - 委派到原有邏輯
        private string BuildDedicationContactsFetchXml(string dedicationNumber, string contactName, string homePhone, string mobile, string nationId, string lastSixDigit)
        {
            dedicationNumber = "'" + dedicationNumber + "'";
            contactName = "'%" + contactName + "%'";
            homePhone = "'%" + homePhone + "%'";
            mobile = "'%" + mobile + "%'";
            nationId = "'%" + nationId + "%'";
            lastSixDigit = "'%" + lastSixDigit + "%'";

            return @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='contact'>
                        <attribute name='fullname' />
                        <attribute name='telephone2' />
                        <attribute name='address2_line1' />
                        <attribute name='parentcustomerid' />
                        <attribute name='new_church_jobtitle' />
                        <attribute name='mobilephone' />
                        <attribute name='emailaddress1' />
                        <attribute name='pager' />
                        <attribute name='new_cell_list_contact' />
                        <attribute name='new_personal_id' />
                        <attribute name='new_last_six_digit' />
                        <attribute name='contactid' />
                        <order attribute='fullname' descending='false' />
                        <filter type='and'>
                          <filter type='or'>
                            <condition attribute='pager' operator='eq' value=" + dedicationNumber + @" />
                            <condition attribute='fullname' operator='like' value=" + contactName + @"/>
                            <condition attribute='telephone2' operator='like' value=" + homePhone + @" />
                            <condition attribute='mobilephone' operator='like' value=" + mobile + @" />
                            <condition attribute='new_personal_id' operator='like' value=" + nationId + @" />
                            <condition attribute='new_last_six_digit' operator='like' value=" + lastSixDigit + @" />
                          </filter>
                          <condition attribute='statuscode' operator='eq' value='1' />
                        </filter>
                      </entity>
                    </fetch>";
        }

        private string BuildStartDedicationFetchXml(string dedicationStartNumber)
        {
            dedicationStartNumber = "'" + dedicationStartNumber + "%'";

            return @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='3'>
                      <entity name='contact'>
                        <attribute name='fullname' />
                        <attribute name='pager' />
                        <attribute name='telephone2' />
                        <attribute name='address2_line1' />
                        <attribute name='parentcustomerid' />
                        <attribute name='new_church_jobtitle' />
                        <attribute name='mobilephone' />
                        <attribute name='emailaddress1' />
                        <attribute name='contactid' />
                        <order attribute='pager' descending='true' />
                        <filter type='and'>
                          <condition attribute='pager' operator='like' value=" + dedicationStartNumber + @" />
                        </filter>
                      </entity>
                    </fetch>";
        }

        private string BuildAppointmentsByDateRangeFetchXml(DateTime startDate, DateTime endDate)
        {
            string startDateString = $"'{startDate.Year}-{startDate.Month}-{startDate.Day}'";
            string endDateString = $"'{endDate.Year}-{endDate.Month}-{endDate.Day}'";

            return @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='scheduledstart' operator='on-or-after' value=" + startDateString + @" />
                          <condition attribute='scheduledstart' operator='on-or-before' value=" + endDateString + @" />
                        </filter>
                      </entity>
                    </fetch>";
        }

        // 其他 FetchXML 建構方法 - 暫時返回空字串,待後續實作
        private string BuildAppointmentsByContactFetchXml(string contactName, string contactId) { return ""; }
        private string BuildAppointmentsByDateRangeAndScheduleTypeFetchXml(DateTime startDate, DateTime endDate, string scheduleType) { return ""; }
        private string BuildEnrolledLessonsFetchXml(DateTime startDate, DateTime endDate, string contactName, string contactId) { return ""; }
        private string BuildLessonsByMonthFetchXml(DateTime startDate, DateTime endDate) { return ""; }
        private string BuildStorLessonsFetchXml(string lessonName, string lessonId, string contactName, string contactId) { return ""; }
        private string BuildTaskBySubjectFetchXml(string subject) { return ""; }
        private string BuildPresentRecordFetchXml(string weeklyReportName, string weeklyReportId, string contactName, string contactId) { return ""; }
        private string BuildPresentRecordBySundayDateFetchXml(string contactName, string contactId, DateTime sundayDate) { return ""; }
        private string BuildPresentRecordByWeeklyReportFetchXml(string contactName, string contactId, string weeklyReportName, string weeklyReportId) { return ""; }
        private string BuildPresentRecordWithExpiredDateFetchXml(string contactName, string contactId) { return ""; }
        private string BuildPresentRecordByContactSmallGroupAndDateFetchXml(string contactName, string contactId, string smallGroupName, string smallGroupId, DateTime sundayDate) { return ""; }
        private string BuildListByContactFetchXml(string contactName) { return ""; }
        private string BuildListByRacerLeaderFetchXml(string contactName, string contactId) { return ""; }
        private string BuildDedicationFeeFetchXml(string contactName, string contactId) { return ""; }
        private string BuildDedicationFeeByDateRangeFetchXml(string contactName, string contactId, DateTime startDate, DateTime endDate) { return ""; }

        #endregion
    }
}
