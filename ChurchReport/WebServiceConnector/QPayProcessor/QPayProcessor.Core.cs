using ChurchReport.Models;
using ChurchReport.Payments;
using ChurchReport.Services;
using ChurchReport.Tools;
using Line.Messaging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using System;
using System.IO;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 金流處理器 - 核心模組
    /// 
    /// 【職責】
    /// - 初始化與依賴注入
    /// - 配置管理
    /// - LINE Bot 整合
    /// - 金流服務提供者選擇（策略模式）
    /// 
    /// 【設計模式】
    /// - Facade 模式：為複雜的金流系統提供統一介面
    /// - Strategy 模式：動態選擇金流提供商
    /// - Factory 模式：ToolUtility 實例化
    /// </summary>
    public partial class QPayProcessor
    {
        #region ===== 私有成員 =====

        // 配置管理（延遲初始化，線程安全）
        private static readonly Lazy<IConfiguration> s_lazyConfiguration = new Lazy<IConfiguration>(() =>
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            return builder.Build();
        });

        private static IConfiguration m_Configuration => s_lazyConfiguration.Value;

        // 商店設定
        private string m_ShopNo = string.Empty;

        // 環境 URL
        private readonly string RETURN_URL;
        private readonly string BACKEND_URL;
        private readonly string QPAY_ORGANIZATION;

        // LINE Bot 服務
        private readonly LineMessagingClient m_LineMessagingClient;
        private readonly PushUtility m_PushUtility;
        private readonly ReplyUtility m_ReplyUtility;

        // CRM 與金流服務
        private readonly ToolUtilityClass m_ToolUtilityClass;
        private readonly QPayCreatePaymentGatewayAdapter m_QPayCreatePaymentGatewayAdapter;
        private readonly OptionSetMetadataService _optionSetMetadataService;

        // 業務資料
        public Entity m_LoginContact { get; set; }

        #endregion

        #region ===== 建構函式 =====

        /// <summary>
        /// 主要建構函式（推薦使用）
        /// </summary>
        public QPayProcessor(
            QPayCreatePaymentGatewayAdapter qPayCreatePaymentGatewayAdapter = null)
        {
            // 初始化環境設定
            RETURN_URL = m_Configuration["RETURN_URL"];
            BACKEND_URL = m_Configuration["BACKEND_URL"];
            QPAY_ORGANIZATION = m_Configuration["QPAY_ORGANIZATION"];

            // 初始化 LINE Bot
            var channelAccessToken = GetLineChannelAccessToken();
            m_LineMessagingClient = new LineMessagingClient(channelAccessToken);
            m_PushUtility = new PushUtility(m_LineMessagingClient);
            m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);

            // 初始化 CRM 工具
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

            // 初始化金流服務
            m_QPayCreatePaymentGatewayAdapter = qPayCreatePaymentGatewayAdapter;

            // 初始化 OptionSet 服務
            _optionSetMetadataService = new OptionSetMetadataService(
                m_ToolUtilityClass.m_Crm2011OrganizationService,
                null,
                new MemoryCache(new MemoryCacheOptions())
            );

            // 設定商店編號
            InitializeShopNumber();
        }

        /// <summary>
        /// 相容性建構函式（用於現有 LINE Bot 整合）
        /// </summary>
        public QPayProcessor(
            LineMessagingClient aLineMessagingClient,
            PushUtility aPushUtility,
            ReplyUtility aReplyUtility,
            QPayCreatePaymentGatewayAdapter qPayCreatePaymentGatewayAdapter = null)
        {
            // 初始化環境設定
            RETURN_URL = m_Configuration["RETURN_URL"];
            BACKEND_URL = m_Configuration["BACKEND_URL"];
            QPAY_ORGANIZATION = m_Configuration["QPAY_ORGANIZATION"];

            // 使用注入的 LINE Bot 服務
            m_LineMessagingClient = aLineMessagingClient ?? throw new ArgumentNullException(nameof(aLineMessagingClient));
            m_PushUtility = aPushUtility ?? throw new ArgumentNullException(nameof(aPushUtility));
            m_ReplyUtility = aReplyUtility ?? throw new ArgumentNullException(nameof(aReplyUtility));

            // 初始化 CRM 工具
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

            // 根據配置選擇金流服務（策略模式）
            m_QPayCreatePaymentGatewayAdapter = qPayCreatePaymentGatewayAdapter;

            // 初始化 OptionSet 服務
            _optionSetMetadataService = new OptionSetMetadataService(
                m_ToolUtilityClass.m_Crm2011OrganizationService,
                null,
                new MemoryCache(new MemoryCacheOptions())
            );

            // 設定商店編號
            InitializeShopNumber();
        }

        #endregion

        #region ===== 初始化輔助方法 =====

        /// <summary>
        /// 初始化商店編號（根據環境選擇）
        /// </summary>
        private void InitializeShopNumber()
        {
            var cashEnvironment = m_Configuration["Cash_Environment"];
            
            m_ShopNo = cashEnvironment == "正式環境"
                ? m_Configuration["Sinopac:ShopNo"]
                : m_Configuration["Sandbox:ShopNo"];

            System.Diagnostics.Trace.WriteLine($"[QPayProcessor] ShopNo initialized: {m_ShopNo} (Environment: {cashEnvironment})");
        }

        /// <summary>
        /// 取得 LINE Channel Access Token
        /// </summary>
        private static string GetLineChannelAccessToken()
        {
            try
            {
                // 嘗試從組織設定讀取
                var organization = m_Configuration["CrmConnection:Organization"];
                if (!string.IsNullOrEmpty(organization))
                {
                    var configKey = char.ToUpper(organization[0]) + organization.Substring(1).ToLower();
                    var token = m_Configuration[$"LineMessaging:{configKey}:ChannelAccessToken"];
                    
                    if (!string.IsNullOrEmpty(token))
                    {
                        System.Diagnostics.Trace.WriteLine($"[QPayProcessor] LINE Token loaded for organization: {organization}");
                        return token;
                    }
                }

                // 使用預設組織
                var defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                var defaultToken = m_Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"];

                if (string.IsNullOrEmpty(defaultToken))
                {
                    System.Diagnostics.Trace.WriteLine("[QPayProcessor] 警告: LINE Channel Access Token 未設定");
                }

                return defaultToken ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] 錯誤: 讀取 LINE Token 配置失敗 - {ex.Message}");
                return string.Empty;
            }
        }

        #endregion

        #region ===== 公開屬性（供其他 partial 類別使用）=====

        /// <summary>配置實例</summary>
        protected static IConfiguration Configuration => m_Configuration;

        /// <summary>商店編號</summary>
        protected string ShopNo => m_ShopNo;

        /// <summary>返回 URL</summary>
        protected string ReturnUrl => RETURN_URL;

        /// <summary>後端 URL</summary>
        protected string BackendUrl => BACKEND_URL;

        /// <summary>組織代碼</summary>
        protected string QPayOrganization => QPAY_ORGANIZATION;

        /// <summary>CRM 工具類</summary>
        protected ToolUtilityClass ToolUtility => m_ToolUtilityClass;


        /// <summary>QPay neutral gateway create adapter</summary>
        protected QPayCreatePaymentGatewayAdapter QPayCreatePaymentGatewayAdapter => m_QPayCreatePaymentGatewayAdapter;

        /// <summary>OptionSet 服務</summary>
        protected OptionSetMetadataService OptionSetService => _optionSetMetadataService;

        /// <summary>LINE 推播工具</summary>
        protected PushUtility PushUtility => m_PushUtility;

        #endregion
    }
}
