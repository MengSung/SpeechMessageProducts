// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class DonationPaymentProcessor
// 主要成員：InitializeShopNumber、GetLineChannelAccessToken、ResolvePaymentOrganization、m_LoginContact
// 引用命名空間：ChurchReport.Models、ChurchReport.Payments、ChurchReport.Services、ChurchReport.Tools、Line.Messaging、LineMessagingProcessor、LineMessagingProcessor.Workflows、Microsoft.Extensions.Caching.Memory
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Payments;
using ChurchReport.Services;
using ChurchReport.Tools;
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
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
    /// ChurchReport 奉獻付款處理器 - 核心模組
    ///
    /// 【職責】
    /// - 初始化與依賴注入
    /// - 配置管理
    /// - LINE Bot 整合
    /// - 建立 ChurchReport 產品層付款流程所需的 CRM、LINE 與金流 adapter 依賴
    ///
    /// 【設計模式】
    /// - Facade 模式：為複雜的金流系統提供統一介面
    /// - Strategy 模式：動態選擇金流提供商
    /// - Factory 模式：ToolUtility 實例化
    /// </summary>
    public partial class DonationPaymentProcessor
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
        private readonly string PAYMENT_ORGANIZATION;

        // LINE Bot 服務
        private readonly LineMessagingClient m_LineMessagingClient;
        private readonly PushUtility m_PushUtility;
        private readonly ReplyUtility m_ReplyUtility;

        // CRM 與金流服務。
        // 這裡只依賴 ChurchReport 產品層的中性 adapter 介面；
        // 實際 provider protocol 由 SpeechMessage.Payments 決定，避免主流程再綁死 QPay/永豐命名。
        private readonly ToolUtilityClass m_ToolUtilityClass;
        private readonly IDonationPaymentCreateGatewayAdapter m_DonationPaymentCreateGatewayAdapter;
        private readonly OptionSetMetadataService _optionSetMetadataService;

        // 業務資料
        public Entity m_LoginContact { get; set; }

        #endregion

        #region ===== 建構函式 =====

        /// <summary>
        /// 主要建構函式（推薦使用）。
        /// 新程式應注入 <see cref="DonationPaymentCreateGatewayAdapter"/>，
        /// 讓奉獻付款流程維持產品中性命名，而不是再從 QPay adapter 開始。
        /// </summary>
        public DonationPaymentProcessor(
            DonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter)
            : this((IDonationPaymentCreateGatewayAdapter)donationPaymentCreateGatewayAdapter)
        {
        }

        /// <summary>
        /// 內部共用建構函式。
        /// 測試或相容 wrapper 可以提供任何實作 <see cref="IDonationPaymentCreateGatewayAdapter"/> 的 adapter，
        /// 但主要 DI 註冊仍應使用 <see cref="DonationPaymentCreateGatewayAdapter"/>。
        /// </summary>
        public DonationPaymentProcessor(
            IDonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter)
            : this(donationPaymentCreateGatewayAdapter, null, null)
        {
        }

        /// <summary>
        /// DI-friendly constructor used by ChurchReport product flows that already have shared LINE workflows.
        ///
        /// DonationPaymentProcessor still owns ChurchReport-specific CRM and donation-payment orchestration.
        /// The injected workflows own only the product-neutral LINE transport boundary. This keeps the
        /// dependency direction simple: ChurchReport decides the business message, LineMessagingProcessor
        /// sends it. Future ASP.NET Core products can reuse the same workflow classes without depending on
        /// DonationPaymentProcessor, CRM entities, MVC result handling, or ChurchReport page state.
        /// </summary>
        public DonationPaymentProcessor(
            IDonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter,
            ILineNotificationWorkflow? lineNotificationWorkflow,
            ILineReplyWorkflow? lineReplyWorkflow)
        {
            // 初始化環境設定
            RETURN_URL = m_Configuration["RETURN_URL"];
            BACKEND_URL = m_Configuration["BACKEND_URL"];
            PAYMENT_ORGANIZATION = ResolvePaymentOrganization();

            // 初始化 LINE Bot
            var channelAccessToken = GetLineChannelAccessToken();
            m_LineMessagingClient = LineMessagingProcessor.LineMessagingClientFactory.CreateOwnedClient(channelAccessToken);
            m_PushUtility = new PushUtility(m_LineMessagingClient, lineNotificationWorkflow);
            m_ReplyUtility = new ReplyUtility(m_LineMessagingClient, lineReplyWorkflow);

            // 初始化 CRM 工具
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

            // 初始化金流建單 adapter。
            // 這是 ChurchReport 與抽離後金流核心的唯一建立付款邊界；
            // 不在 processor 內直接建立 provider request，避免未來新增產品時複製一套金流細節。
            m_DonationPaymentCreateGatewayAdapter = donationPaymentCreateGatewayAdapter
                ?? throw new ArgumentNullException(nameof(donationPaymentCreateGatewayAdapter));

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
        /// 相容性建構函式（用於現有 LINE Bot 整合）。
        /// 仍然收 LINE 相關物件，是因為 ChurchReport 的奉獻通知流程屬於產品層；
        /// 這些依賴不會被搬入可重用金流核心。
        /// </summary>
        public DonationPaymentProcessor(
            LineMessagingClient aLineMessagingClient,
            PushUtility aPushUtility,
            ReplyUtility aReplyUtility,
            DonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter)
            : this(
                  aLineMessagingClient,
                  aPushUtility,
                  aReplyUtility,
                  (IDonationPaymentCreateGatewayAdapter)donationPaymentCreateGatewayAdapter)
        {
        }

        /// <summary>
        /// 內部共用建構函式，讓相容 wrapper 可用舊 adapter 轉接到同一條實作路徑。
        /// </summary>
        public DonationPaymentProcessor(
            LineMessagingClient aLineMessagingClient,
            PushUtility aPushUtility,
            ReplyUtility aReplyUtility,
            IDonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter)
        {
            // 初始化環境設定
            RETURN_URL = m_Configuration["RETURN_URL"];
            BACKEND_URL = m_Configuration["BACKEND_URL"];
            PAYMENT_ORGANIZATION = ResolvePaymentOrganization();

            // 使用注入的 LINE Bot 服務
            m_LineMessagingClient = aLineMessagingClient ?? throw new ArgumentNullException(nameof(aLineMessagingClient));
            m_PushUtility = aPushUtility ?? throw new ArgumentNullException(nameof(aPushUtility));
            m_ReplyUtility = aReplyUtility ?? throw new ArgumentNullException(nameof(aReplyUtility));

            // 初始化 CRM 工具
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

            // 根據配置選擇金流服務（策略模式）。
            // provider 的差異已封裝在 adapter/core 內，processor 只保留 ChurchReport 的產品流程。
            m_DonationPaymentCreateGatewayAdapter = donationPaymentCreateGatewayAdapter
                ?? throw new ArgumentNullException(nameof(donationPaymentCreateGatewayAdapter));

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

            System.Diagnostics.Trace.WriteLine($"[DonationPaymentProcessor] ShopNo initialized: {m_ShopNo} (Environment: {cashEnvironment})");
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
                        System.Diagnostics.Trace.WriteLine($"[DonationPaymentProcessor] LINE Token loaded for organization: {organization}");
                        return token;
                    }
                }

                // 使用預設組織
                var defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                var defaultToken = m_Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"];

                if (string.IsNullOrEmpty(defaultToken))
                {
                    System.Diagnostics.Trace.WriteLine("[DonationPaymentProcessor] 警告: LINE Channel Access Token 未設定");
                }

                return defaultToken ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[DonationPaymentProcessor] 錯誤: 讀取 LINE Token 配置失敗 - {ex.Message}");
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

        /// <summary>
        /// ChurchReport 付款組織代碼。
        ///
        /// 這個值會交給共用建單 adapter，讓 adapter 再依目前設定的 provider profile
        /// 決定要送永豐、高鉅、台新或未來新增的金流。屬性名稱刻意使用 PaymentOrganization，
        /// 不再使用 QPayOrganization，避免產品層共用建單流程看起來只屬於永豐 QPay。
        /// </summary>
        protected string PaymentOrganization => PAYMENT_ORGANIZATION;

        /// <summary>CRM 工具類</summary>
        protected ToolUtilityClass ToolUtility => m_ToolUtilityClass;


        /// <summary>
        /// ChurchReport 產品層建立付款 adapter。
        /// partial 類別只能透過這個中性屬性建單，避免新的付款流程再依賴 QPay 命名。
        /// </summary>
        protected IDonationPaymentCreateGatewayAdapter DonationPaymentCreateGatewayAdapter => m_DonationPaymentCreateGatewayAdapter;

        /// <summary>OptionSet 服務</summary>
        protected OptionSetMetadataService OptionSetService => _optionSetMetadataService;

        /// <summary>LINE 推播工具</summary>
        protected PushUtility PushUtility => m_PushUtility;

        #endregion

        /// <summary>
        /// 讀取 ChurchReport 付款組織設定。
        ///
        /// 新設定鍵是 Payment:Organization，語意是「這個產品目前使用哪個付款組織」；
        /// 舊設定鍵 QPAY_ORGANIZATION 只作為既有 appsettings 尚未遷移時的相容 fallback。
        /// 這裡只做設定鍵相容，不在 C# 型別或主要流程保留 QPay alias。
        /// </summary>
        private static string ResolvePaymentOrganization()
        {
            return m_Configuration["Payment:Organization"]
                ?? m_Configuration["QPAY_ORGANIZATION"]
                ?? string.Empty;
        }
    }
}
