/*
檔案整體說明
目的與定位：好牧人 2.0 F2 的唯一 ASP.NET Core 組合根，只組裝固定產品的主機服務，不承接租戶或 LINE 事件業務。
主要功能與流程：Main 依 DOTNET、ASPNETCORE、Production 選定環境，BuildApplication 先驗測試組織與禁止的外送設定，再由獨立註冊入口組合 Hosting、語系、ToolUtility 與共用 Webhook 管線，最後映射健康與空 Webhook。後續工作包各自維護 Add 入口。
使用邊界與維護：啟動設定只來自受信任部署來源，不從 request 選產品或組織。Build 成功後 WebApplication 接管組態，Main 等待主機停止及非同步清理；Build 失敗時呼叫端仍擁有組態。正常取消不告警，真正失敗交共用 diagnostics，啟動失敗只用固定本地診斷，不建立任何產品 LINE client。沒有 Session、跨使用者快取或自行建立背景 task；離線主機測試不代表 VM、D365、SQL 或 LINE 實機成功。
*/
using SpeechMessageProducts.GoodShepherd2.Web.Health;
using SpeechMessageProducts.GoodShepherd2.Web.Startup;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web;

/// <summary>固定 GoodShepherd2 產品的主機入口；所有服務與設定均以當次主機為唯一 owner。</summary>
public static class Program
{
    /// <summary>建立主機並完整等待關閉；建立失敗時釋放尚未移交的組態，執行失敗時先上報再釋放主機。</summary>
    /// <param name="args">受信任程序參數；環境邊界仍由 D27 的程序環境優先序決定。</param>
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            EnvironmentName = GoodShepherd2StartupGuard.ResolveEnvironmentName(
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"))
        });
        WebApplication? app = null;
        try
        {
            app = BuildApplication(builder);
            await using (app.ConfigureAwait(false))
            {
                try { await app.RunAsync().ConfigureAwait(false); }
                catch (Exception exception)
                {
                    app.Services.GetRequiredService<ExceptionDiagnostics>().Report(exception, "GoodShepherd2.Host.Run");
                    throw;
                }
            }
        }
        catch (Exception exception) when (app is null)
        {
            await ReportStartupFailureAsync(builder, exception).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (app is null) ((IDisposable)builder.Configuration).Dispose();
        }
    }

    /// <summary>借用部署 builder 建立尚未開始接收 HTTP 的主機；成功後由主機接管組態，失敗則仍由呼叫端清理。</summary>
    /// <param name="builder">單一組合根擁有的 builder；不得從 request 建立或重用。</param>
    /// <returns>呼叫端必須 Start／Stop／DisposeAsync 的完整主機。</returns>
    public static WebApplication BuildApplication(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        GoodShepherd2StartupGuard.Validate(builder.Configuration, builder.Environment);
        builder.Services.AddGoodShepherd2Foundation(builder.Configuration, builder.Environment, builder.Logging);
        var app = builder.Build();
        app.UseExceptionHandler(error => error.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        }));
        app.Use(async (context, next) =>
        {
            try { await next(context).ConfigureAwait(false); }
            catch (Exception exception)
            {
                context.RequestServices.GetRequiredService<ExceptionDiagnostics>()
                    .Report(exception, "GoodShepherd2.Http", context.RequestAborted);
                throw;
            }
        });
        app.MapHealthEndpoints();
        app.MapPost("/webhook/{channelKey}", () => Results.Ok());
        return app;
    }

    /// <summary>Build 前沒有 DI owner 的真正失敗使用共用安全記錄器；遵守寫檔開關，不建立 LINE 傳送者或留下資源。</summary>
    /// <param name="builder">仍由 Main 擁有的組態與部署路徑。</param>
    /// <param name="exception">保留真實例外供安全型別／位置記錄，不序列化原始訊息。</param>
    private static async Task ReportStartupFailureAsync(WebApplicationBuilder builder, Exception exception)
    {
        try
        {
            var options = ExceptionOutputOptions.FromConfiguration(builder.Configuration);
            await using var diagnostics = new ExceptionDiagnostics(
                Path.Combine(builder.Environment.ContentRootPath, "Logs"),
                outputOptions: new ExceptionOutputOptions(options.WriteExceptionLog, false));
            diagnostics.Report(exception, "GoodShepherd2.Host.Startup");
        }
        catch (Exception)
        {
            // 診斷本身失敗只交獨立 stderr，不能遞迴通知或取代正在傳播的原始錯誤。
            Console.Error.WriteLine("[GoodShepherd2] StartupDiagnosticsFailed");
        }
    }
}
