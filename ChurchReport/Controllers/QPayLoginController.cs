using System;
using System.Collections.Generic;
using ChurchReport.Models;
using ChurchReport.Payments;
using ChurchReport.Tools;
using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
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
                    ClearRememberedWebLoginContact();
                    return Json(new { status = "3", message = "錯誤!所有欄位都要填寫，拜託!" });
                }

                if (string.IsNullOrWhiteSpace(model.FullName) ||
                    string.IsNullOrWhiteSpace(model.NationId) ||
                    string.IsNullOrWhiteSpace(model.Mobile))
                {
                    ClearRememberedWebLoginContact();
                    return Json(new { status = "3", message = "錯誤!所有欄位都要填寫，拜託!" });
                }

                model.NationId = model.NationId.ToUpperInvariant();

                InMemoryContext.DonationPaymentManager.LoginType = "網頁登入";

                string queryResult = string.Empty;
                var loginContact = InMemoryContext.DonationPaymentManager.GetDonationPaymentLoginContact(model, ref queryResult);

                if (loginContact != null)
                {
                    InMemoryContext.DonationPaymentManager.SetDonationPaymentModel(loginContact);
                    RememberWebLoginContact(loginContact);
                    return Json(new { status = "1", message = queryResult });
                }

                ClearRememberedWebLoginContact();
                return Json(new { status = "2", message = queryResult });
            }
            catch (Exception e)
            {
                return HandleError(e, nameof(ProcessQPayLogin));
            }
        }

        /// <summary>
        /// 保存網頁奉獻登入成功後的 CRM contact id。
        ///
        /// DonationPaymentManager 仍是主要畫面模型來源；這個 Session 值只做 redirect / AJAX 邊界的恢復錨點。
        /// 若後續請求因 memory-cache key 分裂而讀到新的空 manager，DedicationController 可用 contact id
        /// 重新呼叫 SetDonationPaymentModel，避免奉獻者姓名、奉獻編號與信用卡清單消失。
        /// </summary>
        private void RememberWebLoginContact(Entity loginContact)
        {
            if (loginContact == null || loginContact.Id == Guid.Empty)
            {
                ClearRememberedWebLoginContact();
                return;
            }

            HttpContext.Session.SetString(
                DonationPaymentSessionKeys.WebLoginContactId,
                loginContact.Id.ToString("D"));

            // 同步給會員資料模型，讓同一個 session 內其他 ChurchReport 頁面也能沿用目前登入者。
            // 若 memory-cache key 之後分裂，Session contact id 仍是更穩定的恢復來源。
            InMemoryContext.PersonalInfomationModel.m_LoginContact = loginContact;
        }

        /// <summary>
        /// 清除上一位網頁奉獻登入者的 contact id，避免登入失敗後沿用舊奉獻者資料。
        /// </summary>
        private void ClearRememberedWebLoginContact()
        {
            HttpContext.Session.Remove(DonationPaymentSessionKeys.WebLoginContactId);
        }
    }
}

