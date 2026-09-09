// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Program.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 Program 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class Program
// 主要成員：Main、ConfigureSafeLogging、InitializeTraceListener、CleanupTraceListener、StartGCMonitoring
// 引用命名空間：Microsoft.AspNetCore.Builder、Microsoft.AspNetCore.Hosting、Microsoft.AspNetCore.Server.Kestrel.Core、Microsoft.Extensions.DependencyInjection、Microsoft.Extensions.Logging、Microsoft.Extensions.Hosting、System、System.Diagnostics
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Diagnostics;
using ChurchReport.Logging;
using ToolUtilityNameSpace.Diagnostics;

namespace ChurchReport
{
    /// <summary>
    /// ChurchReport ASP.NET Core 程序的組合根，負責建立 Host、集中診斷設定、服務註冊與
    /// Debug-only Trace 資源生命週期；不保存任何 request、使用者、租戶或驗證狀態。
    /// </summary>
    public class Program
    {
        // ⚠️【安全不變量】寫入 Logs\Trace.log 的 TextWriterTraceListener 必須永遠位於 #if DEBUG 內。
        // 一旦移出，Release 將開始寫 Trace.log（違反硬性要求②）。剖析子系統的 Release 無痕亦依賴此不變量。
#if DEBUG
        // ========================================
        // 靜態成員變數用來保存 TraceListener，確保單例且可釋放
        // 僅在 Debug 組態下編譯此區塊
        // ========================================
        private static TextWriterTraceListener _traceListener;
        private static readonly object _traceLock = new object();
        private static DebugTraceMonitorLifetime _gcMonitoringLifetime;
        private static bool _unhandledExceptionFlushRegistered;
        private static bool? _previousTraceAutoFlush;
#endif

        /// <summary>
        /// 建立並執行 ChurchReport Host。Release 組態固定建立停用的診斷設定，外部設定無法
        /// 重新啟用檔案 writer；Debug 組態則由單一 <c>DiagnosticsTrace</c> 區段控制三種 Trace。
        /// </summary>
        /// <param name="args">傳給 ASP.NET Core 組態與 Host 的命令列參數。</param>
        public static void Main(string[] args)
        {
            // Exception.log 不受 DEBUG 或 DiagnosticsTrace 開關控制，早於 Host 建置建立 owner。
            // 只從部署目錄決定路徑，原始例外不進通知佇列；先落檔 flush 後才排入 LINE。
            var diagnostics = new ExceptionDiagnostics(Path.Combine(AppContext.BaseDirectory, "Logs"));
            IDisposable registration = null;
            ChurchReport.Services.LineExceptionSender sender = null;
            try
            {
                registration = ExceptionReporting.Attach(diagnostics);
                var builder = WebApplication.CreateBuilder(args);
                sender = new ChurchReport.Services.LineExceptionSender(builder.Configuration);
                diagnostics.StartNotifications(sender.SendAsync);
                RunApplication(builder, diagnostics);
            }
            catch (Exception exception)
            {
                diagnostics.Report(exception, "Program.Fatal");
                throw;
            }
            finally
            {
                registration?.Dispose();
                diagnostics.DisposeAsync().AsTask().GetAwaiter().GetResult();
                sender?.Dispose();
            }
        }

        /// <summary>
        /// 建立正常 Host 管線，借用最外層管理的 Exception.log owner；錯誤 provider 在所有組態啟用。
        /// 原本三個診斷 Trace 檔仍遵守 Release 關閉契約，與正式錯誤紀錄完全獨立。
        /// </summary>
        private static void RunApplication(WebApplicationBuilder builder, ExceptionDiagnostics diagnostics)
        {

            ConfigureSafeLogging(builder, diagnostics);
            builder.Services.AddSingleton(diagnostics);

#if DEBUG
            DiagnosticTraceOptions diagnosticTraceOptions;
            try
            {
                diagnosticTraceOptions = DiagnosticTraceOptions.FromConfiguration(
                    builder.Configuration,
                    builder.Environment.ContentRootPath,
                    allowEnabled: true);
            }
            catch (Exception ex)
            {
                // 設定錯誤必須 fail closed：診斷功能不可阻止主程式啟動，也不可退回其他硬編碼路徑。
                Console.WriteLine($"[Trace Init] 統一診斷設定無效，已停用檔案 Trace：{ex.Message}");
                diagnosticTraceOptions = DiagnosticTraceOptions.CreateDisabled(
                    builder.Environment.ContentRootPath);
            }
#else
            // Release 永遠不讀取 Enabled=true；外部 appsettings／環境變數無法繞過此防線。
            var diagnosticTraceOptions = DiagnosticTraceOptions.CreateDisabled(
                builder.Environment.ContentRootPath);
#endif

            // 設定 Kestrel
            builder.WebHost.ConfigureKestrel(options =>
            {
                // ========================================
                // ✅【記憶體上界】請求標頭逾時
                // ========================================
                // 原本設定為 30 分鐘。標頭逾時是「客戶端從建立連線到送完完整請求標頭」的時限，
                // 沒有任何正常客戶端需要超過數秒。設成 30 分鐘等於允許單一來源開啟大量連線、
                // 每次只送一個位元組，就能長時間佔住連線物件與其緩衝區（slowloris）。
                // 配合下方 1000 條並行連線上限，這是可被觸發的記憶體耗盡途徑。
                //
                // 60 秒對最慢的行動網路仍然非常寬鬆，同時讓半開連線能被及時回收。
                // 注意：這不影響請求「主體」的傳輸時間，大檔上傳不受此設定限制。
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(60);

                // ========================================
                // ✅【記憶體上界】請求緩衝區大小
                // ========================================
                // 原本設定為 null，意思是「無上限」。
                // MaxRequestBufferSize 是 Kestrel 對尚未被應用程式讀取的請求資料所保留的
                // 緩衝上限，也是它施加背壓的依據。設為 null 會完全解除背壓：
                // 客戶端可以比應用程式讀取速度更快地推送資料，Kestrel 只能一直把資料
                // 累積在記憶體裡。1000 條並行連線同時這樣做足以耗盡整台機器的記憶體。
                //
                // 這不是傳統意義的「洩漏」（記憶體最終會被回收），
                // 但在負載下的可觀察行為與洩漏完全相同，因此必須設上界。
                //
                // 1 MB 是 Kestrel 的預設值，也是實務上的建議值。它「不」限制請求主體大小，
                // 只是流量控制視窗；大檔上傳仍可正常運作，只是改為串流而非全部先進記憶體。
                options.Limits.MaxRequestBufferSize = 1024 * 1024;

                options.Limits.MaxConcurrentConnections = 1000;
                options.Limits.MaxConcurrentUpgradedConnections = 1000;
            });

            // 使用 Startup 類別設定服務
            var startup = new Startup(builder.Configuration, diagnosticTraceOptions);
            startup.ConfigureServices(builder.Services);

            var app = builder.Build();

#if DEBUG
            // 只有服務容器成功建立後才取得 listener owner；若組態或 DI 建置失敗，
            // 尚未建立檔案 writer，避免啟動例外留下未由 Host 接管的 handle。
            InitializeTraceListener(diagnosticTraceOptions);
            var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            if (diagnosticTraceOptions.Enabled)
            {
                // Program 是全域 Trace listener 的唯一 owner，因此也必須是唯一註冊未處理例外
                // flush callback 的 owner。此 middleware 包住後續完整管線；不保存 HttpContext、
                // identity、Session 或 tenant，finally 只 flush 程序級 listener 的已緩衝資料。
                RegisterUnhandledExceptionTraceFlush();
                app.Use(async (context, next) =>
                {
                    try
                    {
                        await next();
                    }
                    finally
                    {
                        FlushTraceListener();
                    }
                });

                _gcMonitoringLifetime = DebugTraceMonitorLifetime.Start(StartGCMonitoringAsync);
            }
            try
            {
                // 使用 Startup 類別設定中介層；若此處失敗，外層 finally 仍會釋放 Debug 資源。
                startup.Configure(app, app.Environment, app.Services.GetRequiredService<ILoggerFactory>());
                lifetime.ApplicationStopping.Register(StopDebugTraceResources);
                app.Run();
            }
            finally
            {
                // ApplicationStopping callback 與 app.Run 例外路徑共用冪等清理 owner。
                StopDebugTraceResources();
            }
#else
            // Release 沒有任何檔案 listener、GC monitor 或診斷 provider。
            startup.Configure(app, app.Environment, app.Services.GetRequiredService<ILoggerFactory>());
            app.Run();
#endif
        }

        /// <summary>註冊只擷取安全 metadata 的正式錯誤 provider；不沿用會複製原始訊息的舊 FileLogger。</summary>
        private static void ConfigureSafeLogging(WebApplicationBuilder builder, ExceptionDiagnostics diagnostics)
        {
            // Windows EventLog provider can fail under non-admin local runs and prevent Kestrel from starting.
            builder.Logging.ClearProviders();
            builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();
            builder.Logging.AddEventSourceLogger();
            builder.Logging.AddProvider(new ExceptionLoggerProvider(diagnostics));
            builder.Logging.AddFilter<ExceptionLoggerProvider>(null, LogLevel.Error);
        }

#if DEBUG
        /// <summary>
        /// 初始化 Trace Listener（僅在 Debug 組態下編譯）
        /// 確保 TextWriterTraceListener 只建立一次，並且可以在應用程式停止時正確釋放
        ///
        /// 【自動建立機制】
        /// - 若統一設定指定的診斷目錄不存在，會自動建立
        /// - 若 Trace.log 檔案不存在，會自動建立
        /// - 若檔案已存在，會以 Append 模式追加寫入
        /// </summary>
        /// <param name="options">
        /// 已由 Debug 組合根驗證的程序級設定；停用時不建立目錄、stream、writer 或 listener。
        /// </param>
        private static void InitializeTraceListener(DiagnosticTraceOptions options)
        {
            if (options == null || !options.Enabled)
            {
                return;
            }

            lock (_traceLock)
            {
                if (_traceListener != null)
                {
                    // 已經初始化，直接返回
                    return;
                }

                FileStream stream = null;
                StreamWriter writer = null;
                TextWriterTraceListener candidate = null;
                try
                {
                    var dirInfo = Directory.CreateDirectory(options.Directory);

                    Console.WriteLine($"[Trace Init] Logs directory: {dirInfo.FullName}");

                    // ========================================
                    // 步驟 2：建立或開啟 Trace.log 檔案
                    // ========================================
                    var tracePath = options.TraceLogPath;

                    // 檢查檔案是否已存在（用於日誌記錄）
                    bool fileExists = File.Exists(tracePath);

                    // TextWriterTraceListener 建構函式特性：
                    // - 若檔案不存在，會自動建立
                    // - 若檔案已存在，會以 Append 模式開啟（追加寫入，不會覆蓋）
                    stream = new FileStream(
                        tracePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete);
                    writer = new StreamWriter(
                        stream,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        4096,
                        leaveOpen: false)
                    {
                        // 逐行同步 flush 會把每個 Debug.WriteLine 變成請求執行緒上的磁碟 I/O。
                        // 因此批次寫入並由 request 結束、正常停止與未處理例外三個確定點 flush；
                        // 這保留診斷資料的有限遺失風險邊界，同時不讓量測本身主導延遲數字。
                        AutoFlush = false
                    };
                    // StreamWriter 已接管 FileStream；後續例外由 writer 的清理路徑負責。
                    stream = null;
                    candidate = new TextWriterTraceListener(writer)
                    {
                        Name = "ChurchReportTraceListener"
                    };
                    // candidate 已接管 writer；成功或失敗都只由一個 owner Dispose。
                    writer = null;

                    Console.WriteLine($"[Trace Init] Trace file: {tracePath} (Exists: {fileExists})");

                    // ========================================
                    // 步驟 3：註冊 Trace Listener
                    // ========================================
                    // 檢查是否已存在相同名稱的 Listener（避免重複新增）
                    var existingListener = Trace.Listeners.Cast<TraceListener>()
                        .FirstOrDefault(l => l.Name == "ChurchReportTraceListener");

                    if (existingListener == null)
                    {
                        Trace.Listeners.Add(candidate);
                        _traceListener = candidate;
                        candidate = null;
                        _previousTraceAutoFlush = Trace.AutoFlush;
                        Trace.AutoFlush = false;

                        // 寫入初始化成功訊息
                        var initMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Trace listener initialized successfully (Debug build).";
                        Trace.WriteLine(initMessage);
                        Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Log file: {tracePath}");
                        Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] File mode: {(fileExists ? "Append" : "Create New")}");

                        Console.WriteLine("[Trace Init] ? Initialization complete");
                    }
                    else
                    {
                        // 已有同名 listener 時釋放本次候選 writer，避免重複初始化形成
                        // 未掛接但仍持有檔案 handle 的 orphan listener。
                        candidate.Dispose();
                        candidate = null;
                        Console.WriteLine("[Trace Init] ?? Listener already exists, skipping registration");
                    }
                }
                catch (Exception ex)
                {
                    if (candidate != null)
                    {
                        try { Trace.Listeners.Remove(candidate); } catch { }
                        try { candidate.Dispose(); } catch { }
                    }
                    if (_traceListener != null)
                    {
                        try { Trace.Listeners.Remove(_traceListener); } catch { }
                        try { _traceListener.Dispose(); } catch { }
                        _traceListener = null;
                    }
                    if (writer != null)
                    {
                        try { writer.Dispose(); } catch { }
                    }
                    if (stream != null)
                    {
                        try { stream.Dispose(); } catch { }
                    }
                    // 初始化失敗，記錄到控制台（避免影響應用程式啟動）
                    Console.WriteLine($"[Trace Init] ? Failed to initialize trace listener: {ex.Message}");
                    Console.WriteLine($"[Trace Init] Stack trace: {ex.StackTrace}");
                }
            }
        }

        /// <summary>
        /// 清理 Trace Listener（確保資源正確釋放，僅 Debug 組態）
        /// 在應用程式停止時呼叫；先從全域集合停止接受事件，再 Flush 並 Dispose 其私有
        /// writer/stream。方法以鎖保護且可重複呼叫，不保存 request 或使用者狀態。
        /// </summary>
        private static void CleanupTraceListener()
        {
            lock (_traceLock)
            {
                var listener = _traceListener;
                _traceListener = null;
                if (listener != null)
                {
                    try { Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Application shutting down. Cleaning up trace listener."); } catch { }
                    try { Trace.Listeners.Remove(listener); } catch { }
                    try { listener.Flush(); } catch { }
                    try { listener.Dispose(); } catch (Exception ex) { Console.WriteLine($"Failed to cleanup trace listener: {ex.Message}"); }
                }

                if (_previousTraceAutoFlush.HasValue)
                {
                    Trace.AutoFlush = _previousTraceAutoFlush.Value;
                    _previousTraceAutoFlush = null;
                }
            }
        }

        /// <summary>
        /// 將目前已緩衝的 Trace.log 資料送入唯一 listener，但不建立、移除或 Dispose 任何資源。
        /// </summary>
        /// <remarks>
        /// 此方法由 request middleware 的 <c>finally</c> 與未處理例外 callback 共用。鎖只保護
        /// listener 與 cleanup 的交接，避免停止期間對已 Dispose writer flush；它不保存 request、
        /// Session、Claims、租戶、例外內容或其他使用者資料。I/O 只在每個 request 結束或故障邊界
        /// 發生一次，而不是每一行診斷文字都同步發生一次。
        /// </remarks>
        private static void FlushTraceListener()
        {
            lock (_traceLock)
            {
                try
                {
                    _traceListener?.Flush();
                }
                catch (ObjectDisposedException)
                {
                    // Stop 與 exception callback 競態時 listener 已被唯一 owner 釋放；診斷不得影響主流程。
                }
                catch (IOException)
                {
                    // 診斷檔不可寫時不重試、不快取例外，避免形成背景工作或無界記憶體保留。
                }
            }
        }

        /// <summary>
        /// 註冊程序級未處理例外 flush callback，確保非正常終止前盡量保留已緩衝的 Trace.log。
        /// </summary>
        /// <remarks>
        /// <see cref="AppDomain.UnhandledException"/> 是程序級事件，必須只由 Program 註冊一次並在
        /// 正常停止時解除訂閱；否則重複初始化會讓 static event 保留 callback。callback 不記錄例外
        /// 原文，避免把 credential、Session 或使用者資料擴散到 trace，只執行既有 listener 的 flush。
        /// </remarks>
        private static void RegisterUnhandledExceptionTraceFlush()
        {
            lock (_traceLock)
            {
                if (_traceListener == null || _unhandledExceptionFlushRegistered)
                {
                    return;
                }

                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                _unhandledExceptionFlushRegistered = true;
            }
        }

        /// <summary>
        /// 移除程序級未處理例外 callback，讓關機後不再保留 Program 的 static event 訂閱。
        /// </summary>
        private static void UnregisterUnhandledExceptionTraceFlush()
        {
            lock (_traceLock)
            {
                if (!_unhandledExceptionFlushRegistered)
                {
                    return;
                }

                AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                _unhandledExceptionFlushRegistered = false;
            }
        }

        /// <summary>
        /// 處理未處理例外通知時只 flush 已存在的 listener，絕不攔截或改寫例外傳播。
        /// </summary>
        /// <param name="sender">發出程序級例外通知的 AppDomain。</param>
        /// <param name="eventArgs">未處理例外資訊；不得序列化或寫入其中可能含敏感資料的內容。</param>
        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
        {
            FlushTraceListener();
        }

        /// <summary>
        /// 以唯一 owner 停止 Debug GC 監控並清理全域 Trace listener。此方法可由 Host 停止
        /// callback 與 app.Run 的例外 finally 同時呼叫；Interlocked 交換確保監控只被停止一次，
        /// 而 listener cleanup 即使重複執行也不會保留 writer、stream 或 task。
        /// </summary>
        private static void StopDebugTraceResources()
        {
            var monitor = Interlocked.Exchange(ref _gcMonitoringLifetime, null);
            try
            {
                monitor?.Dispose();
            }
            catch (Exception ex)
            {
                // 診斷監控停止失敗不得略過 listener 的 flush/Dispose，也不得阻止 Host 關機。
                Console.WriteLine($"[Trace Cleanup] GC monitor shutdown failed: {ex.Message}");
            }
            finally
            {
                // 先解除 static event，才釋放 listener；確定清理後不會再由例外 callback 觸碰 writer。
                UnregisterUnhandledExceptionTraceFlush();
                CleanupTraceListener();
            }
        }

        /// <summary>
        /// 啟動 GC 監控（僅 Debug 組態）
        /// 每 10 分鐘記錄一次 GC 統計，幫助診斷記憶體問題
        /// 記錄內容：Gen0/Gen1/Gen2 回收次數、GC Memory、Private Memory
        /// </summary>
        /// <param name="cancellationToken">
        /// 由 <see cref="DebugTraceMonitorLifetime"/> 唯一擁有的專屬 token；取消後延遲立即結束，
        /// 讓 Host 停止 callback 能先取消再 drain，不留下 task、timer 或 token registration。
        /// </param>
        /// <returns>監控完整生命週期工作；只在收到取消後正常完成。</returns>
        private static async Task StartGCMonitoringAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);

                    var gen0 = GC.CollectionCount(0);
                    var gen1 = GC.CollectionCount(1);
                    var gen2 = GC.CollectionCount(2);
                    var totalMemory = GC.GetTotalMemory(false) / 1024 / 1024; // MB
                    using var process = Process.GetCurrentProcess();
                    var privateMemory = process.PrivateMemorySize64 / 1024 / 1024; // MB

                    var message = $"[GC Monitor] " +
                                $"Gen0: {gen0}, Gen1: {gen1}, Gen2: {gen2}, " +
                                $"GC Memory: {totalMemory} MB, " +
                                $"Private Memory: {privateMemory} MB";

                    Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                    Console.WriteLine(message);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GC Monitor Error: {ex.Message}");
                }
            }
        }
#endif
    }
}
