/*
檔案整體說明
目的與定位：好牧人 2.0 F2 的唯一 ASP.NET Core 組合根，只組裝固定產品的主機服務，不承接租戶或 LINE 事件業務。
主要功能與流程：Main 依 DOTNET、ASPNETCORE、Production 選定環境，BuildApplication 先驗測試組織與禁止的外送設定，再由獨立註冊入口組合 Hosting、語系、ToolUtility 與共用 Webhook 管線，最後映射健康端點；F3 的單行 Channels 入口以固定路由接上共用原始本文驗簽接收器，不保留無條件成功的回呼。F6以單行Add入口註冊LIFF服務及固定分支；F4以單行入口註冊固定組織重驗與排程核心，未配置時明確拒絕且不開SQL，沒有dashboard或產品工作。詳細授權與資源責任由該模組負責，後續工作包各自維護Add入口。
使用邊界與維護：啟動設定只來自受信任部署來源，不從 request 選產品或組織。Build 成功後 WebApplication 接管組態，Main 是唯一啟停及非同步釋放 owner；不使用會自行 Dispose 主機的 RunAsync。以單次 ApplicationStopping 登錄等待停止，先記錄真正啟動失敗，再 Stop 排空及 Dispose；清理失敗不取代原始例外，登錄與主機均完整釋放。Build 失敗時呼叫端仍擁有組態。正常取消不告警，真正失敗交共用 diagnostics，無可用診斷 owner 的清理失敗走固定 stderr，不建立任何產品 LINE client。沒有 Session、跨使用者快取或自行建立背景 task；離線主機測試不代表 VM、D365、SQL 或 LINE 實機成功。
*/
using System.Runtime.ExceptionServices;
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
            await RunOwnedApplicationAsync(app).ConfigureAwait(false);
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

    /// <summary>
    /// 接管已 Build 的唯一主機；原始啟動例外先交仍存活的診斷入口，再依序停止及釋放。
    /// 停止通知只持有本方法的純完成訊號，不啟動背景工作；登錄在成功與故障路徑均解除。
    /// 任一清理步驟失敗仍嘗試其餘步驟，最後保留第一個真實例外及其原始堆疊。
    /// </summary>
    /// <param name="app">成功後由本方法唯一 Stop／DisposeAsync 的主機，不得再交給 RunAsync。</param>
    private static async Task RunOwnedApplicationAsync(WebApplication app)
    {
        Exception? primary = null;
        ExceptionDiagnostics? diagnostics = null;
        try
        {
            diagnostics = app.Services.GetRequiredService<ExceptionDiagnostics>();
            var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = lifetime.ApplicationStopping.Register(
                static state => ((TaskCompletionSource)state!).TrySetResult(), stopping);
            await app.StartAsync().ConfigureAwait(false);
            await stopping.Task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            primary = exception;
            ReportHostFailure(diagnostics, exception, "GoodShepherd2.Host.Run");
        }
        finally
        {
            try { await app.StopAsync().ConfigureAwait(false); }
            catch (Exception exception)
            {
                primary ??= exception;
                ReportHostFailure(diagnostics, exception, "GoodShepherd2.Host.Stop");
            }
            try { await app.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception)
            {
                primary ??= exception;
                // 容器可能已部分關閉，不能重新解析或建立第二個診斷 owner，也不能蓋掉原始故障。
                ReportHostFailure(diagnostics, exception, "GoodShepherd2.Host.Dispose");
            }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    /// <summary>先使用借入的共用入口；owner 已關閉或不可用時走獨立固定 stderr，診斷自身故障不能阻擋清理。</summary>
    /// <param name="diagnostics">單一主機已解析的 owner，不能在容器關閉後重新取得。</param>
    /// <param name="exception">保留真實型別與程式位置的故障，不序列化訊息或資料。</param>
    /// <param name="operation">本檔固定的 operation 符號，不接受 request 或設定值。</param>
    private static void ReportHostFailure(ExceptionDiagnostics? diagnostics, Exception exception, string operation)
    {
        try
        {
            if (diagnostics is not null && diagnostics.Report(exception, operation)) return;
        }
        catch (Exception)
        {
            // 保留目前的主故障，stderr 僅是診斷通道本身不可用的最後出口。
        }
        try { Console.Error.WriteLine("[GoodShepherd2] HostFailureDiagnosticsUnavailable"); }
        catch (Exception)
        {
            // stderr 也可能由主機關閉；不得因告警出口故障而略過 Stop／Dispose。
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
        Channels.GoodShepherd2ChannelHosting.AddGoodShepherd2Channels(builder);
        Scheduling.GoodShepherd2SchedulingHosting.AddGoodShepherd2Scheduling(builder);
        Liff.GoodShepherd2LiffHosting.AddGoodShepherd2Liff(builder);
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
