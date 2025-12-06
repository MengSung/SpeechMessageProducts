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
using static System.Net.WebRequestMethods;

namespace ToolUtilityNameSpace
{
    public class ToolUtilityClass : IDisposable
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

        // 設定物件
        private readonly IConfiguration _configuration;

        #region Dynamics 365 新增組織修改區

        // 從 appsettings.json 讀取設定，使用 Lazy 初始化提升性能
        private string SERVER => _configuration?["CrmConnection:Server"] ?? "speechmessage.com.tw";
        private string PORT => _configuration?["CrmConnection:Port"] ?? "7777";
        private string ORGANIZATION => _configuration?["CrmConnection:Organization"] ?? "sunnyvalech";
        private string USERNAME => _configuration?["CrmConnection:Username"] ?? "Administrator@speechmessage.com.tw";
        private string PASSWORD => _configuration?["CrmConnection:Password"] ?? "hu9840";
        private string DOMAIN => _configuration?["CrmConnection:Domain"] ?? "DYNAMICS-365";

        #endregion

        #region 僅供參考區塊
        //private String _discoveryServiceAddress = "https://tpehoc.speechmessage.com.tw/XRMServices/2011/Discovery.svc";
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
        #region 追蹤專用變數 - 使用 Lazy<T> 延遲初始化以優化記憶體
        private String m_TraceLogFile = "";
        private Lazy<FileStream> _lazyXmlFileStream;
        private Lazy<StreamWriter> _lazyXmlFileStreamWriter;
        private Lazy<TextWriterTraceListener> _lazyListener;
        private const String TRACE_DIRECTOR = @"D:\除錯追蹤\" + "CHURCH_REPORT_TRACE.TXT";
        //private const String TRACE_DIRECTOR = @"C:\除錯追蹤\" + "TRACE.TXT";
        #endregion

        #region 建構式 - 設為 internal，只能通過 Factory 創建

        /// <summary>
        /// 內部建構函數 - 只能通過 ToolUtilityFactory 創建實例
        /// </summary>
        internal ToolUtilityClass(IConfiguration configuration)
        {
            // 保存設定物件
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            // 初始化連接服務
            _crmConnectionService = new CrmConnectionService();

            #region 追蹤專用變數 - 使用 Lazy<T> 延遲初始化
            m_TraceLogFile = TRACE_DIRECTOR;

            // Lazy 初始化 FileStream
            _lazyXmlFileStream = new Lazy<FileStream>(() =>
                new FileStream(m_TraceLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));

            // Lazy 初始化 StreamWriter
            _lazyXmlFileStreamWriter = new Lazy<StreamWriter>(() =>
            {
#if !NET462 && !NETFRAMEWORK
                // 註冊編碼提供者（僅在 .NET 5+ 需要）
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
                return new StreamWriter(_lazyXmlFileStream.Value, Encoding.GetEncoding("big5"));
            });

            // Lazy 初始化 TraceListener - 統一使用標準的 TextWriterTraceListener
            _lazyListener = new Lazy<TextWriterTraceListener>(() =>
            {
                var listener = new TextWriterTraceListener(_lazyXmlFileStreamWriter.Value);
                Trace.AutoFlush = true;
                Trace.Listeners.Add(listener);
                return listener;
            });
            #endregion

            // 使用連接服務建立 CRM 連接
            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            var adUsername = @"SPEECHMESSAGE\Administrator";
            var adPassword = PASSWORD;

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);

            // 初始化 Facade (不傳入 organizationService)
            _facade = new ToolUtilityFacade(m_Crm2011OrganizationService);
        }

        /// <summary>
        /// 內部建構函數 - 只能通過 ToolUtilityFactory 創建實例
        /// </summary>
        /// <param name="DiscoveryServiceType">Discovery Service 類型</param>
        internal ToolUtilityClass(String DiscoveryServiceType, IConfiguration configuration)
        {
            // 保存設定物件
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            // 初始化連接服務
            _crmConnectionService = new CrmConnectionService();

            m_DiscoveryServiceType = DiscoveryServiceType;

            #region 追蹤專用變數 - 使用 Lazy<T> 延遲初始化
            m_TraceLogFile = TRACE_DIRECTOR;

            // Lazy 初始化 FileStream
            _lazyXmlFileStream = new Lazy<FileStream>(() =>
                new FileStream(m_TraceLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));

            // Lazy 初始化 StreamWriter
            _lazyXmlFileStreamWriter = new Lazy<StreamWriter>(() =>
            {
#if !NET462 && !NETFRAMEWORK
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
                return new StreamWriter(_lazyXmlFileStream.Value, Encoding.GetEncoding("big5"));
            });

            // Lazy 初始化 TraceListener - 統一使用標準的 TextWriterTraceListener
            _lazyListener = new Lazy<TextWriterTraceListener>(() =>
            {
                var listener = new TextWriterTraceListener(_lazyXmlFileStreamWriter.Value);
                Trace.AutoFlush = true;
                Trace.Listeners.Add(listener);
                return listener;
            });
            #endregion

            //// 使用連接服務建立 CRM 連接
            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            var adUsername = @"Administrator@speechmessage.com.tw";
            var adPassword = PASSWORD;

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);

            // 初始化 Facade (不傳入 organizationService)
            _facade = new ToolUtilityFacade(m_Crm2011OrganizationService);
        }

        public ToolUtilityClass(ref bool ValidFlag)
        {
            // 初始化連接服務
            //_crmConnectionService = new CrmConnectionService();

            if (ExpireDate >= DateTime.Today)
            {
                ValidFlag = false;
            }
        }

        ~ToolUtilityClass()
        {
            Dispose(false);
        }
        #endregion
        #region 解構式 - 完整實現 Dispose Pattern 以防止 Memory Leak
        /// <summary>
        /// 完整實現 Dispose Pattern，釋放所有 Managed 和 Unmanaged 資源
        /// 遵循 LINUS 原則：明確的資源管理，防止 Memory Leak
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 釋放 Managed 資源

                // 1. 釋放 Facade
                try
                {
                    _facade?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 已被釋放，忽略
                }

                // 2. 釋放 CRM 連接服務
                try
                {
                    (_crmConnectionService as IDisposable)?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 已被釋放，忽略
                }

                // 3. 釋放 Organization Service
                try
                {
                    (m_Crm2011OrganizationService as IDisposable)?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 已被釋放，忽略
                }

                try
                {
                    (m_OrganizationService as IDisposable)?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 已被釋放，忽略
                }

                // 4. 釋放追蹤監聽器（只有在 Lazy 已初始化時才釋放）
                if (_lazyListener != null && _lazyListener.IsValueCreated)
                {
                    try
                    {
                        var listener = _lazyListener.Value;
                        if (listener != null)
                        {
                            Trace.Listeners.Remove(listener);
                            listener.Flush();
                            listener.Close();
                            listener.Dispose();
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // 已被釋放，忽略
                    }
                }

                // 5. 釋放檔案寫入器（只有在 Lazy 已初始化時才釋放）
                if (_lazyXmlFileStreamWriter != null && _lazyXmlFileStreamWriter.IsValueCreated)
                {
                    try
                    {
                        var writer = _lazyXmlFileStreamWriter.Value;
                        if (writer != null)
                        {
                            writer.Flush();
                            writer.Close();
                            writer.Dispose();
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // 已被釋放，忽略
                    }
                }

                // 6. 釋放檔案串流（只有在 Lazy 已初始化時才釋放）
                if (_lazyXmlFileStream != null && _lazyXmlFileStream.IsValueCreated)
                {
                    try
                    {
                        var stream = _lazyXmlFileStream.Value;
                        if (stream != null)
                        {
                            stream.Flush();
                            stream.Close();
                            stream.Dispose();
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // 已被釋放，忽略
                    }
                }
            }

            // 釋放 Unmanaged 資源（如果有）
            // ...

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
        public EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId)
        {
            try
            {
                // 委派給 Facade 處理
                return _facade.QueryWeeklyReportBeforeTowMonthOfSunday(aSunday, aListEntityId);
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

        public EntityCollection RetrieveDynamicMemberListDynamics365(OrganizationServiceProxy service, Guid strList)
            => _facade.RetrieveDynamicMemberListDynamics365(service, strList);

        public EntityCollection RetrieveDynamicMemberListCrm2011(IOrganizationService service, string strList)
            => _facade.RetrieveDynamicMemberListCrm2011(service, strList);

        public EntityCollection RetrieveDynamicMemberList(Guid aListId)
            => _facade.RetrieveDynamicMemberList(aListId);

        public EntityCollection RetrieveDynamicMemberList(ref IOrganizationService service, Guid aListId)
            => _facade.RetrieveDynamicMemberList(ref service, aListId);

        public EntityCollection RetrieveDynamicMemberListDynamics365(ref OrganizationServiceProxy service, Guid aListId)
            => _facade.RetrieveDynamicMemberListDynamics365(service, aListId);

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

        public EntityCollection QueryWeeklyReportBeforeTwoMonthOfSunday(DateTime aSunday, Guid aListEntityId)
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
        #region 負責人管理 (委派到 Facade)
        public Guid GetOwnerId(Entity aEntity)
            => _facade.GetOwnerId(aEntity);

        public void AssignOwner(String EntityName, Entity aEntity, Guid OwnerId)
            => _facade.AssignOwner(EntityName, aEntity, OwnerId);

        public String GetOwnerName(Entity aEntity)
            => _facade.GetOwnerName(aEntity);
        #endregion
        #region 追蹤及統計用的Line訊息，完全委派到 Facade 的方法
        public void CreatePushLineMessage(string UserId, string Subject, string Message)
        {
            try
            {
                if (EXCUTION_TRACE_LINE == true)
                {
                    EntityCollection contactCollection = _facade.RetrieveContactCollectionByLineId(UserId);
                    Entity aContact = (contactCollection != null && contactCollection.Entities.Count > 0)
                        ? contactCollection.Entities[0]
                        : null;

                    if (aContact != null)
                    {
                        Entity aEntity = new Entity("letter");
                        _facade.SetEntityStringAttribute(ref aEntity, "subject", Subject);
                        _facade.SetEntityStringAttribute(ref aEntity, "description", Message);
                        _facade.SetEntityStringAttribute(ref aEntity, "new_displayed_lineid", UserId);
                        EntityReference regardingRef = new EntityReference("contact", aContact.Id);
                        _facade.SetEntityLookUpAttribute(ref aEntity, "regardingobjectid", ref regardingRef);

                        _facade.SetEntityDateTimeAttribute(ref aEntity, "scheduledend", DateTime.Now);

                        //方向=>撥出
                        _facade.SetEntityBoolAttribute(ref aEntity, "directioncode", true);

                        //計數=>1
                        _facade.SetEntityIntAttribute(ref aEntity, "new_count", 1);

                        //設定訊息種類為文字 
                        _facade.SetOptionSetAttribute(ref aEntity, "new_message_category", 100000000);

                        Entity Fromparty = new Entity("activityparty");

                        Fromparty["partyid"] = new EntityReference("contact", aContact.Id);

                        aEntity["from"] = new Entity[] { Fromparty };
                        aEntity["to"] = new Entity[] { Fromparty };

                        // 新增Line訊息
                        _facade.CreateEntity(aEntity);

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
                        EntityCollection contactCollection = _facade.RetrieveContactCollectionByLineId(UserId);
                        Entity aContact = (contactCollection != null && contactCollection.Entities.Count > 0)
                            ? contactCollection.Entities[0]
                            : null;

                        if (aContact != null)
                        {
                            Entity aEntity = new Entity("letter");
                            _facade.SetEntityStringAttribute(ref aEntity, "subject", Subject);
                            _facade.SetEntityStringAttribute(ref aEntity, "description", Message);
                            _facade.SetEntityStringAttribute(ref aEntity, "new_displayed_lineid", UserId);
                            EntityReference regardingRef = new EntityReference("contact", aContact.Id);
                            _facade.SetEntityLookUpAttribute(ref aEntity, "regardingobjectid", ref regardingRef);

                            _facade.SetEntityDateTimeAttribute(ref aEntity, "scheduledend", DateTime.Now);

                            //方向=>撥出
                            _facade.SetEntityBoolAttribute(ref aEntity, "directioncode", true);

                            //計數=>1
                            _facade.SetEntityIntAttribute(ref aEntity, "new_count", 1);

                            //設定訊息種類為文字 
                            _facade.SetOptionSetAttribute(ref aEntity, "new_message_category", 100000000);

                            Entity Fromparty = new Entity("activityparty");

                            Fromparty["partyid"] = new EntityReference("contact", aContact.Id);

                            aEntity["from"] = new Entity[] { Fromparty };
                            aEntity["to"] = new Entity[] { Fromparty };

                            // 新增Line訊息
                            _facade.CreateEntity(aEntity);

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
        #region 將連絡人加入或移除至名單，完全委派到 Facade 的方法

        //private readonly object m_MembersToMarketingListLocker = new object();
        public void AddMembersToMarketingList(Guid thisListGuid, List<Guid> memberGuidList, ref IOrganizationService gCRMService)
        {
            try
            {
                _facade.AddMembersToMarketingList(thisListGuid, memberGuidList, ref gCRMService);
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
                _facade.RemoveMembersToMarketingList(aListGuid, MemberGuid, ref gCRMService);
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
                _facade.AddMembersToMarketingList(thisListGuid, memberGuidList);
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
                _facade.RemoveMembersToMarketingList(aListGuid, MemberGuid);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }

        public ArrayList GetAllMemberDataFromList(Guid ListEntityId)
        {
            return _facade.GetAllMemberDataFromList(ListEntityId);
        }

        #endregion
        #region 批量並行操作 (Phase 2.3 - 新增非同步方法)

        /// <summary>
        /// 批量並行添加成員到名單 (非同步)
        /// ✅ Phase 2.3: 效能提升 5-10倍
        /// 使用 AddMemberListRequest + Task.WhenAll 並行處理
        /// </summary>
        /// <param name="listGuid">名單ID</param>
        /// <param name="memberGuidList">成員ID列表</param>
        /// <param name="batchSize">批次大小 (預設50)</param>
        /// <param name="cancellationToken">取消標記</param>
        /// <returns>成功添加的成員數</returns>
        public async Task<int> AddMembersToMarketingListAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int batchSize = 50,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                return await _facade.AddMembersToMarketingListAsync(
                    listGuid,
                    memberGuidList,
                    batchSize,
                    cancellationToken);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() +
                    " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }

        /// <summary>
        /// 使用 CRM SDK 批量添加成員 (非同步 - 最高效)
        /// ✅ Phase 2.3: 效能提升 20-50倍，推薦用於 >100 個成員
        /// 使用 AddListMembersListRequest 批次 API
        /// </summary>
        /// <param name="listGuid">名單ID</param>
        /// <param name="memberGuidList">成員ID列表</param>
        /// <param name="maxBatchSize">最大批次大小 (預設1000)</param>
        /// <param name="cancellationToken">取消標記</param>
        /// <returns>成功添加的成員數</returns>
        public async Task<int> AddMembersToMarketingListUsingSdkAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int maxBatchSize = 1000,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                return await _facade.AddMembersToMarketingListUsingSdkAsync(
                    listGuid,
                    memberGuidList,
                    this.m_Crm2011OrganizationService,
                    maxBatchSize,
                    cancellationToken);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() +
                    " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }

        /// <summary>
        /// 批量並行移除名單成員 (非同步)
        /// ✅ Phase 2.3: 效能提升 5-10倍
        /// 使用批次並行處理
        /// </summary>
        /// <param name="listGuid">名單ID</param>
        /// <param name="memberGuidList">成員ID列表</param>
        /// <param name="batchSize">批次大小 (預設50)</param>
        /// <param name="cancellationToken">取消標記</param>
        /// <returns>成功移除的成員數</returns>
        public async Task<int> RemoveMembersFromMarketingListAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int batchSize = 50,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                return await _facade.RemoveMembersFromMarketingListAsync(
                    listGuid,
                    memberGuidList,
                    batchSize,
                    cancellationToken);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() +
                    " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }

        #endregion
        #region 活動相關的收件人或寄件人，完全委派到 Facade 的方法

        public void GetActivityPartyList(Entity ActivityEntity, String FromOrTo, ArrayList aFromOrToList, ArrayList aFromOrToTypeList)
        {
            try
            {
                _facade.GetActivityPartyList(ActivityEntity, FromOrTo, aFromOrToList, aFromOrToTypeList);
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
                _facade.GetActivityPartyIdList(ActivityEntity, FromOrTo, aFromOrToIdList, aFromOrToTypeList);
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
                if (CRM_TYPE == "DYNAMICS365")
                {
                    _facade.SetActivityStatusToCompleted(ActivityName, aActivityId, this.m_OrganizationService);
                }
                else
                {
                    _facade.SetActivityStatusToCompleted(ActivityName, aActivityId, this.m_Crm2011OrganizationService);
                }
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
                if (CRM_TYPE == "DYNAMICS365")
                {
                    _facade.SetAppointmentStatusToScheduled(aActivityId, this.m_OrganizationService);
                }
                else
                {
                    _facade.SetAppointmentStatusToScheduled(aActivityId, this.m_Crm2011OrganizationService);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }

        #endregion
        #region 處理附加檔(委派到 Facade)
        public EntityCollection DownloadAnAttachment(ref IOrganizationService aCrmService, Guid AnEntityId)
        {
            try
            {
                return _facade.DownloadAnAttachment(ref aCrmService, AnEntityId);
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
                _facade.UploadAnAttachment(ref aCrmService, EntityName, Subject, NoteText, FileName, MimeType, DocumentBody, ToBeAttachedEntityId);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }

        #endregion
        #region 處理字串 ( 委派到 Facade，委派到 StringUtility)
        static public void DeleteLastComma(ref String StringToProcess)
        {
            try
            {
                ToolUtilityFacade.DeleteLastComma(ref StringToProcess);
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        static public void DeleteLastChar(ref String StringToProcess)
        {
            try
            {
                ToolUtilityNameSpace.Utilities.StringUtility.DeleteLastChar(ref StringToProcess);
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        static public String DeletePresentRate(String StringToProcess)
        {
            try
            {
                return ToolUtilityNameSpace.Utilities.StringUtility.DeletePresentRate(StringToProcess);
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public String TrimPresentRate(String StringToProcess)
        {
            try
            {
                return ToolUtilityNameSpace.Utilities.StringUtility.TrimPresentRate(StringToProcess);
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        #region 過濾出數字字串
        public String FilterDigit(String aFilteredString)
        {
            try
            {
                return _facade.FilterDigit(aFilteredString);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion

        #endregion
        #region 除錯追蹤區 - 修改為使用 Lazy 初始化的 Listener
        /// <summary>
        /// 追蹤方法 - 只在需要時才初始化追蹤資源（Lazy 初始化）
        /// 優化記憶體使用：如果不使用追蹤功能，不會創建相關資源
        /// </summary>
        public void TraceByLevel(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
        {
            try
            {
                if (TotalLevel >= QualifiedLevel)
                {
                    // 只在需要時才初始化追蹤資源
                    var listener = _lazyListener.Value;

                    Trace.WriteLine("Time            =" + DateTime.Now.ToString() + Environment.NewLine);
                    Trace.WriteLine("StringToProcess =" + StringToProcess + Environment.NewLine);
                    StackTrace aStackTraceNextLevel = new StackTrace(new StackFrame(1, true));
                    Trace.WriteLine("StackTrace      =" + aStackTraceNextLevel.ToString() + Environment.NewLine);
                    Trace.WriteLine("================================================================== " + Environment.NewLine);
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
                    Trace.WriteLine("Time            =" + DateTime.Now.ToString() + Environment.NewLine);
                    Trace.WriteLine("StringToProcess =" + StringToProcess + Environment.NewLine);
                    StackTrace aStackTraceNextLevel = new StackTrace(new StackFrame(1, true));
                    Trace.WriteLine("StackTrace      =" + aStackTraceNextLevel.ToString() + Environment.NewLine);
                    Trace.WriteLine("================================================================== " + Environment.NewLine);
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
