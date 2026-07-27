// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityClass
// 主要成員：InitializeTracing、InitializeCrmConnection、Dispose、TraceByLevel
// 引用命名空間：Microsoft.Crm.Sdk.Messages、Microsoft.Extensions.Configuration、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Client、System、System.Diagnostics、System.IO、System.Text
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Core;

namespace ToolUtilityNameSpace
{
    /// <summary>
    /// ToolUtilityClass - 核心部分 (Partial Class 1/10)
    /// 包含：欄位、常數、建構式、Dispose Pattern
    /// </summary>
    public partial class ToolUtilityClass : IDisposable
    {
        #region 私有欄位
        private const String CRM_TYPE = "DYNAMICS365-9.0";
        private const string SecretPlaceholder = "REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT";
        private String m_DiscoveryServiceType = "";

        public IOrganizationService m_Crm2011OrganizationService;
        public OrganizationServiceProxy m_OrganizationService;

        private bool _disposed = false;
        private readonly ICrmConnectionService _crmConnectionService;
        private readonly ToolUtilityFacade _facade;
        private readonly IConfiguration _configuration;
        #endregion

        #region 設定屬性
        private string SERVER => _configuration?["CrmConnection:Server"] ?? "speechmessage.com.tw";
        private string PORT => _configuration?["CrmConnection:Port"] ?? "7777";
        private string ORGANIZATION => _configuration?["CrmConnection:Organization"] ?? "jesus";
        private string USERNAME => _configuration?["CrmConnection:Username"] ?? "Administrator@speechmessage.com.tw";
        private string PASSWORD => ResolveRequiredSecret("CrmConnection:Password", "CRM_PASSWORD");
        private string DOMAIN => _configuration?["CrmConnection:Domain"] ?? "DYNAMICS-365";
        #endregion

        private string ResolveRequiredSecret(string configurationKey, string environmentVariableName)
        {
            var value = _configuration?[configurationKey];
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, SecretPlaceholder, StringComparison.Ordinal))
            {
                value = Environment.GetEnvironmentVariable(environmentVariableName);
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"{configurationKey} is not configured. Set it through User Secrets or environment variable {environmentVariableName}.");
            }

            return value;
        }

        #region 常數
        private DateTime ExpireDate = new DateTime(2013, 3, 30);
        private const String FILTERED_PROJECT = "";
        private const int EMPTY_VALUE = -999999999;
        protected const bool EXCUTION_FLAG = true;
        protected const bool EXCUTION_TRACE_LINE = true;

        protected const int TOTAL_LEVEL = 5;
        protected const int LEVEL_1 = 1;
        protected const int LEVEL_2 = 2;
        protected const int LEVEL_3 = 3;
        protected const int LEVEL_4 = 4;
        protected const int LEVEL_5 = 5;
        #endregion

        #region 追蹤變數
        private String m_TraceLogFile = "";
        private Lazy<FileStream> _lazyXmlFileStream;
        private Lazy<StreamWriter> _lazyXmlFileStreamWriter;
        private Lazy<TextWriterTraceListener> _lazyListener;
        private const String TRACE_DIRECTOR = @"D:\除錯追蹤\CHURCH_REPORT_TRACE.TXT";
        #endregion

        #region 建構式
        internal ToolUtilityClass(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _crmConnectionService = new CrmConnectionService();

            InitializeTracing();
            InitializeCrmConnection();

            _facade = new ToolUtilityFacade(m_Crm2011OrganizationService);
        }

        internal ToolUtilityClass(String DiscoveryServiceType, IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _crmConnectionService = new CrmConnectionService();
            m_DiscoveryServiceType = DiscoveryServiceType;

            InitializeTracing();
            InitializeCrmConnection();

            _facade = new ToolUtilityFacade(m_Crm2011OrganizationService);
        }

        public ToolUtilityClass(ref bool ValidFlag)
        {
            if (ExpireDate >= DateTime.Today)
            {
                ValidFlag = false;
            }
        }

        ~ToolUtilityClass() => Dispose(false);
        #endregion

        #region 初始化方法
        private void InitializeTracing()
        {
            m_TraceLogFile = TRACE_DIRECTOR;

            _lazyXmlFileStream = new Lazy<FileStream>(() =>
                new FileStream(m_TraceLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));

            _lazyXmlFileStreamWriter = new Lazy<StreamWriter>(() =>
            {
#if !NET462 && !NETFRAMEWORK
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
                return new StreamWriter(_lazyXmlFileStream.Value, Encoding.GetEncoding("big5"));
            });

            _lazyListener = new Lazy<TextWriterTraceListener>(() =>
            {
                var listener = new TextWriterTraceListener(_lazyXmlFileStreamWriter.Value);
                Trace.AutoFlush = true;
                Trace.Listeners.Add(listener);
                return listener;
            });
        }

        private void InitializeCrmConnection()
        {
            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            var adUsername = @"SPEECHMESSAGE\Administrator";
            var adPassword = PASSWORD;

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);
        }
        #endregion

        #region Dispose Pattern
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                try { _facade?.Dispose(); } catch (ObjectDisposedException) { }
                try { (_crmConnectionService as IDisposable)?.Dispose(); } catch (ObjectDisposedException) { }
                try { (m_Crm2011OrganizationService as IDisposable)?.Dispose(); } catch (ObjectDisposedException) { }
                try { (m_OrganizationService as IDisposable)?.Dispose(); } catch (ObjectDisposedException) { }

                if (_lazyListener?.IsValueCreated == true)
                {
                    try
                    {
                        var listener = _lazyListener.Value;
                        Trace.Listeners.Remove(listener);
                        listener.Flush();
                        listener.Close();
                        listener.Dispose();
                    }
                    catch (ObjectDisposedException) { }
                }

                if (_lazyXmlFileStreamWriter?.IsValueCreated == true)
                {
                    try
                    {
                        var writer = _lazyXmlFileStreamWriter.Value;
                        writer.Flush();
                        writer.Close();
                        writer.Dispose();
                    }
                    catch (ObjectDisposedException) { }
                }

                if (_lazyXmlFileStream?.IsValueCreated == true)
                {
                    try
                    {
                        var stream = _lazyXmlFileStream.Value;
                        stream.Flush();
                        stream.Close();
                        stream.Dispose();
                    }
                    catch (ObjectDisposedException) { }
                }
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region 工具方法
        public void TraceByLevel(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
        {
            try
            {
                if (TotalLevel >= QualifiedLevel)
                {
                    var listener = _lazyListener.Value;
                    Trace.WriteLine("Time            =" + DateTime.Now.ToString() + Environment.NewLine);
                    Trace.WriteLine("StringToProcess =" + StringToProcess + Environment.NewLine);
                    Trace.WriteLine("StackTrace      =" + new StackTrace(new StackFrame(1, true)).ToString() + Environment.NewLine);
                    Trace.WriteLine("================================================================== " + Environment.NewLine);
                }
            }
            catch (Exception e)
            {
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
                    Trace.WriteLine("StackTrace      =" + new StackTrace(new StackFrame(1, true)).ToString() + Environment.NewLine);
                    Trace.WriteLine("================================================================== " + Environment.NewLine);
                }
            }
            catch (Exception e)
            {
                throw e;
            }
        }
        #endregion
    }
}
