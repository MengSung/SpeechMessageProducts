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
#endif

        /// <summary>
        /// 建立並執行 ChurchReport Host。Release 組態固定建立停用的診斷設定，外部設定無法
        /// 重新啟用檔案 writer；Debug 組態則由單一 <c>DiagnosticsTrace</c> 區段控制三種 Trace。
        /// </summary>
        /// <param name="args">傳給 ASP.NET Core 組態與 Host 的命令列參數。</param>
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            ConfigureSafeLogging(builder);

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
                options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
                options.Limits.MaxRequestBufferSize = null;
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

        private static void ConfigureSafeLogging(WebApplicationBuilder builder)
        {
            // Windows EventLog provider can fail under non-admin local runs and prevent Kestrel from starting.
            builder.Logging.ClearProviders();
            builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();
            builder.Logging.AddEventSourceLogger();
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
                        AutoFlush = true
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
                        Trace.AutoFlush = true;

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
            }
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
