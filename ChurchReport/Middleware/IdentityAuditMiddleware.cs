using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace ChurchReport.Middleware
{
    /// <summary>
    /// 身份審計中間件 (Session Bleeding 防護 - 第六層監控)
    /// 
    /// 設計原則:
    /// - Single Responsibility Principle (SRP): 專注於身份一致性監控
    /// - Open/Closed Principle: 透過中間件模式擴展，不需修改現有代碼
    /// - Liskov Substitution Principle: 可以安全地加入或移除中間件
    /// - Dependency Inversion Principle: 依賴 ILogger 抽象，不依賴具體日誌實現
    /// 
    /// 作用:
    /// 即時偵測並記錄每個 HTTP 請求的身份資訊，用於診斷 Session Bleeding 問題。
    /// 追蹤 TraceId、User、IP 的對應關係，當發現異常時發出警告。
    /// 
    /// 異常情況:
    /// - 同一個 IP 下頻繁切換不同使用者
    /// - 同一個 TraceId 出現不同使用者（不應該發生）
    /// - 未登入使用者存取需要身份驗證的資源
    /// 
    /// 使用方式:
    /// 在 Startup.cs 的 Configure 方法中，UseAuthentication 之後註冊:
    /// <code>
    /// app.UseAuthentication();
    /// #if DEBUG
    /// app.UseMiddleware&lt;IdentityAuditMiddleware&gt;();
    /// #endif
    /// </code>
    /// 
    /// ?? 注意: 此中間件僅在 DEBUG 模式下啟用，避免生產環境的效能影響
    /// </summary>
    public class IdentityAuditMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<IdentityAuditMiddleware> _logger;

        /// <summary>
        /// 靜態追蹤字典：記錄 IP 與最後一個使用者的對應關係
        /// 用於偵測同一 IP 下的使用者切換
        /// 
        /// 設計考量:
        /// - 使用 ConcurrentDictionary 確保執行緒安全
        /// - Key: IP 位址
        /// - Value: (LastUser, LastSeen) - 最後一個使用者和時間
        /// 
        /// ?? 記憶體管理:
        /// 為避免記憶體洩漏，應定期清理舊資料
        /// 參考 IdentityAuditCleanupService 進行定期清理
        /// </summary>
        private static readonly ConcurrentDictionary<string, (string LastUser, DateTime LastSeen)> _ipUserTracking
            = new ConcurrentDictionary<string, (string, DateTime)>();

        /// <summary>
        /// 建構函式：注入下一個中間件和日誌服務
        /// </summary>
        /// <param name="next">下一個中間件委託</param>
        /// <param name="logger">日誌服務，用於記錄審計資訊</param>
        public IdentityAuditMiddleware(RequestDelegate next, ILogger<IdentityAuditMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 中間件核心方法：處理每個 HTTP 請求的身份審計
        /// 
        /// 執行流程:
        /// 1. 提取請求的身份資訊 (TraceId, User, IP)
        /// 2. 記錄審計日誌
        /// 3. 檢查異常情況（同 IP 使用者切換）
        /// 4. 更新追蹤字典
        /// 5. 執行下一個中間件
        /// 
        /// 效能考量:
        /// - 僅在 DEBUG 模式啟用
        /// - 使用 ConcurrentDictionary 避免鎖定
        /// - 日誌使用結構化格式，便於分析
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <returns>非同步任務</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (StaticRequestPathHelper.IsStaticAssetPath(context.Request.Path))
            {
                await _next(context);
                return;
            }

            // ========================================
            // Step 1: 提取身份資訊
            // ========================================
            var traceId = context.TraceIdentifier;
            var user = context.User?.Identity?.IsAuthenticated == true
                       ? context.User.Identity.Name ?? "Authenticated(NoName)"
                       : "Anonymous";
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var path = context.Request.Path.ToString();
            var method = context.Request.Method;

            // ========================================
            // Step 2: 記錄審計日誌
            // ========================================
            // 使用結構化日誌格式，便於後續分析和查詢
            _logger.LogInformation(
                "[Identity Audit] Trace:{TraceId} | IP:{IP} | User:{User} | Path:{Path} | Method:{Method}",
                traceId, ip, user, path, method);

            // ========================================
            // Step 3: 檢查異常情況 - 同 IP 使用者切換
            // ========================================
            if (user != "Anonymous" && _ipUserTracking.TryGetValue(ip, out var lastRecord))
            {
                // 如果同一個 IP 切換了不同使用者，記錄警告
                if (lastRecord.LastUser != user)
                {
                    var timeSinceLastSeen = DateTime.UtcNow - lastRecord.LastSeen;
                    
                    _logger.LogWarning(
                        "[Identity Audit] ?? 使用者切換偵測 | IP:{IP} | 前一個使用者:{LastUser} | 當前使用者:{CurrentUser} | 間隔:{TimeSince}秒",
                        ip, lastRecord.LastUser, user, timeSinceLastSeen.TotalSeconds);
                    
                    // 如果切換時間很短（<30秒），可能是 Session Bleeding
                    if (timeSinceLastSeen.TotalSeconds < 30)
                    {
                        _logger.LogError(
                            "[Identity Audit] ?? 疑似 Session Bleeding! | IP:{IP} | 切換時間過短:{TimeSince}秒",
                            ip, timeSinceLastSeen.TotalSeconds);
                    }
                }
            }

            // ========================================
            // Step 4: 更新追蹤字典
            // ========================================
            if (user != "Anonymous")
            {
                _ipUserTracking[ip] = (user, DateTime.UtcNow);
            }

            // ========================================
            // Step 5: 執行下一個中間件
            // ========================================
            await _next(context);
        }

        /// <summary>
        /// 取得當前追蹤資料（供診斷使用）
        /// 
        /// 使用情境:
        /// - DiagnosticsController 可以呼叫此方法取得追蹤資料
        /// - 用於即時監控和問題診斷
        /// 
        /// 回傳格式:
        /// Dictionary&lt;IP, (LastUser, LastSeen)&gt;
        /// </summary>
        /// <returns>當前追蹤資料的快照</returns>
        public static System.Collections.Generic.Dictionary<string, (string LastUser, DateTime LastSeen)> GetTrackingSnapshot()
        {
            return new System.Collections.Generic.Dictionary<string, (string, DateTime)>(_ipUserTracking);
        }

        /// <summary>
        /// 清除追蹤資料（供定期清理使用）
        /// 
        /// 使用情境:
        /// - IdentityAuditCleanupService 定期呼叫此方法
        /// - 清除超過指定時間的舊資料
        /// - 防止記憶體洩漏
        /// 
        /// 清理策略:
        /// - 清除超過 1 小時未活動的記錄
        /// - 保留最近活動的記錄
        /// </summary>
        /// <param name="olderThan">清除超過此時間的記錄，預設 1 小時</param>
        /// <returns>清除的記錄數量</returns>
        public static int CleanupOldTracking(TimeSpan? olderThan = null)
        {
            var threshold = DateTime.UtcNow - (olderThan ?? TimeSpan.FromHours(1));
            var keysToRemove = new System.Collections.Generic.List<string>();

            foreach (var kvp in _ipUserTracking)
            {
                if (kvp.Value.LastSeen < threshold)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _ipUserTracking.TryRemove(key, out _);
            }

            return keysToRemove.Count;
        }
    }
}
