using ChurchReport.Models;
using ChurchReport.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
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

        #region 建構函式

        /// <summary>
        /// AuthenticationController 建構函數 (使用 Dependency Injection)
        /// </summary>
        public AuthenticationController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            IWebHostEnvironment env)
            : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider, connectionPool)
        {
            _env = env;
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
    }
}
