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
        // ========================================
        // ?? 修復：使用靜態變數保存 TraceListener，確保單例且可釋放
        // ========================================
        private static TextWriterTraceListener _traceListener;
        private static readonly object _traceLock = new object();

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 配置 Kestrel
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
                options.Limits.MaxRequestBufferSize = null;
                options.Limits.MaxConcurrentConnections = 1000;
                options.Limits.MaxConcurrentUpgradedConnections = 1000;
            });

            // 使用 Startup 類別配置服務
            var startup = new Startup(builder.Configuration);
            startup.ConfigureServices(builder.Services);

            var app = builder.Build();

            // ========================================
            // ?? 修復：正確初始化 Trace Listener（線程安全，單例模式）
            // ========================================
            InitializeTraceListener(app.Environment.ContentRootPath);

            // ========================================
            // ?? 新增：GC 監控（僅 Development 環境）
            // 每 10 分鐘記錄一次 GC 統計，監控記憶體使用情況
            // ========================================
            if (app.Environment.IsDevelopment())
            {
                StartGCMonitoring();
            }

            // 使用 Startup 類別配置中間件
            startup.Configure(app, app.Environment, app.Services.GetRequiredService<ILoggerFactory>());

            // ========================================
            // ?? 新增：註冊應用程式關閉事件，確保資源正確釋放
            // ========================================
            var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.Register(() =>
            {
                CleanupTraceListener();
            });

            app.Run();
        }

        /// <summary>
        /// 初始化 Trace Listener（線程安全，單例模式）
        /// 確保 TextWriterTraceListener 只創建一次，並且可以在應用程式關閉時正確釋放
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
                    // 創建日誌目錄
                    var logsDir = Path.Combine(contentRootPath, "Logs");
                    Directory.CreateDirectory(logsDir);
                    var tracePath = Path.Combine(logsDir, "Trace.log");

                    // ? 創建 TextWriterTraceListener 並保存引用（確保可以釋放）
                    _traceListener = new TextWriterTraceListener(tracePath)
                    {
                        Name = "ChurchReportTraceListener"
                    };

                    // 檢查是否已存在相同名稱的 Listener（防止重複添加）
                    var existingListener = Trace.Listeners.Cast<TraceListener>()
                        .FirstOrDefault(l => l.Name == "ChurchReportTraceListener");

                    if (existingListener == null)
                    {
                        Trace.Listeners.Add(_traceListener);
                        Trace.AutoFlush = true;
                        Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Trace listener initialized successfully.");
                    }
                }
                catch (Exception ex)
                {
                    // 初始化失敗，記錄到控制台（避免影響應用程式啟動）
                    Console.WriteLine($"Failed to initialize trace listener: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 清理 Trace Listener（確保資源正確釋放）
        /// 在應用程式關閉時調用，釋放 FileStream 和 StreamWriter
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

                        // ? 從 Listeners 集合中移除
                        Trace.Listeners.Remove(_traceListener);

                        // ? 確保 Flush（將緩衝區數據寫入文件）
                        _traceListener.Flush();

                        // ? 釋放資源（FileStream, StreamWriter）
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
        /// ?? 啟動 GC 監控（Development 環境）
        /// 每 10 分鐘記錄一次 GC 統計，幫助診斷記憶體問題
        /// 記錄內容：Gen0/Gen1/Gen2 收集次數、GC Memory、Private Memory
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
    }
}
