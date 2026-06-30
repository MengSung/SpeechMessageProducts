using ChurchReport.Services;
using ChurchReport.Tools;
using ChurchReport.Filters;
using ChurchReport.Services.Theme;
using ChurchReport.Payments;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.ConnectionOperations;
using ChurchReport.WebServiceConnector;
using SpeechMessage.Payments.AspNetCore.DependencyInjection;
using SpeechMessage.Payments.DependencyInjection;
using SpeechMessage.Payments.Workflows;

namespace ChurchReport
{
    /// <summary>
    /// 應用程式啟動類別，負責配置服務和 HTTP 請求管道。
    /// 此類別定義了 ASP.NET Core 應用程式的啟動邏輯，包括依賴注入、路由配置等。
    /// </summary>
    public partial class Startup
    {
        private static readonly HashSet<string> AllowedThemes = new HashSet<string>(StringComparer.Ordinal)
        {
            "藍色",
            "橘色",
            "綠色",
            "粉紅色",
            "晨霧紫",
            "月光藍",
            "皇家紫金",
            "勃根地金",
            "行道靛紫",
            "陽光黃",
            "珊瑚橘"
        };

        /// <summary>
        /// 建構函式，注入配置物件。
        /// </summary>
        /// <param name="configuration">應用程式配置物件，用於讀取 appsettings.json 或其他配置來源。</param>
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            CurrentTheme = ResolveThemeName(configuration["Theme:Current"]);
            CurrentThemeCssClass = MapThemeCssClass(CurrentTheme);
        }

        /// <summary>
        /// 應用程式配置屬性，提供對配置資料的存取。
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// 目前啟用的主題名稱（藍色、橘色、綠色、粉紅色、晨霧紫、月光藍、皇家紫金、勃根地金、陽光黃、珊瑚橘、行道靛紫）。
        /// </summary>
        public string CurrentTheme { get; }

        /// <summary>
        /// 目前啟用主題對應的 CSS class。
        /// </summary>
        public string CurrentThemeCssClass { get; }

        private static string ResolveThemeName(string configuredTheme)
        {
            var normalizedTheme = configuredTheme?.Trim();
            return string.IsNullOrWhiteSpace(normalizedTheme) || !AllowedThemes.Contains(normalizedTheme)
                ? "藍色"
                : normalizedTheme;
        }

        private static string MapThemeCssClass(string themeName)
        {
            switch (themeName)
            {
                case "橘色":
                    return "theme-orange";
                case "綠色":
                    return "theme-green";
                case "粉紅色":
                    return "theme-pink";
                case "晨霧紫":
                    return "theme-mist-purple";
                case "月光藍":
                    return "theme-moon-blue";
                case "皇家紫金":
                    return "theme-royal-purple-gold";
                case "勃根地金":
                    return "theme-burgundy-gold";
                case "陽光黃":
                    return "theme-sunshine-yellow";
                case "珊瑚橘":
                    return "theme-coral-orange";
                case "行道靛紫":
                    return "theme-indigo-purple";
                case "藍色":
                default:
                    return "theme-blue";
            }
        }

        /// <summary>
        /// 配置服務的方法。此方法由運行時呼叫，用於將服務添加到依賴注入容器中。
        /// 包括 HTTP 客戶端、快取、CRM 連接池、健康檢查、MVC 配置、身份驗證等服務的註冊。
        /// </summary>
        /// <param name="services">服務集合，用於註冊應用程式所需的服務。</param>
        public void ConfigureServices(IServiceCollection services)
        {
#if DEBUG
            ChurchReport.Diagnostics.Profiling.ProfilingSwitch.Enabled =
                Configuration.GetValue<bool>("Profiling:Enabled", false);
            using var __perfConfigureServices =
                ChurchReport.Diagnostics.Profiling.StartupProfiler.Phase("ConfigureServices");
#endif

            services.AddSingleton(new ThemeSettings(CurrentTheme, CurrentThemeCssClass));
            services.AddScoped<ThemeViewDataFilter>();

            // ========================================
            // ✅ 初始化 ToolUtilityFactory 配置 (必須最先執行)
            // ========================================
            // 設定 ToolUtilityFactory 的配置物件，確保後續使用 ToolUtility 時能正確讀取 appsettings.json
            ToolUtilityNameSpace.Factory.ToolUtilityFactory.SetConfiguration(Configuration);

            // ========================================
            // 註冊 HttpClientFactory (修復記憶體洩漏)
            // ========================================
            // 使用 HttpClientFactory 來管理 HttpClient 實例，避免記憶體洩漏問題。
            // 這是最佳實務，能夠重用連接並自動處理資源清理。
            services.AddHttpClient();

            // ========================================
            // 🔧 修復：MemoryCache 添加過期策略（不限制大小，避免登入卡住）
            // ========================================
            // 配置記憶體快取，設定壓縮百分比和掃描頻率，以避免因快取大小限制導致的登入問題。
            // 不設定 SizeLimit，讓系統根據記憶體壓力自動管理。
            services.AddMemoryCache(options =>
            {
                // 不設定 SizeLimit，讓系統根據記憶體壓力自動管理
                // 這樣可以避免因快取大小限制導致的登入問題

                // 設定壓縮百分比（當記憶體壓力達到 90% 時才開始清理）
                options.CompactionPercentage = 0.10;

                // 設定掃描頻率（每 5 分鐘掃描一次過期項目）
                options.ExpirationScanFrequency = TimeSpan.FromMinutes(5);
            });

            // 註冊分散式記憶體快取，用於支援 Session 等功能。
            services.AddDistributedMemoryCache();

            // ========================================
            // 每週排程設定
            // ========================================
            // 將 appsettings.json 的 WeeklySchedule 區段綁定到設定類別，
            // 讓集中式主日計算服務可以透過統一設定取得每週第一日。
            services.Configure<WeeklyScheduleSettings>(Configuration.GetSection("WeeklySchedule"));

            // ========================================
            // ✅ Phase 5: 配置 ForwardedHeaders (Session Bleeding 防護 - 第五層)
            // ========================================
            // 配置轉發標頭中間件，確保在反向代理或負載平衡器後方能正確識別客戶端真實 IP
            // 這對於 Wi-Fi 環境下的身份追蹤至關重要
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                // 限制處理的轉發標頭數量，防止標頭偽造攻擊
                options.ForwardLimit = 2;

                // ========================================
                // ✅ P0: 生產環境不要無條件信任所有 Proxy
                // ========================================
                // 預設行為：保留 KnownNetworks/KnownProxies 的限制（較安全）。
                // 若部署在反向代理/負載平衡器後方，請在 appsettings.json 設定：
                //   ForwardedHeaders:TrustAllProxies = true
                // 或改成提供明確的已知代理 IP 清單（較推薦）。
                var trustAllProxies = Configuration.GetValue<bool>("ForwardedHeaders:TrustAllProxies", false);
                if (trustAllProxies)
                {
                    options.KnownNetworks.Clear();
                    options.KnownProxies.Clear();
                }
            });
            
            Console.WriteLine("[Startup] ✅ ForwardedHeaders 已配置（支援反向代理和負載平衡器）");

            // ========================================
            // ✅ P0: Response Caching（預設停用）
            // ========================================
            // Session Bleeding 的根因之一常見是「錯誤的代理/伺服器快取」。
            // 此專案已對動態頁面採取最嚴格 no-store，因此預設不啟用 ResponseCaching。
            // 若未來確實需要針對『匿名且不含使用者資料』的端點快取，才開啟下列設定並以路徑隔離。
            var enableResponseCaching = Configuration.GetValue<bool>("SessionBleeding:EnableResponseCaching", false);
            if (enableResponseCaching)
            {
                services.AddResponseCaching();
                Console.WriteLine("[Startup] ⚠️ ResponseCaching 已啟用（請僅用於匿名/公共資料端點）");
            }

            // ========================================
            // ✅ Phase 4.1: 註冊 Response Compression 服務
            // ========================================
            // 啟用 Brotli 和 Gzip 壓縮，減少傳輸量約 60-80%
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    new[] { 
                        "application/json", 
                        "text/html", 
                        "application/javascript",
                        "text/css",
                        "image/svg+xml"
                    });
            });

            // 設定 Brotli 壓縮等級（Optimal 平衡壓縮率和速度）
            services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Optimal;
            });

            // 設定 Gzip 壓縮等級
            services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Optimal;
            });

            // ========================================
            // ✅ Phase 2.2: 註冊應用程式快取服務
            // ========================================
            // 註冊 ICacheService 為 Singleton，全應用程式共用一個快取服務實例
            services.AddSingleton<ChurchReport.Services.Caching.ICacheService, ChurchReport.Services.Caching.CacheService>();

            // ========================================
            // ✅ Phase 5.2: 註冊 StringBuilder 物件池
            // ========================================
            // 減少字串處理時的記憶體分配
            services.AddSingleton<ChurchReport.Services.Performance.IStringBuilderPool, ChurchReport.Services.Performance.StringBuilderPool>();

#if DEBUG
            // ========================================
            // ✅ 最終驗證: 註冊效能監控服務（僅 DEBUG 模式）
            // ========================================
            // 用於監控應用程式效能指標和驗證效能目標
            // ⚠️ Release 版本不會包含此服務
            services.AddSingleton<ChurchReport.Services.Performance.IPerformanceMonitor, ChurchReport.Services.Performance.PerformanceMonitor>();
            
            // ========================================
            // ✅ Phase 8: 註冊 Session 監控服務（僅 DEBUG 模式）
            // ========================================
            // 追蹤活躍 Session 數量和記憶體使用
            services.AddSingleton<ChurchReport.Services.Monitoring.ISessionMonitorService, ChurchReport.Services.Monitoring.SessionMonitorService>();
            services.AddHostedService(sp => (ChurchReport.Services.Monitoring.SessionMonitorService)sp.GetRequiredService<ChurchReport.Services.Monitoring.ISessionMonitorService>());
            
            Console.WriteLine("[Startup] ✅ 效能監控服務已註冊（DEBUG 模式）");
            Console.WriteLine("[Startup] ✅ Session 監控服務已註冊（DEBUG 模式）");
#endif

            // ========================================
            // 註冊 CRM 連接池 (Singleton 模式)
            // ========================================
            // 註冊 CRM 連接池服務，使用 Singleton 模式確保全應用程式共用一個連接池實例。
            // 從配置中讀取 CRM 連接參數，包括伺服器 URL、認證資訊和連接池設定。
            services.AddSingleton<ICrmConnectionPool>(sp =>
            {
                // 建立 CRM 連接服務實例。
                var connectionService = new CrmConnectionService();

                // 從配置中取得 CRM 連接相關設定區段。
                var crmConfig = Configuration.GetSection("CrmConnection");

                // 讀取伺服器 URL，若配置中未設定則使用預設值。
                var serverUrl = crmConfig["ServerUrl"] ?? "https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc";

                // 讀取使用者名稱，若配置中未設定則使用預設值。
                var username = crmConfig["Username"] ?? @"SPEECHMESSAGE\Administrator";

                // 讀取密碼：避免硬編碼預設密碼（Hardcoded secret）。
                // 建議來源：Environment Variables / Secret Manager / Key Vault
                var password = crmConfig["Password"]
                               ?? Environment.GetEnvironmentVariable("CRM_PASSWORD");
                if (string.IsNullOrWhiteSpace(password))
                {
                    throw new InvalidOperationException(
                        "CRM password is not configured. Set CrmConnection:Password or environment variable CRM_PASSWORD.");
                }

                // 讀取最小連接池大小，若配置中未設定則使用預設值 3。
                var minPoolSize = int.TryParse(crmConfig["MinPoolSize"], out var minSize) ? minSize : 3;

                // 讀取最大連接池大小，若配置中未設定則使用預設值 20。
                var maxPoolSize = int.TryParse(crmConfig["MaxPoolSize"], out var maxSize) ? maxSize : 20;

                // 讀取連接超時秒數，若配置中未設定則使用預設值 30。
                var connectionTimeoutSeconds = int.TryParse(crmConfig["ConnectionTimeoutSeconds"], out var connTimeout) ? connTimeout : 30;

                // 讀取閒置超時分鐘數，若配置中未設定則使用預設值 10。
                var idleTimeoutMinutes = int.TryParse(crmConfig["IdleTimeoutMinutes"], out var idleTimeout) ? idleTimeout : 10;

                // 建立並返回 CRM 連接池實例，使用讀取到的配置參數。
                return new CrmConnectionPool(
                    connectionService,
                    serverUrl,
                    username,
                    password,
                    minPoolSize: minPoolSize,      // 最小連接數：從配置讀取
                    maxPoolSize: maxPoolSize,     // 最大連接數：從配置讀取
                    connectionTimeout: TimeSpan.FromSeconds(connectionTimeoutSeconds),  // 連接超時：從配置讀取
                    idleTimeout: TimeSpan.FromMinutes(idleTimeoutMinutes)         // 閒置超時：從配置讀取
                );
            });

            // ========================================
            // 🆕 新增：Health Checks（健康檢查）
            // ========================================
            // 註冊健康檢查服務，用於監控應用程式的健康狀態。
            // 包括自我檢查和記憶體使用檢查。
            services.AddHealthChecks()
                .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Application is running"))  // 應用程式運行檢查
                .AddCheck("memory", () =>  // 記憶體使用檢查
                {
                    var process = Process.GetCurrentProcess();
                    var memoryMB = process.PrivateMemorySize64 / 1024 / 1024;
                    var maxMemoryMB = 2048; // 2 GB 最大記憶體限制

                    if (memoryMB > maxMemoryMB)
                    {
                        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                            $"Memory usage too high: {memoryMB} MB");
                    }

                    return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                        $"Memory usage normal: {memoryMB} MB");
                });

            // 配置 MVC 服務，禁用端點路由以使用傳統 MVC 路由。
            // 並設定 Newtonsoft.Json 序列化選項。
            services
                .AddMvc(options =>
                {
                    options.EnableEndpointRouting = false;

                    options.Filters.Add<ThemeViewDataFilter>();
                    
                    // ========================================
                    // ✅ Phase 3.1: 註冊全域無快取過濾器 (Session Bleeding 防護)
                    // ========================================
                    // 防止 Session Bleeding（會話串連）問題
                    // 確保所有 Controller Action 都不會被中間層代理伺服器或瀏覽器快取
                    options.Filters.Add<ChurchReport.Filters.StrictNoCacheFilter>();

#if DEBUG
                    options.Filters.Add<ChurchReport.Filters.PerfTimingActionFilter>();
#endif
                    
                    // ========================================
                    // ✅ Phase 3.2: 註冊全域 ResponseCache 屬性 (Session Bleeding 防護 - Step 2)
                    // ========================================
                    // 雙重防護：除了 StrictNoCacheFilter，再加上 ResponseCacheAttribute
                    // 確保從 MVC 層面也禁止快取
                    options.Filters.Add(new ResponseCacheAttribute
                    {
                        NoStore = true,
                        Location = ResponseCacheLocation.None,
                        Duration = 0
                    });
                    
                    Console.WriteLine("[Startup] ✅ StrictNoCacheFilter 已註冊為全域過濾器");
                    Console.WriteLine("[Startup] ✅ ResponseCacheAttribute 已註冊為全域過濾器 (NoStore=true)");
                    System.Diagnostics.Debug.WriteLine($"[Startup] ========================================");
                    System.Diagnostics.Debug.WriteLine($"[Startup] ✅ Session Bleeding 雙重防護已啟用");
                    System.Diagnostics.Debug.WriteLine($"[Startup] 第一層: 全站無快取中介軟體 (Middleware 層)");
                    System.Diagnostics.Debug.WriteLine($"[Startup] 第二層: ResponseCacheAttribute (MVC 層)");
                    System.Diagnostics.Debug.WriteLine($"[Startup] 第三層: StrictNoCacheFilter (Action 層)");
                    System.Diagnostics.Debug.WriteLine($"[Startup] 所有 Controller Action 將套用:");
                    System.Diagnostics.Debug.WriteLine($"[Startup]   - Cache-Control: no-store, no-cache");
                    System.Diagnostics.Debug.WriteLine($"[Startup]   - Pragma: no-cache");
                    System.Diagnostics.Debug.WriteLine($"[Startup]   - Expires: -1");
                    System.Diagnostics.Debug.WriteLine($"[Startup] ========================================");
                })
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                });

            // ========================================
            // 註冊 ToolUtility 服務 (Singleton 模式)
            // ========================================
            // 註冊 ToolUtility 相關服務，使用擴充方法進行批量註冊。
            services.AddToolUtility();

#if DEBUG
            {
                var providerDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IToolUtilityProvider));
                if (providerDescriptor != null)
                {
                    services.Remove(providerDescriptor);
                    services.Add(new ServiceDescriptor(
                        typeof(IToolUtilityProvider),
                        sp =>
                        {
                            IToolUtilityProvider inner;
                            if (providerDescriptor.ImplementationFactory != null)
                            {
                                inner = (IToolUtilityProvider)providerDescriptor.ImplementationFactory(sp);
                            }
                            else if (providerDescriptor.ImplementationInstance != null)
                            {
                                inner = (IToolUtilityProvider)providerDescriptor.ImplementationInstance;
                            }
                            else if (providerDescriptor.ImplementationType != null)
                            {
                                inner = (IToolUtilityProvider)ActivatorUtilities.CreateInstance(
                                    sp, providerDescriptor.ImplementationType);
                            }
                            else
                            {
                                throw new InvalidOperationException("Unsupported IToolUtilityProvider registration.");
                            }

                            var http = sp.GetRequiredService<IHttpContextAccessor>();
                            return new ChurchReport.Diagnostics.Profiling.TimedToolUtilityProvider(inner, http);
                        },
                        providerDescriptor.Lifetime));
                }
            }
#endif

            // ========================================
            // ✅ Phase 3.2: 註冊 CRM 快取服務
            // ========================================
            // 註冊 CRM 快取服務為 Singleton，全應用程式共用一個快取實例
            services.AddSingleton<ToolUtility.Caching.CrmCacheService>(sp =>
            {
                var memoryCache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                var distributedCache = sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ToolUtility.Caching.CrmCacheService>>();
                return new ToolUtility.Caching.CrmCacheService(memoryCache, distributedCache, logger);
            });

            // ========================================
            // ✅ Phase 3.2: 註冊 ChurchListDataProcessor（使用快取）
            // ========================================
            services.AddScoped<ChurchReport.WebServiceConnector.ChurchListDataProcessor>(sp =>
            {
                var cacheService = sp.GetRequiredService<ToolUtility.Caching.CrmCacheService>();
                return new ChurchReport.WebServiceConnector.ChurchListDataProcessor(cacheService);
            });


            // 註冊 HttpContext 存取器服務，用於在非控制器類別中存取 HTTP 上下文。
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            // ========================================
            // 註冊 MyPay 相關服務
            // ========================================
            // 註冊 MyPay 相關的服務類別，使用 Scoped 生命週期（每個請求一個實例）。
            services.AddScoped<ChurchReport.Services.MyPayMessageBuilder>();
            services.AddScoped<ChurchReport.Services.MyPayFeeTypeHelper>();
            services.AddScoped<ChurchReport.Services.MyPayLogger>();
            services.AddScoped<ChurchReport.Services.MyPayCrmService>();
            services.AddScoped<ChurchReport.Services.MyPayNotificationService>();

            // ========================================
            // 註冊抽離後的通用金流核心與 ChurchReport adapter
            // ========================================
            // SpeechMessage.Payments 擁有永豐、高鉅、台新的 provider protocol、加解密、簽章、
            // request/response mapping 與 callback parsing。ChurchReport 只註冊薄 adapter，
            // 負責把 ASP.NET request、CRM/LINE 產品流程與抽離後的付款核心接回來。
            services.AddSpeechMessagePayments(Configuration.GetSection("Payment"));
            services.AddSpeechMessagePaymentAspNetCore();
            services.AddScoped<ChurchReportPaymentProfileResolver>();
            services.AddScoped<IPaymentRecordUpdater, ChurchReportPaymentRecordUpdater>();
            services.AddScoped<IPaymentPayerNotifier, ChurchReportPaymentPayerNotifier>();
            services.AddScoped<IDonationPaymentReturnWorkflow, DonationPaymentReturnWorkflow>();
            services.AddScoped<IQPayReturnWorkflow, QPayReturnWorkflow>();
            services.AddScoped<IDonationPaymentProductWorkflowDispatcher, DonationPaymentProductWorkflowDispatcher>();
            services.AddScoped<IQPayProductWorkflowDispatcher, QPayProductWorkflowDispatcher>();
            // ChurchReport 產品層的建單 adapter，以中性介面供 controller/context/manager 使用。
            // 舊 QPayCreatePaymentGatewayAdapter 只保留給相容 constructor，不作為新流程的主要入口。
            services.AddScoped<DonationPaymentCreateGatewayAdapter>();
            services.AddScoped<IDonationPaymentCreateGatewayAdapter>(sp =>
                sp.GetRequiredService<DonationPaymentCreateGatewayAdapter>());
            services.AddScoped<QPayCreatePaymentGatewayAdapter>();

#if DEBUG
            // ========================================
            // ✅ Phase 4: 註冊身份審計清理服務 (Session Bleeding 防護 - 記憶體管理)
            // ========================================
            // 定期清理 IdentityAuditMiddleware 的追蹤資料，防止記憶體洩漏
            // 僅在 DEBUG 模式下啟用
            services.AddHostedService<ChurchReport.Middleware.IdentityAuditCleanupService>();
            Console.WriteLine("[Startup] ✅ IdentityAuditCleanupService 已註冊（定期清理追蹤資料）");
#endif

            // ========================================
            // ✅ Phase 3.3: Session 配置與安全性強化 (Session Bleeding 防護 - Step 3)
            // ========================================
            // 配置 Session 服務，設定閒置超時、Cookie 選項等
            services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                
                // ========================================
                // ✅ Phase 3.3: 強化 Session Cookie 安全性 (Session Bleeding 防護)
                // ========================================
                // 防止 Session Cookie 被 Proxy 共用或竊取
                // ✅ P1: SecurePolicy 依環境調整，避免開發環境（HTTP）無法正常工作
#if DEBUG
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
#else
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
#endif

                // ========================================
                // ✅ P0: SameSite 設為 Lax 以支援 LINE LIFF 登入
                // ========================================
                // 原本設為 Strict，但會阻擋 LINE LIFF 的跨站請求
                // 改為 Lax：
                // - 仍然防止大部分 CSRF 攻擊
                // - 允許從外部網站導航過來時攜帶 Cookie（例如 LIFF 登入）
                // - 不允許在跨站的 POST/PUT/DELETE 請求中發送 Cookie
                options.Cookie.SameSite = SameSiteMode.Lax;  // 改為 Lax 以支援 LINE LIFF
                
                options.IOTimeout = TimeSpan.FromSeconds(30);
            });
            
            Console.WriteLine("[Startup] ✅ Session Cookie 安全性已強化（Session Bleeding 防護）");
            Console.WriteLine("[Startup]   - HttpOnly: true (防 XSS)");
            Console.WriteLine("[Startup]   - SecurePolicy: Always (防 MITM，需 HTTPS)");
            Console.WriteLine("[Startup]   - SameSite: Lax (防 CSRF，但允許 LINE LIFF 登入)");
            System.Diagnostics.Debug.WriteLine($"[Startup] ========================================");
            System.Diagnostics.Debug.WriteLine($"[Startup] ✅ Session Cookie 三層安全防護");
            System.Diagnostics.Debug.WriteLine($"[Startup]   1. HttpOnly → JavaScript 無法存取");
            System.Diagnostics.Debug.WriteLine($"[Startup]   2. Secure → 只能在 HTTPS 傳輸");
            System.Diagnostics.Debug.WriteLine($"[Startup]   3. SameSite.Lax → 防 CSRF，但允許合法的跨站導航（LINE LIFF）");
            System.Diagnostics.Debug.WriteLine($"[Startup] ========================================");

            // 配置身份驗證服務，使用 Cookie 身份驗證方案。
            services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Login";
                    options.LogoutPath = "/Logout";

                    // 新版 API：需要設定 options.Cookie.Expiration，但用 ExpireTimeSpan 即可替代
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);

                    // ========================================
                    // ✅ P0: 強化 Authentication Cookie（避免與 Session Cookie 混淆）
                    // ========================================
                    // Session Cookie 與 Authentication Cookie 不能同名。
                    // 否則會造成 Cookie 覆蓋/混淆，進而引發身份錯亂風險。
                    options.Cookie.Name = ".ChurchReport.Auth";
                    options.Cookie.HttpOnly = true;

#if DEBUG
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
#else
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
#endif

                    // SameSite 設為 Lax：兼顧安全與常見第三方登入/回跳流程相容性。
                    // 若確定沒有跨站登入流程，可改 Strict。
                    options.Cookie.SameSite = SameSiteMode.Lax;

                    options.AccessDeniedPath = "/Login";
                    options.ReturnUrlParameter = "returnUrl";
                });

            // 📁 Startup.cs - ConfigureServices 方法
            services.AddScoped<ChurchListDataProcessor>();
            services.AddScoped<PersonalInfomatioManager>();
            services.AddScoped<DownloadListManager>();
            services.AddScoped<WeeklyReportManager>();

            // ========================================
            // ✅ 註冊 InMemoryDataContext (Scoped 生命週期)
            // ========================================
            // 註冊為 Scoped，確保每個請求有獨立的記憶體上下文
            // 避免靜態依賴，提升可測試性和可維護性
            services.AddScoped<ChurchReport.Models.IInMemoryDataContext, ChurchReport.Models.InMemoryDataContextSmallGroup>();

            // ========================================
            // ✅ 註冊 SmallGroupCacheManager (Scoped 生命週期)
            // ========================================
            // 註冊小組快取管理服務，用於管理小組相關資料的快取
            // 提供統一的快取清除和查詢介面，方便單元測試模擬
            services.AddScoped<ChurchReport.Services.Caching.ISmallGroupCacheManager, ChurchReport.Services.Caching.SmallGroupCacheManager>();

            Console.WriteLine("[Startup] ✅ IInMemoryDataContext 已註冊為 Scoped 服務");
            Console.WriteLine("[Startup] ✅ ISmallGroupCacheManager 已註冊為 Scoped 服務");
        }

        /// <summary>
        /// 配置 HTTP 請求管道的方法。此方法由運行時呼叫，用於設定中間件管道。
        /// 包括異常處理、健康檢查、靜態檔案、Session、身份驗證和路由配置。
        /// </summary>
        /// <param name="app">應用程式建構器，用於配置中間件。</param>
        /// <param name="env">Web 主機環境，用於判斷開發或生產環境。</param>
        /// <param name="loggerFactory">日誌工廠，用於建立日誌記錄器。</param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
#if DEBUG
            using var __perfConfigure =
                ChurchReport.Diagnostics.Profiling.StartupProfiler.Phase("Configure");
#endif

            // ========================================
            // 初始化每週第一日設定
            // ========================================
            // 舊有程式碼大量透過靜態類別直接計算主日日期，
            // 因此在應用程式啟動階段先將設定載入到 Provider，
            // 讓所有舊類別都能共用相同的週起始日規則。
            WeeklyScheduleSettings weeklyScheduleSettings = Configuration
                .GetSection("WeeklySchedule")
                .Get<WeeklyScheduleSettings>() ?? new WeeklyScheduleSettings();
            WeeklyScheduleProvider.Initialize(weeklyScheduleSettings.GetFirstDayOfWeek());

            // ========================================
            // ✅ Phase 5: 使用 ForwardedHeaders 中間件（必須最先執行）
            // ========================================
            // 必須在所有其他中間件之前執行，以確保後續中間件能正確取得客戶端真實 IP
            app.UseForwardedHeaders();
            Console.WriteLine("[Startup] ✅ ForwardedHeaders 中間件已啟用（識別真實客戶端 IP）");

            // ========================================
            // 🆕 在 Development 環境或啟用 Trace 設定時註冊 Trace Logger Provider
            // 讓 ILogger 的輸出也能寫入 Trace.log
            // ========================================
            var enableTrace = Configuration.GetValue<bool>("EnableTrace", env.IsDevelopment());
            if (enableTrace)
            {
                try
                {
                    loggerFactory.AddProvider(new ChurchReport.Logging.TraceLoggerProvider());
                    Console.WriteLine("[Startup] ✅ TraceLoggerProvider 已註冊");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Startup] ❌ TraceLoggerProvider 註冊失敗: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("[Startup] ⚠️ TraceLoggerProvider 未啟用（Release 或 EnableTrace=false）");
            }

            // ========================================
            // ✅ Phase 3.0: 全站無快取中介軟體（Session Bleeding 防護 - 最優先執行）
            // ========================================
            // 這是防止 Session Bleeding 的第一道也是最重要的防線
            // 必須在所有其他中介軟體之前執行，確保每個回應都帶有正確的快取控制標頭
            app.Use(async (context, next) =>
            {
                // 設定最嚴格的快取策略：禁止瀏覽器與任何中間代理伺服器存儲內容
                context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                context.Response.Headers["Pragma"] = "no-cache";
                context.Response.Headers["Expires"] = "0";

                // ========================================
                // ✅ Web Cache Deception 防護 - 安全標頭
                // ========================================
                // X-Content-Type-Options: nosniff - 防止瀏覽器嗅探內容類型
                // 這能阻止 CDN/Proxy 將動態 HTML 回應誤判為靜態資源
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                
                // ⚠️ 重要：告訴所有 Proxy「不同 Cookie = 不同內容，不准共用」
                // 這是解決 Session Bleeding 的關鍵設定！
                // ✅ P1: 不覆蓋既有 Vary（例如 Accept-Encoding），改用合併策略
                if (context.Response.Headers.TryGetValue("Vary", out var varyValues))
                {
                    var vary = varyValues.ToString();
                    if (!vary.Contains("Cookie", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.Headers["Vary"] = string.IsNullOrWhiteSpace(vary) ? "Cookie" : $"{vary}, Cookie";
                    }
                }
                else
                {
                    context.Response.Headers["Vary"] = "Cookie";
                }

                await next();
            });
            
            Console.WriteLine("[Startup] ========================================");
            Console.WriteLine("[Startup] ✅ 全站無快取中介軟體已啟用（Session Bleeding 防護）");
            Console.WriteLine("[Startup]   - Cache-Control: no-store, no-cache, must-revalidate, max-age=0");
            Console.WriteLine("[Startup]   - Pragma: no-cache");
            Console.WriteLine("[Startup]   - Expires: 0");
            Console.WriteLine("[Startup]   - Vary: Cookie (防止 Proxy 共用不同使用者的回應)");
            Console.WriteLine("[Startup] ========================================");

            // 異常處理中間件
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                // BrowserLink 在新版 ASP.NET Core 已不支援，移除 app.UseBrowserLink();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            // ========================================
            // ✅ Web Cache Deception 防護中間件
            // ========================================
            // 偵測並阻擋在動態路由後附加靜態副檔名的攻擊
            // 例如：/Home/IntegrateView/evil.css → 404
            // 必須在 UseStaticFiles 之前執行
            app.UseMiddleware<ChurchReport.Middleware.WebCacheDeceptionMiddleware>();
            Console.WriteLine("[Startup] ✅ Web Cache Deception 防護中間件已啟用");

            // ========================================
            // 🆕 新增：Health Check 端點
            // ========================================
            // 啟用健康檢查端點，路徑為 /health。
            app.UseHealthChecks("/health");

#if DEBUG
            // ========================================
            // ✅ 最終驗證: 啟用效能監控中介軟體（僅 DEBUG 模式）
            // ========================================
            // 追蹤每個請求的效能指標
            // ⚠️ Release 版本不會包含此中介軟體
            app.UseMiddleware<ChurchReport.Middleware.PerformanceMonitoringMiddleware>();
            Console.WriteLine("[Startup] ✅ 效能監控中介軟體已啟用（DEBUG 模式）");
#else
            Console.WriteLine("[Startup] ⚠️ 效能監控功能已排除（RELEASE 模式）");
#endif

            // ========================================
            // ✅ Phase 4.1: 啟用 Response Compression 中介軟體
            // ========================================

            // 必須在其他中介軟體之前加入，以確保所有回應都能被壓縮
            app.UseResponseCompression();

            // ========================================
            // ✅ Phase 4.4: 靜態檔案快取優化
            // ========================================
            // 為靜態檔案加入長期快取標頭，減少重複請求
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    // 設定靜態檔案快取 1 年（對於有版本控制的檔案）
                    // 瀏覽器將快取這些檔案，減少伺服器負載
                    const int durationInSeconds = 60 * 60 * 24 * 365; // 1 年
                    ctx.Context.Response.Headers.Append(
                        "Cache-Control", $"public,max-age={durationInSeconds}");
                }
            });

#if DEBUG
            app.UseMiddleware<ChurchReport.Middleware.PerfProfilingMiddleware>();
#endif
            
            // ========================================
            // ✅ Phase 2.4: 啟用 Response Caching 中介軟體
            // ========================================
            // 必須在 UseSession 之前加入，以支援 [ResponseCache] 的 VaryByQueryKeys
            var enableResponseCaching = Configuration.GetValue<bool>("SessionBleeding:EnableResponseCaching", false);
            if (enableResponseCaching)
            {
                app.UseResponseCaching();
                Console.WriteLine("[Startup] ⚠️ ResponseCaching 中介軟體已啟用（請僅用於匿名/公共資料端點）");
            }
            
            app.UseSession();      // 啟用 Session 中間件

            // ========================================
            // ✅ P0: Session 驗證中間件（Session Bleeding 防護 - 核心層）
            // ========================================
            // 必須在 UseSession 之後、UseAuthentication 之前加入
            // 驗證每個請求的 Session 合法性，防止跨用戶 Session 洩漏
            // 
            // 防護目標：
            // 1. 防止「A 登入 WiFi → B 登入 WiFi 看到 A 網頁」
            // 2. 防止 Session 被後登入的人繼承/共用
            // 3. 防止 Session Hijacking（會話劫持）
            app.UseMiddleware<ChurchReport.Middleware.SessionValidationMiddleware>();
            Console.WriteLine("[Startup] ✅ Session 驗證中間件已啟用（Session Bleeding 防護 - 核心層）");
            Console.WriteLine("[Startup]   - 驗證 User-Agent 一致性（防劫持）");
            Console.WriteLine("[Startup]   - 追蹤真實 IP 變化（審計用）");
            Console.WriteLine("[Startup]   - 防止跨用戶 Session 洩漏");

#if DEBUG
            // ========================================
            // ✅ Phase 8: 啟用 Session 監控中介軟體（僅 DEBUG 模式）
            // ========================================
            // 必須在 UseSession 之後加入，以確保 Session 已可用
            app.UseMiddleware<ChurchReport.Middleware.SessionMonitoringMiddleware>();
            Console.WriteLine("[Startup] ✅ Session 監控中介軟體已啟用（DEBUG 模式）");
#endif

            app.UseAuthentication();  // 啟用身份驗證中間件

            // ========================================
            // ✅ LINE Mini App 環境偵測中間件
            // ========================================
            // 偵測請求是否來自 LINE LIFF Browser，結果存入 HttpContext.Items["IsLineMiniApp"]
            // 📖 詳細說明：文件\Line Mini App\好牧人-LINE-Mini-App-導入佈署步驟.md 第八章
            app.UseMiddleware<ChurchReport.Middleware.MiniAppDetectionMiddleware>();
            Console.WriteLine("[Startup] ✅ LINE Mini App 環境偵測中間件已啟用");

#if DEBUG
            // ========================================
            // ✅ Phase 4: 啟用身份審計中介軟體（Session Bleeding 防護 - 監控層）
            // ========================================
            // 即時偵測並記錄身份混淆問題
            // 必須在 UseAuthentication 之後，以確保身份驗證資訊可用
            app.UseMiddleware<ChurchReport.Middleware.IdentityAuditMiddleware>();
            Console.WriteLine("[Startup] ✅ 身份審計中介軟體已啟用（Session Bleeding 監控）");
#endif


            // 使用舊式路由 (已關閉 Endpoint Routing)
            app.UseMvc(routes =>
            {
                // 根路由：預設導向登入頁面
                routes.MapRoute(
                    name: "root",
                    template: string.Empty,
                    defaults: new { controller = "Authentication", action = "Login" });

                // QPay 登入路由
                routes.MapRoute(
                    name: "qpaylogin",
                    template: "Home/QPayLogin",
                    defaults: new { controller = "Home", action = "QPayLogin" });

                routes.MapRoute(
                    name: "processqpaylogin",
                    template: "Home/ProcessQPayLogin",
                    defaults: new { controller = "Home", action = "ProcessQPayLogin" });

                // 登入相關路由
                routes.MapRoute(
                    name: "login",
                    template: "Login",
                    defaults: new { controller = "Authentication", action = "Login" });

                routes.MapRoute(
                    name: "authlogin",
                    template: "Authentication/Login",
                    defaults: new { controller = "Authentication", action = "Login" });

                // 登出路由
                routes.MapRoute(
                    name: "logout",
                    template: "Logout",
                    defaults: new { controller = "Authentication", action = "Logout" });

                // Line 登入路由
                routes.MapRoute(
                    name: "linelogin",
                    template: "Authentication/LineIdLoginView/{LineIdLoginViewPatameter}",
                    defaults: new { controller = "Authentication", action = "LineIdLoginView" });

                // 隱私政策頁面（LINE Mini App 審核必備，必須可公開存取）
                routes.MapRoute(
                    name: "privacy",
                    template: "Privacy",
                    defaults: new { controller = "Authentication", action = "Privacy" });

                // 小組相關路由
                routes.MapRoute(
                    name: "multigroup",
                    template: "SmallGroup/MultiGroupView/{LoginParameter?}",
                    defaults: new { controller = "SmallGroup", action = "MultiGroupView" });

                routes.MapRoute(
                    name: "integrate",
                    template: "SmallGroup/IntegrateView/{LoginParameter?}",
                    defaults: new { controller = "SmallGroup", action = "IntegrateView" });

                routes.MapRoute(
                    name: "smallgroupreport",
                    template: "SmallGroup/SmallGroupReportView/{LoginParameter?}",
                    defaults: new { controller = "SmallGroup", action = "SmallGroupReportView" });

                // 設備路由
                routes.MapRoute(
                    name: "equipmentview",
                    template: "Equipment/EquipmentView",
                    defaults: new { controller = "Equipment", action = "EquipmentView" });

                // 新人相關路由
                routes.MapRoute(
                    name: "addnewperson",
                    template: "NewPerson/NewPerson",
                    defaults: new { controller = "NewPerson", action = "NewPerson" });

                routes.MapRoute(
                    name: "newpersonfollowup",
                    template: "NewPerson/FollowUpView",
                    defaults: new { controller = "NewPerson", action = "NewPersonFollowUpView" });

                // 個人相關路由
                routes.MapRoute(
                    name: "personalreport",
                    template: "Personal/Report",
                    defaults: new { controller = "Personal", action = "PersonalReport" });

                routes.MapRoute(
                    name: "personalinfo",
                    template: "Personal/InfomationView",
                    defaults: new { controller = "Personal", action = "PersonalInfomationView" });

                routes.MapRoute(
                    name: "maintainpersonalinfo",
                    template: "Personal/MaintainInfomationView",
                    defaults: new { controller = "Personal", action = "MaintainPersonInfomationView" });

                // 排程相關路由
                routes.MapRoute(
                    name: "scheduler",
                    template: "Scheduler/{ScheduleType}",
                    defaults: new { controller = "Scheduler", action = "Scheduler" });

                routes.MapRoute(
                    name: "schedulerview",
                    template: "Scheduler/SchedulerView/{SchedulerViewPatameter}",
                    defaults: new { controller = "Scheduler", action = "SchedulerView" });

                // 奉獻相關路由
                routes.MapRoute(
                    name: "qpayview",
                    template: "Dedication/QPayView/{LineId?}",
                    defaults: new { controller = "Dedication", action = "QPayView" });

                routes.MapRoute(
                    name: "dedicationfeeview",
                    template: "Dedication/DedicationFeeView",
                    defaults: new { controller = "Dedication", action = "DedicationFeeView" });

                routes.MapRoute(
                    name: "dedicationfeeviewweb",
                    template: "Dedication/DedicationFeeViewWeb",
                    defaults: new { controller = "Dedication", action = "DedicationFeeViewWeb" });

                routes.MapRoute(
                    name: "keyindedicationfeeview",
                    template: "Dedication/KeyInDedicationFeeView",
                    defaults: new { controller = "Dedication", action = "KeyInDedicationFeeView" });

                routes.MapRoute(
                    name: "dedicationlinelogin",
                    template: "Dedication/DediationLineLoginView/{LineIdLoginViewPatameter}",
                    defaults: new { controller = "Dedication", action = "DediationLineLoginView" });

                // 奉獻審核路由
                routes.MapRoute(
                    name: "auditviewline",
                    template: "DedicationAudit/AuditViewLine",
                    defaults: new { controller = "DedicationAudit", action = "DedicationFeeAuditViewLine" });

                routes.MapRoute(
                    name: "auditviewweb",
                    template: "DedicationAudit/AuditViewWeb",
                    defaults: new { controller = "DedicationAudit", action = "DedicationFeeAuditViewWeb" });

                // QR 碼相關路由
                // 課程 QR Code 路由（支援多種 URL 格式）
                routes.MapRoute(
                    name: "qrcodeview_short",
                    template: "QrCodeView",
                    defaults: new { controller = "QrCode", action = "QrCodeView" });

                routes.MapRoute(
                    name: "qrcodeview_home",
                    template: "Home/QrCodeView",
                    defaults: new { controller = "QrCode", action = "QrCodeView" });

                routes.MapRoute(
                    name: "qrcodeview_home_param",
                    template: "Home/QrCodeView/{QrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "QrCodeView" });

                routes.MapRoute(
                    name: "qrcodeview",
                    template: "QrCode/CourseView/{QrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "QrCodeView" });

                // 問卷調查 QR Code 路由（支援多種 URL 格式）
                routes.MapRoute(
                    name: "pollqrcodeview_short",
                    template: "PollQrCodeView",
                    defaults: new { controller = "QrCode", action = "PollQrCodeView" });

                routes.MapRoute(
                    name: "pollqrcodeview_home",
                    template: "Home/PollQrCodeView",
                    defaults: new { controller = "QrCode", action = "PollQrCodeView" });

                routes.MapRoute(
                    name: "pollqrcodeview_home_param",
                    template: "Home/PollQrCodeView/{PollQrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "PollQrCodeView" });

                routes.MapRoute(
                    name: "pollqrcodeview",
                    template: "QrCode/PollView/{PollQrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "PollQrCodeView" });

                // 小組聚會 QR Code 路由（支援多種 URL 格式）
                routes.MapRoute(
                    name: "smallgroupqrcodeview_short",
                    template: "SmallGroupQrCodeView",
                    defaults: new { controller = "QrCode", action = "SmallGroupQrCodeView" });

                routes.MapRoute(
                    name: "smallgroupqrcodeview_home",
                    template: "Home/SmallGroupQrCodeView",
                    defaults: new { controller = "QrCode", action = "SmallGroupQrCodeView" });

                routes.MapRoute(
                    name: "smallgroupqrcodeview_home_param",
                    template: "Home/SmallGroupQrCodeView/{QrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "SmallGroupQrCodeView" });

                routes.MapRoute(
                    name: "smallgroupqrcodeview",
                    template: "QrCode/SmallGroupView/{QrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "SmallGroupQrCodeView" });

                // 主日 QR Code 路由（支援多種 URL 格式）
                routes.MapRoute(
                    name: "sundayqrcodeview_short",
                    template: "SundayQrCodeView",
                    defaults: new { controller = "QrCode", action = "SundayQrCodeView" });

                routes.MapRoute(
                    name: "sundayqrcodeview_home",
                    template: "Home/SundayQrCodeView",
                    defaults: new { controller = "QrCode", action = "SundayQrCodeView" });

                routes.MapRoute(
                    name: "sundayqrcodeview_home_param",
                    template: "Home/SundayQrCodeView/{QrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "SundayQrCodeView" });

                routes.MapRoute(
                    name: "sundayqrcodeview",
                    template: "QrCode/SundayView/{QrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "SundayQrCodeView" });

                // 個人 QR Code 路由（支援多種 URL 格式）
                routes.MapRoute(
                    name: "personalqrcodeview_short",
                    template: "PersonalQrCodeView",
                    defaults: new { controller = "QrCode", action = "PersonalQrCodeView" });

                routes.MapRoute(
                    name: "personalqrcodeview_home",
                    template: "Home/PersonalQrCodeView",
                    defaults: new { controller = "QrCode", action = "PersonalQrCodeView" });

                routes.MapRoute(
                    name: "personalqrcodeview_home_param",
                    template: "Home/PersonalQrCodeView/{QrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "PersonalQrCodeView" });

                routes.MapRoute(
                    name: "personalqrcodeview",
                    template: "QrCode/PersonalView/{QrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "PersonalQrCodeView" });

                // 列表管理路由
                routes.MapRoute(
                    name: "churchroot",
                    template: "ListManagement/ChurchRoot",
                    defaults: new { controller = "ListManagement", action = "ChurchRoot" });

                // 支付結果路由
                routes.MapRoute(
                    name: "paymentsuccess",
                    template: "payment-success",
                    defaults: new { controller = "Home", action = "PaymentSuccess" });

                routes.MapRoute(
                    name: "paymentfailed",
                    template: "payment-failed",
                    defaults: new { controller = "Home", action = "PaymentError" });

                // 錯誤顯示路由
                routes.MapRoute(
                    name: "errorview",
                    template: "Home/DisplayErrorView/{ErrorMessage}",
                    defaults: new { controller = "Home", action = "DisplayErrorView" });

                // 手機綁定相關路由
                routes.MapRoute(
                    name: "changephone",
                    template: "Phone/ChangePhoneView/{LineIdLoginViewPatameter}",
                    defaults: new { controller = "PhoneBinding", action = "ChangePhoneView" });

                routes.MapRoute(
                    name: "phoneqrcode",
                    template: "Phone/PhoneQrCodeView/{QrCodeViewPatameter}",
                    defaults: new { controller = "PhoneBinding", action = "PhoneQrCodeView" });

                // 預設路由：若無匹配，導向 Authentication/Login
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Authentication}/{action=Login}/{id?}");
            });
        }
    }
}
