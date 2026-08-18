// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/AuthenticationController/AuthenticationController.Core.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class AuthenticationController
// 主要成員：BuildHeroImages、Privacy
// 引用命名空間：ChurchReport.Models、ChurchReport.Tools、Microsoft.AspNetCore.Hosting、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Caching.Memory、System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.IO;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 認證控制器（Core）
    /// - 包含建構函式與基礎類別繼承宣告
    /// - 其餘功能分割到其他 partial 檔案
    /// </summary>
    public partial class AuthenticationController : BaseChurchController
    {
        private readonly IWebHostEnvironment _env;

        /// <summary>
        /// 由 DI request scope 擁有的 Dataverse 服務。其最大生命週期為目前 HTTP request，容器會在
        /// 正常、例外或取消結束時確定性釋放；控制器不可自行歸還或快取，避免跨使用者狀態洩漏。
        /// </summary>
        private readonly IOrganizationService _organizationService;

        #region 建構函式

        /// <summary>
        /// AuthenticationController 建構函數 (使用 Dependency Injection)
        /// </summary>
        public AuthenticationController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            IWebHostEnvironment env,
            IOrganizationService organizationService = null)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
            _env = env;
            _organizationService = organizationService
                ?? httpContextAccessor?.HttpContext?.RequestServices?.GetService(typeof(IOrganizationService)) as IOrganizationService
                ?? throw new ArgumentNullException(nameof(organizationService));
        }

        #endregion

        #region 共同 UI 輔助

        private List<string> BuildHeroImages(params string[] relativePaths)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in relativePaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                var webPath = Url.Content(path);
                if (!seen.Add(webPath)) continue;

                var physicalPath = Path.Combine(_env.WebRootPath ?? string.Empty, path.TrimStart('~', '/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(physicalPath))
                {
                    list.Add(webPath);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[BuildHeroImages] 找不到檔案: {physicalPath}");
                }
            }

            return list;
        }

        #endregion

        #region 隱私政策頁面

        /// <summary>
        /// 隱私政策頁面（LINE Mini App 審核必備）。
        /// 此頁面必須可公開存取，不需要登入。
        /// LINE 審核時會檢查此頁面是否可正常瀏覽。
        /// </summary>
        /// <returns>隱私政策視圖</returns>
        [HttpGet]
        [Route("/Privacy")]
        public IActionResult Privacy()
        {
            return View();
        }

        #endregion
    }
}
