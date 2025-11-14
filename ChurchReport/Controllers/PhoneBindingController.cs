using ChurchReport.Models;
using ChurchReport.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 手機號碼變更與 QRCode 綁定控制器
    /// </summary>
    public class PhoneBindingController : BaseChurchController
    {
        public PhoneBindingController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService)
            : base(httpContextAccessor, memoryCache, paymentService)
        {
        }

        #region 單獨換手機號碼
        [HttpGet]
        [Route("/Phone/ChangePhoneView/{LineIdLoginViewPatameter}")]
        public IActionResult ChangePhoneView(string LineIdLoginViewPatameter)
        {
            try
            {
                var images = new System.Collections.Generic.List<string>();
                images.Add(Url.Content("~/assets/images/sunnyvalech.jpg"));

                InMemoryContext.LineBindingViewModel.Images = images;
                TempData["Proponent"] = LineIdLoginViewPatameter;

                return View(InMemoryContext.LineBindingViewModel);
            }
            catch (System.Exception e)
            {
                return HandleError(e, "ChangePhoneView");
            }
        }
        #endregion

        #region QRcode 換手機號碼
        [HttpGet]
        [Route("/Phone/PhoneQrCodeView/{QrCodeViewPatameter}")]
        public IActionResult PhoneQrCodeView(string QrCodeId, string QrCodeViewPatameter)
        {
            try
            {
                // 控制 Navigation 下拉項目
                ViewBag.LoginType = "小組長";
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;

                InMemoryContext.ListManager.QrCodeId = QrCodeId;

                TempData["Proponent"] = QrCodeViewPatameter;
                TempData["QrCodeId"] = QrCodeId;
                TempData["ClassName"] = " ";

                return View();
            }
            catch (System.Exception e)
            {
                return HandleError(e, "PhoneQrCodeView");
            }
        }

        [HttpPost]
        [Route("/Phone/PhoneQrCodeGetLineId")]
        public IActionResult PhoneQrCodeGetLineId(string DisplayName, string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                // 把LineUserId放在伺服器端了
                InMemoryContext.LineBindingViewModel.LineUserId = InMemoryContext.AppointmentsListManager.LineUserId = UserLineId;
                InMemoryContext.LineBindingViewModel.RoomId = InMemoryContext.AppointmentsListManager.RoomId = RoomId;
                InMemoryContext.LineBindingViewModel.GroupId = InMemoryContext.AppointmentsListManager.GroupId = GroupId;
                InMemoryContext.LineBindingViewModel.ViewType = InMemoryContext.AppointmentsListManager.ViewType = ViewType;

                if (!string.IsNullOrEmpty(GroupId))
                {
                    InMemoryContext.LineBindingViewModel.DisplayId = GroupId;
                }
                else if (!string.IsNullOrEmpty(RoomId))
                {
                    InMemoryContext.LineBindingViewModel.DisplayId = RoomId;
                }
                else
                {
                    InMemoryContext.LineBindingViewModel.DisplayId = UserLineId;
                }

                // 控制 Navigation 下拉項目
                ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;

                TempData["ClassName"] = "從相信到堅信";

                // 使用 QrCodeUtility 處理 QR Code 邏輯
                QrCodeUtility aQrCodeUtility = new QrCodeUtility();

                string UserName = "";
                string OnboardType = "";
                string ClassIndex = "";
                string ClassName = "";

                // 主日掃描後的相關設定 (補充缺少的兩個參數)
                aQrCodeUtility.SetupQrCodeIdString(
                    InMemoryContext.ListManager.QrCodeId,
                    DisplayName,
                    UserLineId,
                    ref UserName,
                    ref OnboardType,
                    ref ClassIndex,
                    ref ClassName);

                return Json(new { result = OnboardType, username = UserName, onboardtype = OnboardType });
            }
            catch (System.Exception e)
            {
                return HandleError(e, "PhoneQrCodeGetLineId");
            }
        }
        #endregion
    }
}
