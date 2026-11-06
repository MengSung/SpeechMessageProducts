/*
 * 檔案整體說明
 *
 * 目的與定位：Session 監控中介軟體 ? Phase 8: 自動追蹤每個請求的 Session 活動 ?? 僅在 DEBUG 編譯模式下啟用
 *
 * 主要功能與流程：
 * 本檔宣告 SessionMonitoringMiddleware。
 * 主要操作入口包含 InvokeAsync。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Startup.cs 追查目前工作樹中的合作／使用位置。
 */
#if DEBUG
using System.Threading.Tasks;
using ChurchReport.Services.Monitoring;
using Microsoft.AspNetCore.Http;

namespace ChurchReport.Middleware
{
    /// <summary>
    /// Session 監控中介軟體
    /// ? Phase 8: 自動追蹤每個請求的 Session 活動
    /// ?? 僅在 DEBUG 編譯模式下啟用
    /// </summary>
    public class SessionMonitoringMiddleware
    {
        private readonly RequestDelegate _next;

        public SessionMonitoringMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ISessionMonitorService sessionMonitor)
        {
            // 確保 Session 已初始化
            if (context.Session != null && context.Session.IsAvailable)
            {
                // 記錄 Session 活動
                sessionMonitor.RecordSessionActivity(context.Session.Id);
            }

            await _next(context);
        }
    }
}
#endif
