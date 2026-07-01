using System;
using System.Collections.Generic;
using ChurchReport.Models;
using ChurchReport.Payments;
using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 處理網頁版奉獻付款登入的 Controller。
    ///
    /// 這個 Controller 的重點不是「呼叫金流」，而是「確認奉獻者身分」：
    /// 使用者在網頁登入表單輸入姓名、身分證字號與手機後，ChurchReport 會到 CRM 找 contact。
    /// 找到 contact 後，才把 contact 轉成後續奉獻付款頁需要的表單狀態。
    ///
    /// 為什麼這個類別要叫 DonationPaymentLoginController：
    /// - 登入流程是 ChurchReport 的奉獻付款產品流程，不屬於永豐、高鉅或台新任何一家 provider。
    /// - 真正要使用哪一家金流，是後續建立付款訂單時由 appsettings 與 payment profile 決定。
    /// - 舊網址 /QPayLogin 仍保留在 Route attribute，因為那是外部網址相容，不代表 C# 類別要保留舊 provider 名稱。
    ///
    /// 這樣切分後，未來其他 ASP.NET Core 產品也可以學同一個模式：
    /// 各產品自己處理會員/客戶登入與資料查詢，再把結果轉成共用付款 DTO，而不是把產品登入流程塞進金流核心。
    /// </summary>
    public class DonationPaymentLoginController : BaseChurchController
    {
        public DonationPaymentLoginController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
        }

        /// <summary>
        /// 顯示網頁版奉獻付款登入頁。
        ///
        /// 這裡保留舊路由 /QPayLogin，避免舊連結失效；
        /// 但 View 檔名使用 DonationPaymentLogin.cshtml，讓專案檔與檔案總管中看到的名稱都是產品中性名稱。
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

            return View("~/Views/Home/DonationPaymentLogin.cshtml", new GalleryViewModel { Images = images });
        }

        /// <summary>
        /// 處理網頁版奉獻付款登入表單。
        ///
        /// 流程說明：
        /// 1. 先檢查表單是否有姓名、身分證字號、手機。這些是 ChurchReport 查 CRM contact 的基本條件。
        /// 2. 將身分證字號轉成大寫，避免大小寫差異造成 CRM 查詢不到。
        /// 3. 呼叫 DonationPaymentManager.GetDonationPaymentLoginContact 查詢或建立奉獻者 contact。
        /// 4. 查到 contact 後，呼叫 SetDonationPaymentModel，把 CRM contact 轉成奉獻付款表單模型。
        /// 5. 把 contact id 存到 ASP.NET Session，讓下一個 redirect request 可以還原同一位奉獻者資料。
        ///
        /// 這個 action 回傳 JSON，是為了配合原本 AJAX 登入頁流程。
        /// </summary>
        [HttpPost]
        [Route("/QPayLogin/ProcessQPayLogin")]
        public IActionResult ProcessDonationPaymentLogin(GalleryViewModel model)
        {
            try
            {
                if (model == null)
                {
                    ClearRememberedWebLoginContact();
                    return Json(new { status = "3", message = "登入失敗，請確認資料是否完整。" });
                }

                if (string.IsNullOrWhiteSpace(model.FullName) ||
                    string.IsNullOrWhiteSpace(model.NationId) ||
                    string.IsNullOrWhiteSpace(model.Mobile))
                {
                    ClearRememberedWebLoginContact();
                    return Json(new { status = "3", message = "登入失敗，請輸入姓名、身分證字號與手機。" });
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
                return HandleError(e, nameof(ProcessDonationPaymentLogin));
            }
        }

        /// <summary>
        /// 記住網頁登入取得的 CRM contact id。
        ///
        /// 為什麼要放進 Session：
        /// AJAX 登入成功後，前端通常會 redirect 到奉獻付款頁。redirect 會產生新的 HTTP request，
        /// 而新的 request 不一定還拿得到剛才記憶體中的完整 DonationPaymentManager 狀態。
        /// 把 contact id 放進 Session 後，DedicationController 可以用這個 id 重新讀取 CRM contact，
        /// 再還原姓名、奉獻編號、信用卡清單、定期定額清單等畫面需要的資料。
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

            // 同步保存到目前 request 的記憶體模型。
            // Session 是跨 request 的備援；這個欄位則讓同一個 request 後續程式可以直接使用已查到的 contact。
            InMemoryContext.PersonalInfomationModel.m_LoginContact = loginContact;
        }

        /// <summary>
        /// 清除網頁登入 contact id。
        ///
        /// 只要表單資料不完整、CRM 查詢失敗、contact id 無效，就清掉 Session。
        /// 這樣下一次進入奉獻頁時不會誤用上一位奉獻者的資料。
        /// </summary>
        private void ClearRememberedWebLoginContact()
        {
            HttpContext.Session.Remove(DonationPaymentSessionKeys.WebLoginContactId);
        }
    }
}
