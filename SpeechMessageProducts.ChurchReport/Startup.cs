// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Startup.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 Startup 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class Startup
// 主要成員：ResolveThemeName、MapThemeCssClass、ConfigureServices、Configure、Configuration、CurrentTheme、CurrentThemeCssClass
// 引用命名空間：ChurchReport.Services、ChurchReport.Tools、ChurchReport.Filters、ChurchReport.Services.Theme、ChurchReport.Payments、Microsoft.AspNetCore.Authentication.Cookies、Microsoft.AspNetCore.Builder、Microsoft.AspNetCore.Hosting
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.DependencyInjection;
using ChurchReport.WebServiceConnector;
using LineMessagingProcessor.AspNetCore;
using LineMessagingProcessor.RichMenus;
using SpeechMessage.Payments.AspNetCore.DependencyInjection;
using SpeechMessage.Payments.DependencyInjection;
using SpeechMessage.Payments.Workflows;
using ToolUtilityNameSpace.Diagnostics;

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

        private readonly DiagnosticTraceOptions _diagnosticTraceOptions;

        /// <summary>
        /// 建構函式，注入配置物件。
        /// </summary>
        /// <param name="configuration">應用程式配置物件，用於讀取 appsettings.json 或其他配置來源。</param>
        /// <param name="diagnosticTraceOptions">
        /// 由 Program 依 Debug/Release 編譯邊界解析的統一 Trace 設定；Startup 不可重新讀取
        /// 另一組開關，避免 Release 設定誤開與三個 writer 狀態分歧。
        /// </param>
        public Startup(IConfiguration configuration, DiagnosticTraceOptions diagnosticTraceOptions)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _diagnosticTraceOptions = diagnosticTraceOptions
                ?? throw new ArgumentNullException(nameof(diagnosticTraceOptions));

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
        /// <remarks>
        /// Debug 組態會在 ToolUtility 登記完成後，以原始生命週期重新登記
        /// <see cref="IOrganizationService"/> 的計時裝飾器。裝飾必須在 DI 解析點完成，
        /// 才能讓 <c>ToolUtilityClass</c> 及其 facade 在建構當下取得相同的已裝飾服務；
        /// 絕不可在建構後置換欄位，否則已被 Lazy 子服務捕獲的原始參考會繞過量測。
        /// </remarks>
        public void ConfigureServices(IServiceCollection services)
        {
#if DEBUG
            // 兩個開關都只能由已驗證的程序級 DiagnosticsTrace 設定指派；不得從 request、
            // Session、使用者或租戶資料推導。Profiling 仍依一般 Trace 開關運作；Session 詳細
            // 診斷則必須明確啟用 SessionVerbose，預設不建立高頻 Debug 輸出或留下跨請求敏感資料。
            ChurchReport.Diagnostics.Profiling.ProfilingSwitch.Enabled =
                _diagnosticTraceOptions.Enabled;
            ChurchReport.Diagnostics.SessionDiagnosticsSwitch.Enabled =
                _diagnosticTraceOptions.SessionVerbose;
            using var __perfConfigureServices =
                ChurchReport.Diagnostics.Profiling.StartupProfiler.Phase("ConfigureServices");
#endif

            services.AddSingleton(new ThemeSettings(CurrentTheme, CurrentThemeCssClass));

            // ✅ 效能：全域過濾器改為 Singleton。
            //
            // options.Filters.Add<T>() 會登記成 TypeFilter，MVC 在「每個請求」用
            // ActivatorUtilities 重新建立一個實例；配上 Scoped 登記，等於每個請求
            // 都付一次 DI 解析 + 物件配置的成本。
            //
            // 這兩個過濾器都是無狀態的（只持有 readonly 的設定與啟動期解析結果），
            // 所有請求相關資料都由方法參數的 context 傳入，因此改為 Singleton 後
            // 不會有任何跨請求狀態共用，不會造成 Session Leakage。
            // 下方 AddMvc 內對應改用 Filters.AddService<T>() 以解析同一個單例。
            services.AddSingleton<ThemeViewDataFilter>();
            services.AddSingleton<ChurchReport.Filters.GlobalAuthorizationFilter>();

            // ========================================
            // ✅ 初始化 ToolUtilityFactory 配置 (必須最先執行)
            // ========================================
            // 設定 ToolUtilityFactory 的配置物件，確保後續使用 ToolUtility 時能正確讀取 appsettings.json
            ToolUtilityNameSpace.Factory.ToolUtilityFactory.SetConfiguration(Configuration);

            // 追蹤資源（FileStream / TraceListener）為程序級：Trace.Listeners 是行程內的
            // 靜態集合，若隨請求建立就會無界成長並使每行日誌重複輸出。
            // 因此註冊為 Singleton，且刻意「由容器建立」而非傳入現成實例 ——
            // DI 只會釋放自己建立的物件；傳入外部實例會導致應用程式關閉時不被 Dispose。
            // 實際交給 ToolUtilityFactory 的動作在 Configure() 中進行（見該處說明）。
            services.AddSingleton(_diagnosticTraceOptions);
#if DEBUG
            if (_diagnosticTraceOptions.Enabled)
            {
                services.AddSingleton<IToolUtilityTracer, FileToolUtilityTracer>();
            }
            else
            {
                services.AddSingleton<IToolUtilityTracer, NullToolUtilityTracer>();
            }
#else
            // Release 只註冊零副作用實作，且不包含任何依設定分支建立檔案 tracer 的路徑。
            services.AddSingleton<IToolUtilityTracer, NullToolUtilityTracer>();
#endif

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

            // ========================================
            // ✅ 效能：動態回應的壓縮等級必須用 Fastest，不能用 Optimal
            // ========================================
            // CompressionLevel.Optimal 對 Brotli 而言是 quality 11，這是 Brotli 最慢的等級，
            // 設計用途是「壓縮一次、傳送數百萬次」的靜態資產（例如建置階段預壓縮的 .js/.css）。
            // 本站所有 HTML/JSON 都是每次請求即時產生，套用 quality 11 會讓壓縮本身的 CPU 時間
            // 遠超過所節省的傳輸時間，且會直接佔用處理請求的執行緒。
            //
            // CompressionLevel.Fastest 對 Brotli 是 quality 1：壓縮率只差數個百分點，
            // 速度快一到兩個數量級。這是動態內容唯一合理的選擇。
            services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
            });

            // Gzip 同理：Optimal 為 level 6~9，Fastest 為 level 1。
            services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
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

            // Dataverse Gateway、Singleton connection manager、keyed bounded pool、
            // IOrganizationService 代理與 ICrmConnectionPool 相容 adapter 全部由
            // ToolUtility 的組合根擴充方法註冊。這裡不再建立第二個舊池，避免
            // 診斷計數、連線上限與 client 所屬權分裂；Startup 只保留產品層的配置來源。

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

                    // ✅ 效能：AddService 解析上方登記的單例，取代每請求重新建立實例。
                    options.Filters.AddService<ThemeViewDataFilter>();

                    // ========================================
                    // ✅ Phase 3.1: 註冊全域無快取過濾器 (Session Bleeding 防護)
                    // ========================================
                    // 防止 Session Bleeding（會話串連）問題
                    // 確保所有 Controller Action 都不會被中間層代理伺服器或瀏覽器快取
                    //
                    // ✅ 效能：StrictNoCacheFilter 沒有任何相依項與狀態，
                    // 直接放入共用實例即可，不需要每個請求重新配置一個。
                    options.Filters.Add(ChurchReport.Filters.StrictNoCacheFilter.Instance);
                    options.Filters.AddService<ChurchReport.Filters.GlobalAuthorizationFilter>();

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
            // 註冊 ToolUtility 服務（request Scoped 模式）
            // ========================================
            // AddToolUtility 在 ToolUtility 組件內以明確 factory 建立 ToolUtilityClass，
            // 使其取得同一 request 的 IOrganizationService 租約；不得在此跨組件以
            // Activator 解析 internal legacy 建構式，也不得把 scoped Provider 捕獲為 Singleton。
            services.AddToolUtility();

#if DEBUG
            // CRM 計時必須在組合根的唯一 IOrganizationService 入口完成。這個 descriptor 是
            // AddToolUtility 以 scoped gateway 所建立；我們只捕獲不可變的登記中繼資料，實際
            // inner、IHttpContextAccessor 與任何 request/lease 都在下方 scope factory 內才解析，
            // 因此 wrapper 不會把使用者、Session、身分或連線租約提升到其他 request。
            var organizationServiceDescriptor = services.LastOrDefault(
                descriptor => descriptor.ServiceType == typeof(IOrganizationService));
            if (organizationServiceDescriptor == null)
            {
                // 找不到代表 ToolUtility 的組合根契約已被破壞；若靜默略過，Perf CRM 歸因會
                // 重新固定為零並誤導診斷。此為啟動期 fail-closed：拒絕啟動比帶著不可信量測
                // 繼續服務安全，且例外不含 request、Session、使用者、租戶或憑證資料。
                throw new InvalidOperationException(
                    "找不到 ToolUtility 註冊的 IOrganizationService；無法安全建立 Debug CRM 計時裝飾器。");
            }

            services.Remove(organizationServiceDescriptor);
            services.Add(new ServiceDescriptor(
                typeof(IOrganizationService),
                serviceProvider =>
                {
                    // 依原 descriptor 的三種合法形式重建 inner；不得以 GetRequiredService 重新
                    // 解析同一 service type，否則會遞迴回到本 decorator。此 factory 的結果只由
                    // 原 descriptor 的生命週期快取，沒有額外 Singleton 或跨 scope 可變狀態。
                    IOrganizationService inner;
                    if (organizationServiceDescriptor.ImplementationFactory != null)
                    {
                        inner = (IOrganizationService)organizationServiceDescriptor
                            .ImplementationFactory(serviceProvider);
                    }
                    else if (organizationServiceDescriptor.ImplementationInstance != null)
                    {
                        inner = (IOrganizationService)organizationServiceDescriptor.ImplementationInstance;
                    }
                    else if (organizationServiceDescriptor.ImplementationType != null)
                    {
                        inner = (IOrganizationService)ActivatorUtilities.CreateInstance(
                            serviceProvider,
                            organizationServiceDescriptor.ImplementationType);
                    }
                    else
                    {
                        // 無實作形式不是可恢復狀態；繼續解析會產生沒有 inner 的 wrapper，讓
                        // pool/lease 擁有權與 CRM 量測同時失真，因此明確 fail closed。
                        throw new InvalidOperationException(
                            "IOrganizationService 的原始 DI descriptor 未提供可重建的實作形式。");
                    }

                    var httpContextAccessor = serviceProvider
                        .GetRequiredService<IHttpContextAccessor>();
                    return new ChurchReport.Diagnostics.Profiling.TimedOrganizationService(
                        inner,
                        httpContextAccessor);
                },
                organizationServiceDescriptor.Lifetime));
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
            // 註冊付款後產品流程服務
            // ========================================
            // 這些服務消費共用金流核心的 provider-neutral 結果，再接回 ChurchReport 的 CRM、LINE 與收費單分類流程；
            // 因此命名採用 Payment*，避免誤解成只屬於 MyPay provider protocol 的實作。
            services.AddScoped<ChurchReport.Services.PaymentMessageBuilder>();
            services.AddScoped<ChurchReport.Services.PaymentFeeTypeHelper>();
            services.AddScoped<ChurchReport.Services.PaymentCallbackLogger>();
            services.AddScoped<ChurchReport.Services.PaymentCrmService>();
            services.AddScoped<ChurchReport.Services.PaymentNotificationService>();
            services.AddLineMessagingProcessor(options =>
            {
                var defaultOrg = Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                options.ChannelAccessToken =
                    Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"] ??
                    Configuration["LINE_CHANNEL_ACCESS_TOKEN"] ??
                    string.Empty;
            });
            // ChurchReport 的 RichMenu 圖片、alias 與 legacy-auth menu key 屬於產品層設定；
            // 共用 LineMessagingProcessor.RichMenus 只負責依 catalog 佈建、快取 richMenuId 並執行指派。
            services.AddLineRichMenuProvisioning<ChurchReportLegacyRichMenuCatalog>();
            services.AddScoped<ChurchReport.Services.IChurchReportLineProfileProvider, ChurchReport.Services.ChurchReportLineProfileProvider>();
            services.AddScoped<ChurchReport.Services.IChurchReportLineBindingNotificationService, ChurchReport.Services.ChurchReportLineBindingNotificationService>();

            // ========================================
            // 註冊抽離後的通用金流核心與 ChurchReport adapter
            // ========================================
            // SpeechMessage.Payments 擁有永豐、高鉅、台新的 provider protocol、加解密、簽章、
            // request/response mapping 與 callback parsing。ChurchReport 只註冊薄 adapter，
            // 負責把 ASP.NET request、CRM/LINE 產品流程與抽離後的付款核心接回來。
            services.AddSpeechMessagePayments(Configuration.GetSection("Payment"));
            services.AddSpeechMessagePaymentAspNetCore();
            services.AddScoped<ChurchReportPaymentProfileResolver>();
            services.AddScoped<ChurchReportPaymentContextBuilder>();
            services.AddScoped<DonationPaymentReturnPresenter>();
            services.AddScoped<IPaymentRecordUpdater, ChurchReportPaymentRecordUpdater>();
            services.AddScoped<IPaymentPayerNotifier, ChurchReportPaymentPayerNotifier>();
            services.AddScoped<IDonationPaymentReturnWorkflow, DonationPaymentReturnWorkflow>();
            services.AddScoped<IDonationPaymentProductWorkflowDispatcher, DonationPaymentProductWorkflowDispatcher>();
            // ChurchReport 產品層的建單 adapter，以中性介面供 controller/context/manager 使用。
            // 這裡只註冊中性的 DonationPaymentCreateGatewayAdapter，避免 ChurchReport 產品層再依賴永豐 QPay 命名。
            services.AddScoped<DonationPaymentCreateGatewayAdapter>();
            services.AddScoped<IDonationPaymentCreateGatewayAdapter>(sp =>
                sp.GetRequiredService<DonationPaymentCreateGatewayAdapter>());

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
                options.Cookie.Name = ".ChurchReport.Session";
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
            // 把容器建立的程序級追蹤器交給 ToolUtilityFactory。
            // 必須在任何 GetInstance() 之前完成；放在 Configure 而非 ConfigureServices，
            // 是因為此時服務容器已建置完成，可取得由容器擁有（並負責釋放）的實例。
            ToolUtilityNameSpace.Factory.ToolUtilityFactory.SetTracer(
                app.ApplicationServices.GetRequiredService<ToolUtilityNameSpace.Diagnostics.IToolUtilityTracer>());

            // legacy Factory 是程序級單例，絕不可保存此刻 request 的 HttpContext、RequestServices、
            // gateway、lease 或 raw CRM client。此 delegate 每次操作才讀取當前 HttpContext；背景
            // 執行緒沒有 request 時 AmbientGatewayOrganizationService 會建立短命 scope 並在操作後
            // 立即釋放，確保 session 快取中的 legacy 持有者不會跨 request 共用連線或身分狀態。
            ToolUtilityNameSpace.Factory.ToolUtilityFactory.SetAmbientService(
                new ToolUtilityNameSpace.Dataverse.AmbientGatewayOrganizationService(
                    () => app.ApplicationServices
                        .GetRequiredService<IHttpContextAccessor>()
                        .HttpContext?
                        .RequestServices,
                    app.ApplicationServices.GetRequiredService<IServiceScopeFactory>()));

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
#if DEBUG
            if (_diagnosticTraceOptions.Enabled)
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
                Console.WriteLine("[Startup] ⚠️ 檔案 Trace 未啟用（DiagnosticsTrace:Enabled=false）");
            }
#else
            Console.WriteLine("[Startup] ⚠️ Release 編譯已硬性停用三種檔案 Trace");
#endif

            // ========================================
            // ✅ Phase 3.0: 全站無快取中介軟體（Session Bleeding 防護 - 最優先執行）
            // ========================================
            // 這是防止 Session Bleeding 的第一道也是最重要的防線
            // 必須在所有其他中介軟體之前執行，確保每個回應都帶有正確的快取控制標頭
            // ✅ 效能修正：安全標頭與快取標頭必須分流處理。
            //
            // 【原本的缺陷】
            // 這段中介層先對「所有」請求設定 Cache-Control: no-store，之後 UseStaticFiles 的
            // OnPrepareResponse 再用 Headers.Append 追加 public,max-age=31536000。
            // 結果標頭變成：
            //   Cache-Control: no-store, no-cache, must-revalidate, max-age=0, public,max-age=31536000
            // 瀏覽器與 Proxy 只要看到 no-store 就完全不快取，那一整年的 max-age 從未生效。
            // 每次換頁都重新下載全部 CSS/JS/字型/圖片。
            //
            // 【修正後的分流】
            // - 安全標頭（nosniff / Referrer-Policy / X-Frame-Options）對所有請求一律套用，不變。
            // - no-store 與 Vary: Cookie 只套用在動態頁面。這兩者正是 Session Bleeding 防護的核心，
            //   而靜態資源本來就不含任何使用者資料，不需要也不應該加上。
            // - 靜態資源的快取標頭改由 UseStaticFiles 以「指派」而非「追加」的方式設定（見下方）。
            //
            // 【安全性未回退的理由】
            // 判定為靜態的路徑必須同時滿足「副檔名在白名單」且「位於 /css/、/js/、/lib/、/assets/、
            // /images/、/img/、/fonts/ 等已知靜態目錄下」（見 StaticRequestPathHelper.IsStaticAssetPath）。
            // 任何動態路由後綴靜態副檔名的 Web Cache Deception 嘗試都不符合前綴條件，
            // 會繼續走動態分支拿到 no-store，並且已先被 WebCacheDeceptionMiddleware 擋成 404。
            app.Use(async (context, next) =>
            {
                var headers = context.Response.Headers;

                // 安全標頭：所有請求一律套用（含靜態資源）
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                headers["X-Frame-Options"] = "SAMEORIGIN";

                if (!ChurchReport.Middleware.StaticRequestPathHelper.IsStaticAssetPath(context.Request.Path))
                {
                    // 動態頁面：維持最嚴格的快取策略，禁止瀏覽器與任何中間代理伺服器存儲內容
                    headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                    headers["Pragma"] = "no-cache";
                    headers["Expires"] = "0";

                    // ⚠️ 重要：告訴所有 Proxy「不同 Cookie = 不同內容，不准共用」
                    // 這是解決 Session Bleeding 的關鍵設定！
                    // ✅ P1: 不覆蓋既有 Vary（例如 Accept-Encoding），改用合併策略
                    if (headers.TryGetValue("Vary", out var varyValues))
                    {
                        var vary = varyValues.ToString();
                        if (!vary.Contains("Cookie", StringComparison.OrdinalIgnoreCase))
                        {
                            headers["Vary"] = string.IsNullOrWhiteSpace(vary) ? "Cookie" : $"{vary}, Cookie";
                        }
                    }
                    else
                    {
                        headers["Vary"] = "Cookie";
                    }
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
                    // ✅ 效能修正：必須用「指派」而非 Headers.Append。
                    // Append 會把長效快取接在前面中介層可能留下的 no-store 之後，
                    // 瀏覽器看到 no-store 就整條忽略，導致靜態資源每次都重新下載。
                    //
                    // immutable 讓支援的瀏覽器在使用者按重新整理時也不發出重新驗證請求，
                    // 進一步消除每次換頁的一輪 304 往返。
                    const int durationInSeconds = 60 * 60 * 24 * 365; // 1 年
                    var headers = ctx.Context.Response.Headers;
                    headers["Cache-Control"] = $"public,max-age={durationInSeconds},immutable";

                    // 清掉動態分支可能留下的 HTTP/1.0 相容標頭，避免與長效快取互相矛盾。
                    headers.Remove("Pragma");
                    headers.Remove("Expires");
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
            app.UseMiddleware<ChurchReport.Middleware.DataverseTraceMiddleware>();

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

                // 奉獻付款登入路由。
                // template 保留舊 URL，defaults 指向中性 action，避免 conventional route 依賴已移除的 QPay action 名稱。
                routes.MapRoute(
                    name: "legacy-donation-payment-login",
                    template: "Home/QPayLogin",
                    defaults: new { controller = "Home", action = "DonationPaymentLogin" });

                routes.MapRoute(
                    name: "legacy-process-donation-payment-login",
                    template: "Home/ProcessQPayLogin",
                    defaults: new { controller = "Home", action = "ProcessDonationPaymentLogin" });

                routes.MapRoute(
                    name: "donationpaymentlogin",
                    template: "Home/DonationPaymentLogin",
                    defaults: new { controller = "Home", action = "DonationPaymentLogin" });

                routes.MapRoute(
                    name: "processdonationpaymentlogin",
                    template: "Home/ProcessDonationPaymentLogin",
                    defaults: new { controller = "Home", action = "ProcessDonationPaymentLogin" });

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
                    name: "donationpaymentview",
                    template: "Dedication/DonationPaymentView/{LineId?}",
                    defaults: new { controller = "Dedication", action = "DonationPaymentView" });

                routes.MapRoute(
                    name: "legacy-donation-payment-view",
                    template: "Dedication/QPayView/{LineId?}",
                    defaults: new { controller = "Dedication", action = "DonationPaymentView" });

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
