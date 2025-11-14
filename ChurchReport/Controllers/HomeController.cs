using ChurchReport.Models;
using ChurchReport.Tools;
using ChurchReport.ViewModel;
using ChurchReport.ViewModels;
using ChurchReport.WebServiceConnector;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ToolUtilityNameSpace;

namespace ChurchReport.Controllers
{
    public class HomeController : BaseChurchController
    {
        #region 初始化
        public HomeController(IHttpContextAccessor httpContextAccessor, IMemoryCache memoryCache, IPayment qpayService)
            : base(httpContextAccessor, memoryCache, qpayService)
        {
        }
        #endregion
        
        #region 向後相容路由 (Backward Compatibility Routes)
        
        /// <summary>
        /// 向後相容: 將舊的 /Home/Login 重導向到 /Authentication/Login
        /// </summary>
        [Route("/Home/Login")]
        public IActionResult LoginRedirect()
        {
            return RedirectToAction("Login", "Authentication");
        }
        
        /// <summary>
        /// 向後相容: 處理舊的 /Home/ProcessLogin POST 請求
        /// </summary>
        [HttpPost]
        [Route("/Home/ProcessLogin")]
        public async Task<IActionResult> ProcessLoginRedirect(GalleryViewModel aGalleryViewModel)
        {
            // 直接調用新控制器的方法
            var authController = new AuthenticationController(
                HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
                HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
                HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment);
            
            return await authController.ProcessLogin(aGalleryViewModel);
        }
        
        /// <summary>
        /// 向後相容: 將舊的 /Home/LineIdLoginView 重導向到 /Authentication/LineIdLoginView
        /// </summary>
        [Route("/Home/LineIdLoginView/{LineIdLoginViewPatameter}")]
        public IActionResult LineIdLoginViewRedirect(string LineIdLoginViewPatameter)
        {
            return RedirectToAction("LineIdLoginView", "Authentication", new { LineIdLoginViewPatameter = LineIdLoginViewPatameter });
        }
        
        /// <summary>
        /// 向後相容: 將舊的 /Home/IntegrateView 重導向到 /SmallGroup/IntegrateView
        /// </summary>
        [Route("/Home/IntegrateView/{LoginParameter}")]
        public IActionResult IntegrateViewRedirect(string LoginParameter)
        {
            return RedirectToAction("IntegrateView", "SmallGroup", new { LoginParameter = LoginParameter });
        }
        
        /// <summary>
        /// 向後相容: 將舊的 /Home/MultiGroupView 重導向到 /SmallGroup/MultiGroupView
        /// </summary>
        [Route("/Home/MultiGroupView/{LoginParameter}")]
        public IActionResult MultiGroupViewRedirect(string LoginParameter)
        {
            return RedirectToAction("MultiGroupView", "SmallGroup", new { LoginParameter = LoginParameter });
        }
        
        /// <summary>
        /// 向後相容: 將舊的 /Home/NewPersonFollowUpView 重導向到 /NewPerson/FollowUpView
        /// </summary>
        [Route("/Home/NewPersonFollowUpView")]
        public IActionResult NewPersonFollowUpViewRedirect()
        {
            return RedirectToAction("NewPersonFollowUpView", "NewPerson");
        }
        
        /// <summary>
        /// 向後相容: 將舊的 /Home/PersonalReport 重導向到 /Personal/PersonalReport
        /// </summary>
        [Route("/Home/PersonalReport")]
        public IActionResult PersonalReportRedirect()
        {
            return RedirectToAction("PersonalReport", "Personal");
        }
        
        /// <summary>
        /// 向後相容: 將舊的 /Home/PersonalInfomationView 重導向到 /Personal/PersonalInfomationView
        /// </summary>
        [Route("/Home/PersonalInfomationView")]
        public IActionResult PersonalInfomationViewRedirect()
        {
            return RedirectToAction("PersonalInfomationView", "Personal");
        }
        
        /// <summary>
        /// 向後相容: 將舊的 /Home/QPayView 重導向到 /Dedication/QPayView
        /// </summary>
        [Route("/Home/QPayView/{LineId}")]
        public IActionResult QPayViewRedirect(string LineId)
        {
            return RedirectToAction("QPayView", "Dedication", new { LineId = LineId });
        }
        
        /// <summary>
        /// 向後相容: 將舊的 /Home/ChurchRoot 重導向到 /ListManagement/ChurchRoot
        /// </summary>
        [Route("/Home/ChurchRoot")]
        public IActionResult ChurchRootRedirect()
        {
            return RedirectToAction("ChurchRoot", "ListManagement");
        }
        
        /// <summary>
        /// 向後相容: 將舊的 /Home/EquipmentView 重導向到 /Equipment/EquipmentView
        /// </summary>
        [Route("/Home/EquipmentView")]
        public IActionResult EquipmentViewRedirect()
        {
            return RedirectToAction("EquipmentView", "Equipment");
        }
        
        #endregion
        
        #region 單獨換手機號碼
        [Route("/Home/ChangePhoneView/{LineIdLoginViewPatameter}")]
        public IActionResult ChangePhoneView(string LineIdLoginViewPatameter)
        {
            try
            {
                var images = new List<string>();
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
        [Route("/Home/PhoneQrCodeView/{QrCodeViewPatameter}")]
        public IActionResult PhoneQrCodeView(String QrCodeId, string QrCodeViewPatameter)
        {
            try
            {
                #region 控制 Navigation 下拉項目
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
                #endregion

                return View();
            }
            catch (System.Exception e)
            {
                return HandleError(e, "PhoneQrCodeView");
            }
        }

        [HttpPost]
        public IActionResult PhoneQrCodeGetLineId(string DisplayName, string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                // 把LineUserId放在伺服器端了
                InMemoryContext.LineBindingViewModel.LineUserId = InMemoryContext.AppointmentsListManager.LineUserId = UserLineId;
                InMemoryContext.LineBindingViewModel.RoomId = InMemoryContext.AppointmentsListManager.RoomId = RoomId;
                InMemoryContext.LineBindingViewModel.GroupId = InMemoryContext.AppointmentsListManager.GroupId = GroupId;
                InMemoryContext.LineBindingViewModel.ViewType = InMemoryContext.AppointmentsListManager.ViewType = ViewType;

                if (GroupId != null && GroupId != "")
                {
                    InMemoryContext.LineBindingViewModel.DisplayId = GroupId;
                }
                else if (RoomId != null && RoomId != "")
                {
                    InMemoryContext.LineBindingViewModel.DisplayId = RoomId;
                }
                else
                {
                    InMemoryContext.LineBindingViewModel.DisplayId = UserLineId;
                }

                #region 控制 Navigation 下拉項目
                ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;
                #endregion

                TempData["ClassName"] = "從相信到堅信";

                // 使用 QrCodeUtility 處理 QR Code 邏輯
                QrCodeUtility aQrCodeUtility = new QrCodeUtility();

                String UserName = "";
                String OnboardType = "";
                String ClassIndex = "";
                String ClassName = "";

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
