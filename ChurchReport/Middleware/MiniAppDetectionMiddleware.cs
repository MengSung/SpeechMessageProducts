using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace ChurchReport.Middleware
{
    /// <summary>
    /// LINE Mini App 環境偵測中間件。
    /// 偵測請求是否來自 LINE LIFF Browser（LINE App 內建瀏覽器），
    /// 並將偵測結果存入 HttpContext.Items 供後續 Controller / View 使用。
    /// 
    /// 使用方式：
    ///   - 在 Controller 中：var isMiniApp = (bool)(HttpContext.Items["IsLineMiniApp"] ?? false);
    ///   - 在 Razor View 中：var isMiniApp = (bool)(Context.Items["IsLineMiniApp"] ?? false);
    /// 
    /// 📖 詳細說明請參閱：文件\Line Mini App\好牧人-LINE-Mini-App-導入佈署步驟.md 第八章
    /// </summary>
    public class MiniAppDetectionMiddleware
    {
        private readonly RequestDelegate _next;

        /// <summary>
        /// 建構函式，注入下一個中間件委派。
        /// </summary>
        /// <param name="next">下一個中間件委派。</param>
        public MiniAppDetectionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        /// <summary>
        /// 中間件執行方法。
        /// 分析 User-Agent 標頭，判斷請求是否來自 LINE LIFF Browser，
        /// 並將結果存入 HttpContext.Items。
        /// </summary>
        /// <param name="context">HTTP 上下文。</param>
        public async Task InvokeAsync(HttpContext context)
        {
            // ========================================
            // 偵測 User-Agent 是否包含 LINE 相關標識
            // ========================================
            // LINE LIFF Browser 的 User-Agent 通常包含以下字串：
            //   - "Line/"    → LINE App 的內建瀏覽器
            //   - "LIFF"     → LIFF Browser 特有標識
            // 範例 User-Agent：
            //   Mozilla/5.0 ... Line/12.0.0 LIFF/2.21.0 ...
            var userAgent = context.Request.Headers["User-Agent"].ToString();

            var isLineBrowser = !string.IsNullOrEmpty(userAgent) &&
                (userAgent.Contains("Line/", StringComparison.OrdinalIgnoreCase) ||
                 userAgent.Contains("LIFF", StringComparison.OrdinalIgnoreCase));

            // ========================================
            // 將偵測結果存入 HttpContext.Items
            // ========================================
            // 這樣後續的 Controller 和 View 可以直接取用，
            // 不需要重複解析 User-Agent。
            context.Items["IsLineMiniApp"] = isLineBrowser;
            context.Items["UserAgent"] = userAgent;

            // 繼續執行下一個中間件
            await _next(context);
        }
    }
}
