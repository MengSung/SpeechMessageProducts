using ChurchReport.Models;
using ChurchReport.ViewModel;
using ChurchReport.ViewModels;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ToolUtilityNameSpace;

using Line.Pay;
using Line.Pay.Models;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Threading.Tasks;
using ChurchReport.WebServiceConnector;
using System.Globalization;
using ChurchReport.Tools;

namespace ChurchReport.Controllers
{
    public class HomeController : Controller, IDisposable
    {
        #region 資料區
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        private InMemoryDataContextSmallGroup m_InMemoryDataContextSmallGroup;
        private readonly Disposable _disposable;

        //private ContextDictionary m_ContextDictionary = new ContextDictionary();

        #region 除錯用參數
        private const int TOTAL_LEVEL = 1;//改變這個值，就會改追蹤的階層，值越小越不會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        //private const int TOTAL_LEVEL = 5;//改變這個值，就會改追蹤的階層，值越大越會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        private const int LEVEL_1 = 1; // 比較容易被看到的，可能是比較大範圍的部分
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5; // 比較不會被看到的，可能是比較細節的部分
        // 如果 TRACE_LEVEL >= TRACE_LEVEL_GROUND 就會進行追蹤
        // 如果 TRACE_LEVEL < TRACE_LEVEL_GROUND 就不會進行追蹤
        //int TRACE_LEVEL = 5;
        //int TRACE_LEVEL_GROUND = 3;
        #endregion
        #endregion
        #region 初始化
        public HomeController(IHttpContextAccessor httpContextAccessor, IMemoryCache memoryCache)
        {
            m_InMemoryDataContextSmallGroup = new InMemoryDataContextSmallGroup(httpContextAccessor, memoryCache);
            //m_InMemoryDataContextSmallGroup = ContextDictionary.GetInMemoryDataContextSmallGroup(httpContextAccessor, memoryCache);
            //m_InMemoryDataContextSmallGroup = m_ContextDictionary.GetInMemoryDataContextSmallGroup(httpContextAccessor, memoryCache);
        }
        #endregion
        #region 登入帳號
        //[CheckSessionOut]
        public async System.Threading.Tasks.Task<IActionResult> Login()
        {
            try
            {
                var images = new List<string>();
                images.Add(Url.Content("~/assets/images/永和堂牧養系統web_banner-01.jpg"));
                images.Add(Url.Content("~/assets/images/永和堂牧養系統web_banner-02.png"));

                return View(new GalleryViewModel
                {
                    Images = images
                });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                await aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
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
                    ContactIdString = m_ToolUtilityClass.RetrieveContactByAccountNumber(aGalleryViewModel.Account, aGalleryViewModel.Password);
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
                        #region 使用者透過網頁的帳號密碼登入，所以帳號密碼就依據使用者輸入的為準
                        aLoginContact = m_ToolUtilityClass.RetrieveEntityDynamics365("contact", new Guid(ContactIdString));
                        FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLoginContact, "fullname");
                        #endregion
                    }
                    else
                    {
                        #region 使用者透過 Line Id 登入，所以帳號 Account="LineIdLogin"字串，密碼 Password=LineId
                        aLoginContact = m_ToolUtilityClass.RetrieveContactEntityByLineUserId(m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId);
                        FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLoginContact, "fullname");
                        aGalleryViewModel.Account = "LineIdLogin";
                        aGalleryViewModel.Password = m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId;
                        #endregion
                    }

                    // 依據登入方式設定行事曆的帳密
                    m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Account = aGalleryViewModel.Account;
                    m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Password = aGalleryViewModel.Password;

                    // 設定多個組長處理需要的資料
                    m_InMemoryDataContextSmallGroup.ListManager.SetupListManager(aGalleryViewModel.Account, aGalleryViewModel.Password, DateTime.Now);

                    // 差勤簽核 OR 場地及資源預約
                    m_InMemoryDataContextSmallGroup.AppointmentsListManager.SetupAppointmentList();

                    // 永豐金流奉獻
                    if ( aLoginContact != null)
                    {
                        m_InMemoryDataContextSmallGroup.QpayManager.SetQpayModel(aLoginContact);
                    }

                    #region 控制 Navigation 下拉項目
                    ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "不是單純行事曆";
                    ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "顯示牧養回報項目";
                    ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.UserType;
                    #endregion

                    // 透過取得多小組網頁需要的資料之後，判斷這是多小組還是單一小組長的回報
                    string DisplayViewType = m_InMemoryDataContextSmallGroup.ListManager.GetDisplayViewType();
                    if (DisplayViewType == "IntegrateView")
                    {
                        // 得知這是單一小組長的回報，所以就直接下載整合式網頁所需的資料
                        m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData( m_InMemoryDataContextSmallGroup.ListManager.ActiveListId );
                    }
                    else
                    {
                        // 得知這是多小組的回報，就不需要下載整合式網頁所需的資料
                    }

                    // 設定幸福小組資料
                    //m_InMemoryDataContextSmallGroup.SetupHappyGroupData(aGalleryViewModel.Account, aGalleryViewModel.Password);
                    //if (m_InMemoryDataContextSmallGroup.m_ListManager.m_ListSmallGroupWeeklyReport.GroupType == "幸福小組")
                    //{
                    //    m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType = "有幸福小組名單";
                    //}
                    //else
                    //{
                    //    m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType = "沒幸福小組名單";
                    //}

                    // 設定繳費與報名資料
                    //m_InMemoryDataContextSmallGroup.FeeList.SetupFeeDataList(aGalleryViewModel.Account, aGalleryViewModel.Password);

                    // 設定需要點名的課程清單
                    m_InMemoryDataContextSmallGroup.FeeList.SetupLessonList(aGalleryViewModel.Account, aGalleryViewModel.Password);

                    if (m_InMemoryDataContextSmallGroup.ListManager.LoginType == "小組長" && m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        // 小組長回報，而且有幸福小組
                        ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;
                        ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                        ViewBag.HappyType = "有幸福小組名單";
                        ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                        #region 繳費與點名是否顯示在選單中
                        if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                        {
                            ViewBag.FeeDataListCount = "繳費與點名已有資料";
                        }
                        else
                        {
                            ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                        }
                        #endregion

                        SetMultiGroupLayoutParameter();

                        return Json(new { DisplayViewType = DisplayViewType, ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                    }
                    else if (m_InMemoryDataContextSmallGroup.ListManager.LoginType == "小組長" && m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "沒幸福小組名單")
                    {
                        // 小組長回報，沒有幸福小組
                        ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;
                        ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                        ViewBag.HappyType = "沒幸福小組名單";
                        ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                        #region 繳費與點名是否顯示在選單中
                        if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                        {
                            ViewBag.FeeDataListCount = "繳費與點名已有資料";
                        }
                        else
                        {
                            ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                        }
                        #endregion

                        SetMultiGroupLayoutParameter();

                        //return Json(new { DisplayViewType = DisplayViewType, ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                        //return Json(new { DisplayViewType = DisplayViewType, ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                        return Json(new { DisplayViewType = DisplayViewType, ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                    }
                    else if (m_InMemoryDataContextSmallGroup.ListManager.LoginType != "小組長" && m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "沒幸福小組名單")
                    {
                        // 個人回報，不是小組長，沒有幸福小組
                        ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;
                        ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                        ViewBag.HappyType = "沒幸福小組名單";
                        ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                        #region 繳費與點名是否顯示在選單中
                        if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                        {
                            ViewBag.FeeDataListCount = "繳費與點名已有資料";
                        }
                        else
                        {
                            ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                        }
                        #endregion

                        SetMultiGroupLayoutParameter();

                        return Json(new { DisplayViewType = DisplayViewType, ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                    }
                    else if (m_InMemoryDataContextSmallGroup.ListManager.LoginType != "小組長" && m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        // 個人回報 + 單純幸福小組長回報
                        ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;
                        ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                        ViewBag.HappyType = "有幸福小組名單";
                        ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                        #region 繳費與點名是否顯示在選單中
                        if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                        {
                            ViewBag.FeeDataListCount = "繳費與點名已有資料";
                        }
                        else
                        {
                            ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                        }
                        #endregion

                        SetMultiGroupLayoutParameter();

                        DisplayViewType = "HappyGroupView";
                        return Json(new { DisplayViewType = DisplayViewType, ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                    }
                    else
                    {
                        ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;
                        ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                        ViewBag.HappyType = "沒幸福小組名單";
                        ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                        #region 繳費與點名是否顯示在選單中
                        if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                        {
                            ViewBag.FeeDataListCount = "繳費與點名已有資料";
                        }
                        else
                        {
                            ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                        }
                        #endregion

                        SetMultiGroupLayoutParameter();

                        DisplayViewType = "HappyGroupView";
                        return Json(new { DisplayViewType = DisplayViewType, ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                    }
                }
                else
                {
                    return Json(new { DisplayViewType = "登入錯誤", ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = ContactIdString, fullname = ContactIdString });
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region Line Id Login 登入
        [Route("/Home/LineIdLoginView/{LineIdLoginViewPatameter}")]
        public IActionResult LineIdLoginView(string LineIdLoginViewPatameter)
        {
            try
            {
                var images = new List<string>();
                images.Add(Url.Content("~/assets/images/tpehoc-005.jpg"));
                images.Add(Url.Content("~/assets/images/tpehoc-006.jpg"));
                images.Add(Url.Content("~/assets/images/tpehoc-007.jpg"));
                images.Add(Url.Content("~/assets/images/tpehoc-008.jpg"));
                images.Add(Url.Content("~/assets/images/tpehoc-009.jpg"));

                m_InMemoryDataContextSmallGroup.LineBindingViewModel.Images = images;

                TempData["Proponent"] = LineIdLoginViewPatameter;

                return View(m_InMemoryDataContextSmallGroup.LineBindingViewModel);

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveUserLineId(string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId = UserLineId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.RoomId = RoomId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.GroupId = GroupId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.ViewType = ViewType;

                if (GroupId != null && GroupId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = GroupId;
                }
                else if (RoomId != null && RoomId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = RoomId;
                }
                else
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = UserLineId;
                }

                Entity LineLoginContact = this.m_ToolUtilityClass.RetrieveContactByLineId(UserLineId);

                if (this.m_ToolUtilityClass.GetEntityStringAttribute( LineLoginContact, "fullname").Contains("(Line)") == false )
                {
                    // 已經綁定過了。全名不包含"(Line")
                    GalleryViewModel aGalleryViewModel = new GalleryViewModel();
                    //aGalleryViewModel.Account = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "new_app_acount");
                    //aGalleryViewModel.Password = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "new_app_pass");

                    return await ProcessLogin(aGalleryViewModel);
                }
                else
                {
                    // 還沒有綁定
                    return Json(new { status = "1", message = "尚未綁定" });
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region 註冊帳號
        public IActionResult Register()
        {
            try
            {
                var images = new List<string>();
                images.Add(Url.Content("~/assets/images/register-001.jpg"));
                images.Add(Url.Content("~/assets/images/register-002.jpg"));
                images.Add(Url.Content("~/assets/images/register-003.jpg"));
                images.Add(Url.Content("~/assets/images/register-004.jpg"));
                images.Add(Url.Content("~/assets/images/register-005.jpg"));
                //images.Add(Url.Content("~/assets/images/photo-1.jpg"));
                //images.Add(Url.Content("~/assets/images/photo-10.jpg"));
                //images.Add(Url.Content("~/assets/images/photo-6.jpg"));
                //images.Add(Url.Content("~/assets/images/photo-9.jpg"));
                return View(new RegisterViewModel
                {
                    Images = images
                });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPost]
        public IActionResult ProcessRegister(RegisterViewModel aRegisterViewModel)
        {
            try
            {
                RegisterManager aRegisterManager = new RegisterManager();

                string RegisterResult = aRegisterManager.Register(aRegisterViewModel.FullName, aRegisterViewModel.Mobile, aRegisterViewModel.Account, aRegisterViewModel.Password, aRegisterViewModel.ConfirmPassword);

                if (RegisterResult.StartsWith("註冊成功"))
                {
                    return Json(new { status = "1", message = aRegisterViewModel.FullName + RegisterResult, fullname = aRegisterViewModel.FullName, account = aRegisterViewModel.Account, password = aRegisterViewModel.Password });
                }
                else
                {
                    return Json(new { status = "2", message = RegisterResult, fullname = RegisterResult });
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #region 多小組回報
        [Route("/Home/MultiGroupView/{LoginParameter}")]
        public ActionResult MultiGroupView(string LoginParameter)
        {
            try
            {
                ViewBag.ListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;

                if (LoginParameter != "AccountPassword")
                {
                    string DisplayViewType = m_InMemoryDataContextSmallGroup.ListManager.GetDisplayViewType();
                    if (DisplayViewType == "MultiGroupView")
                    {
                        #region 用小組長回報網頁登入
                        if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport != null)
                        {
                            // 登入到多小組回報，整合是頁面要先歸零
                            m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag = false;

                            m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport = null;
                        }

                        #region 選單控制區塊
                        #region 控制 Navigation 下拉項目
                        ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "不是單純行事曆";
                        ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "顯示牧養回報項目";
                        ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType;
                        #endregion

                        ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType; // 看是小組長還是個人回報
                        ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;

                        ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType; // 繳費點名
                        #region 繳費與點名是否顯示在選單中
                        if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                        {
                            ViewBag.FeeDataListCount = "繳費與點名已有資料";
                        }
                        else
                        {
                            ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                        }
                        #endregion

                        if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                        {
                            ViewBag.HappyType = "有幸福小組名單";
                        }
                        else
                        {
                            ViewBag.HappyType = "沒幸福小組名單";
                        }
                        SetMultiGroupLayoutParameter();
                        #endregion

                        //ListSmallGroupWeeklyReport bSmallGroupData = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.Where(e => e.ListEntityId == "001").ToList()[0];

                        //return View(m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.Where(e => e.ListEntityId == m_InMemoryDataContextSmallGroup.m_ListManager.ActiveListId).Select());

                        // 設定多個組長處理需要的資料
                        if (m_InMemoryDataContextSmallGroup.ListManager.InitialFlag == true)
                        {
                            //m_InMemoryDataContextSmallGroup.SetupListManager(m_InMemoryDataContextSmallGroup.ListManager.m_Account, m_InMemoryDataContextSmallGroup.ListManager.m_Password, DateTime.Now, true);
                            m_InMemoryDataContextSmallGroup.ListManager.SetupListManager(m_InMemoryDataContextSmallGroup.ListManager.m_Account, m_InMemoryDataContextSmallGroup.ListManager.m_Password, m_InMemoryDataContextSmallGroup.ListManager.m_SelectDate);
                        }
                        else
                        {
                            m_InMemoryDataContextSmallGroup.ListManager.InitialFlag = true;
                        }

                        //throw new Exception("沒有資料!");
                        return View(m_InMemoryDataContextSmallGroup.ListManager);
                        //return View(m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.Select(ListEntityId=> m_InMemoryDataContextSmallGroup.m_ListManager.ActiveListId));
                        #endregion
                    }
                    else
                    {
                        // 只有單一個小組，直接跳轉到整合式回報畫面
                        //m_InMemoryDataContextSmallGroup.ListManager.m_SmallGroupWeeklyReport.LoadFlag = false;

                        //if (m_InMemoryDataContextSmallGroup.ListManager.m_SmallGroupWeeklyReport == null)
                        //{
                        //    // 登入到多小組回報，整合是頁面要先歸零
                        //    m_InMemoryDataContextSmallGroup.ListManager.m_SmallGroupWeeklyReport.LoadFlag = false;
                        //}

                        //throw new Exception("沒有資料!");

                        return RedirectToAction("IntegrateView");
                    }
                }
                else if (LoginParameter == "jquery.js")
                {
                    ViewBag.LoginType = "個人登入";
                    return Ok();
                }
                else
                {
                    #region 小組長 Line 登入
                    string FullName = m_ToolUtilityClass.RetrieveContactEntityByLineUserId(LoginParameter).Attributes["fullname"].ToString();

                    LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                    // 寫入LINE的個人基本資料
                    if (FullName.EndsWith("(Line)"))
                    {
                        aLineMessagingProcessorClass.NotifyLineBinding(LoginParameter);

                        return RedirectToAction("Login");
                    }
                    else
                    {
                        m_InMemoryDataContextSmallGroup.SetupSmallGroupData(FullName, "LineIdLogin", LoginParameter, DateTime.Now, true);

                        ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType; // 看是小組長還是個人回報
                        ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                        ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                        #region 繳費與點名是否顯示在選單中
                        if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                        {
                            ViewBag.FeeDataListCount = "繳費與點名已有資料";
                        }
                        else
                        {
                            ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                        }
                        #endregion


                        if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                        {
                            ViewBag.HappyType = "有幸福小組名單";
                        }
                        else
                        {
                            ViewBag.HappyType = "沒幸福小組名單";
                        }
                        SetMultiGroupLayoutParameter();

                        return View(m_InMemoryDataContextSmallGroup.ListManager);
                    }
                    #endregion
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region 整合式小組長點名
        [Route("/Home/IntegrateView/{LoginParameter}")]
        //[CheckSessionOut]
        public ActionResult IntegrateView(string LoginParameter)
        {
            try
            {
                //ViewBag.ListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId = LoginParameter;
                #region 控制 Navigation 下拉項目
                ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "不是單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType;
                #endregion

                if (LoginParameter != "AccountPassword")
                {
                    #region 看看這是不是多小組回報統計點過來的
                    if (m_InMemoryDataContextSmallGroup.ListManager.GetDisplayViewType() == "MultiGroupView")
                    {
                        // 這是多小組回報統計點過來的
                        if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport == null)
                        {
                            m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(LoginParameter);
                        }
                        else if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag == false)
                        {
                            m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(LoginParameter);
                        }
                        else { }
                    }
                    else
                    { 
                        // 這是單一小組，選單裡其他選項(新增新人、維護基本資料)點過來的，所以不要再載入資料
                    }
                    #endregion

                    if (m_InMemoryDataContextSmallGroup.ListManager.GetDisplayViewType() == "IntegrateView")
                    {
                        ViewBag.ListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;
                    }
                    else
                    {
                        if (LoginParameter == "undefined")
                        {
                            ViewBag.ListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;
                            ViewBag.SpiritualLeaderListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;
                        }
                        else if (LoginParameter != "IntegrateView")
                        {
                            ViewBag.ListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId = LoginParameter;
                            ViewBag.SpiritualLeaderListId = LoginParameter;
                        }
                        else
                        {
                            ViewBag.ListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;
                            ViewBag.SpiritualLeaderListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;
                        }
                    }

                    #region 用小組長回報網頁登入
                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType; // 看是小組長還是個人回報
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;

                    ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                    #region 繳費與點名是否顯示在選單中
                    if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                    {
                        ViewBag.FeeDataListCount = "繳費與點名已有資料";
                    }
                    else
                    {
                        ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                    }
                    #endregion

                    if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }
                    SetMultiGroupLayoutParameter();

                    if(m_InMemoryDataContextSmallGroup.ListManager.LoginType == "個人回報" )
                    {
                        return RedirectToAction("DisplayErrorView", new { ErrorMessage = "您沒有點名的權限" });
                    }
                    else
                    {
                        return View(m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport);
                    }
                    #endregion
                }
                else if (LoginParameter == "jquery.js")
                {
                    ViewBag.LoginType = "個人登入";
                    return Ok();
                }
                else
                {
                    #region 小組長 Line 登入
                    string FullName = m_ToolUtilityClass.RetrieveContactEntityByLineUserId(LoginParameter).Attributes["fullname"].ToString();

                    LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                    // 寫入LINE的個人基本資料
                    if (FullName.EndsWith("(Line)"))
                    {
                        aLineMessagingProcessorClass.NotifyLineBinding(LoginParameter);

                        return RedirectToAction("Login");
                    }
                    else
                    {
                        m_InMemoryDataContextSmallGroup.SetupSmallGroupData(FullName, "LineIdLogin", LoginParameter, DateTime.Now, true);

                        ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType; // 看是小組長還是個人回報
                        ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                        ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                        #region 繳費與點名是否顯示在選單中
                        if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                        {
                            ViewBag.FeeDataListCount = "繳費與點名已有資料";
                        }
                        else
                        {
                            ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                        }
                        #endregion

                        if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                        {
                            ViewBag.HappyType = "有幸福小組名單";
                        }
                        else
                        {
                            ViewBag.HappyType = "沒幸福小組名單";
                        }
                        SetMultiGroupLayoutParameter();

                        m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(LoginParameter);
                        return View(m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport);
                    }
                    #endregion
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpGet]
        public object LoadIntegrate(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                //m_InMemoryDataContextSmallGroup.ListManager.SetupListSmallGroupWeeklyReport(id);

                if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport == null)
                {
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(id);
                }
                else if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag == false)
                {
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(id);
                }
                else { }

                var tasks = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members;

                //var tasks = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.Where(e => e.ListEntityId == id).Select(e => e.m_SmallGroupDataList.m_SmallGroupData.Members).FirstOrDefault();

                //return DataSourceLoader.Load<Member>(tasks, loadOptions);
                return DataSourceLoader.Load(tasks, loadOptions);

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPost]
        public IActionResult InsertPresentRecord(string values)
        {
            try
            {
                //SmallGroupData bSmallGroupData = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.First(o => o.ListEntityId == m_InMemoryDataContextSmallGroup.ListManager.ActiveListId).m_SmallGroupDataList.m_SmallGroupData;

                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.InsertMember(values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPut]
        //[CheckSessionOut]
        public IActionResult UpdateSmallGroupPresentRecord(string key, string values)
        {
            try
            {
                // 修改小組長牧養主日出席、小組出席、代禱事項
                //Task.Run(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.UpdateMember(key, values));
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.UpdateMember(key, values);

                // 修改全部的(也就是維護基本)資料
                //Task.Run(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values));
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpDelete]
        public IActionResult DeletePresentRecord(string key)
        {
            try
            {
                // 刪除全部的(也就是維護基本)資料
                Member DeletedMember = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.DeleteMember(key);

                if (DeletedMember != null)
                {
                    // 整合式網頁按上傳按鈕
                    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.DeleteMemberData
                    (
                        m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                        m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                        DeletedMember
                    );
                }

                // 刪除小組長牧養主日出席、小組出席、代禱事項
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.DeleteMember(key);

                // 刪除小組長牧養主日出席、小組出席、代禱事項
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.DeleteMember(key);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPost]
        public IActionResult SaveSmallGroup(string aResult)
        {
            try
            {
                // 小組長點名按上傳
                //m_InMemoryDataContextSmallGroup.SmallGroupDataList.UploadIntegrateData();

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public async Task<IActionResult> SaveIntegrate(string WeeklyReportData, String HappyWeekIndex, String HappyWeekTopic, String CheckBox)
        {
            try
            {
                // 整合式網頁按上傳按鈕
                if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.ListEntityName.Contains("幸福"))
                {
                    if (HappyWeekIndex == null && HappyWeekTopic == null)
                    {
                        return Json(new { status = "2", message = "幸福小組必須填寫第幾週和主題" });
                    }
                    else if (HappyWeekIndex == null)
                    {
                        return Json(new { status = "2", message = "幸福小組必須填寫第幾週" });
                    }
                    else if (HappyWeekTopic == null)
                    {
                        return Json(new { status = "2", message = "幸福小組必須填寫主題" });
                    }
                    else { }
                }
                bool PasueCheckBox = CheckBox == "true" ? true : false;

                //Task AsyncTask = Task.Run( () =>  m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                //(
                //    m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                //    m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                //    m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                //    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                //    WeeklyReportData,
                //    HappyWeekIndex,
                //    HappyWeekTopic,
                //    PasueCheckBox // 小組是否暫停
                //));

                //await AsyncTask;

                Task.Factory.StartNew(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                (
                    m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                    m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                    m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                    WeeklyReportData,
                    HappyWeekIndex,
                    HappyWeekTopic,
                    PasueCheckBox // 小組是否暫停
                ), TaskCreationOptions.LongRunning);
                //Parallel.ForEach( (item) =>
                //{
                //    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                //                    (
                //                        m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                //                        m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                //                        m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                //                        m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                //                        WeeklyReportData,
                //                        HappyWeekIndex,
                //                        HappyWeekTopic,
                //                        PasueCheckBox // 小組是否暫停
                //                    );             
                // });

                //return Json(new { status = "1", message = "成功上傳了.... : " + "Status = " + AsyncTask.Status + " IsFaulted = " + AsyncTask.IsFaulted + " IsCanceled=" + AsyncTask.IsCanceled });
                return Json(new { status = "1", message = "成功上傳了.... !"  });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #region 個人回報
        [HttpGet]
        public IActionResult PersonalReport()
        {
            try
            {
                #region 個人回報網頁選項設定
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                ViewBag.HappyType = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType;
                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion


                SetMultiGroupLayoutParameter();

                if (m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData.Count == 1)
                {
                    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData.First().ListEntityId;
                }
                else if (ViewBag.MultiGroupIndex == "HybridView")
                {
                    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;
                }
                else if (ViewBag.MultiGroupIndex == "SingleMultiGroupView")
                {
                    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = "";
                }
                else
                {
                    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = "";
                }
                #endregion

                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.SetPersonalReportViewModel();

                return View(m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_PersonalReportViewModel);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [HttpGet]
        public object LoadPersonReport(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                //m_InMemoryDataContextSmallGroup.ListManager.SetupListSmallGroupWeeklyReport(id);

                if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport == null)
                {
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(id);
                }
                else if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag == false)
                {
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(id);
                }
                else { }


                var tasks = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members;

                //var tasks = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.Where(e => e.ListEntityId == id).Select(e => e.m_SmallGroupDataList.m_NewPersonFollowUpData.Members).FirstOrDefault();

                //return DataSourceLoader.Load<Member>(tasks, loadOptions);
                return DataSourceLoader.Load(tasks, loadOptions);

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPost]
        public IActionResult InsertPersonReport(string values)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.InsertMember(values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPut]
        public IActionResult UpdatePersonReport(string key, string values)
        {
            try
            {
                // 修改全部的(也就是維護基本)資料
                //Task.Run(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values));
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpDelete]
        public IActionResult DeletePersonReport(string key)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.DeleteMember(key);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPost]
        public IActionResult SavePersonReport(string WeeklyReportData)
        {
            try
            {
                // 整合式網頁按上傳按鈕
                //Task.Run(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                //(
                //    m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                //    m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                //    m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                //    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                //    WeeklyReportData,"","",false
                //));

                Task.Factory.StartNew(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                (
                    m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                    m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                    m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                    WeeklyReportData, "", "", false
                ), TaskCreationOptions.LongRunning);

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public IActionResult SavePersonalReportForm(PersonalReportViewModel aPersonalReportViewModel)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.GetPersonalReportViewModelResult( aPersonalReportViewModel );

                // 整合式網頁按上傳按鈕
                //Task.Run(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                //(
                //    m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                //    m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                //    m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                //    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                //    "不需更新小組日誌", "", "",false
                //));

                Task.Factory.StartNew(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                (
                    m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                    m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                    m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                    "不需更新小組日誌", "", "", false
                ), TaskCreationOptions.LongRunning);

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }


        #endregion
        #region 小組長點名及個人回報
        [Route("/Home/SmallGroupReportView/{LoginParameter}")]
        public ActionResult SmallGroupReportView(string LoginParameter)
        {
            try
            {
                ViewBag.ListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;
                #region 控制 Navigation 下拉項目
                ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "不是單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType;
                #endregion

                if (LoginParameter == "AccountPassword")
                {
                    #region 用小組長回報網頁登入
                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                    ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                    #region 繳費與點名是否顯示在選單中
                    if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                    {
                        ViewBag.FeeDataListCount = "繳費與點名已有資料";
                    }
                    else
                    {
                        ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                    }
                    #endregion

                    if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }
                    SetMultiGroupLayoutParameter();

                    //return View(m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.Where(e => e.ListEntityId == m_InMemoryDataContextSmallGroup.m_ListManager.ActiveListId).Select());
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(LoginParameter);
                    return View(m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport);

                    #endregion
                }
                else if (LoginParameter == "jquery.js")
                {
                    ViewBag.LoginType = "個人登入";
                    return Ok();
                }
                else
                {
                    #region 小組長 Line 登入
                    string FullName = m_ToolUtilityClass.RetrieveContactEntityByLineUserId(LoginParameter).Attributes["fullname"].ToString();

                    LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                    // 寫入LINE的個人基本資料
                    if (FullName.EndsWith("(Line)"))
                    {
                        aLineMessagingProcessorClass.NotifyLineBinding(LoginParameter);

                        return RedirectToAction("Login");
                    }
                    else
                    {
                        m_InMemoryDataContextSmallGroup.SetupSmallGroupData(FullName, "LineIdLogin", LoginParameter, DateTime.Now, true);

                        ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                        ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                        ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                        #region 繳費與點名是否顯示在選單中
                        if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                        {
                            ViewBag.FeeDataListCount = "繳費與點名已有資料";
                        }
                        else
                        {
                            ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                        }
                        #endregion


                        if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                        {
                            ViewBag.HappyType = "有幸福小組名單";
                        }
                        else
                        {
                            ViewBag.HappyType = "沒幸福小組名單";
                        }
                        SetMultiGroupLayoutParameter();

                        //m_InMemoryDataContextSmallGroup.ListManager.SetupListSmallGroupWeeklyReport(LoginParameter);
                        if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport == null)
                        {
                            m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(LoginParameter);
                        }
                        else if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag == false)
                        {
                            m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(LoginParameter);
                        }
                        else { }

                        return View(m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport);

                        //return View(m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport);
                    }
                    #endregion
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpGet]
        public object LoadSmallGroup(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                //m_InMemoryDataContextSmallGroup.ListManager.SetupListSmallGroupWeeklyReport(id);
                if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport == null)
                {
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(id);
                }
                else if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag == false)
                {
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(id);
                }
                else { }

                var tasks = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members;

                //var tasks = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.Where(e => e.ListEntityId == id).Select(e => e.m_SmallGroupDataList.m_SmallGroupData.Members).FirstOrDefault();

                //return DataSourceLoader.Load<Member>(tasks, loadOptions);
                return DataSourceLoader.Load(tasks, loadOptions);

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPost]
        #endregion
        #region 新人跟進關懷
        [HttpGet]
        public ActionResult NewPersonFollowUpView()
        {
            try
            {
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion

                ViewBag.ListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                SetMultiGroupLayoutParameter();

                return View(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_NewPersonFollowUpData);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [HttpGet]
        public object LoadNewPersonFollowUp(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                //m_InMemoryDataContextSmallGroup.ListManager.SetupListSmallGroupWeeklyReport(id);

                if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport == null)
                {
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(id);
                }
                else if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag == false)
                {
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(id);
                }
                else { }


                var tasks = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.Members;

                //var tasks = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.Where(e => e.ListEntityId == id).Select(e => e.m_SmallGroupDataList.m_NewPersonFollowUpData.Members).FirstOrDefault();

                //return DataSourceLoader.Load<Member>(tasks, loadOptions);
                return DataSourceLoader.Load(tasks, loadOptions);

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPost]
        public IActionResult InsertNewPresentRecord(string values)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.InsertMember(values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPut]
        public IActionResult UpdateNewPresentRecord(string key, string values)
        {
            try
            {
                // 修改新人跟進關懷主日出席、小組出席、代禱事項
                //Task.Run(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.UpdateMember(key, values));
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.UpdateMember(key, values);

                // 修改全部的(也就是維護基本)資料
                //Task.Run(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values));
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpDelete]
        public IActionResult DeleteNewPresentRecord(string key)
        {
            try
            {
                // 刪除小組長牧養主日出席、小組出席、代禱事項
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.DeleteMember(key);

                // 刪除小組長牧養主日出席、小組出席、代禱事項
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.DeleteMember(key);

                // 刪除全部的(也就是維護基本)資料
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.DeleteMember(key);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPost]
        public IActionResult SaveNewPersonFollowUp(string aResult)
        {
            try
            {
                // 新人跟進關懷按上傳
                //m_InMemoryDataContextSmallGroup.SmallGroupDataList.UploadIntegrateData();

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #region 基本資料維護
        [HttpGet]
        public ActionResult MaintainPersonInfomationView()
        {
            try
            {
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }
                SetMultiGroupLayoutParameter();

                return View(m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpGet]
        public object LoadMaintainPersonInfomation(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                var tasks = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members;

                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPut]
        public IActionResult UpdateMaintainPersonInfomationPresentRecord(string key, string values)
        {
            try
            {
                // 修改全部的(也就是維護基本)資料
                //Task.Run(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values));
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPost]
        public IActionResult SavePersonInfomation(ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            try
            {
                // 維護基本資料按上傳
                //Task.Run(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                //(
                //    m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                //    m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                //    m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                //    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                //    "不需更新小組日誌", "", "",false
                //));

                Task.Factory.StartNew(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                (
                    m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                    m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                    m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                    "不需更新小組日誌", "", "", false
                ), TaskCreationOptions.LongRunning);

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public JsonResult GetMarkers()
        {
            return Json(m_InMemoryDataContextSmallGroup.ListManager.GetMarkers());
        }
        #endregion
        #region 更換日期
        [HttpGet]
        public IActionResult UpdateDate(string SelectedDate)
        {
            try
            {
                #region 小組 主日 點名
                //SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();
                //DateTime aSelectDate = DateTime.Parse(SelectedDate);
                DateTime aSelectDate = DateTime.Parse(SelectedDate).ToLocalTime();
                #endregion
                #region 下載資料
                // 設定多個組長處理需要的資料
                m_InMemoryDataContextSmallGroup.ListManager.SetupListManager(m_InMemoryDataContextSmallGroup.ListManager.m_Account, m_InMemoryDataContextSmallGroup.ListManager.m_Password, aSelectDate);

                // 透過取得多小組網頁需要的資料之後，判斷這是多小組還是單一小組長的回報
                string DisplayViewType = m_InMemoryDataContextSmallGroup.ListManager.GetDisplayViewType();
                if (DisplayViewType == "IntegrateView")
                {
                    // 得知這是單一小組長的回報，所以就直接下載整合式網頁所需的資料
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(m_InMemoryDataContextSmallGroup.ListManager.ActiveListId);
                }
                else
                {
                    // 得知這是多小組的回報，就不需要下載整合式網頁所需的資料
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(m_InMemoryDataContextSmallGroup.ListManager.ActiveListId);
                }
                #endregion

                #region 個人回報網頁選項設定
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion

                ViewBag.HappyType = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType;

                SetMultiGroupLayoutParameter();

                if (m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData.Count == 1)
                {
                    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData.First().ListEntityId;
                }
                else if (ViewBag.MultiGroupIndex == "HybridView")
                {
                    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;
                }
                else if (ViewBag.MultiGroupIndex == "SingleMultiGroupView")
                {
                    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = "";
                }
                else
                {
                    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = "";
                }
                #endregion

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public IActionResult UpdateMultiGroupDate(string SelectedDate)
        {
            try
            {
                #region 小組 主日 點名
                //SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();
                //DateTime aSelectDate = DateTime.Parse(SelectedDate);
                DateTime aSelectDate = DateTime.Parse(SelectedDate).ToLocalTime();
                #endregion

                #region 下載資料
                // 設定多個組長處理需要的資料
                m_InMemoryDataContextSmallGroup.ListManager.SetupListManager(m_InMemoryDataContextSmallGroup.ListManager.m_Account, m_InMemoryDataContextSmallGroup.ListManager.m_Password, aSelectDate);
                #endregion

                return Json(new { ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId });

                //return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public IActionResult UpdateIntegrateDate(string SelectedDate)
        {
            try
            {
                #region 小組 主日 點名
                //SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();
                //DateTime aSelectDate = DateTime.Parse(SelectedDate);
                DateTime aSelectDate = DateTime.Parse(SelectedDate).ToLocalTime();
                #endregion

                #region 下載資料
                // 設定多個組長處理需要的資料

                // 因為換日期時呼叫SetupListManager()會更動到 ActiveListId；但是換日期是不應該更動到ActiveListId
                // 所以再把他暫存起來
                String ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;

                m_InMemoryDataContextSmallGroup.ListManager.SetupListManager(m_InMemoryDataContextSmallGroup.ListManager.m_Account, m_InMemoryDataContextSmallGroup.ListManager.m_Password, aSelectDate);

                // 因為換日期時呼叫SetupListManager()會更動到 ActiveListId；但是換日期是不應該更動到ActiveListId
                // 所以再把他還原回來
                m_InMemoryDataContextSmallGroup.ListManager.ActiveListId = ActiveListId;

                m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(ActiveListId);

                //if (m_InMemoryDataContextSmallGroup.ListManager.ActiveListId != null && m_InMemoryDataContextSmallGroup.ListManager.ActiveListId != "")
                //{
                //    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(m_InMemoryDataContextSmallGroup.ListManager.ActiveListId);
                //}

                //if (DisplayViewType == "IntegrateView")
                //{
                //    // 得知這是單一小組長的回報，所以就直接下載整合式網頁所需的資料
                //    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(m_InMemoryDataContextSmallGroup.ListManager.ActiveListId);
                //}
                //else
                //{
                //    // 得知這是多小組的回報，就不需要下載整合式網頁所需的資料
                //    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(m_InMemoryDataContextSmallGroup.ListManager.ActiveListId);
                //}
                #endregion

                return Json(new { ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId });

                //return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #region 週報管理

        public IActionResult WeeklyReport()
        {
            try
            {
                if (m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReport.WeeklyReportContent == "尚未初始化")
                {
                    m_InMemoryDataContextSmallGroup.WeeklyReportData.SetupWeeklyReport(m_InMemoryDataContextSmallGroup.m_ListManager.m_Account, m_InMemoryDataContextSmallGroup.m_ListManager.m_Password, m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_SundayDate);

                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                    ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                    #region 繳費與點名是否顯示在選單中
                    if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                    {
                        ViewBag.FeeDataListCount = "繳費與點名已有資料";
                    }
                    else
                    {
                        ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                    }
                    #endregion

                    if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }
                    SetMultiGroupLayoutParameter();

                    return View(m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReportViewModel);
                }
                else
                {

                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;

                    if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }
                    SetMultiGroupLayoutParameter();

                    return View(m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReportViewModel);
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPost]
        public IActionResult SaveWeeklyReport(SmallGroupData aSmallGroupData)
        {
            try
            {
                //if (m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_Account != "")
                //{
                //    // 先上傳小組點名資料，萬一沒有先上傳小組點名，則仍然可以上傳小組日誌，因為在後台會建立新增周報
                //    m_InMemoryDataContextSmallGroup.SmallGroupDataList.UploadMemberInfomationPackage();

                //    // 給上傳用的
                //    m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReport.WeeklyReportContent = aWeeklyReportViewModel.WeeklyReportData;
                //    m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReport.PresentContent = aWeeklyReportViewModel.WeeklyReportAnalysis;

                //    // 給網頁顯示用的
                //    m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReportViewModel.WeeklyReportData = aWeeklyReportViewModel.WeeklyReportData;
                //    m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReportViewModel.WeeklyReportAnalysis = aWeeklyReportViewModel.WeeklyReportAnalysis;

                //    m_InMemoryDataContextSmallGroup.WeeklyReportData.UploadWeeklyReport(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_Account, m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_Password, m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_SundayDate, m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReport);
                //}

                return Json(new { status = "1", message = "成功上傳了...." });
                //return Json(new { status = "2", message = "密碼錯誤...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [Route("/Home/InputReport/{FullName}")]
        public IActionResult InputReport(string FullName)
        {
            try
            {
                return View((object)FullName);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region 幸福小組回報
        public ActionResult HappyGroup()
        {
            try
            {
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.SpiritLeaderList = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList.SpiritLeaderList;
                    ViewBag.ListEntityId = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList.ListEntityId;

                    ViewBag.HappyGroupName = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList.HappyGroupName;

                    //m_InMemoryDataContextSmallGroup.HappyGroupDataManager.InitialHappyGroupData( ref m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupListClass );

                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }
                SetMultiGroupLayoutParameter();

                return View();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }


        [HttpGet]
        public object LoadHappyGroupList(DataSourceLoadOptions loadOptions)
        {
            try
            {
                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupListClass != null)
                {
                    return DataSourceLoader.Load(m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass, loadOptions);
                }
                else { return null; }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpGet]
        public object LoadHappyWeeklyReport(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupListClass != null)
                {
                    var tasks = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass.Where(e => e.ListEntityId == id).Select(e => e.HappyGroupWeeklyReportList).FirstOrDefault();

                    return DataSourceLoader.Load(tasks, loadOptions);
                }
                else
                {
                    return null;
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpGet]
        public object LoadBest(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.GetHappyGroupWeeklyReportListClassByWeeklyReportId(id);

                if (aHappyGroupWeeklyReportListClass != null)
                {
                    //var tasks = SampleData_001.DataGridEmployees.Where(e => e.ID == id).Select(e => e.Tasks).FirstOrDefault();
                    var tasks = aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList.Where(e => e.HappyGroupWeeklyReportId == id).Select(e => e.BestRecordList).FirstOrDefault();

                    return DataSourceLoader.Load(tasks, loadOptions);
                }
                else { return null; }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        // POST api/values
        [HttpPost]
        public IActionResult PostBest(string values)
        {
            try
            {
                // 新增週報或是BEST

                m_InMemoryDataContextSmallGroup.HappyGroupDataManager.AddActiveHappyGroup(values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }

        }



        [HttpGet]
        public object LoadHappyGroupListToIntegrate(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                //m_InMemoryDataContextSmallGroup.ListManager.SetupListSmallGroupWeeklyReport(id);

                if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport == null)
                {
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(id);
                }
                else if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag == false)
                {
                    m_InMemoryDataContextSmallGroup.ListManager.SetupIntegrateData(id);
                }
                else { }

                var tasks = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.Members;

                //var tasks = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.Where(e => e.ListEntityId == id).Select(e => e.m_SmallGroupDataList.m_SmallGroupData.Members).FirstOrDefault();

                //return DataSourceLoader.Load<Member>(tasks, loadOptions);
                return DataSourceLoader.Load(tasks, loadOptions);

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPost]
        public IActionResult InsertHappyGroupPresentRecord(string values)
        {
            try
            {
                //SmallGroupData bSmallGroupData = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.First(o => o.ListEntityId == m_InMemoryDataContextSmallGroup.ListManager.ActiveListId).m_SmallGroupDataList.m_SmallGroupData;

                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.InsertMember(values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPut]
        public IActionResult UpdateHappyGroupPresentRecord(string key, string values)
        {
            try
            {
                // 修改小組長牧養主日出席、小組出席、代禱事項
                //Task.Run(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.UpdateMember(key, values));
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.UpdateMember(key, values);

                // 修改全部的(也就是維護基本)資料
                //Task.Run(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values));
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpDelete]
        public IActionResult DeleteHappyGroupPresentRecord(string key)
        {
            try
            {
                // 刪除全部的(也就是維護基本)資料
                Member DeletedMember = m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.DeleteMember(key);

                if (DeletedMember != null)
                {
                    // 整合式網頁按上傳按鈕
                    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.DeleteMemberData
                    (
                        m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                        m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                        DeletedMember
                    );
                }

                // 刪除小組長牧養主日出席、小組出席、代禱事項
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.DeleteMember(key);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        // PUT api/values/5
        [HttpPut]
        public IActionResult PutBest(string key, string values)
        {
            try
            {
                // 修改週報或是BEST
                //m_InMemoryDataContextSmallGroup.HappyGroupDataManager.UpdateUpdatedMasterOrDetail(key, values);
                //Task.Run(() => m_InMemoryDataContextSmallGroup.HappyGroupDataManager.UpdateActiveHappyGroup(key, values));
                m_InMemoryDataContextSmallGroup.HappyGroupDataManager.UpdateActiveHappyGroup(key, values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        // DELETE api/values/5
        [HttpDelete]
        public void DeleteBest(string key)
        {
            try
            {
                // 刪除週報或是BEST
                //Dictionary < string, string> aDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(key);
                //m_HappyGroupDataManager.UpdateActiveHappyGroup(aDictionary["BestRecordId"], values);
                m_InMemoryDataContextSmallGroup.HappyGroupDataManager.DeleteActiveHappyGroup(key);
                return;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }


        [HttpPost]
        public IActionResult SaveHappyGroup()
        {
            try
            {
                // 上傳至雲端系統資料庫
                string SerializedHappyGroupDataManager = (string)TempData.Peek("HappyGroupDataManager");

                //Task.Run(() => m_InMemoryDataContextSmallGroup.HappyGroupDataManager.SaveActiveHappyGroup());
                m_InMemoryDataContextSmallGroup.HappyGroupDataManager.SaveActiveHappyGroup();

                // 初始化成為尚未修改的旗標
                m_InMemoryDataContextSmallGroup.HappyGroupDataManager.InitialHappyGroupData(ref m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupListClass);

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region 課程繳費與點名
        public ActionResult PresentFeeListView()
        {
            try
            {
                #region 用小組長回報網頁登入
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                ViewBag.FeeResult = m_InMemoryDataContextSmallGroup.FeeList.Result;

                // 設定繳費與報名資料
                //m_InMemoryDataContextSmallGroup.SetupFeeList();

                SetFeeManagerViewBag();
                SetMultiGroupLayoutParameter();

                return View(m_InMemoryDataContextSmallGroup.FeeList);

                #endregion
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        public ActionResult PresentView()
        {
            try
            {
                #region 用小組長回報網頁登入
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                ViewBag.FeeResult = m_InMemoryDataContextSmallGroup.FeeList.Result;

                // 設定繳費與報名資料
                //m_InMemoryDataContextSmallGroup.SetupFeeList();

                SetFeeManagerViewBag();
                SetMultiGroupLayoutParameter();

                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion
                return View(m_InMemoryDataContextSmallGroup.FeeList);

                #endregion
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [Route("/Home/FeeView/{LessonIdParameter?}")]
        public ActionResult FeeView(string LessonIdParameter)
        {
            try
            {
                #region 用小組長回報網頁登入
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                ViewBag.FeeResult = m_InMemoryDataContextSmallGroup.FeeList.Result;

                // 設定繳費與報名資料
                if (LessonIdParameter != null)
                {
                    m_InMemoryDataContextSmallGroup.FeeList.SetupPresentFeeList(LessonIdParameter);
                }

                SetFeeManagerViewBag();
                SetMultiGroupLayoutParameter();

                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion

                return View(m_InMemoryDataContextSmallGroup.FeeList);

                #endregion
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public ActionResult FeeManagerView()
        {
            try
            {
                #region 用小組長回報網頁登入
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                ViewBag.FeeResult = m_InMemoryDataContextSmallGroup.FeeList.Result;

                // 設定繳費與報名資料
                //m_InMemoryDataContextSmallGroup.SetupFeeList();

                SetFeeManagerViewBag();
                SetMultiGroupLayoutParameter();

                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion
                return View(m_InMemoryDataContextSmallGroup.FeeList);

                #endregion
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        public void SetFeeManagerViewBag()
        {
            try
            {
                ViewBag.Colume9 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson1;
                ViewBag.Colume10 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson2;
                ViewBag.Colume11 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson3;
                ViewBag.Colume12 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson4;
                ViewBag.Colume13 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson5;
                ViewBag.Colume14 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson6;
                ViewBag.Colume15 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson7;
                ViewBag.Colume16 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson8;
                ViewBag.Colume17 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson9;
                ViewBag.Colume18 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson10;
                ViewBag.Colume19 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson11;
                ViewBag.Colume20 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson12;
                ViewBag.Colume21 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson13;
                ViewBag.Colume22 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson14;
                ViewBag.Colume23 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.Lesson15;
                ViewBag.Colume24 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.HomeWorkA;
                ViewBag.Colume25 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.HomeWorkB;
                ViewBag.Colume26 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.HomeWorkC;
                ViewBag.Colume27 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.HomeWorkD;
                ViewBag.Colume28 = m_InMemoryDataContextSmallGroup.FeeList.m_ClassName.HomeWorkE;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [HttpPost]
        public IActionResult SaveFeeManager(string aResult)
        {
            try
            {
                #region 不正確的日期格式
                //var Format = "ddd MMM dd yyyy HH:mm:ss GMT+0800 (台北標準時間)"; // DataGrid如果沒有設PAGE，則正確的日期格式
                ////var Format = "ddd MMM dd yyyy HH:mm:ss GMT+0800"; // DataGrid如果沒有設PAGE，則正確的日期格式
                //var aSerializer = new JsonSerializer { DateFormatString = Format };
                //var dateTimeConverter = new IsoDateTimeConverter { DateTimeFormat = Format };
                //var serializer = new JsonSerializer
                //{
                //    // Tue Jan 01 1901 00:00:00 GMT+0800 (台北標準時間)
                //    //en-US     ddd, dd MMM yyyy HH':'mm':'ss 'GMT'
                //    //ja-JP     ddd, dd MMM yyyy HH':'mm':'ss 'GMT'
                //    //fr-FR     ddd, dd MMM yyyy HH':'mm':'ss 'GMT'
                //    DateFormatString = "ddd MMM dd yyyy HH:mm:ss GMT+0800 (台北標準時間)",
                //};
                //m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Clear();
                //m_InMemoryDataContextSmallGroup.FeeList.FeeDataList = JsonConvert.DeserializeObject<List<Fee>>(aResult, dateTimeConverter);
                #endregion

                #region 正確的日期格式
                //var Format = "ddd MMM dd yyyy HH:mm:ss GMT+0800 (台北標準時間)"; // DataGrid如果沒有設PAGE，則正確的日期格式
                ////var Format = "ddd MMM dd yyyy HH:mm:ss GMT+0800"; // DataGrid如果沒有設PAGE，則正確的日期格式
                //var settings = new JsonSerializerSettings
                //{
                //    // 轉換成當地時間
                //    DateTimeZoneHandling = DateTimeZoneHandling.Local,
                //    //DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                //    DateFormatString = Format,
                //    NullValueHandling = NullValueHandling.Ignore,
                //    MissingMemberHandling = MissingMemberHandling.Ignore
                //};

                //m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Clear();
                //m_InMemoryDataContextSmallGroup.FeeList.FeeDataList = JsonConvert.DeserializeObject<List<Fee>>(aResult, settings);
                #endregion

                //m_InMemoryDataContextSmallGroup.SmallGroupDataList.TransferToMemberInfomationPackage(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_SmallGroupData);
                //m_InMemoryDataContextSmallGroup.SmallGroupDataList.UploadMemberInfomationPackage();

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        //public static T FromJSON<T>(this string str)
        //{
        //    var serializer = new JsonSerializer { DateFormatString = "dd-MM-yyyy" };
        //    return serializer.Deserialize<T>(new JsonTextReader(new StringReader(str)));
        //}

        [HttpGet]
        public object LoadLessonList(DataSourceLoadOptions loadOptions)
        {
            try
            {
                // 上課紀錄單過濾掉上完十課的
                // 下載對課單紀錄，含對課中及完整清單
                //m_InMemoryDataContext.ClassSheetManager.LoadReportDiscipleLessonsList();

                //loadOptions.Filter = new List<object>(new object[] { "DiscipleLessonsStatusCode", "<>", "對完十課" });

                return DataSourceLoader.Load(m_InMemoryDataContextSmallGroup.FeeList.LessonList, loadOptions);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [HttpGet]
        public object LoadFeeDataList(DataSourceLoadOptions loadOptions)
        {
            try
            {
                // 上課紀錄單過濾掉上完十課的
                // 下載對課單紀錄，含對課中及完整清單
                //m_InMemoryDataContext.ClassSheetManager.LoadReportDiscipleLessonsList();

                //loadOptions.Filter = new List<object>(new object[] { "DiscipleLessonsStatusCode", "<>", "對完十課" });

                return DataSourceLoader.Load(m_InMemoryDataContextSmallGroup.FeeList.FeeDataList, loadOptions);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [HttpPut]
        public IActionResult UpdateFeeDataList(string key, string values)
        {
            try
            {
                Fee aFee = m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.First(a => a.StorLessonsId == key);

                // 更新後台資料庫
                //m_InMemoryDataContextSmallGroup.FeeList.UpdateEntity(key, values, aFee);

                // 更新前台顯示的網頁及更新後台資料庫
                m_InMemoryDataContextSmallGroup.FeeList.PopulateObjectAndUpdateEntity(values, aFee);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region 行事曆
        [Route("/Home/Scheduler/{ScheduleType}")]
        public ActionResult Scheduler(string ScheduleType)
        {
            try
            {
                if ( ScheduleType == "差勤簽核" )
                {
                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType; // 看是小組長還是個人回報
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                    ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                    #region 繳費與點名是否顯示在選單中
                    if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                    {
                        ViewBag.FeeDataListCount = "繳費與點名已有資料";
                    }
                    else
                    {
                        ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                    }
                    #endregion

                    if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }
                    ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView;
                    ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation;
                    ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType; // 是否是行政同工
                    m_InMemoryDataContextSmallGroup.AppointmentsListManager.ScheduleType = ScheduleType; // 差勤簽核 OR 場地及資源預約
                    ViewBag.SchedulerDisplayType = "差勤簽核";
                    SetMultiGroupLayoutParameter();
                }
                else 
                {
                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType; // 看是小組長還是個人回報
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                    ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                    #region 繳費與點名是否顯示在選單中
                    if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                    {
                        ViewBag.FeeDataListCount = "繳費與點名已有資料";
                    }
                    else
                    {
                        ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                    }
                    #endregion

                    if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }
                    ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView;
                    ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation;
                    ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType; // 是否是行政同工
                    m_InMemoryDataContextSmallGroup.AppointmentsListManager.ScheduleType = ScheduleType; // 差勤簽核 OR 場地及資源預約
                    ViewBag.SchedulerDisplayType = "場地簽核";
                    SetMultiGroupLayoutParameter();
                }

                return View(m_InMemoryDataContextSmallGroup.AppointmentsListManager);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #region 新增新人
        public IActionResult NewPerson()
        {
            try
            {
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion

                ViewBag.HappyType = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType;

                SetMultiGroupLayoutParameter();

                if(m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData.Count == 1 )
                {
                    //m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData.First().ListEntityId;
                    //m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.GroupName = m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData.First().Name;
                }
                else if (ViewBag.MultiGroupIndex == "HybridView")
                {
                    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;
                }
                else if (ViewBag.MultiGroupIndex == "SingleMultiGroupView")
                {
                    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = "";
                }
                else
                {
                    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = "";
                }

                // 設定要加入的小組名稱
                m_InMemoryDataContextSmallGroup.NewPersonModel.SetupGroupArray(m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData, m_InMemoryDataContextSmallGroup.ListManager.ActiveListId);

                return View(m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [HttpPost]
        public IActionResult SaveNewPerson(PersonFormViewModel aPersonFormViewModel)
        {
            try
            {
                if (aPersonFormViewModel.Phone == "" || aPersonFormViewModel.Phone == null)
                {
                    return Json(new { status = "2", message = "新增新人必須要有行動電話" });
                }

                string Result = m_InMemoryDataContextSmallGroup.NewPersonModel.UploadNewPerson(m_InMemoryDataContextSmallGroup.ListManager.m_Account, m_InMemoryDataContextSmallGroup.ListManager.m_Password, aPersonFormViewModel);

                if (Result.Contains("成功"))
                {
                    if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport != null && aPersonFormViewModel.Position != "0")
                    {
                        aPersonFormViewModel.PresentRecordId = m_InMemoryDataContextSmallGroup.NewPersonModel.m_NewContact.PresentRecordId;
                        //Task.Run(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.AddNewPersonToMember(aPersonFormViewModel));
                        Task.Factory.StartNew(() => m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.AddNewPersonToMember(aPersonFormViewModel), TaskCreationOptions.LongRunning);
                        //m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.AddNewPersonToMember(aPersonFormViewModel);
                    }

                    return Json(new { status = "1", message = Result });
                }
                else
                {
                    return Json(new { status = "2", message = Result });
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [HttpGet]
        public object AssignSmallGroupGet(DataSourceLoadOptions loadOptions)
        {
            try
            {
                //// 待修正
                return DataSourceLoader.Load(m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData, loadOptions);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }

        }
        #endregion
        #region 顯示錯誤訊息
        [Route("/Home/DisplayErrorView/{ErrorMessage}")]
        public IActionResult DisplayErrorView(string ErrorMessage)
        {
            try
            {
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                ViewBag.HappyType = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType;
                @ViewBag.DisplayNavigation = "不顯示牧養回報項目";
                //SetMultiGroupLayoutParameter();

                //if (m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData.Count == 1)
                //{
                //    //m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData.First().ListEntityId;
                //    //m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.GroupName = m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData.First().Name;
                //}
                //else if (ViewBag.MultiGroupIndex == "HybridView")
                //{
                //    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;
                //}
                //else if (ViewBag.MultiGroupIndex == "SingleMultiGroupView")
                //{
                //    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = "";
                //}
                //else
                //{
                //    m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = "";
                //}

                m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel.Position = "";

                ViewBag.ErrorMessage = ErrorMessage;

                return View();
            }
            catch (System.Exception e)
            {
                string ErrorString = "c FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #region 圖形區塊
        [HttpGet]
        public object GetChartDataList(string WeeklyReportId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                //// 待修正
                //m_InMemoryDataContextSmallGroup.m_ListManager.ActiveListId = id;
                return m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_WeeklyReportChart.m_ChartDataList;
                //return m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.Where(e => e.ListEntityId == m_InMemoryDataContextSmallGroup.m_ListManager.ActiveListId).FirstOrDefault().m_WeeklyReportChart.m_ChartDataList;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }

        }
        public object GetMultiGroupChartDataList(string WeeklyReportId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                //// 待修正
                //m_InMemoryDataContextSmallGroup.m_ListManager.ActiveListId = id;
                return m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupChartDataList.m_MultiGroupChartDataList;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }

        }
        #endregion
        #region Line LIFF 綁定
        [Route("/Home/LineLiffView/{LineLiffViewPatameter}")]
        public IActionResult LineLiffView(string LineLiffViewPatameter)
        {
            try
            {
                //真正註冊在 Line Developer
                var images = new List<string>();
                images.Add(Url.Content("~/assets/images/tpehoc-005.jpg"));
                images.Add(Url.Content("~/assets/images/tpehoc-006.jpg"));
                images.Add(Url.Content("~/assets/images/tpehoc-007.jpg"));
                images.Add(Url.Content("~/assets/images/tpehoc-008.jpg"));
                images.Add(Url.Content("~/assets/images/tpehoc-009.jpg"));

                m_InMemoryDataContextSmallGroup.LineBindingViewModel.Images = images;

                TempData["Proponent"] = LineLiffViewPatameter;

                return View(m_InMemoryDataContextSmallGroup.LineBindingViewModel);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [HttpPost]
        public IActionResult ProcessLineBinding(LineBindingViewModel aLineBindingViewModel)
        {
            try
            {
                if (aLineBindingViewModel.FullName == null || aLineBindingViewModel.FullName == "")
                {
                    return Json(new { status = "2", message = aLineBindingViewModel.DisplayName + " 沒有輸入姓名!" });

                }
                if (aLineBindingViewModel.Mobile == null || aLineBindingViewModel.Mobile == "")
                {
                    return Json(new { status = "2", message = aLineBindingViewModel.DisplayName + " 沒有輸入行動電話!" });

                }

                Regex DigitsOnly = new Regex(@"[^\d]");
                string Mobile = DigitsOnly.Replace(aLineBindingViewModel.Mobile, "");

                //string BindingString = "//" + aLineBindingViewModel.FullName + "," + aLineBindingViewModel.Mobile;

                //Guid aLineEntityId = CreateLineMessage(m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId, m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId, BindingString, 100000000);
                //Guid aLineEntityId = CreateLineMessage(aLineBindingViewModel.DisplayId, BindingString, 100000000);

                String BindingResult = BindingContactLineId(m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId, m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId, aLineBindingViewModel.FullName, aLineBindingViewModel.Mobile);

                if ( BindingResult.Contains("成功")) 
                {
                    //return Json(new { status = "1", message = "感謝 " + aLineBindingViewModel.FullName + " 完成綁定程序，請回到LINE視窗進行報名或回報，謝謝您!" + Environment.NewLine + BindingResult, encoded = aLineBindingViewModel.DisplayName + "," + aLineBindingViewModel.LineUserId });
                    return Json(new { status = "1", message = "感謝 " + aLineBindingViewModel.FullName + " 完成綁定程序!" + Environment.NewLine + BindingResult, encoded = aLineBindingViewModel.DisplayName + "," + aLineBindingViewModel.LineUserId });
                }
                else
                {
                    return Json(new { status = "2", message = aLineBindingViewModel.FullName + " 綁定失敗!" + Environment.NewLine + BindingResult });
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }

        }
        public Guid CreateLineMessage(string DisplayId, string UserId, string Message, int OptionSetValueOfMessageType)
        {
            try
            {
                Entity aContact = m_ToolUtilityClass.RetrieveContactByLineId(UserId);

                //await SendMessage(UserId, "001: " + UserId);

                if (aContact != null)
                {
                    //await SendMessage(UserId, "002");
                    Entity aEntity = new Entity("letter");
                    m_ToolUtilityClass.SetEntityStringAttribute(ref aEntity, "subject", Message);
                    m_ToolUtilityClass.SetEntityStringAttribute(ref aEntity, "new_displayed_lineid", DisplayId);
                    m_ToolUtilityClass.SetEntityLookUpAttribute(ref aEntity, "regardingobjectid", "contact", aContact.Id);
                    m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aEntity, "scheduledend", DateTime.Now);
                    m_ToolUtilityClass.SetEntityBoolAttribute(ref aEntity, "directioncode", false);

                    //await SendMessage(UserId, "003");
                    //設定訊息種類為文字 
                    m_ToolUtilityClass.SetOptionSetAttribute(ref aEntity, "new_message_category", OptionSetValueOfMessageType);

                    //await SendMessage(UserId, "004");
                    Entity Fromparty = new Entity("activityparty");

                    //await SendMessage(UserId, "005");
                    Fromparty["partyid"] = new EntityReference("contact", aContact.Id);

                    //await SendMessage(UserId, "006");
                    aEntity["from"] = new Entity[] { Fromparty };

                    //await SendMessage(UserId, "007");
                    return m_ToolUtilityClass.CreateEntity(aEntity);
                    //return m_ToolUtilityClass.CreateEntity( ref m_ToolUtilityClass.m_OrganizationService, aEntity);
                }
                else
                {
                    //await SendMessage(UserId, "008");
                    return Guid.Empty;
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public String BindingContactLineId(string DisplayId, string UserLineId, string EnteredFullName, String EnteredMobilePhone)
        {
            try
            {
                WebServiceConnector.LineBindingUtility aLineBindingUtility = new WebServiceConnector.LineBindingUtility();

                return aLineBindingUtility.RegisterContact(UserLineId, EnteredFullName, EnteredMobilePhone);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [Route("/Home/BindingResultView/{LineBindingResult}")]
        public IActionResult BindingResultView(string LineBindingResult)
        {
            var images = new List<string>();
            images.Add(Url.Content("~/assets/images/tpehoc-005.jpg"));
            images.Add(Url.Content("~/assets/images/tpehoc-006.jpg"));
            images.Add(Url.Content("~/assets/images/tpehoc-007.jpg"));
            images.Add(Url.Content("~/assets/images/tpehoc-008.jpg"));
            images.Add(Url.Content("~/assets/images/tpehoc-009.jpg"));

            m_InMemoryDataContextSmallGroup.LineBindingViewModel.Images = images;

            m_InMemoryDataContextSmallGroup.LineBindingViewModel.BindingResult = LineBindingResult;

            return View(m_InMemoryDataContextSmallGroup.LineBindingViewModel);
        }
        [HttpPost]
        public IActionResult SaveUserId(string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId = UserLineId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.RoomId = RoomId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.GroupId = GroupId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.ViewType = ViewType;

                if (GroupId != null && GroupId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = GroupId;
                }
                else if (RoomId != null && RoomId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = RoomId;
                }
                else
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = UserLineId;
                }

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region Line 上課資格
        [Route("/Home/QualificationView/{QualificationViewPatameter}")]
        public IActionResult QualificationView(string QualificationViewPatameter)
        {
            try
            {
                var images = new List<string>();
                images.Add(Url.Content("~/assets/images/tpehoc-005.jpg"));
                images.Add(Url.Content("~/assets/images/tpehoc-006.jpg"));
                images.Add(Url.Content("~/assets/images/tpehoc-007.jpg"));
                images.Add(Url.Content("~/assets/images/tpehoc-008.jpg"));
                images.Add(Url.Content("~/assets/images/tpehoc-009.jpg"));

                m_InMemoryDataContextSmallGroup.LineBindingViewModel.Images = images;

                TempData["Proponent"] = QualificationViewPatameter;

                return View(m_InMemoryDataContextSmallGroup.LineBindingViewModel);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [HttpPost]
        public IActionResult GetQualificationData(string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId = UserLineId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.RoomId = RoomId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.GroupId = GroupId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.ViewType = ViewType;

                if (GroupId != null && GroupId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = GroupId;
                }
                else if (RoomId != null && RoomId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = RoomId;
                }
                else
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = UserLineId;
                }

                string FaithStatus = "基督徒";
                string GenderCode = "男性";
                //DateTime BirthDate = DateTime.Now;
                DateTime BirthDate = DateTime.MinValue;
                //DateTime BirthDate = null;
                String PersonalId = "";

                m_InMemoryDataContextSmallGroup.LineBindingViewModel.GetContactInfomation(UserLineId, ref FaithStatus, ref GenderCode, ref BirthDate, ref PersonalId );

                return Json(new { faithStatus = FaithStatus, genderCode = GenderCode, birthDate = BirthDate, personalId = PersonalId, message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }


        [HttpPost]
        public IActionResult SaveQualificationData(LineBindingViewModel aLineBindingViewModel)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.UpdateContactInfomation(aLineBindingViewModel.FaithStatus, aLineBindingViewModel.GenderCode, aLineBindingViewModel.BirthDate, aLineBindingViewModel.PersonalId);

                return Json(new { status = "1", message = "謝謝 " + aLineBindingViewModel.FullName + " 填寫基本資料!", encoded = aLineBindingViewModel.DisplayName + "," + aLineBindingViewModel.LineUserId });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }

        }

        #endregion
        #region Layout 工具區
        private void SetMultiGroupLayoutParameter()
        {
            string DisplayViewType = m_InMemoryDataContextSmallGroup.ListManager.GetDisplayViewType();

            bool IntegrateFlag = false;
            if
            (
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport != null &&
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag == true
            )
            { IntegrateFlag = true; }

            //if (m_InMemoryDataContextSmallGroup.ListManager.m_SmallGroupWeeklyReport == null) { IntegrateFlag = false; }
            //else if (m_InMemoryDataContextSmallGroup.ListManager.m_SmallGroupWeeklyReport.LoadFlag == false) { IntegrateFlag = false; }
            //else { IntegrateFlag = true; }

            if (DisplayViewType == "MultiGroupView" && IntegrateFlag == false)
            {
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
            }
            else if (DisplayViewType == "IntegrateView" && IntegrateFlag == true)
            {
                ViewBag.MultiGroupIndex = "IntegrateView";
            }
            else if (DisplayViewType == "MultiGroupView" && IntegrateFlag == true)
            {
                ViewBag.MultiGroupIndex = "HybridView";
            }
            else
            {
                ViewBag.MultiGroupIndex = "IntegrateView";
            }

        }

        #endregion
        #region Line Tiff 行事曆
        [Route("/Home/SchedulerView/{SchedulerViewPatameter}")]
        public ActionResult SchedulerView( String ScheduleId, string SchedulerViewPatameter)
        {
            try
            {
                #region 控制 Navigation 下拉項目
                ViewBag.LoginType = "小組長"; // 看是小組長還是個人回報
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.UserType;// 是否是行政同工
                ViewBag.SchedulerDisplayType = SchedulerViewPatameter == "差勤簽核" ? "差勤簽核" : "場地簽核";

                //ViewBag.Prop = SchedulerViewPatameter;
                TempData["Proponent"] = SchedulerViewPatameter;
                #endregion

                return View();
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                throw e;
            }
        }

        [HttpPost]
        public IActionResult LoadAppointmentByLineId(string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId = m_InMemoryDataContextSmallGroup.AppointmentsListManager.LineUserId = UserLineId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.RoomId = m_InMemoryDataContextSmallGroup.AppointmentsListManager.RoomId = RoomId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.GroupId = m_InMemoryDataContextSmallGroup.AppointmentsListManager.GroupId = GroupId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.ViewType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.ViewType = ViewType;

                if (GroupId != null && GroupId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = GroupId;
                }
                else if (RoomId != null && RoomId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = RoomId;
                }
                else
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = UserLineId;
                }

                // 依據登入方式設定行事曆的帳密
                m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Account = "LineIdLogin";
                m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Password = m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId;

                #region 控制 Navigation 下拉項目
                ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.UserType;// 是否是行政同工
                ViewBag.SchedulerDisplayType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.UserType == "行政同工" ? "差勤簽核" : "場地簽核";
                #endregion

                return Json(new { message = "歡迎" + "登入成功!" });

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [HttpGet]
        public object LoadAppointments(DataSourceLoadOptions loadOptions)
        {
            //AppointmentsListManager aAppointmentsList = new AppointmentsListManager();
            //m_InMemoryDataContextSmallGroup.AppointmentsListManager.SetupAppointmentList("", "", DateTime.Now);
            // 準備整理好約會清單 m_Appointments
            m_InMemoryDataContextSmallGroup.AppointmentsListManager.SetupAppointmentList();

            //return DataSourceLoader.Load(m_InMemoryDataContextSmallGroup.AppointmentsListManager.SetupAppointmentList("", "", DateTime.Now), loadOptions);
            // 回傳整理好的約會清單 m_Appointments
            return DataSourceLoader.Load(m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Appointments, loadOptions);
        }

        [HttpPost]
        public IActionResult PostAppointments(string values)
        {
            #region 新增約會
            var newAppointment = new Appointment();
            JsonConvert.PopulateObject(values, newAppointment);

            //m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Appointments.Add(newAppointment);

            //Task.Run(() => m_InMemoryDataContextSmallGroup.AppointmentsListManager.CreateAppointment(newAppointment));
            newAppointment.StartDate = newAppointment.StartDate.ToLocalTime();
            newAppointment.EndDate = newAppointment.EndDate.ToLocalTime();
            m_InMemoryDataContextSmallGroup.AppointmentsListManager.CreateAppointment( ref newAppointment );

            //m_InMemoryDataContextSmallGroup.SaveChanges();

            return Ok();
            #endregion
        }

        [HttpPut]
        public IActionResult PutAppointments(String key, string values)
        {
            #region 修改約會
            var appointment = m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Appointments.First(a => a.AppointmentId == key);
            JsonConvert.PopulateObject(values, appointment);

            //Task.Run(() => m_InMemoryDataContextSmallGroup.AppointmentsListManager.UpdateAppointment(appointment));
            appointment.StartDate = appointment.StartDate.ToLocalTime();
            appointment.EndDate = appointment.EndDate.ToLocalTime();
            m_InMemoryDataContextSmallGroup.AppointmentsListManager.UpdateAppointment(appointment);

            //m_InMemoryDataContextSmallGroup.SaveChanges();

            return Ok();
            #endregion
        }

        [HttpDelete]
        public void DeleteAppointments(String key)
        {
            #region 刪除約會
            var appointment = m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Appointments.First(a => a.AppointmentId == key);

            //Task.Run(() => m_InMemoryDataContextSmallGroup.AppointmentsListManager.DeleteAppointment(appointment));
            m_InMemoryDataContextSmallGroup.AppointmentsListManager.DeleteAppointment(appointment);

            //m_InMemoryDataContextSmallGroup.SaveChanges();
            #endregion
        }

        public IActionResult NavigateAppointmentDate(string SelectedDate)
        {
            try
            {
                string[] DateTimeList = 
                {
                    "yyyy/M/d tt hh:mm:ss",
                    "yyyy/MM/dd tt hh:mm:ss",
                    "yyyy/MM/dd HH:mm:ss",
                    "yyyy/M/d HH:mm:ss",
                    "ddd MMM dd yyyy HH:mm:ss GMT+0800 (台北標準時間)",
                    "ddd MMM dd yyyy HH:mm:ss GMT+0800 (CST)",
                    "ddd MMM dd yyyy HH:mm:ss GMT+0800",
                    "ddd MMM dd yyyy HH:mm:ss",
                    "yyyy/M/d",
                    "yyyy/MM/dd"
                };

                DateTime ParsedSelectDate = DateTime.ParseExact(SelectedDate, DateTimeList, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces);

                // 因為我們都是以月為行事曆的單位，所以我強迫選擇的日期永遠為當月的1 號
                //ParsedSelectDate = new DateTime(ParsedSelectDate.Year, ParsedSelectDate.Month, 1);

                m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_SelectDate = ParsedSelectDate;

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                //LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                //aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                return Ok();

                //throw e;
            }
        }

        #endregion
        #region  教會課程 QR CODE 簽到、簽退+ 報名 掃描(Line Liff)
        [Route("/Home/QrCodeView/{QrCodeViewPatameter}")]
        public ActionResult QrCodeView(String QrCodeId, string QrCodeViewPatameter)
        {
            try
            {
                #region 控制 Navigation 下拉項目
                ViewBag.LoginType = "小組長"; // 看是小組長還是個人回報
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.UserType;// 是否是行政同工

                m_InMemoryDataContextSmallGroup.ListManager.QrCodeId = QrCodeId;

                //ViewBag.Prop = SchedulerViewPatameter;
                // 傳遞參數給網頁
                TempData["Proponent"] = QrCodeViewPatameter;
                TempData["QrCodeId"] = QrCodeId;
                //TempData["ClassName"] = "從懷疑到相信";
                //TempData["ClassName"] = m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId;
                TempData["ClassName"] = " ";
                #endregion

                return View();
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                throw e;
            }
        }

        [HttpPost]
        public IActionResult QrCodeGetLineId( string DisplayName, string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId = m_InMemoryDataContextSmallGroup.AppointmentsListManager.LineUserId = UserLineId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.RoomId = m_InMemoryDataContextSmallGroup.AppointmentsListManager.RoomId = RoomId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.GroupId = m_InMemoryDataContextSmallGroup.AppointmentsListManager.GroupId = GroupId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.ViewType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.ViewType = ViewType;

                if (GroupId != null && GroupId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = GroupId;
                }
                else if (RoomId != null && RoomId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = RoomId;
                }
                else
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = UserLineId;
                }

                // 依據登入方式設定行事曆的帳密
                //m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Account = "LineIdLogin";
                //m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Password = m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId;

                #region 控制 Navigation 下拉項目
                ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.UserType;// 是否是行政同工
                #endregion

                TempData["ClassName"] = "從相信到堅信";

                QrCodeUtility aQrCodeUtility = new QrCodeUtility();

                String ClassName = "";
                String UserName = "";
                String ClassIndex = "";
                String OnboardType = "";

                LineMessagingProcessor.UserProfile aUserProfile = new LineMessagingProcessor.UserProfile();

                aQrCodeUtility.SetupQrCodeIdString( m_InMemoryDataContextSmallGroup.ListManager.QrCodeId, DisplayName, UserLineId, ref ClassName, ref UserName, ref ClassIndex, ref OnboardType );

                //aQrCodeUtility.SetupQrCodeIdString(m_InMemoryDataContextSmallGroup.ListManager.QrCodeId);

                //return Json(new { DisplayViewType = DisplayViewType, ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                return Json(new { result = OnboardType, classname = ClassName, username = UserName, classindex = ClassIndex, onboardtype = OnboardType });

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region  小組聚會 QR CODE 簽到簽退掃描(Line Liff)
        [Route("/Home/SmallGroupQrCodeView/{QrCodeViewPatameter}")]
        public ActionResult SmallGroupQrCodeView(String QrCodeId, string QrCodeViewPatameter)
        {
            try
            {
                #region 控制 Navigation 下拉項目
                ViewBag.LoginType = "小組長"; // 看是小組長還是個人回報
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.UserType;// 是否是行政同工

                m_InMemoryDataContextSmallGroup.ListManager.QrCodeId = QrCodeId;

                //ViewBag.Prop = SchedulerViewPatameter;
                // 傳遞參數給網頁
                TempData["Proponent"] = QrCodeViewPatameter;
                TempData["QrCodeId"] = QrCodeId;
                //TempData["ClassName"] = "從懷疑到相信";
                //TempData["ClassName"] = m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId;
                TempData["ClassName"] = " ";
                #endregion

                return View();
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                throw e;
            }
        }

        [HttpPost]
        public IActionResult SmallGroupQrCodeGetLineId( string DisplayName, string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId = m_InMemoryDataContextSmallGroup.AppointmentsListManager.LineUserId = UserLineId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.RoomId = m_InMemoryDataContextSmallGroup.AppointmentsListManager.RoomId = RoomId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.GroupId = m_InMemoryDataContextSmallGroup.AppointmentsListManager.GroupId = GroupId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.ViewType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.ViewType = ViewType;

                if (GroupId != null && GroupId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = GroupId;
                }
                else if (RoomId != null && RoomId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = RoomId;
                }
                else
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = UserLineId;
                }

                // 依據登入方式設定行事曆的帳密
                m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Account = "LineIdLogin";
                m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Password = m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId;

                #region 控制 Navigation 下拉項目
                ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.UserType;// 是否是行政同工
                #endregion

                TempData["ClassName"] = "從相信到堅信";

                SmallGroupQrCodeUtility aSmallGroupQrCodeUtility = new SmallGroupQrCodeUtility();

                String SmallGroupName = "";
                String UserName = "";
                String OnboardType = "";

                LineMessagingProcessor.UserProfile aUserProfile = new LineMessagingProcessor.UserProfile();

                aSmallGroupQrCodeUtility.SetupQrCodeIdString(m_InMemoryDataContextSmallGroup.ListManager.QrCodeId, DisplayName, UserLineId, ref SmallGroupName, ref UserName, ref OnboardType);

                //aQrCodeUtility.SetupQrCodeIdString(m_InMemoryDataContextSmallGroup.ListManager.QrCodeId);

                //return Json(new { DisplayViewType = DisplayViewType, ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                return Json(new { result = OnboardType, smallgroupname = SmallGroupName, username = UserName,  onboardtype = OnboardType });

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region  主日 QR CODE 簽到、簽退 掃描(Line Liff)
        [Route("/Home/SundayQrCodeView/{QrCodeViewPatameter}")]
        public ActionResult SundayQrCodeView(String QrCodeId, string QrCodeViewPatameter)
        {
            try
            {
                #region 控制 Navigation 下拉項目
                ViewBag.LoginType = "小組長"; // 看是小組長還是個人回報
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.UserType;// 是否是行政同工

                m_InMemoryDataContextSmallGroup.ListManager.QrCodeId = QrCodeId;

                //ViewBag.Prop = SchedulerViewPatameter;
                // 傳遞參數給網頁
                TempData["Proponent"] = QrCodeViewPatameter;
                TempData["QrCodeId"] = QrCodeId;
                //TempData["ClassName"] = "從懷疑到相信";
                //TempData["ClassName"] = m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId;
                TempData["ClassName"] = " ";
                #endregion

                return View();
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                throw e;
            }
        }

        [HttpPost]
        public IActionResult SundayQrCodeGetLineId(string DisplayName, string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId = m_InMemoryDataContextSmallGroup.AppointmentsListManager.LineUserId = UserLineId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.RoomId = m_InMemoryDataContextSmallGroup.AppointmentsListManager.RoomId = RoomId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.GroupId = m_InMemoryDataContextSmallGroup.AppointmentsListManager.GroupId = GroupId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.ViewType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.ViewType = ViewType;

                if (GroupId != null && GroupId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = GroupId;
                }
                else if (RoomId != null && RoomId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = RoomId;
                }
                else
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = UserLineId;
                }

                // 依據登入方式設定行事曆的帳密
                //m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Account = "LineIdLogin";
                //m_InMemoryDataContextSmallGroup.AppointmentsListManager.m_Password = m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId;

                #region 控制 Navigation 下拉項目
                ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = m_InMemoryDataContextSmallGroup.AppointmentsListManager.UserType;// 是否是行政同工
                #endregion

                TempData["ClassName"] = "從相信到堅信";

                SundayQrCodeUtility aSundayQrCodeUtility = new SundayQrCodeUtility();

                String SundayName = "";
                String CategoryName = "";
                String UserName = "";
                String ClassIndex = "";
                String OnboardType = "";

                LineMessagingProcessor.UserProfile aUserProfile = new LineMessagingProcessor.UserProfile();

                // 主日掃描後的相關設定
                aSundayQrCodeUtility.SetupQrCodeIdString(m_InMemoryDataContextSmallGroup.ListManager.QrCodeId, DisplayName, UserLineId, ref SundayName, ref CategoryName, ref UserName, ref OnboardType);

                //aQrCodeUtility.SetupQrCodeIdString(m_InMemoryDataContextSmallGroup.ListManager.QrCodeId);

                //return Json(new { DisplayViewType = DisplayViewType, ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                //return Json(new { result = OnboardType, classname = ClassName, username = UserName, classindex = ClassIndex, onboardtype = OnboardType });
                return Json(new { result = OnboardType, sundayname = SundayName, categoryname = CategoryName,username = UserName, onboardtype = OnboardType });

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region Line Pay 奉獻
        [Route("/Home/DedicationView/{DedicationViewPatameter}")]
        public ActionResult DedicationView(string DedicationViewPatameter)
        {
            try
            {
                ViewBag.LoginType = "小組長"; // 看是小組長還是個人回報
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                //ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = "行政同工";

                DedicationModel DedicationModel = new DedicationModel();

                TempData["Proponent"] = DedicationViewPatameter;

                return View(DedicationModel);

                //return View();
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                throw e;
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveDedication(DedicationModel DedicationModel)
        {
            try
            {
                //LinePayClient m_LinePayClient;
                //IConfiguration configuration;

                //var builder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json");

                //configuration = builder.Build();

                //m_LinePayClient = new LinePayClient(configuration["LinePay:ChannelId"], configuration["LinePay:ChannelSecret"], bool.Parse(configuration["LinePay:IsSandbox"]));

                LinePayProcessor LinePayProcessor = new LinePayProcessor();

                //String aLinePayUrl = await LinePayProcessor.NotifyLinePay(m_LinePayClient, DedicationModel);
                String aLinePayUrl = await LinePayProcessor.NotifyLinePay(m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId, DedicationModel);

                //return Json(new { LinePayUrl = LinePayUrl, DisplayViewType = DisplayViewType, ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });

                return Json(new { status = "1", message = "感謝您的奉獻", LinePayUrl = aLinePayUrl });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }


        #endregion
        #region 奉獻資訊，僅填寫相關資訊而已
        [Route("/Home/DedicationInofView/{DedicationInfoViewPatameter}")]
        public ActionResult DedicationInofView(string DedicationInfoViewPatameter)
        {
            try
            {
                ViewBag.LoginType = "小組長"; // 看是小組長還是個人回報
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                //ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = "行政同工";

                DedicationInfoModel DedicationInfoModel = new DedicationInfoModel();

                TempData["Proponent"] = DedicationInfoViewPatameter;

                return View(DedicationInfoModel);
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                throw e;
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveDedicationInfo(DedicationInfoModel DedicationInfoModel)
        {
            try
            {
                DedicationInfo DedicationInfo = new DedicationInfo();

                String FullName = await DedicationInfo.CreateFeeAsync(m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId, DedicationInfoModel);

                return Json(new { status = "1", message = "感謝" + FullName + "的奉獻，願神與" + FullName+ "同在" } ) ;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #region 永豐金流奉獻 Line 單獨登入
        #region Line 單獨登入
        [Route("/Home/QPayView/{DedicationViewPatameter}")]
        public ActionResult QPayView(string DedicationViewPatameter)
        {
            try
            {
                ViewBag.LoginType = "小組長"; // 看是小組長還是個人回報
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                //ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = "行政同工";
                ViewBag.DedicationType = m_InMemoryDataContextSmallGroup.ListManager.UserType = "奉獻管理";

                TempData["Proponent"] = DedicationViewPatameter;

                //m_InMemoryDataContextSmallGroup.QpayManager.SetQpayModel();

                return View( m_InMemoryDataContextSmallGroup.QpayManager.SetQpayModel() );
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                throw e;
            }
        }
        [HttpPost]
        public async Task<IActionResult> SaveQPayDedication( QpayModel QpayModel )
        {
            try
            {
                return await m_InMemoryDataContextSmallGroup.QpayManager.SaveQPayDedication(QpayModel, m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #region 電腦網頁登入
        [HttpGet]
        public ActionResult QPayViewWeb()
        {
            try
            {
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                SetMultiGroupLayoutParameter();

                return View( m_InMemoryDataContextSmallGroup.QpayManager.m_QpayModel );
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                throw e;
            }
        }
        [HttpPost]
        public async Task<IActionResult> SaveQPayDedicationWeb(QpayModel QpayModel)
        {
            try
            {
                return await m_InMemoryDataContextSmallGroup.QpayManager.SaveQPayDedication(QpayModel);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #endregion
        #region 奉獻收費清單
        #region Line 單獨登入
        public ActionResult DedicationFeeView()
        {
            try
            {
                #region 用小組長回報網頁登入
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                ViewBag.LoginType = "小組長"; // 看是小組長還是個人回報
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                //ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = "行政同工";
                ViewBag.DedicationType = m_InMemoryDataContextSmallGroup.ListManager.UserType = "奉獻管理";
                #endregion

                return View(m_InMemoryDataContextSmallGroup.QpayManager.SetDedicationFeeList(m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId));

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #region 電腦網頁登入
        public ActionResult DedicationFeeViewWeb()
        {
            try
            {
                #region 用小組長回報網頁登入
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                SetMultiGroupLayoutParameter();
                #endregion

                return View(m_InMemoryDataContextSmallGroup.QpayManager.SetDedicationFeeList(m_InMemoryDataContextSmallGroup.QpayManager.m_Contact));
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion

        public async Task<IActionResult> UpdateDedicationFeeView(QpayModel aQpayModel)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.QpayManager.m_QpayModel.QueryStartDate = aQpayModel.QueryStartDate;
                m_InMemoryDataContextSmallGroup.QpayManager.m_QpayModel.QueryEndDate = aQpayModel.QueryEndDate;

                return Json(new { status = "1", message = "成功上傳了.... !" });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region 行政人員奉獻管理
        #region Line 單獨登入
        public ActionResult KeyInDedicationFeeView()
        {
            try
            {
                #region 用小組長回報網頁登入
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                ViewBag.LoginType = "小組長"; // 看是小組長還是個人回報
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                //ViewBag.SchedulerView = m_InMemoryDataContextSmallGroup.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = m_InMemoryDataContextSmallGroup.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = m_InMemoryDataContextSmallGroup.ListManager.UserType = "行政同工";
                ViewBag.DedicationType = m_InMemoryDataContextSmallGroup.ListManager.UserType = "奉獻管理";
                #endregion

                return View(m_InMemoryDataContextSmallGroup.QpayManager.SetDedicationFeeList(m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId));

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #region 電腦網頁登入
        public ActionResult KeyInDedicationFeeViewWeb()
        {
            try
            {
                #region 用小組長回報網頁登入
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
                #region 繳費與點名是否顯示在選單中
                if (m_InMemoryDataContextSmallGroup.FeeList.FeeDataList != null && m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.Count > 0)
                {
                    ViewBag.FeeDataListCount = "繳費與點名已有資料";
                }
                else
                {
                    ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                }
                #endregion

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                SetMultiGroupLayoutParameter();
                #endregion

                return View(m_InMemoryDataContextSmallGroup.QpayManager.SetDedicationFeeList(m_InMemoryDataContextSmallGroup.QpayManager.m_Contact));
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion

        [HttpPost]
        public async Task<IActionResult> SaveKeyInDedication(QpayModel QpayModel)
        {
            try
            {
                return await m_InMemoryDataContextSmallGroup.QpayManager.SaveKeyInDedication( QpayModel );
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region Line Id 資訊區
        [HttpPost]
        public IActionResult SetupUserLineId(string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId = UserLineId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.RoomId = RoomId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.GroupId = GroupId;
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.ViewType = ViewType;

                if (GroupId != null && GroupId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = GroupId;
                }
                else if (RoomId != null && RoomId != "")
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = RoomId;
                }
                else
                {
                    m_InMemoryDataContextSmallGroup.LineBindingViewModel.DisplayId = UserLineId;
                }

                Entity LineLoginContact = this.m_ToolUtilityClass.RetrieveContactByLineId(UserLineId);

                // 全名
                String FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname");
                // 奉獻單編號
                String DedicationNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "pager");

                return Json( new { FullName = FullName , DedicationNumber = DedicationNumber } );

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
    }
}
