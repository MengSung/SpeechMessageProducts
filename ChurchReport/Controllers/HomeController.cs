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

namespace ChurchReport.Controllers
{
    public class HomeController : Controller, IDisposable
    {
        #region 資料區
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        private InMemoryDataContextSmallGroup m_InMemoryDataContextSmallGroup;
        private readonly Disposable _disposable;

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
            m_InMemoryDataContextSmallGroup = ContextDictionary.GetInMemoryDataContextSmallGroup(httpContextAccessor, memoryCache);
        }
        #endregion
        #region 登入帳號
        public async System.Threading.Tasks.Task<IActionResult> Login()
        {
            try
            {
                var images = new List<string>();
                images.Add(Url.Content("~/assets/images/001.jpg"));
                images.Add(Url.Content("~/assets/images/003.jpg"));
                images.Add(Url.Content("~/assets/images/005.jpg"));
                images.Add(Url.Content("~/assets/images/004.jpg"));

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

                await aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPost]
        public IActionResult ProcessLogin(GalleryViewModel aGalleryViewModel)
        {
            try
            {
                string ContactIdString = "";
                if (aGalleryViewModel.Account != "")
                {
                    ContactIdString = m_ToolUtilityClass.RetrieveContactByAccountNumber(aGalleryViewModel.Account, aGalleryViewModel.Password);
                }
                else
                {
                    ContactIdString = "透過Line Id 登入";
                }

                if (　ContactIdString != "密碼錯誤" && ContactIdString != "系統沒有設定密碼" && ContactIdString != "帳號錯誤"　)
                {
                    string FullName = "";
                    if (ContactIdString != "透過Line Id 登入")
                    {
                        Guid aContactGuid = new Guid(ContactIdString);

                        FullName = m_ToolUtilityClass.RetrieveEntityDynamics365("contact", aContactGuid).Attributes["fullname"].ToString();
                    }
                    else
                    {
                        Entity aLoginContact = m_ToolUtilityClass.RetrieveContactEntityByLineUserId(m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId);
                        FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLoginContact, "fullname");
                        aGalleryViewModel.Account = "LineIdLogin";
                        aGalleryViewModel.Password = m_InMemoryDataContextSmallGroup.LineBindingViewModel.LineUserId;
                    }

                    // 設定多個組長處理需要的資料
                    m_InMemoryDataContextSmallGroup.SetupListManager(aGalleryViewModel.Account, aGalleryViewModel.Password, DateTime.Now, true);

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

                    // 設定繳費與報名資料
                    m_InMemoryDataContextSmallGroup.FeeList.SetupFeeDataList(aGalleryViewModel.Account, aGalleryViewModel.Password);

                    if (m_InMemoryDataContextSmallGroup.ListManager.LoginType == "小組長" && m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        // 小組長回報，而且有幸福小組
                        ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;
                        ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                        ViewBag.HappyType = "有幸福小組名單";
                        ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;

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
                        SetMultiGroupLayoutParameter();

                        return Json(new { DisplayViewType = DisplayViewType, ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId, message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                    }
                    else if (m_InMemoryDataContextSmallGroup.ListManager.LoginType != "小組長" && m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "沒幸福小組名單")
                    {
                        // 個人回報，不是小組長，沒有幸福小組
                        ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType;
                        ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;
                        ViewBag.HappyType = "沒幸福小組名單";
                        ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType;
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #region Line Id Login 登入
        public IActionResult LineIdLoginView()
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

                return View(m_InMemoryDataContextSmallGroup.LineBindingViewModel);

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        [HttpPost]
        public IActionResult SaveUserLineId(string UserLineId, string GroupId, string RoomId, string ViewType)
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

                    return ProcessLogin(aGalleryViewModel);
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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
                        ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType; // 看是小組長還是個人回報
                        ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.ListManager.LoginFullName;

                        ViewBag.FeeType = m_InMemoryDataContextSmallGroup.FeeList.FeeType; // 繳費點名
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
                            m_InMemoryDataContextSmallGroup.SetupListManager(m_InMemoryDataContextSmallGroup.ListManager.m_Account, m_InMemoryDataContextSmallGroup.ListManager.m_Password, m_InMemoryDataContextSmallGroup.ListManager.m_SelectDate, true);
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region 整合式小組長點名
        [Route("/Home/IntegrateView/{LoginParameter}")]
        public ActionResult IntegrateView(string LoginParameter)
        {
            try
            {
                //ViewBag.ListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId = LoginParameter;

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
                        if (LoginParameter != "IntegrateView")
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
                        return RedirectToAction("PersonalReport");
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpPut]
        public IActionResult UpdateSmallGroupPresentRecord(string key, string values)
        {
            try
            {
                // 修改小組長牧養主日出席、小組出席、代禱事項
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.UpdateMember(key, values);

                // 修改全部的(也就是維護基本)資料
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public IActionResult SaveIntegrate(string WeeklyReportData, String HappyWeekIndex, String HappyWeekTopic)
        {
            try
            {
                // 整合式網頁按上傳按鈕
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                (
                    m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                    m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                    m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                    WeeklyReportData,
                    HappyWeekIndex, 
                    HappyWeekTopic
                );

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                (
                    m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                    m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                    m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                    WeeklyReportData,"",""
                );

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                (
                    m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                    m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                    m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                    "不需更新小組日誌", "", ""
                );

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                if (LoginParameter == "AccountPassword")
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.UpdateMember(key, values);

                // 修改全部的(也就是維護基本)資料
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData
                (
                    m_InMemoryDataContextSmallGroup.ListManager.m_Account,
                    m_InMemoryDataContextSmallGroup.ListManager.m_Password,
                    m_InMemoryDataContextSmallGroup.ListManager.LoginType,
                    m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                    "不需更新小組日誌", "", ""
                );

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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
                m_InMemoryDataContextSmallGroup.SetupListManager(m_InMemoryDataContextSmallGroup.ListManager.m_Account, m_InMemoryDataContextSmallGroup.ListManager.m_Password, aSelectDate, true);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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
                m_InMemoryDataContextSmallGroup.SetupListManager(m_InMemoryDataContextSmallGroup.ListManager.m_Account, m_InMemoryDataContextSmallGroup.ListManager.m_Password, aSelectDate, true);
                #endregion

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                String ActiveListId = m_InMemoryDataContextSmallGroup.ListManager.ActiveListId;

                m_InMemoryDataContextSmallGroup.SetupListManager(m_InMemoryDataContextSmallGroup.ListManager.m_Account, m_InMemoryDataContextSmallGroup.ListManager.m_Password, aSelectDate, true);

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

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.UpdateMember(key, values);

                // 修改全部的(也就是維護基本)資料
                m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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
                m_InMemoryDataContextSmallGroup.HappyGroupDataManager.UpdateActiveHappyGroup(key, values);

                return Ok();
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region 課程繳費與點名
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

                return View(m_InMemoryDataContextSmallGroup.FeeList);

                #endregion
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region 行事曆
        public ActionResult Scheduler()
        {
            try
            {
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.ListManager.LoginType; // 看是小組長還是個人回報
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
                SetMultiGroupLayoutParameter();

                AppointmentsList aAppointmentsList = new AppointmentsList();
                return View(aAppointmentsList);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        [HttpGet]
        public object LoadAppointments(DataSourceLoadOptions loadOptions)
        {
            AppointmentsList aAppointmentsList = new AppointmentsList();

            return DataSourceLoader.Load(aAppointmentsList.Appointments, loadOptions);
        }

        [HttpPost]
        public IActionResult PostAppointments(string values)
        {
            var newAppointment = new Appointment();
            JsonConvert.PopulateObject(values, newAppointment);


            return Ok();
        }

        [HttpPut]
        public IActionResult PutAppointments(int key, string values)
        {
            AppointmentsList aAppointmentsList = new AppointmentsList();
            var appointment = aAppointmentsList.Appointments.First(a => a.AppointmentId == key);
            JsonConvert.PopulateObject(values, appointment);

            return Ok();
        }

        [HttpDelete]
        public void DeleteAppointments(int key)
        {
            AppointmentsList aAppointmentsList = new AppointmentsList();

            var appointment = aAppointmentsList.Appointments.First(a => a.AppointmentId == key);
            aAppointmentsList.Appointments.Remove(appointment);
            //_data.SaveChanges();
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
                m_InMemoryDataContextSmallGroup.m_NewPersonModel.SetupGroupArray(m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData, m_InMemoryDataContextSmallGroup.ListManager.ActiveListId);

                return View(m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                string Result = m_InMemoryDataContextSmallGroup.m_NewPersonModel.UploadNewPerson( m_InMemoryDataContextSmallGroup.ListManager.m_Account, m_InMemoryDataContextSmallGroup.ListManager.m_Password, aPersonFormViewModel);

                if (Result.Contains("成功"))
                {
                    if (m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport != null && aPersonFormViewModel.Position != "0" )
                    {
                        m_InMemoryDataContextSmallGroup.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.AddNewPersonToMember(aPersonFormViewModel);
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                SetMultiGroupLayoutParameter();

                if (m_InMemoryDataContextSmallGroup.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData.Count == 1)
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

                ViewBag.ErrorMessage = ErrorMessage;

                return View();
            }
            catch (System.Exception e)
            {
                string ErrorString = "c FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "台北基督之家 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }

        }
        #endregion
        #region Line LIFF 綁定
        public IActionResult LineLiffView()
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

                return View(m_InMemoryDataContextSmallGroup.LineBindingViewModel);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region Line 上課資格
        public IActionResult QualificationView()
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

                return View(m_InMemoryDataContextSmallGroup.LineBindingViewModel);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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

                m_InMemoryDataContextSmallGroup.LineBindingViewModel.GetContactInfomation(UserLineId, ref FaithStatus, ref GenderCode, ref BirthDate);

                return Json(new { faithStatus = FaithStatus, genderCode = GenderCode, birthDate = BirthDate, message = "成功上傳了...." });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }


        [HttpPost]
        public IActionResult SaveQualificationData(LineBindingViewModel aLineBindingViewModel)
        {
            try
            {
                m_InMemoryDataContextSmallGroup.LineBindingViewModel.UpdateContactInfomation(aLineBindingViewModel.FaithStatus, aLineBindingViewModel.GenderCode, aLineBindingViewModel.BirthDate);

                return Json(new { status = "1", message = "謝謝 " + aLineBindingViewModel.FullName + " 填寫基本資料!", encoded = aLineBindingViewModel.DisplayName + "," + aLineBindingViewModel.LineUserId });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "城市之光聖教會 : 綁定錯誤 => " + ErrorString);

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
    }
}
