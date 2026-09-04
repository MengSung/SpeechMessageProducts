// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Middleware/SessionValidationMiddleware.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 SessionValidationMiddleware 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class SessionValidationMiddleware
// 主要成員：InvokeAsync、ShouldExcludePath、GetRealClientIp、ClearSessionAndRedirectToLogin
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
    /// Session 驗證中間件 - 防止 Session Bleeding 和 Session Hijacking
    ///
    /// 設計模式：
    /// - Middleware Pattern: 在請求管道中驗證 Session 合法性
    /// - Single Responsibility: 專注於 Session 安全驗證
    /// - Fail-Fast Principle: 發現問題立即中斷請求
    ///
    /// 功能：
    /// 1. 驗證 Session 中的用戶身份標識是否一致
    /// 2. 檢查 User-Agent 是否發生變化（防劫持）
    /// 3. 驗證 IP 地址是否合理（考慮代理模式）
    /// 4. 防止跨用戶 Session 洩漏
    ///
    /// 核心防護場景：
    /// - A 登入 WiFi → B 登入 WiFi 看到 A 網頁 ? 防止
    /// - Session 被後登入的人繼承/共用 ? 防止
    /// - Session Hijacking（會話劫持） ? 防止
    ///
    /// 使用方式：
    /// 在 Startup.cs 的 Configure 方法中，在 UseSession 之後加入：
    /// <code>
    /// app.UseSession();
    /// app.UseMiddleware&lt;SessionValidationMiddleware&gt;();
    /// </code>
    /// </summary>
    public class SessionValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SessionValidationMiddleware> _logger;

        /// <summary>
        /// 排除清單：這些路徑不需要 Session 驗證
        /// - 登入相關路徑
        /// - LINE LIFF 相關路徑（重要！）
        /// - 公開資源路徑
        /// - 健康檢查路徑
        /// </summary>
        private static readonly string[] ExcludedPaths = new[]
        {
            "/login",
            "/authentication/login",
            "/authentication/processlogin",
            "/authentication/lineidloginview",
            "/authentication/saveuserlineid",
            "/authentication/processlinelogin",
            "/authentication/linebinding",           // ? LINE 綁定相關
            "/authentication/linenotify",            // ? LINE Notify
            "/liff",                                 // ? LIFF 相關路徑（通用）
            "/logout",
            "/authentication/logout",
            "/health",
            "/favicon.ico",
            "/assets/",
            "/css/",
            "/js/",
            "/lib/",
            "/images/",                              // ? 圖片資源
            "/.well-known/"                          // ? OIDC/OAuth 相關
        };

        /// <summary>
        /// 建構函式：注入下一個中間件和日誌服務
        /// </summary>
        public SessionValidationMiddleware(RequestDelegate next, ILogger<SessionValidationMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 中間件核心方法：驗證每個 HTTP 請求的 Session 合法性
        ///
        /// 驗證流程：
        /// 1. 檢查路徑是否需要驗證
        /// 2. 檢查是否已登入（是否有 Session 資料）
        /// 3. 驗證 User-Agent 是否一致
        /// 4. 驗證 IP 地址是否合理
        /// 5. 通過驗證後繼續下一個中間件
        ///
        /// 安全考量：
        /// - 使用結構化日誌格式，便於監控
        /// - 發現異常立即清除 Session 並重導向登入
        /// - 防止資訊洩漏（不在回應中暴露敏感資訊）
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            // ✅ 效能：不再呼叫 ToLowerInvariant()。
            // ShouldExcludePath 與 IsStaticAssetPath 都已使用 OrdinalIgnoreCase 比較，
            // 事先轉小寫只是在每個請求上多配置一個字串。
            var path = context.Request.Path;

            // ========================================
            // Step 1: 檢查是否為排除路徑或靜態資源
            // ========================================
            // ✅ 效能：靜態資源不含任何使用者狀態，不需要 Session 驗證。
            // 原本只排除 /css/、/js/、/lib/、/images/ 等少數前綴，
            // /fonts/、/assets/ 下的字型與 .map 檔仍會走完整驗證並強制載入 Session。
            //
            // ⚠️ PathString.Value 在路徑為空（例如 PathBase 剝離後剩下空字串）時會是 null，
            // 而 ShouldExcludePath 內部會呼叫 string.StartsWith。因此這裡必須做 null 保護，
            // 不能直接把 path.Value 傳進去。原本的 Path.ToString() 內建了這個保護
            // （回傳 Value ?? string.Empty），移除它時必須自己補上。
            if (ChurchReport.Middleware.StaticRequestPathHelper.IsStaticAssetPath(path)
                || ShouldExcludePath(path.Value ?? string.Empty))
            {
                await _next(context);
                return;
            }

            // ========================================
            // Step 2: 檢查是否已登入
            // ========================================
            // ✅ 效能：先明確 await LoadAsync()，再讀取 Session。
            //
            // ISession.GetString 在 Session 尚未載入時，內部會執行
            // LoadAsync().GetAwaiter().GetResult() —— 也就是在處理請求的執行緒上
            // 同步阻塞等待分散式快取。這條中介層位於每個動態請求的必經路徑，
            // 等於每個請求都付一次同步阻塞成本，高併發時會造成執行緒池飢餓。
            //
            // 明確 await 之後，後續所有 GetString/SetString 都命中已載入的記憶體副本，
            // 不再有任何同步阻塞。這是官方建議的用法。
            await context.Session.LoadAsync().ConfigureAwait(false);

            // 如果 Session 中沒有用戶 ID，表示未登入或 Session 已過期
            var sessionUserId = context.Session.GetString("_SessionUserId");
            if (string.IsNullOrEmpty(sessionUserId))
            {
                // 未登入，允許繼續（會被其他驗證機制處理）
                await _next(context);
                return;
            }

            // ========================================
            // Step 3: 驗證 User-Agent（防 Session Hijacking）
            // ========================================
            // 這裡讀取 Session 已經不會有任何 I/O，因為上面的 LoadAsync 已把整份 Session
            // 載入記憶體；後續所有 GetString/SetString 都只是字典操作。
            var currentUserAgent = context.Request.Headers["User-Agent"].ToString();
            var sessionUserAgent = context.Session.GetString("_SessionUserAgent");

            // ? 特殊處理：LINE 內建瀏覽器的 User-Agent 可能變化
            // LINE 瀏覽器的 User-Agent 包含 "Line/" 字串
            bool isLineUserAgent = currentUserAgent.Contains("Line/", StringComparison.OrdinalIgnoreCase);
            bool wasLineUserAgent = !string.IsNullOrEmpty(sessionUserAgent) &&
                                   sessionUserAgent.Contains("Line/", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(sessionUserAgent) && currentUserAgent != sessionUserAgent)
            {
                // ? 如果是 LINE 瀏覽器，允許 User-Agent 變化（LINE 內建瀏覽器可能有版本更新）
                if (isLineUserAgent && wasLineUserAgent)
                {
                    _logger.LogInformation(
                        "[Session Validation] ?? LINE User-Agent 變化（允許） | UserId:{UserId}",
                        sessionUserId);

                    // 更新 Session 中的 User-Agent
                    context.Session.SetString("_SessionUserAgent", currentUserAgent);
                }
                else
                {
                    // 非 LINE 瀏覽器的 User-Agent 變化 → 可能是 Session Hijacking
                    _logger.LogWarning(
                        "[Session Validation] ?? User-Agent 不一致 | UserId:{UserId} | Session UA:{SessionUA} | Current UA:{CurrentUA}",
                        sessionUserId, sessionUserAgent, currentUserAgent);

                    await ClearSessionAndRedirectToLoginAsync(context).ConfigureAwait(false);
                    return;
                }
            }

            // ========================================
            // Step 4: 驗證 IP 地址（考慮代理模式，寬鬆檢查）
            // ========================================
            var currentIp = GetRealClientIp(context);
            var sessionIp = context.Session.GetString("_SessionRealIp");

            if (!string.IsNullOrEmpty(sessionIp) && currentIp != sessionIp)
            {
                // IP 變化 → 在代理模式下可能正常（例如 WiFi 切換），記錄警告但不強制登出
                _logger.LogInformation(
                    "[Session Validation] ?? IP 地址變化（可能正常） | UserId:{UserId} | Session IP:{SessionIP} | Current IP:{CurrentIP}",
                    sessionUserId, sessionIp, currentIp);

                // 更新 Session 中的 IP（允許 IP 變化，但記錄以便審計）
                context.Session.SetString("_SessionRealIp", currentIp);
            }

            // ========================================
            // Step 5: 驗證通過，繼續下一個中間件
            // ========================================
            await _next(context);
        }

        /// <summary>
        /// 判斷路徑是否應該排除驗證。
        ///
        /// 精確路徑（例如 <c>/login</c>）只允許本身及其下一層路徑段，
        /// 不得使用單純的前綴比對，否則 <c>/login-evil</c> 會被錯誤視為公開登入路徑，
        /// 讓動態請求繞過 Session 驗證。以斜線結尾的目錄規則（例如 <c>/css/</c>）
        /// 則維持目錄內所有檔案的快速排除行為。
        /// </summary>
        private bool ShouldExcludePath(string path)
        {
            foreach (var excludedPath in ExcludedPaths)
            {
                var matches = excludedPath.EndsWith("/", StringComparison.Ordinal)
                    ? path.StartsWith(excludedPath, StringComparison.OrdinalIgnoreCase)
                    : path.Equals(excludedPath, StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith(excludedPath + "/", StringComparison.OrdinalIgnoreCase);

                if (matches)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 取得真實客戶端 IP（考慮代理模式）
        ///
        /// 優先順序：
        /// 1. X-Forwarded-For（反向代理）
        /// 2. X-Real-IP（Nginx 等）
        /// 3. RemoteIpAddress（直連）
        /// </summary>
        private string GetRealClientIp(HttpContext context)
        {
            // 優先使用 X-Forwarded-For（反向代理環境）
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                // X-Forwarded-For 可能包含多個 IP，取第一個（真實客戶端 IP）
                var ips = forwardedFor.Split(',');
                return ips[0].Trim();
            }

            // 其次使用 X-Real-IP
            var realIp = context.Request.Headers["X-Real-IP"].ToString();
            if (!string.IsNullOrEmpty(realIp))
            {
                return realIp;
            }

            // 最後使用 RemoteIpAddress
            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        /// <summary>
        /// 清除 Session 並重導向至登入頁面
        /// </summary>
        private async Task ClearSessionAndRedirectToLoginAsync(HttpContext context)
        {
            try
            {
                context.Session.Clear();

                // ? 修復：必須 Commit 以確保 Session 立即失效
                // 若不 Commit，Session 清除可能不會即時生效，
                // 在高併發情境下可能造成短暫的 Session 洩漏
                //
                // ✅ 效能：改為 await，移除原本的 GetAwaiter().GetResult() 同步阻塞。
                // 語意完全相同（Commit 仍在重導向之前完成），但不再佔用執行緒等待 I/O。
                await context.Session.CommitAsync().ConfigureAwait(false);

                _logger.LogInformation("[Session Validation] ? Session 已清除並提交");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Session Validation] ? 清除 Session 失敗");
            }

            // 重導向至登入頁面
            context.Response.Redirect("/Login");
        }
    }
}
