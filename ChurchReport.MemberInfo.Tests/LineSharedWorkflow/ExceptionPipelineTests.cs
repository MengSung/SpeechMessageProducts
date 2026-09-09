using System.Diagnostics;
using ChurchReport.Logging;
using ChurchReport.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

/// <summary>使用真實 ASP.NET Core 例外處理管線與 ILoggerFactory，保護跨層先落檔再通知契約。</summary>
public sealed class ExceptionPipelineTests
{
    /// <summary>下游拋錯後標準 handler 仍回 500，內側 middleware 先落檔；外層 ILogger 不重複事件。</summary>
    [Fact]
    public async Task Standard_handler_receives_original_failure_after_log_and_line_enqueue()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var exception = new InvalidOperationException("sensitive-body");
        var sent = 0;
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory))
            {
                diagnostics.StartNotifications((message, _) =>
                {
                    Assert.Contains("IncidentId", File.ReadAllText(Path.Combine(directory, "Exception.log")));
                    Interlocked.Increment(ref sent);
                    return Task.CompletedTask;
                });
                var services = new ServiceCollection();
                services.AddLogging(b => b.AddProvider(new ExceptionLoggerProvider(diagnostics)));
                services.AddOptions();
                services.AddMetrics();
                services.AddSingleton(new DiagnosticListener("ExceptionTest"));
                services.AddSingleton(diagnostics);
                await using var provider = services.BuildServiceProvider();
                var builder = new ApplicationBuilder(provider);
                builder.UseExceptionHandler(new ExceptionHandlerOptions
                {
                    ExceptionHandler = context =>
                    {
                        Assert.Same(exception, context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()!.Error);
                        Assert.True(File.Exists(Path.Combine(directory, "Exception.log")));
                        context.Response.StatusCode = 500;
                        return Task.CompletedTask;
                    }
                });
                builder.UseMiddleware<UnhandledExceptionLineNotificationMiddleware>();
                builder.Run(_ => throw exception);
                var context = new DefaultHttpContext { RequestServices = provider };
                await builder.Build()(context);
                Assert.Equal(500, context.Response.StatusCode);
            }
            Assert.Equal(1, sent);
            Assert.Single(File.ReadAllLines(Path.Combine(directory, "Exception.log")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>Error/Critical 即使無 Exception 也必須記錄；scope、formatter 敏感內容不外洩。</summary>
    [Fact]
    public async Task Logger_reports_errors_without_evaluating_sensitive_formatter_or_scope()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory))
            {
                using var factory = LoggerFactory.Create(b => b.AddProvider(new ExceptionLoggerProvider(diagnostics)));
                var logger = factory.CreateLogger("FailureTest");
                using var scope = logger.BeginScope("private-A");
                logger.LogInformation("recovered retry");
                logger.LogError("private-B");
                logger.LogCritical(new Exception("private-C"), "private-D");
                logger.Log(LogLevel.Error, new EventId(42), "private-E", null,
                    (_, _) => throw new InvalidOperationException("Formatter must not run."));
            }
            var records = File.ReadAllLines(Path.Combine(directory, "Exception.log"));
            Assert.Equal(3, records.Count(l => l.Contains("IncidentId")));
            Assert.DoesNotContain("private-", string.Join("", records));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
