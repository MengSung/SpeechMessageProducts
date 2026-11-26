/*
檔案整體說明
目的與定位：F6產品主機的唯一註冊入口，以Program一行組合共用token/filter、產品actor及固定LIFF路由，不修改線A的架構或Channel實作。
主要功能與流程：啟動讀公開Channel映射，配置齊全時由Host顯式初始化及驗證一次平台選擇表；未配置時拒絕登入且不開SQL。scoped產品resolver與共用filter每請求重建身分，F8前無預設角色。IStartupFilter將公開靜態LIFF及有filter的API分支接入同一Host，其他路由仍委派既有管線。
使用邊界與維護：設定不取自query；只在Development建立此功能。靜態FileProvider及SQL啟動服務由DI唯一擁有，HTTP停止後釋放，沒有自建背景工作或跨請求使用者快取。分支在F2使用者管線之外，故真正故障明確交同一共用diagnostics並回固定500，不輸出例外本文；filter本身的預期401／403不告警。LIFF app建立與外部部署另驗，不以本機頁面200冒稱實機。
*/
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using SpeechMessage.Liff.AspNetCore;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Liff;

/// <summary>只由受信任組合根呼叫的F6產品註冊，沒有對外產品業務API。</summary>
internal static class GoodShepherd2LiffHosting
{
    /// <summary>由單一builder建立部署快照、服務及startup filter，不建置隱藏容器或立刻建立外部連線。</summary>
    /// <param name="builder">已通過F2環境與組織守門的同一主機builder。</param>
    internal static void AddGoodShepherd2Liff(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("LIFF test hosting requires Development.");
        var configuration = builder.Configuration;
        var options = new LiffOptions(configuration["Liff:LoginChannelId"] ?? "",
            configuration["Liff:MessagingChannelKey"] ?? "", configuration["Liff:LiffId"] ?? "");
        if ((!string.IsNullOrEmpty(options.LoginChannelId) || !string.IsNullOrEmpty(options.MessagingChannelKey)) && !options.IsConfigured)
            throw new InvalidOperationException("LIFF channel mapping is incomplete.");
        if (options.LiffId.Length > 128 || options.LiffId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new InvalidOperationException("LIFF application identity is invalid.");
        builder.Services.AddLiffAuthentication(options);
        builder.Services.TryAddScoped<GoodShepherd2LiffActorResolver>();
        builder.Services.TryAddScoped<ILiffActorResolver>(p => p.GetRequiredService<GoodShepherd2LiffActorResolver>());
        if (!builder.Services.Any(d => d.ServiceType == typeof(ICurrentTenantSelectionStore)))
        {
            if (options.IsConfigured)
            {
                var connection = configuration.GetConnectionString("Platform");
                if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("LIFF selection storage is not configured.");
                builder.Services.AddSingleton(new SqlCurrentTenantSelectionStore(connection));
                builder.Services.AddSingleton<ICurrentTenantSelectionStore>(p => p.GetRequiredService<SqlCurrentTenantSelectionStore>());
                builder.Services.AddHostedService<LiffSelectionSchemaStartup>();
            }
            else builder.Services.AddSingleton<ICurrentTenantSelectionStore, UnconfiguredSelectionStore>();
        }
        builder.Services.AddSingleton<LiffStaticFiles>();
        builder.Services.AddTransient<IStartupFilter, LiffStartupFilter>();
    }
}

/// <summary>唯一Host啟動工作，先明確建立／驗表一次才開始HTTP；沒有常駐工作或計時器。</summary>
/// <param name="store">單一部署平台資料庫的選擇store，不保存任何request。</param>
internal sealed class LiffSelectionSchemaStartup(SqlCurrentTenantSelectionStore store) : IHostedService
{
    /// <summary>啟動前顯式初始化／驗證表，故障交Main診斷，不能讓第一筆request偷偷建表。</summary>
    public Task StartAsync(CancellationToken cancellationToken) => store.InitializeAsync(cancellationToken);
    /// <summary>沒有持續中的工作或資源，不以停止token開始額外I/O。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>未提供部署映射時的fail closed儲存；不開SQL、不以記憶體假裝平台持久化。</summary>
internal sealed class UnconfiguredSelectionStore : ICurrentTenantSelectionStore
{
    /// <summary>未配置不可讀取選擇，不把設定錯誤當成沒有選擇。</summary>
    public Task<Guid?> GetAsync(string product, string channelKey, string lineUserId, CancellationToken ct) =>
        throw new InvalidOperationException("LIFF selection storage is not configured.");
    /// <summary>未配置不可保存選擇，不建立後備資料或跨請求字典。</summary>
    public Task SetAsync(string product, string channelKey, string lineUserId, Guid contactId, CancellationToken ct) =>
        throw new InvalidOperationException("LIFF selection storage is not configured.");
}

/// <summary>本Host唯一LIFF檔案provider owner，實體路徑來自部署webroot而不是request。</summary>
internal sealed class LiffStaticFiles : IDisposable
{
    /// <summary>沒有部署檔案時為null，健康與空主機測試不會因不存在wwwroot而失敗。</summary>
    internal PhysicalFileProvider? Provider { get; }
    /// <summary>只開既有固定liff資料夾，不建立目錄或監聽使用者指定路徑。</summary>
    public LiffStaticFiles(IWebHostEnvironment environment)
    {
        var root = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "liff");
        if (Directory.Exists(root)) Provider = new PhysicalFileProvider(root);
    }
    /// <summary>HTTP排空後由DI釋放檔案監聽provider，沒有其他共享owner。</summary>
    public void Dispose() => Provider?.Dispose();
}

/// <summary>把F6分支接在既有HTTP管線之前；分支自己完整處理終端故障，其他路由仍交原next。</summary>
/// <param name="files">DI擁有的固定靜態資料夾，只借用不關閉。</param>
internal sealed class LiffStartupFilter(LiffStaticFiles files) : IStartupFilter
{
    /// <summary>建立固定/liff及/api/liff兩個分支，不改F2健康或其他工作包的routing。</summary>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Map("/liff", page =>
        {
            AddSafeFailureBoundary(page);
            if (files.Provider is { } provider)
            {
                page.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
                page.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = provider, ContentTypeProvider = new FileExtensionContentTypeProvider(),
                    OnPrepareResponse = context =>
                    {
                        context.Context.Response.Headers.CacheControl = "no-store";
                        context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                        context.Context.Response.Headers["Referrer-Policy"] = "no-referrer";
                    }
                });
            }
            page.Run(context => { context.Response.StatusCode = 404; return Task.CompletedTask; });
        });
        app.Map("/api/liff", api =>
        {
            AddSafeFailureBoundary(api);
            api.UseRouting();
            api.UseEndpoints(WhoAmIEndpoint.MapEndpoints);
        });
        next(app);
    };

    /// <summary>F6分支最外層的固定安全失敗回應；caller取消原樣向上，真正故障保留真實Exception交同一owner。</summary>
    private static void AddSafeFailureBoundary(IApplicationBuilder branch) =>
        branch.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            try { await next(context).ConfigureAwait(false); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                context.RequestServices.GetRequiredService<ExceptionDiagnostics>()
                    .Report(exception, "GoodShepherd2.Liff.Http", context.RequestAborted);
                if (context.Response.HasStarted) throw;
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
        });
}
