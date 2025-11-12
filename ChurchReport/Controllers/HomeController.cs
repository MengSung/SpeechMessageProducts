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
        
        #region 登入帳號
        public async System.Threading.Tasks.Task<IActionResult> Login()
        {
            try
            {
                var images = new List<string>();
                images.Add(Url.Content("~/assets/images/sunnyvalech.jpg"));

                return View(new GalleryViewModel
                {
                    Images = images
                });
            }
            catch (System.Exception e)
            {
                return HandleError(e, "Login");
            }
        }
        
        [HttpPost]
        public async Task<IActionResult> ProcessLogin(GalleryViewModel aGalleryViewModel)
        {
            try
            {
                string ContactIdString = "";
                if ( aGalleryViewModel.Account != "" )
                {
                    // 透過帳號密碼登入畫面進入的
                    ContactIdString = ToolUtility.RetrieveContactByAccountNumber(aGalleryViewModel.Account, aGalleryViewModel.Password);
                }
                else
                {
                    ContactIdString = "透過Line Id 登入";
                }

                if (　ContactIdString != "密碼錯誤" && ContactIdString != "系統沒有設定密碼" && ContactIdString != "帳號錯誤"　)
                {
                    string FullName = "";
                    Entity aLoginContact;
                    if ( ContactIdString != "透過Line Id 登入" )
                    {
                        #region 使用者透過網頁的帳號密碼_Login，所以帳號密碼就依據使用者輸入的為準
                        aLoginContact = ToolUtility.RetrieveEntityDynamics365("contact", new Guid(ContactIdString));
                        FullName = this.ToolUtility.GetEntityStringAttribute(ref aLoginContact, "fullname");
                        #endregion
                    }
                    else
                    {
                        #region 使用者透過 Line Id 登入，所以帳號 Account="LineIdLogin"字串，密碼 Password=LineId
                        aLoginContact = ToolUtility.RetrieveContactEntityByLineUserId(InMemoryContext.LineBindingViewModel.LineUserId);
                        FullName = this.ToolUtility.GetEntityStringAttribute(ref aLoginContact, "fullname");
                        aGalleryViewModel.Account = "LineIdLogin";
                        aGalleryViewModel.Password = InMemoryContext.LineBindingViewModel.LineUserId;
                        #endregion
                    }

                    // 依據登入方式設定行事曆的帳密
                    InMemoryContext.AppointmentsListManager.m_Account = aGalleryViewModel.Account;
                    InMemoryContext.AppointmentsListManager.m_Password = aGalleryViewModel.Password;

                    // 儲存登入的連絡人實體紀錄
                    InMemoryContext.AppointmentsListManager.m_LoginContact = aLoginContact;

                    // 設定多個組長處理需要的資料
                    InMemoryContext.ListManager.SetupListManager(aGalleryViewModel.Account, aGalleryViewModel.Password, DateTime.Now);

                    // 差勤簽核 OR 場地及資源預約
                    InMemoryContext.AppointmentsListManager.SetupAppointmentList();

                    // 永豐金流奉獻
                    if ( aLoginContact != null)
                    {
                        InMemoryContext.QpayManager.LoginType = "網頁登入";
                        InMemoryContext.QpayManager.SetQpayModel(aLoginContact);
                    }

                    // 個人相關資料:儲存登入者實體紀錄
                    InMemoryContext.PersonalInfomationModel.m_LoginContact = aLoginContact;

                    #region 控制 Navigation 下拉項目
                    ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "不是單純行事曆";
                    ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "顯示牧養回報項目";
                    ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;
                    #endregion

                    // 透過取得多小組網頁需要的資料之後，判斷這是多小組還是單一小組長的回報
                    string DisplayViewType = InMemoryContext.ListManager.GetDisplayViewType();
                    if (DisplayViewType == "IntegrateView")
                    {
                        // 得知這是單一小組長的回報，所以就直接下載整合式網頁所需的資料
                        InMemoryContext.ListManager.SetupIntegrateData( InMemoryContext.ListManager.ActiveListId );
                    }

                    // 設定需要點名的課程清單
                    InMemoryContext.FeeList.SetupLessonList(aGalleryViewModel.Account, aGalleryViewModel.Password);

                    if (InMemoryContext.ListManager.LoginType == "小組長" && InMemoryContext.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        // 小組長回報，而且有幸福小組
                        ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
                        ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
                        ViewBag.HappyType = "有幸福小組名單";
                        ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
                        #region 繳費與點名是否顯示在選單中
                        if (InMemoryContext.FeeList.FeeDataList != null && InMemoryContext.FeeList.FeeDataList.Count > 0)
                        {
                            ViewBag.FeeDataListCount = "繳費與點名已有資料";
                        }
                        else
                        {
                            ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                        }
                        #endregion

                        SetMultiGroupLayoutParameter();

                        return Json(new { DisplayViewType = DisplayViewType, ActiveListId = InMemoryContext.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                    }
                    else if (InMemoryContext.ListManager.LoginType == "小組長" && InMemoryContext.HappyGroupDataManager.HappyType == "沒幸福小組名單")
                    {
                        // 小組長回報，沒有幸福小組
                        ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
                        ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
                        ViewBag.HappyType = "沒幸福小組名單";
                        ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
                        SetupFeeDataListCount();
                        SetMultiGroupLayoutParameter();

                        return Json(new { DisplayViewType = DisplayViewType, ActiveListId = InMemoryContext.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                    }
                    else if (InMemoryContext.ListManager.LoginType != "小組長" && InMemoryContext.HappyGroupDataManager.HappyType == "沒幸福小組名單")
                    {
                        // 個人回報，不是小組長，沒有幸福小組
                        ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
                        ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
                        ViewBag.HappyType = "沒幸福小組名單";
                        ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
                        SetupFeeDataListCount();
                        SetMultiGroupLayoutParameter();

                        return Json(new { DisplayViewType = DisplayViewType, ActiveListId = InMemoryContext.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                    }
                    else if (InMemoryContext.ListManager.LoginType != "小組長" && InMemoryContext.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        // 個人回報 + 單純幸福小組長回報
                        ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
                        ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
                        ViewBag.HappyType = "有幸福小組名單";
                        ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
                        SetupFeeDataListCount();
                        SetMultiGroupLayoutParameter();

                        DisplayViewType = "HappyGroupView";
                        return Json(new { DisplayViewType = DisplayViewType, ActiveListId = InMemoryContext.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                    }
                    else
                    {
                        ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
                        ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
                        ViewBag.HappyType = "沒幸福小組名單";
                        ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
                        SetupFeeDataListCount();
                        SetMultiGroupLayoutParameter();

                        DisplayViewType = "HappyGroupView";
                        return Json(new { DisplayViewType = DisplayViewType, ActiveListId = InMemoryContext.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                    }
                }
                else
                {
                    return Json(new { DisplayViewType = "登入錯誤", ActiveListId = InMemoryContext.ListManager.ActiveListId, message = ContactIdString, fullname = ContactIdString });
                }
            }
            catch (System.Exception e)
            {
                return HandleError(e, "ProcessLogin");
            }
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
