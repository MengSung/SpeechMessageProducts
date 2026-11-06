/*
 * 檔案整體說明
 *
 * 目的與定位：認證控制器（Core） - 包含建構函式與基礎類別繼承宣告 - 其餘功能分割到其他 partial 檔案
 *
 * 主要功能與流程：
 * 本檔宣告 AuthenticationController。
 * - 由 DI request scope 擁有的 Dataverse 服務。其最大生命週期為目前 HTTP request，容器會在 正常、例外或取消結束時確定性釋放；控制器不可自行歸還或快取，避免跨使用者狀態洩漏。
 * - AuthenticationController 建構函數 (使用 Dependency Injection)
 * - 隱私政策頁面（LINE Mini App 審核必備）。 此頁面必須可公開存取，不需要登入。 LINE 審核時會檢查此頁面是否可正常瀏覽。
 * 主要操作入口包含 BuildHeroImages、Privacy。
 *
 * 使用邊界與維護：
 * 本檔與同名控制器的其他partial共用一次請求的依賴及狀態；授權、Session與資料範圍必須沿既有伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 * 可沿 ChurchReport.MemberInfo.Tests/Security/LineLoginOAuthSensitiveLoggingTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineBinding.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLogin.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOA
 * uth.cs、SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Logi
 * n.cs 追查目前工作樹中的合作／使用位置。
 */
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
