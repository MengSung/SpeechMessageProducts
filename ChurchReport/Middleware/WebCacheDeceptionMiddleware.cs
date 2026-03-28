using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace ChurchReport.Middleware
{
    /// <summary>
    /// Web Cache Deception 防護中間件
    /// 
    /// 攻擊原理：
    /// 攻擊者在動態頁面 URL 後附加靜態檔案副檔名（如 /Home/IntegrateView/evil.css），
    /// CDN 或反向代理誤認為靜態資源進行快取，導致後續使用者取得前一位使用者的個人頁面。
    /// 
    /// 防護策略：
    /// 1. 偵測請求路徑是否以常見靜態檔案副檔名結尾
    /// 2. 排除合法靜態資源目錄（/css/, /js/, /lib/, /images/, /assets/）
    /// 3. 對非法路徑直接回傳 404，阻斷攻擊鏈
    /// 4. 額外設定 X-Content-Type-Options: nosniff 防止瀏覽器嗅探內容類型
    /// 
    /// 部署位置：
    /// 必須在 UseStaticFiles 之前、全站無快取中間件之後執行
    /// </summary>
    public class WebCacheDeceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<WebCacheDeceptionMiddleware> _logger;

        /// <summary>
        /// 需要攔截的靜態檔案副檔名清單
        /// 攻擊者常用這些副檔名欺騙快取層
        /// </summary>
        private static readonly string[] StaticFileExtensions = new[]
        {
            ".css", ".js", ".png", ".jpg", ".jpeg", ".gif", ".svg",
            ".ico", ".woff", ".woff2", ".ttf", ".eot",
            ".map", ".json", ".xml", ".txt", ".pdf",
            ".webp", ".avif", ".bmp", ".tiff"
        };

        /// <summary>
        /// 合法靜態資源目錄前綴
        /// 這些目錄下的請求不需要攔截（它們本來就是靜態檔案）
        /// </summary>
        private static readonly string[] LegitimateStaticPaths = new[]
        {
            "/css/", "/js/", "/lib/", "/images/", "/assets/",
            "/fonts/", "/favicon.ico", "/.well-known/",
            "/devextreme/", "/wwwroot/"
        };

        public WebCacheDeceptionMiddleware(RequestDelegate next, ILogger<WebCacheDeceptionMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 中間件核心方法：偵測並阻擋 Web Cache Deception 攻擊
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.ToString();

            // 只檢查 GET 請求（Cache Deception 攻擊只對 GET 有效）
            if (string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                // 步驟 1：檢查路徑是否以靜態檔案副檔名結尾
                if (HasStaticFileExtension(path))
                {
                    // 步驟 2：排除合法靜態資源目錄
                    if (!IsLegitimateStaticPath(path))
                    {
                        // 偵測到 Web Cache Deception 攻擊嘗試
                        _logger.LogWarning(
                            "[WebCacheDeception] ⛔ 偵測到可疑請求 | Path:{Path} | IP:{IP} | UA:{UA}",
                            path,
                            context.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                            context.Request.Headers["User-Agent"].ToString());

                        // 直接回傳 404，阻斷攻擊鏈
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        context.Response.Headers["Cache-Control"] = "no-store";
                        return;
                    }
                }
            }

            await _next(context);
        }

        /// <summary>
        /// 檢查路徑是否以靜態檔案副檔名結尾
        /// </summary>
        private static bool HasStaticFileExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            foreach (var ext in StaticFileExtensions)
            {
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 檢查路徑是否為合法靜態資源目錄
        /// </summary>
        private static bool IsLegitimateStaticPath(string path)
        {
            var lowerPath = path.ToLowerInvariant();

            foreach (var staticPath in LegitimateStaticPaths)
            {
                if (lowerPath.StartsWith(staticPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
