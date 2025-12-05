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
        // 靜態成員變數用來保存 TraceListener，確保單例且可釋放
        // ========================================
        private static TextWriterTraceListener _traceListener;
        private static readonly object _traceLock = new object();

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 設定 Kestrel
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
                options.Limits.MaxRequestBufferSize = null;
                options.Limits.MaxConcurrentConnections = 1000;
                options.Limits.MaxConcurrentUpgradedConnections = 1000;
            });

            // 使用 Startup 類別設定服務
            var startup = new Startup(builder.Configuration);
            startup.ConfigureServices(builder.Services);

            var app = builder.Build();

            // ========================================
            // 修改：只在 Development 環境下初始化 Trace Listener
            // Release 模式下不寫入 Trace.log，減少 I/O 開銷
            // ========================================
            if (app.Environment.IsDevelopment())
            {
                InitializeTraceListener(app.Environment.ContentRootPath);
            }

            // ========================================
            // GC 監控設定（Development 模式）
            // 每 10 分鐘記錄一次 GC 統計，幫助監控記憶體使用
            // ========================================
            if (app.Environment.IsDevelopment())
            {
                StartGCMonitoring();
            }

            // 使用 Startup 類別設定中介層
            startup.Configure(app, app.Environment, app.Services.GetRequiredService<ILoggerFactory>());

            // ========================================
            // 註冊應用程式停止事件，確保資源正確釋放
            // ========================================
            var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.Register(() =>
            {
                if (app.Environment.IsDevelopment())
                {
                    CleanupTraceListener();
                }
            });

            app.Run();
        }

        /// <summary>
        /// 初始化 Trace Listener（僅在 Development 環境下執行）
        /// 確保 TextWriterTraceListener 只建立一次，並且可以在應用程式停止時正確釋放
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
                    // 建立日誌目錄
                    var logsDir = Path.Combine(contentRootPath, "Logs");
                    Directory.CreateDirectory(logsDir);
                    var tracePath = Path.Combine(logsDir, "Trace.log");

                    // 建立 TextWriterTraceListener 並保存參考（確保可以釋放）
                    _traceListener = new TextWriterTraceListener(tracePath)
                    {
                        Name = "ChurchReportTraceListener"
                    };

                    // 檢查是否已存在相同名稱的 Listener（避免重複新增）
                    var existingListener = Trace.Listeners.Cast<TraceListener>()
                        .FirstOrDefault(l => l.Name == "ChurchReportTraceListener");

                    if (existingListener == null)
                    {
                        Trace.Listeners.Add(_traceListener);
                        Trace.AutoFlush = true;
                        Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Trace listener initialized successfully (Development mode).");
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
        /// 啟動 GC 監控（Development 模式）
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
    }
}
