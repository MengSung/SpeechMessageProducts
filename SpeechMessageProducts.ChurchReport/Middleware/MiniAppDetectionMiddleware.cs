/*
 * 檔案整體說明
 *
 * 目的與定位：LINE Mini App 環境偵測中間件。 偵測請求是否來自 LINE LIFF Browser（LINE App 內建瀏覽器）， 並將偵測結果存入 HttpContext.Items 供後續 
 * Controller / View 使用。 使用方式： - 在 Controller 中：var isMiniApp = (bool)(HttpContext.Items["IsLineMiniApp"] ?? 
 * false); - 在 Razor View 中：var isMiniApp = (bool)(Context.Items["IsLineMiniApp"] ?? false); 📖 詳細說明請參閱：文件\Line 
 * Mini App\好牧人-LINE-Mini-App-導入佈署步驟.md 第八章
 *
 * 主要功能與流程：
 * 本檔宣告 MiniAppDetectionMiddleware。
 * - 中間件執行方法。 分析 User-Agent 標頭，判斷請求是否來自 LINE LIFF Browser， 並將結果存入 HttpContext.Items。
 * 主要操作入口包含 InvokeAsync。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Startup.cs 追查目前工作樹中的合作／使用位置。
 */
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
            // ✅ 效能：靜態資源不需要 Mini App 偵測。
            // 這些請求不會進到 Controller 或 View，也就沒有人會讀取 HttpContext.Items，
            // 沒有必要為每個 CSS/JS/圖片請求解析 User-Agent 並寫兩筆 Items。
            if (ChurchReport.Middleware.StaticRequestPathHelper.IsStaticAssetPath(context.Request.Path))
            {
                await _next(context);
                return;
            }

            // ✅ 效能：Headers["User-Agent"] 回傳 StringValues。原本呼叫 .ToString() 會在
            // 多值情形下配置新字串；改用索引取單一值可讓常見的單值情形零配置。
            var userAgentValues = context.Request.Headers.UserAgent;
            var userAgent = userAgentValues.Count == 1 ? userAgentValues[0] : userAgentValues.ToString();
            userAgent ??= string.Empty;

            var isLineBrowser = userAgent.Length != 0 &&
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
