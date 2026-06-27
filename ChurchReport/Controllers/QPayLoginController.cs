using System;
using System.Collections.Generic;
using ChurchReport.Models;
using ChurchReport.Tools;
using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 專責處理官網 QPay 登入的控制器
    /// </summary>
    public class QPayLoginController : BaseChurchController
    {
        public QPayLoginController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
        }

        /// <summary>
        /// 顯示 QPay 登入頁面
        /// </summary>
        [HttpGet]
        [Route("/QPayLogin")]
        public IActionResult Index()
        {
            var images = new List<string>
            {
                Url.Content("~/assets/images/church-001.jpg"),
                Url.Content("~/assets/images/church-002.jpg"),
            };

            return View("~/Views/Home/QPayLogin.cshtml", new GalleryViewModel { Images = images });
        }

        /// <summary>
        /// 處理 QPay 登入表單提交
        /// </summary>
        [HttpPost]
        [Route("/QPayLogin/ProcessQPayLogin")]
        public IActionResult ProcessQPayLogin(GalleryViewModel model)
        {
            try
            {
                if (model == null)
                {
                    return Json(new { status = "3", message = "錯誤!所有欄位都要填寫，拜託!" });
                }

                if (string.IsNullOrWhiteSpace(model.FullName) ||
                    string.IsNullOrWhiteSpace(model.NationId) ||
                    string.IsNullOrWhiteSpace(model.Mobile))
                {
                    return Json(new { status = "3", message = "錯誤!所有欄位都要填寫，拜託!" });
                }

                model.NationId = model.NationId.ToUpperInvariant();

                InMemoryContext.QpayManager.LoginType = "網頁登入";

                string queryResult = string.Empty;
                var loginContact = InMemoryContext.QpayManager.GetLoginContactQpay(model, ref queryResult);

                if (loginContact != null)
                {
                    InMemoryContext.QpayManager.SetQpayModel(loginContact);
                    return Json(new { status = "1", message = queryResult });
                }

                return Json(new { status = "2", message = queryResult });
            }
            catch (Exception e)
            {
                return HandleError(e, nameof(ProcessQPayLogin));
            }
        }
    }
}
