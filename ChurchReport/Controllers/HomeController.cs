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
        /// 將舊的 /Home/MultiGroupView 重導向到 /SmallGroup/MultiGroupView
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
        
        /// <summary>
        /// 向後相容: 將舊的 /Home/ChangePhoneView 重導向到 /Phone/ChangePhoneView
        /// </summary>
        [Route("/Home/ChangePhoneView/{LineIdLoginViewPatameter}")]
        public IActionResult ChangePhoneViewRedirect(string LineIdLoginViewPatameter)
        {
            return RedirectToAction("ChangePhoneView", "PhoneBinding", new { LineIdLoginViewPatameter });
        }
        
        /// <summary>
        /// 向後相容: 將舊的 /Home/PhoneQrCodeView 重導向到 /Phone/PhoneQrCodeView
        /// </summary>
        [Route("/Home/PhoneQrCodeView/{QrCodeViewPatameter}")]
        public IActionResult PhoneQrCodeViewRedirect(string QrCodeViewPatameter, string QrCodeId)
        {
            return RedirectToAction("PhoneQrCodeView", "PhoneBinding", new { QrCodeViewPatameter, QrCodeId });
        }
        
        #endregion
    }
}
