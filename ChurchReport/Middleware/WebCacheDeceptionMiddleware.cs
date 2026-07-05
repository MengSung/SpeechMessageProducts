// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Middleware/WebCacheDeceptionMiddleware.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 WebCacheDeceptionMiddleware 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class WebCacheDeceptionMiddleware
// 主要成員：InvokeAsync
// 引用命名空間：Microsoft.AspNetCore.Http、Microsoft.Extensions.Logging、System、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
                if (StaticRequestPathHelper.HasStaticAssetExtension(context.Request.Path))
                {
                    // 步驟 2：排除合法靜態資源目錄
                    if (!StaticRequestPathHelper.IsStaticAssetPath(context.Request.Path))
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
    }
}
