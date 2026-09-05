using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;

namespace ChurchReport.Filters
{
    public sealed class GlobalAuthorizationFilter : IAsyncAuthorizationFilter
    {
        /// <summary>
        /// ✅ 效能：每個 Action 的 [AllowAnonymous] 判定結果快取。
        ///
        /// 原本每個請求都對 MethodInfo 與 ControllerTypeInfo 各執行一次
        /// GetCustomAttributes(inherit: true)。這是完整的反射屬性掃描，會走訪整條繼承鏈、
        /// 具體化屬性實例並配置陣列，接著再各跑一次 LINQ OfType/Any。
        ///
        /// 這個判定的輸入只有 Action 的型別中繼資料，在 descriptor 存活期間永不改變，
        /// 因此以 descriptor 物件作弱鍵快取既安全又不會根住動態重載後的舊中繼資料。
        ///
        /// 【為何不含任何請求狀態】
        /// 快取的值是 bool，只由編譯期的屬性標註決定，
        /// 不含使用者、Session、租戶、Claims 或任何請求資料。
        /// ConditionalWeakTable 的鍵不會被快取本身強制保留；其數量隨 MVC descriptor
        /// 生命週期而定，不隨請求數或重載次數無界成長，因此不會造成記憶體洩漏。
        /// </summary>
        private sealed class AllowAnonymousResult
        {
            public AllowAnonymousResult(bool value) => Value = value;

            public bool Value { get; }
        }

        /// <summary>
        /// 以 ActionDescriptor 物件作為弱鍵保存反射結果。
        /// ConditionalWeakTable 不會以字串鍵永久根住動態重載後已淘汰的 descriptor，
        /// 因此即使應用程式重新建立 MVC descriptor，也不會讓快取隨重載次數無界成長。
        /// </summary>
        private static readonly ConditionalWeakTable<ControllerActionDescriptor, AllowAnonymousResult>
            AllowAnonymousCache = new();

        /// <summary>
        /// ✅ 效能：設定值在建構時讀取一次。
        ///
        /// IConfiguration.GetValue&lt;T&gt; 每次呼叫都會走訪所有設定提供者、
        /// 以字串比對鍵路徑，再透過 TypeConverter 做型別轉換。原本每個請求最多呼叫兩次。
        /// 這兩個開關都是佈署期決定的，不需要熱重載。
        /// </summary>
        private readonly bool _enforce;
        private readonly bool _allowSessionFallback;

        public GlobalAuthorizationFilter(IConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            _enforce = configuration.GetValue<bool?>("Security:EnforceGlobalAuthorization") ?? true;
            _allowSessionFallback = configuration.GetValue<bool?>("Security:AllowSessionIdentityFallback") ?? true;
        }

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            // 三個放行條件由便宜到昂貴排列，讓最常見的情況以最低成本短路離開：
            //   1. _enforce —— 讀取一個 bool 欄位（原本是每請求一次設定樹查找）
            //   2. AllowsAnonymous —— 第一次之後都是一次字典查找（原本是每請求兩次反射掃描）
            //   3. IsAuthenticated —— 讀取已由驗證中介層填好的 ClaimsPrincipal
            // C# 的 || 具短路特性，所以已登入的一般請求根本不會執行到後面的 Session 讀取。
            if (!_enforce || AllowsAnonymous(context) || IsAuthenticated(context.HttpContext))
            {
                return Task.CompletedTask;
            }

            // 走到這裡代表沒有 Cookie 驗證身分。部分舊有流程只在伺服器端 Session 留下身分，
            // 因此保留這條後援路徑。此處刻意最後才碰 Session，因為存取 Session 可能觸發載入。
            if (_allowSessionFallback && HasServerSessionIdentity(context.HttpContext))
            {
                return Task.CompletedTask;
            }

            // 完全無法辨識身分。AJAX 請求回 401 讓前端自行處理，
            // 一般導覽請求則導向登入頁；對 AJAX 回傳重導向頁面只會讓前端拿到一份登入頁 HTML。
            context.Result = IsAjax(context.HttpContext.Request)
                ? new StatusCodeResult(StatusCodes.Status401Unauthorized)
                : new RedirectToActionResult("Login", "Authentication", null);

            return Task.CompletedTask;
        }

        private static bool AllowsAnonymous(AuthorizationFilterContext context)
        {
            if (context.ActionDescriptor is ControllerActionDescriptor descriptor)
            {
                // ✅ 效能：以 ActionDescriptor 物件作弱鍵快取反射結果。
                // descriptor 由 MVC 管理且不可變；descriptor 被淘汰時，ConditionalWeakTable 項目
                // 也會隨之回收，不會因動態重載或測試建立大量 descriptor 而永久保留字串鍵。
                var cached = AllowAnonymousCache.GetValue(descriptor, static actionDescriptor =>
                {
                    var methodAllowsAnonymous = actionDescriptor.MethodInfo
                        .GetCustomAttributes(true).OfType<IAllowAnonymous>().Any();
                    var controllerAllowsAnonymous = actionDescriptor.ControllerTypeInfo
                        .GetCustomAttributes(true).OfType<IAllowAnonymous>().Any();
                    return new AllowAnonymousResult(methodAllowsAnonymous || controllerAllowsAnonymous);
                });

                if (cached.Value)
                {
                    return true;
                }
            }

            // 過濾器集合是每個請求組出來的，必須每次檢查，不可快取。
            return context.Filters.OfType<IAllowAnonymousFilter>().Any();
        }

        private static bool IsAuthenticated(HttpContext httpContext)
        {
            return httpContext.User?.Identity?.IsAuthenticated == true;
        }

        private static bool HasServerSessionIdentity(HttpContext httpContext)
        {
            try
            {
                return !string.IsNullOrEmpty(httpContext.Session.GetString("_SessionUserId"))
                    || !string.IsNullOrEmpty(httpContext.Session.GetString("_LoginPassword"));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAjax(HttpRequest request)
        {
            if (string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        }
    }
}
