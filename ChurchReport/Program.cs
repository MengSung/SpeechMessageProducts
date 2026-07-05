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
using System.Threading.Tasks;

namespace ChurchReport
{
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
#endif

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            ConfigureSafeLogging(builder);

            // 設定 Kestrel
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
                options.Limits.MaxRequestBufferSize = null;
                options.Limits.MaxConcurrentConnections = 1000;
                options.Limits.MaxConcurrentUpgradedConnections = 1000;
            });

#if DEBUG
            InitializeTraceListener(builder.Environment.ContentRootPath);
#endif

            // 使用 Startup 類別設定服務
            var startup = new Startup(builder.Configuration);
            startup.ConfigureServices(builder.Services);

            var app = builder.Build();

#if DEBUG
            // ========================================
            // 僅在 Debug 組態下初始化 Trace Listener
            // 使用條件編譯確保 Release 組態完全移除此程式碼
            // 命令： dotnet publish -c Debug   → 會寫入 Trace.log
            //       dotnet publish -c Release → 不會寫入 Trace.log（程式碼被移除）
            // ========================================
            InitializeTraceListener(app.Environment.ContentRootPath);

            // ========================================
            // GC 監控設定（僅 Debug 組態）
            // 每 10 分鐘記錄一次 GC 統計，幫助監控記憶體使用
            // ========================================
            StartGCMonitoring();
#endif

            // 使用 Startup 類別設定中介層
            startup.Configure(app, app.Environment, app.Services.GetRequiredService<ILoggerFactory>());

#if DEBUG
            // ========================================
            // 註冊應用程式停止事件，確保資源正確釋放（僅 Debug 組態）
            // ========================================
            var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.Register(() =>
            {
                CleanupTraceListener();
            });
#endif

            app.Run();
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
        /// - 若 Logs 目錄不存在，會自動建立
        /// - 若 Trace.log 檔案不存在，會自動建立
        /// - 若檔案已存在，會以 Append 模式追加寫入
        /// </summary>
        private static void InitializeTraceListener(string contentRootPath)
        {
            lock (_traceLock)
            {
                if (_traceListener != null)
                {
                    // 已經初始化，直接返回
                    return;
                }

                try
                {
                    // ========================================
                    // 步驟 1：確保 Logs 目錄存在（自動建立）
                    // ========================================
                    var logsDir = Path.Combine(contentRootPath, "Logs");

                    // Directory.CreateDirectory 特性：
                    // - 若目錄已存在，不會拋出例外
                    // - 若目錄不存在，會自動建立（包含所有必要的父目錄）
                    var dirInfo = Directory.CreateDirectory(logsDir);

                    Console.WriteLine($"[Trace Init] Logs directory: {dirInfo.FullName}");

                    // ========================================
                    // 步驟 2：建立或開啟 Trace.log 檔案
                    // ========================================
                    var tracePath = Path.Combine(logsDir, "Trace.log");

                    // 檢查檔案是否已存在（用於日誌記錄）
                    bool fileExists = File.Exists(tracePath);

                    // TextWriterTraceListener 建構函式特性：
                    // - 若檔案不存在，會自動建立
                    // - 若檔案已存在，會以 Append 模式開啟（追加寫入，不會覆蓋）
                    _traceListener = new TextWriterTraceListener(tracePath)
                    {
                        Name = "ChurchReportTraceListener"
                    };

                    Console.WriteLine($"[Trace Init] Trace file: {tracePath} (Exists: {fileExists})");

                    // ========================================
                    // 步驟 3：註冊 Trace Listener
                    // ========================================
                    // 檢查是否已存在相同名稱的 Listener（避免重複新增）
                    var existingListener = Trace.Listeners.Cast<TraceListener>()
                        .FirstOrDefault(l => l.Name == "ChurchReportTraceListener");

                    if (existingListener == null)
                    {
                        Trace.Listeners.Add(_traceListener);
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
                        Console.WriteLine("[Trace Init] ?? Listener already exists, skipping registration");
                    }
                }
                catch (Exception ex)
                {
                    // 初始化失敗，記錄到控制台（避免影響應用程式啟動）
                    Console.WriteLine($"[Trace Init] ? Failed to initialize trace listener: {ex.Message}");
                    Console.WriteLine($"[Trace Init] Stack trace: {ex.StackTrace}");
                }
            }
        }

        /// <summary>
        /// 清理 Trace Listener（確保資源正確釋放，僅 Debug 組態）
        /// 在應用程式停止時呼叫，關閉 FileStream 和 StreamWriter
        /// </summary>
        private static void CleanupTraceListener()
        {
            lock (_traceLock)
            {
                if (_traceListener != null)
                {
                    try
                    {
                        Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Application shutting down. Cleaning up trace listener.");

                        // 從 Listeners 集合移除
                        Trace.Listeners.Remove(_traceListener);

                        // 確保 Flush（將緩衝資料寫入檔案）
                        _traceListener.Flush();

                        // 釋放資源（FileStream, StreamWriter）
                        _traceListener.Dispose();
                        _traceListener = null;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to cleanup trace listener: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 啟動 GC 監控（僅 Debug 組態）
        /// 每 10 分鐘記錄一次 GC 統計，幫助診斷記憶體問題
        /// 記錄內容：Gen0/Gen1/Gen2 回收次數、GC Memory、Private Memory
        /// </summary>
        private static void StartGCMonitoring()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(10));

                        var gen0 = GC.CollectionCount(0);
                        var gen1 = GC.CollectionCount(1);
                        var gen2 = GC.CollectionCount(2);
                        var totalMemory = GC.GetTotalMemory(false) / 1024 / 1024; // MB
                        var process = Process.GetCurrentProcess();
                        var privateMemory = process.PrivateMemorySize64 / 1024 / 1024; // MB

                        var message = $"[GC Monitor] " +
                                    $"Gen0: {gen0}, Gen1: {gen1}, Gen2: {gen2}, " +
                                    $"GC Memory: {totalMemory} MB, " +
                                    $"Private Memory: {privateMemory} MB";

                        Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                        Console.WriteLine(message);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GC Monitor Error: {ex.Message}");
                    }
                }
            });
        }
#endif
    }
}
