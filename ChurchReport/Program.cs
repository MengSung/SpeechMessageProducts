using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ChurchReport
{
    public class Program
    {
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

            // 創建日誌目錄
            var logsDir = Path.Combine(app.Environment.ContentRootPath, "Logs");
            Directory.CreateDirectory(logsDir);
            var tracePath = Path.Combine(logsDir, "Trace.log");

            // 添加文件追蹤監聽器
            if (!Trace.Listeners.OfType<TextWriterTraceListener>().Any(l =>
                (l.Writer as StreamWriter)?.BaseStream is FileStream fs && fs.Name == tracePath))
            {
                Trace.Listeners.Add(new TextWriterTraceListener(tracePath));
                Trace.AutoFlush = true;
            }

            // 使用 Startup 類別配置中間件
            startup.Configure(app, app.Environment, app.Services.GetRequiredService<ILoggerFactory>());

            app.Run();
        }
    }
}
