using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ChurchReport.ViewModel;

using ToolUtilityNameSpace;
using ChurchReport.Models;

using ChurchReport.WebServiceConnector;
using ChurchReport.Models.CrmTransmitModule;

// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using ChurchReport.ViewModels;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

using LineMessagingProcessor;
using DevExtreme.AspNet.Mvc;
using DevExtreme.AspNet.Data;

namespace ChurchReport.Controllers
{
    public class HomeController : Controller
    {
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        #region 登入帳號
        public IActionResult Login()
        {
            var images = new List<string>();
            images.Add(Url.Content("~/assets/images/sl-1.jpg"));
            images.Add(Url.Content("~/assets/images/sl-2.jpg"));
            images.Add(Url.Content("~/assets/images/sl-3.jpg"));
            images.Add(Url.Content("~/assets/images/sl-4.jpg"));
            images.Add(Url.Content("~/assets/images/71992.jpg"));
            return View(new GalleryViewModel
            {
                Images = images
            });
        }
        [HttpPost] 
        public IActionResult ProcessLogin(GalleryViewModel aGalleryViewModel)
        {
            String ContactIdString = m_ToolUtilityClass.RetrieveContactByAccountNumber(aGalleryViewModel.Account, aGalleryViewModel.Password);

            SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();
            m_SmallGroupDataList.SetupContactIdString(ContactIdString);

            HappyGroupDataManager m_HappyGroupDataManager = new HappyGroupDataManager();

            if (ContactIdString != "密碼錯誤" && ContactIdString != "系統沒有設定密碼" && ContactIdString != "帳號錯誤")
            {
                Guid aContactGuid = new Guid(ContactIdString);

                String FullName = this.m_ToolUtilityClass.RetrieveEntityDynamics365("contact", aContactGuid).Attributes["fullname"].ToString();
                //String FullName = this.m_ToolUtilityClass.RetrieveEntityCrm2011("contact", aContactGuid).Attributes["fullname"].ToString();

                //m_SmallGroupDataList.SetupSmallGroupData(FullName, aGalleryViewModel.Account, aGalleryViewModel.Password, DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek), false);
                m_SmallGroupDataList.SetupSmallGroupData(FullName, aGalleryViewModel.Account, aGalleryViewModel.Password, DateTime.Now, true);

                //TempData["FullName"] = FullName;
                //TempData["Account"] = aGalleryViewModel.Account;
                //TempData["Password"] = aGalleryViewModel.Password;
                //TempData["SundayDate"] = DateTime.Now;

                String SerializedSmallGroupDataList = JsonConvert.SerializeObject(m_SmallGroupDataList);
                TempData["SmallGroupDataList"] = SerializedSmallGroupDataList;

                // 設定幸福小組資料
                m_HappyGroupDataManager.SetupHappyGroupData(aGalleryViewModel.Account, aGalleryViewModel.Password);

                String SerializedHappyGroupDataManager = JsonConvert.SerializeObject(m_HappyGroupDataManager);
                TempData["HappyGroupDataManager"] = SerializedHappyGroupDataManager;

                if (m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType == "小組長" && m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
                {
                    // 小組長回報，而且有幸福小組
                    ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;
                    ViewBag.HappyType = "有幸福小組名單";
                    return Json(new { status = "1", message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                }
                else if (m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType == "小組長" && m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList == null)
                {
                    // 小組長回報，沒有幸福小組
                    ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;
                    ViewBag.HappyType = "沒幸福小組名單";
                    return Json(new { status = "1", message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                }
                else if (m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType != "小組長" && m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList == null)
                {
                    // 個人回報，不是小組長，沒有幸福小組
                    ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;
                    ViewBag.HappyType = "沒幸福小組名單";
                    return Json(new { status = "1", message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                }
                else if (m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType != "小組長" && m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
                {
                    // 單純幸福小組長回報
                    ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;
                    ViewBag.HappyType = "沒幸福小組名單";
                    return Json(new { status = "2", message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                }
                else
                {
                    // 
                    ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;
                    ViewBag.HappyType = "沒幸福小組名單";

                    return Json(new { status = "2", message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                }
            }
            else
            {
                return Json(new { status = "3", message = ContactIdString, fullname = ContactIdString });
            }
        }
        #endregion
        #region 註冊帳號
        public IActionResult Register()
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
        [HttpPost]
        public IActionResult ProcessRegister(RegisterViewModel aRegisterViewModel)
        {
            RegisterManager aRegisterManager = new RegisterManager();

            String RegisterResult = aRegisterManager.Register(aRegisterViewModel.FullName, aRegisterViewModel.Mobile, aRegisterViewModel.Account, aRegisterViewModel.Password, aRegisterViewModel.ConfirmPassword);

            if (RegisterResult.StartsWith("註冊成功"))
            {
                return Json(new { status = "1", message = aRegisterViewModel.FullName + RegisterResult, fullname = aRegisterViewModel.FullName, account = aRegisterViewModel.Account, password = aRegisterViewModel.Password });
            }
            else
            {
                return Json(new { status = "2", message = RegisterResult, fullname = RegisterResult });
            }
        }
        #endregion
        #region 小組長點名及個人回報
        [Route("/Home/SmallGroupReportView/{LoginParameter}")]
        public ActionResult SmallGroupReportView(String LoginParameter)
        //public ActionResult SmallGroupReportView()
        {
            if (LoginParameter == "AccountPassword")
            {
                String SmallGroupDataListString = (String)TempData.Peek("SmallGroupDataList");
                TempData.Keep("SmallGroupDataList");

                if (SmallGroupDataListString != null)
                {
                    SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataListString);

                    ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報


                    String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
                    TempData.Keep("HappyGroupDataManager");
                    HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);
                    if (m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }

                    return View(m_SmallGroupDataList.m_SmallGroupData);
                }
                else
                {
                    return RedirectToAction("Login");
                }
            }
            else if (LoginParameter == "jquery.js")
            {
                ViewBag.LoginType = "個人登入";
                return Ok();
            }
            else
            {
                String FullName = m_ToolUtilityClass.RetrieveContactEntityByLineUserId(LoginParameter).Attributes["fullname"].ToString();

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                // 寫入LINE的個人基本資料
                if (FullName.EndsWith("(Line)"))
                {
                    aLineMessagingProcessorClass.NotifyLineBinding(LoginParameter);

                    return RedirectToAction("Login");
                }
                else
                {
                    SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();

                    //m_SmallGroupDataList.SetupSmallGroupData(FullName, "LineIdLogin", LoginParameter, DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek), true);
                    m_SmallGroupDataList.SetupSmallGroupData(FullName, "LineIdLogin", LoginParameter, DateTime.Now, true);

                    String SerializedSmallGroupDataList = JsonConvert.SerializeObject(m_SmallGroupDataList);
                    TempData["SmallGroupDataList"] = SerializedSmallGroupDataList;

                    ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報

                    String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
                    TempData.Keep("HappyGroupDataManager");
                    HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);
                    if (m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }

                    return View(m_SmallGroupDataList.m_SmallGroupData);
                }
            }
        }
        [HttpPost]
        public IActionResult SaveSmallGroup(String aResult)
        {
            //string json = @"[
            //    { 'Id':1,'FullName':'吳連碧','Status':'小組長','SmallGroupName':'0201 連碧小組 - 主日出席率:50 % 小組出席率:0 %','SectionName':'0201 連碧小組 - 主日出席率:50 % 小組出席率:0 %','PrayItem':'未填','Sunday':'false','SmallGroup':'true','StateID1':'2','Number1':'4','StateID2':'1','Number2':'2','Picture':'../../ images / employees / 01.png','Shepherd':'null',},
            //    ]";

            //SmallGroupDataList.m_SmallGroupData.members = JsonConvert.DeserializeObject<List<Member>>(json);

            String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");
            TempData.Keep("SmallGroupDataList");
            SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataList);

            m_SmallGroupDataList.m_SmallGroupData.Members.Clear();
            m_SmallGroupDataList.m_SmallGroupData.Members = JsonConvert.DeserializeObject<List<Member>>(aResult);

            m_SmallGroupDataList.TransferToMemberInfomationPackage(m_SmallGroupDataList.m_SmallGroupData);
            m_SmallGroupDataList.UploadMemberInfomationPackage();


            String SerializedSmallGroupDataList = JsonConvert.SerializeObject(m_SmallGroupDataList);
            TempData["SmallGroupDataList"] = SerializedSmallGroupDataList;

            return Json(new { status = "1", message = "成功上傳了...." });
        }
        #endregion
        #region 新人跟進關懷
        [HttpGet]
        public ActionResult NewPersonFollowUpView()
        {
            String SmallGroupDataListString = (String)TempData.Peek("SmallGroupDataList");
            TempData.Keep("SmallGroupDataList");

            if (SmallGroupDataListString != null)
            {
                SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataListString);

                ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報

                String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
                TempData.Keep("HappyGroupDataManager");
                HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);
                if (m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }
                return View(m_SmallGroupDataList.m_NewPersonFollowUpData);
            }
            else
            {
                return RedirectToAction("Login");
            }
        }
        [HttpPost]
        public IActionResult SaveNewPersonFollowUp(String aResult)
        {
            //string json = @"[
            //    { 'Id':1,'FullName':'吳連碧','Status':'小組長','SmallGroupName':'0201 連碧小組 - 主日出席率:50 % 小組出席率:0 %','SectionName':'0201 連碧小組 - 主日出席率:50 % 小組出席率:0 %','PrayItem':'未填','Sunday':'false','SmallGroup':'true','StateID1':'2','Number1':'4','StateID2':'1','Number2':'2','Picture':'../../ images / employees / 01.png','Shepherd':'null',},
            //    ]";

            //SmallGroupDataList.m_SmallGroupData.members = JsonConvert.DeserializeObject<List<Member>>(json);

            String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");
            TempData.Keep("SmallGroupDataList");
            SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataList);

            m_SmallGroupDataList.m_NewPersonFollowUpData.Members.Clear();
            m_SmallGroupDataList.m_NewPersonFollowUpData.Members = JsonConvert.DeserializeObject<List<Member>>(aResult);

            m_SmallGroupDataList.TransferToMemberInfomationPackage(m_SmallGroupDataList.m_NewPersonFollowUpData);
            m_SmallGroupDataList.UploadMemberInfomationPackage();


            String SerializedSmallGroupDataList = JsonConvert.SerializeObject(m_SmallGroupDataList);
            TempData["SmallGroupDataList"] = SerializedSmallGroupDataList;

            return Json(new { status = "1", message = "成功上傳了...." });
        }
        #endregion
        #region 基本資料維護
        [HttpGet]
        public ActionResult MaintainPersonInfomationView()
        {
            String SmallGroupDataListString = (String)TempData.Peek("SmallGroupDataList");
            TempData.Keep("SmallGroupDataList");

            if (SmallGroupDataListString != null)
            {
                SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataListString);

                ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報

                String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
                TempData.Keep("HappyGroupDataManager");
                HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);
                if (m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                return View(m_SmallGroupDataList.m_AllMemeberData);
            }
            else
            {
                return RedirectToAction("Login");
            }
        }
        [HttpPost]
        public IActionResult SavePersonInfomation(String aResult)
        {
            //string json = @"[
            //    { 'Id':1,'FullName':'吳連碧','Status':'小組長','SmallGroupName':'0201 連碧小組 - 主日出席率:50 % 小組出席率:0 %','SectionName':'0201 連碧小組 - 主日出席率:50 % 小組出席率:0 %','PrayItem':'未填','Sunday':'false','SmallGroup':'true','StateID1':'2','Number1':'4','StateID2':'1','Number2':'2','Picture':'../../ images / employees / 01.png','Shepherd':'null',},
            //    ]";

            //SmallGroupDataList.m_SmallGroupData.members = JsonConvert.DeserializeObject<List<Member>>(json);

            String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");
            TempData.Keep("SmallGroupDataList");
            SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataList);

            m_SmallGroupDataList.m_AllMemeberData.Members.Clear();
            m_SmallGroupDataList.m_AllMemeberData.Members = JsonConvert.DeserializeObject<List<Member>>(aResult);

            m_SmallGroupDataList.TransferToMemberInfomationPackage(m_SmallGroupDataList.m_AllMemeberData);
            m_SmallGroupDataList.UploadMemberInfomationPackage();


            String SerializedSmallGroupDataList = JsonConvert.SerializeObject(m_SmallGroupDataList);
            TempData["SmallGroupDataList"] = SerializedSmallGroupDataList;

            return Json(new { status = "1", message = "成功上傳了...." });
        }
        #endregion
        #region 更換日期
        [HttpGet]
        public IActionResult UpdateDate(string SelectedDate)
        {
            #region 小組 主日 點名
            String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");
            TempData.Keep("SmallGroupDataList");
            SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataList);

            //SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();
            //DateTime aSelectDate = DateTime.Parse(SelectedDate);
            DateTime aSelectDate = DateTime.Parse(SelectedDate).ToLocalTime();
            m_SmallGroupDataList.SetupSmallGroupData(m_SmallGroupDataList.m_FullName, m_SmallGroupDataList.m_Account, m_SmallGroupDataList.m_Password, aSelectDate, false);

            String SerializedSmallGroupDataList = JsonConvert.SerializeObject(m_SmallGroupDataList);
            TempData["SmallGroupDataList"] = SerializedSmallGroupDataList;
            #endregion

            #region 小組日誌
            WeeklyReportData aWeeklyReportData = new WeeklyReportData();

            aWeeklyReportData.SetupWeeklyReport(m_SmallGroupDataList.m_Account, m_SmallGroupDataList.m_Password, m_SmallGroupDataList.m_SundayDate);

            String SerializedWeeklyReportData = JsonConvert.SerializeObject(aWeeklyReportData);
            TempData["WeeklyReportData"] = SerializedWeeklyReportData;
            #endregion

            return Ok();

        }
        #endregion
        #region 週報管理

        public IActionResult WeeklyReport()
        {
            //  This type returns a redirect to an action or destination
            //  (using Redirect, LocalRedirect, RedirectToAction, or RedirectToRoute). 
            //  For example, return RedirectToAction("Complete", new { id = 123 });
            //  redirects to Complete, passing an anonymous object.

            String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");
            SmallGroupDataList m_SmallGroupDataList;

            if (SmallGroupDataList != null)
            {
                TempData.Keep("SmallGroupDataList");
                m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataList);
            }
            else
            {
                return RedirectToAction("Login");
            }

            if (m_SmallGroupDataList.m_Account != "")
            {
                String WeeklyReportDataString = (String)TempData.Peek("WeeklyReportData");
                TempData.Keep("WeeklyReportData");

                if (WeeklyReportDataString == null)
                {
                    WeeklyReportData aWeeklyReportData = new WeeklyReportData();

                    aWeeklyReportData.SetupWeeklyReport(m_SmallGroupDataList.m_Account, m_SmallGroupDataList.m_Password, m_SmallGroupDataList.m_SundayDate);

                    String SerializedWeeklyReportData = JsonConvert.SerializeObject(aWeeklyReportData);
                    TempData["WeeklyReportData"] = SerializedWeeklyReportData;

                    ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報

                    String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
                    TempData.Keep("HappyGroupDataManager");
                    HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);
                    if (m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }

                    return View(aWeeklyReportData.m_WeeklyReportViewModel);
                }
                else
                {
                    WeeklyReportData aWeeklyReportData = JsonConvert.DeserializeObject<WeeklyReportData>(WeeklyReportDataString);

                    ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報

                    String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
                    TempData.Keep("HappyGroupDataManager");
                    HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);
                    if (m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }

                    return View(aWeeklyReportData.m_WeeklyReportViewModel);
                }
            }
            else
            {
                return RedirectToAction("Login");
            }
        }
        [HttpPost]
        public IActionResult SaveWeeklyReport(WeeklyReportViewModel aWeeklyReportViewModel)
        {
            String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");
            TempData.Keep("SmallGroupDataList");
            SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataList);

            if (m_SmallGroupDataList.m_Account != "")
            {
                String WeeklyReportDataString = (String)TempData.Peek("WeeklyReportData");
                TempData.Keep("WeeklyReportData");

                if (WeeklyReportDataString != null)
                {
                    // 先上傳小組點名資料，萬一沒有先上傳小組點名，則仍然可以上傳小組日誌，因為在後台會建立新增周報
                    m_SmallGroupDataList.UploadMemberInfomationPackage();

                    WeeklyReportData aWeeklyReportData = JsonConvert.DeserializeObject<WeeklyReportData>(WeeklyReportDataString);

                    // 給上傳用的
                    aWeeklyReportData.m_WeeklyReport.WeeklyReportContent = aWeeklyReportViewModel.WeeklyReportData;
                    aWeeklyReportData.m_WeeklyReport.PresentContent = aWeeklyReportViewModel.WeeklyReportAnalysis;

                    // 給網頁顯示用的
                    aWeeklyReportData.m_WeeklyReportViewModel.WeeklyReportData = aWeeklyReportViewModel.WeeklyReportData;
                    aWeeklyReportData.m_WeeklyReportViewModel.WeeklyReportAnalysis = aWeeklyReportViewModel.WeeklyReportAnalysis;

                    String SerializedWeeklyReportData = JsonConvert.SerializeObject(aWeeklyReportData);
                    TempData["WeeklyReportData"] = SerializedWeeklyReportData;

                    aWeeklyReportData.UploadWeeklyReport(m_SmallGroupDataList.m_Account, m_SmallGroupDataList.m_Password, m_SmallGroupDataList.m_SundayDate, aWeeklyReportData.m_WeeklyReport);
                }
                else
                {
                }
            }

            return Json(new { status = "1", message = "成功上傳了...." });
            //return Json(new { status = "2", message = "密碼錯誤...." });
        }
        //[HttpGet]
        //public IActionResult UpdateDate(string SelectedDate)
        //{
        //    m_SmallGroupDataList.SetupSmallGroupDate(m_SmallGroupDataList.m_SmallGroupData.SmallGroupLeaderContactId, SelectedDate);
        //    //return View(SmallGroupDataList.m_SmallGroupData);
        //    //return Json(new { status = "1", message = "成功上傳了...." });
        //    return Ok();

        //}
        [Route("/Home/InputReport/{FullName}")]
        public IActionResult InputReport(String FullName)
        {
            return View((object)FullName);
        }

        #endregion
        #region 幸福小組回報
        public ActionResult HappyGroup()
        {
            TempData.Keep("SmallGroupDataList");
            String SmallGroupDataListString = (String)TempData.Peek("SmallGroupDataList");
            SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataListString);
            ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報

            String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
            TempData.Keep("HappyGroupDataManager");
            HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);
            
            if (m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
            {
                ViewBag.SpiritLeaderList = m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList.SpiritLeaderList;
                ViewBag.HappyType = "有幸福小組名單";
            }
            else
            {
                ViewBag.HappyType = "沒幸福小組名單";
            }
            //
            return View();
        }

        [HttpGet]
        public object LoadHappyWeeklyReport(DataSourceLoadOptions loadOptions)
        {
            TempData.Keep("HappyGroupDataManager");
            String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");

            HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);

            if (m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
            {
                return DataSourceLoader.Load(m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList.HappyGroupWeeklyReportList, loadOptions);
            }
            else
            {
                return null;
            }
        }

        [HttpGet]
        public object LoadBest(string id, DataSourceLoadOptions loadOptions)
        {
            String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
            TempData.Keep("HappyGroupDataManager");

            HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);

            if (m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
            {
                //var tasks = SampleData_001.DataGridEmployees.Where(e => e.ID == id).Select(e => e.Tasks).FirstOrDefault();
                var tasks = m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList.HappyGroupWeeklyReportList.Where(e => e.HappyGroupWeeklyReportId == id).Select(e => e.BestRecordList).FirstOrDefault();

                return DataSourceLoader.Load(tasks, loadOptions);
            }
            else { return null; }

        }

        // POST api/values
        [HttpPost]
        public IActionResult PostBest(string values)
        {
            // 新增週報或是BEST
            String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
            TempData.Keep("HappyGroupDataManager");

            HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);

            m_HappyGroupDataManager.AddActiveHappyGroup(values);

            SerializedHappyGroupDataManager = JsonConvert.SerializeObject(m_HappyGroupDataManager);
            TempData["HappyGroupDataManager"] = SerializedHappyGroupDataManager;

            return Ok();
        }

        // PUT api/values/5
        [HttpPut]
        public IActionResult PutBest(string key, string values)
        {
            // 修改週報或是BEST
            String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
            TempData.Keep("HappyGroupDataManager");

            HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);

            //Dictionary < string, string> aDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(key);
            //m_HappyGroupDataManager.UpdateActiveHappyGroup(aDictionary["BestRecordId"], values);
            m_HappyGroupDataManager.UpdateActiveHappyGroup(key, values);

            SerializedHappyGroupDataManager = JsonConvert.SerializeObject(m_HappyGroupDataManager);
            TempData["HappyGroupDataManager"] = SerializedHappyGroupDataManager;


            return Ok();
        }

        // DELETE api/values/5
        [HttpDelete]
        public void DeleteBest(string key)
        {
            // 刪除週報或是BEST
            String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
            TempData.Keep("HappyGroupDataManager");

            HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);

            //Dictionary < string, string> aDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(key);
            //m_HappyGroupDataManager.UpdateActiveHappyGroup(aDictionary["BestRecordId"], values);
            m_HappyGroupDataManager.DeleteActiveHappyGroup(key);

            SerializedHappyGroupDataManager = JsonConvert.SerializeObject(m_HappyGroupDataManager);
            TempData["HappyGroupDataManager"] = SerializedHappyGroupDataManager;


            return;
        }


        [HttpPost]
        public IActionResult SaveHappyGroup()
        {
            // 上傳至雲端系統資料庫
            String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
            TempData.Keep("HappyGroupDataManager");

            HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);

            m_HappyGroupDataManager.SaveActiveHappyGroup();

            return Json(new { status = "1", message = "成功上傳了...." });
        }

        #endregion
        #region 行事曆
        public ActionResult Scheduler()
        {
            String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");

            SmallGroupDataList m_SmallGroupDataList;
            if (SmallGroupDataList != null)
            {
                TempData.Keep("SmallGroupDataList");
                m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataList);
            }
            else
            {
                return RedirectToAction("Login");
            }

            AppointmentsList aAppointmentsList = new AppointmentsList();

            ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報

            return View(aAppointmentsList);
        }
        #endregion
        #region 新增新人
        public IActionResult NewPerson()
        {

            String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");

            SmallGroupDataList m_SmallGroupDataList;
            if (SmallGroupDataList != null)
            {
                TempData.Keep("SmallGroupDataList");
                m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataList);
            }
            else
            {
                return RedirectToAction("Login");
            }

            String NewPersonString = (String)TempData.Peek("NewPerson");
            TempData.Keep("NewPerson");

            if (NewPersonString == null)
            {
                NewPersonModel aNewPersonModel = new NewPersonModel();

                String SerializedNewPersonModel = JsonConvert.SerializeObject(aNewPersonModel);
                TempData["NewPerson"] = SerializedNewPersonModel;

                ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報

                String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
                TempData.Keep("HappyGroupDataManager");
                HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);
                if (m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                return View(aNewPersonModel.m_PersonFormViewModel);
            }
            else
            {
                NewPersonModel aNewPersonModel = JsonConvert.DeserializeObject<NewPersonModel>(NewPersonString);

                ViewBag.LoginType = m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報

                String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");
                TempData.Keep("HappyGroupDataManager");
                HappyGroupDataManager m_HappyGroupDataManager = JsonConvert.DeserializeObject<HappyGroupDataManager>(SerializedHappyGroupDataManager);
                if (m_HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList != null)
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                return View(aNewPersonModel.m_PersonFormViewModel);

            }

        }

        [HttpPost]
        public IActionResult SaveNewPerson(PersonFormViewModel aPersonFormViewModel)
        {
            if(aPersonFormViewModel.Phone == "" || aPersonFormViewModel.Phone == null)
            {
                return Json(new { status = "2", message = "新增新人必須要有行動電話" });
            }

            String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");
            TempData.Keep("SmallGroupDataList");
            SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataList);

            String NewPersonString = (String)TempData.Peek("NewPerson");
            TempData.Keep("NewPerson");
            NewPersonModel aNewPersonModel = JsonConvert.DeserializeObject<NewPersonModel>(NewPersonString);

            // 上傳至系統
            String Result = aNewPersonModel.UploadNewPerson(m_SmallGroupDataList.m_Account, m_SmallGroupDataList.m_Password, aPersonFormViewModel);

            if (aPersonFormViewModel.Position == "0" || aPersonFormViewModel.Position == "1" || aPersonFormViewModel.Position == "2" || aPersonFormViewModel.Position == "3" || aPersonFormViewModel.Position == "4" || aPersonFormViewModel.Position == "5")
            {
                int GroupIndex = Convert.ToInt32(aPersonFormViewModel.Position);
                aPersonFormViewModel.Position = AssignSmallGroupList.AssignSmallGroupListData[Convert.ToInt32(aPersonFormViewModel.Position)].Name;
            }

            m_SmallGroupDataList.AddNewPersonToSmallGroup(aPersonFormViewModel);

            String SerializedNewPersonModel = JsonConvert.SerializeObject(aNewPersonModel);
            TempData["NewPerson"] = SerializedNewPersonModel;

            String SerializedSmallGroupDataList = JsonConvert.SerializeObject(m_SmallGroupDataList);
            TempData["SmallGroupDataList"] = SerializedSmallGroupDataList;

            if (Result.Contains("成功"))
            {
                return Json(new { status = "1", message = Result });
            }
            else
            {
                return Json(new { status = "2", message = Result });
            }
        }
        #endregion
        #region Line綁定
        [Route("/Home/LineBindingView/{LineBindingParameter}")]
        //[HttpGet("{LineBindingParameter}")]
        public IActionResult LineBindingView(String LineBindingParameter)
        {
            String[] LineBindingParameterArray = LineBindingParameter.Split(',');

            var images = new List<string>();
            images.Add(Url.Content("~/assets/images/tpehoc-005.jpg"));
            images.Add(Url.Content("~/assets/images/tpehoc-006.jpg"));
            images.Add(Url.Content("~/assets/images/tpehoc-007.jpg"));
            images.Add(Url.Content("~/assets/images/tpehoc-008.jpg"));
            images.Add(Url.Content("~/assets/images/tpehoc-009.jpg"));
            //images.Add(Url.Content("~/assets/images/photo-1.jpg"));
            //images.Add(Url.Content("~/assets/images/photo-10.jpg"));
            //images.Add(Url.Content("~/assets/images/photo-6.jpg"));
            //images.Add(Url.Content("~/assets/images/photo-9.jpg"));

            String EncodeName = System.Net.WebUtility.UrlEncode(LineBindingParameterArray[0]) + "," + System.Net.WebUtility.UrlEncode(LineBindingParameterArray[1]);

            if (LineBindingParameterArray.Length >= 2)
            {
                return View(new LineBindingViewModel
                {
                    DisplayName = LineBindingParameterArray[0],
                    LineUserId = LineBindingParameterArray[1],
                    FullName = LineBindingParameterArray[0],
                    EncodeUrl = System.Net.WebUtility.UrlEncode(LineBindingParameterArray[0]) + "," + System.Net.WebUtility.UrlEncode(LineBindingParameterArray[1]),
                    Images = images
                });
            }
            else
            {
                return RedirectToAction("Register");
            }
        }

        [HttpPost]
        public IActionResult ProcessLineBinding(LineBindingViewModel aLineBindingViewModel)
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
            String Mobile = DigitsOnly.Replace(aLineBindingViewModel.Mobile, "");

            String BindingString = "//" + aLineBindingViewModel.FullName + "," + aLineBindingViewModel.Mobile;

            Guid aLineEntityId = CreateLineMessage(aLineBindingViewModel.LineUserId, BindingString, 100000000);

            if (aLineEntityId != null && aLineEntityId != Guid.Empty)
            {
                return Json(new { status = "1", message = "歡迎 " + aLineBindingViewModel.FullName + " 開始綁定程序，請至台中思恩堂豐富教會接收綁定結果訊息，謝謝您!", encoded = aLineBindingViewModel.DisplayName + "," + aLineBindingViewModel.LineUserId } );
            }
            else
            {
                return Json(new { status = "2", message = aLineBindingViewModel.FullName + " 綁定失敗!" });
            }
        }


        public Guid CreateLineMessage(string UserId, string Message, int OptionSetValueOfMessageType)
        {
            try
            {
                Entity aContact = this.m_ToolUtilityClass.RetrieveContactCollectionByLineId(UserId);

                //await SendMessage(UserId, "001: " + UserId);

                if (aContact != null)
                {
                    //await SendMessage(UserId, "002");
                    Entity aEntity = new Entity("letter");
                    m_ToolUtilityClass.SetEntityStringAttribute(ref aEntity, "subject", Message);
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
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }

        #endregion
    }
}
