/*
檔案整體說明
目的與定位：F7在好牧人2.0主機的唯一公開圖片組裝入口，以Program一行啟用固定/assets唯讀分支及中立store；沒有匿名上傳、租戶授權或產品推播邏輯。
主要功能與流程：從受信任部署讀PublicAssets啟動快照，兩欄皆缺時停用，部分設定或非法設定拒絕啟動。DI以單一factory擁有store，startup filter先解析驗證，再只接受固定產品／非空租戶N格式／SHA16與隨機16的PNG名稱；每次開啟前拒絕每層reparse，只提供有界正式PNG的GET／HEAD及immutable、nosniff標頭，不列目錄、不建立cookie或檔案監聽。
使用邊界與維護：實體根為專用owner及ACL保護的公開資料，不接受HTTP指定根；路由scope不能當授權證明。讀取串流只由該request擁有，取消或故障由await using關閉，停止HTTP後DI排空與處置store；沒有cache、timer、跨請求圖片或credential。來源與例外不輸出本文；分支位於Program例外middleware外側，故真正故障明確交共用diagnostics，caller取消及不存在檔案不告警。
失敗與驗證：非法路由與缺檔回空404，非caller故障回空500並保留真Exception供共用安全日誌；已開始回應則重新拋出中止。依專用ACL前提防止目錄逸出，不宣稱抵抗本機管理員替換競態。真loopback測試驗證接線、分享違規與Host owner清理，不代替公開HTTPS或LINE實機。
*/
using SpeechMessage.Assets;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Assets;

/// <summary>只由受信任主機組合根使用的F7入口；產品設定未啟用時沒有素材服務或目錄副作用。</summary>
internal static class AssetHostingExtensions
{
    /// <summary>建立不reload的部署快照與唯一DI owner，不建第二個容器或立即讀取使用者檔案。</summary>
    /// <param name="builder">已完成F2測試組織及環境守門的同一主機builder。</param>
    internal static void AddGoodShepherd2PublicAssets(WebApplicationBuilder builder)
    {
        var root = builder.Configuration["PublicAssets:RootPath"];
        var baseUrl = builder.Configuration["PublicAssets:PublicBaseUrl"];
        if (string.IsNullOrEmpty(root) && string.IsNullOrEmpty(baseUrl)) return;
        if (string.IsNullOrWhiteSpace(root) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var url))
            throw new InvalidOperationException("Public asset deployment configuration is incomplete.");
        if (!url.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            url.AbsolutePath != "/assets/" || url.Query.Length != 0 || url.Fragment.Length != 0 || url.UserInfo.Length != 0)
            throw new InvalidOperationException("Public asset URL must be the fixed HTTPS /assets/ route.");
        var options = new AssetOptions(root, url);
        builder.Services.AddSingleton(options);
        // 只註冊介面factory，讓容器唯一擁有可處置實體；不能以第二個alias註冊重複捕獲同一store。
        builder.Services.AddSingleton<IPublicAssetStore>(_ => new FileSystemPublicAssetStore(options));
        builder.Services.AddTransient<IStartupFilter, PublicAssetStartupFilter>();
    }
}

/// <summary>把固定公開素材分支接入Program；只保存無秘密部署快照，沒有使用者或request參照。</summary>
/// <param name="options">受信任主機的固定根與HTTPS設定，不由URL決定。</param>
internal sealed class PublicAssetStartupFilter(AssetOptions options) : IStartupFilter
{
    /// <summary>啟動先解析由DI擁有的store以驗證設定；分支有自己的安全例外邊界，其餘路由仍交原next。</summary>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        _ = app.ApplicationServices.GetRequiredService<IPublicAssetStore>();
        app.Map("/assets", branch => branch.Run(ServeAsync));
        next(app);
    };

    /// <summary>單次唯讀串流的唯一owner；先開啟並確認長度才寫成功標頭，所有故障及取消均確定關閉handle。</summary>
    private async Task ServeAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        try
        {
            if ((!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) ||
                !TryGetRelativePath(context.Request.Path.Value, out var relative))
            { context.Response.StatusCode = 404; return; }
            var root = Path.GetFullPath(options.RootPath);
            var filePath = Path.Combine(root, relative);
            EnsureRegularPath(filePath);
            await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length is <= 0 or > 300 * 1024)
                throw new InvalidDataException("The published asset violates the fixed size contract.");
            context.Response.ContentType = "image/png";
            context.Response.ContentLength = file.Length;
            context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            context.Response.Headers.ETag = "\"" + Path.GetFileName(relative) + "\"";
            if (HttpMethods.IsGet(context.Request.Method))
                await file.CopyToAsync(context.Response.Body, 64 * 1024, context.RequestAborted).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { context.Response.StatusCode = 404; }
        catch (DirectoryNotFoundException) { context.Response.StatusCode = 404; }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            context.RequestServices.GetRequiredService<ExceptionDiagnostics>()
                .Report(error, "GoodShepherd2.Assets.Http", context.RequestAborted);
            if (context.Response.HasStarted) throw;
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Remove("ETag");
            context.Response.ContentType = null;
            context.Response.ContentLength = 0;
            context.Response.StatusCode = 500;
        }
    }

    /// <summary>只接受store能產生的固定路由；先限制長度再split，拒絕編碼穿越、替代分隔、空GUID及臨時檔。</summary>
    private static bool TryGetRelativePath(string? path, out string relative)
    {
        relative = "";
        if (path is null || path.Length > 128) return false;
        var parts = path.Split('/');
        if (parts.Length != 4 || parts[0].Length != 0 ||
            parts[1] is not ("GoodShepherd2" or "GoodButler" or "GoodGuild") ||
            !Guid.TryParseExact(parts[2], "N", out var tenant) || tenant == Guid.Empty ||
            parts[2] != tenant.ToString("N")) return false;
        var name = parts[3];
        if (name.Length != 37 || name[16] != '-' || !name.EndsWith(".png", StringComparison.Ordinal)) return false;
        for (var index = 0; index < 33; index++)
            if (index != 16 && name[index] is not (>= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        relative = Path.Combine(parts[1], parts[2], name);
        return true;
    }

    /// <summary>檔案與每層祖先皆不得是reparse；缺檔由上層固定404，其他IO或權限故障保留原例外交共用告警。</summary>
    private static void EnsureRegularPath(string filePath)
    {
        var attributes = File.GetAttributes(filePath);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException("The public asset is not a regular file.");
        for (var current = new DirectoryInfo(Path.GetDirectoryName(filePath)!); current is not null; current = current.Parent)
        {
            if ((File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The public asset directory boundary is not regular.");
        }
    }
}
