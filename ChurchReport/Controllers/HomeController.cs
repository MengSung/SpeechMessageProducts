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

namespace ChurchReport.Controllers
{
    public class HomeController : Controller
    {
        ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        //SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();

        //private String m_FullName ="";
        //private String m_Account = "";
        //private String m_Password = "";
        //private DateTime m_SundayDate = DateTime.Now;
        #region 登入帳號
        public IActionResult Login()
        {
            var images = new List<string>();
            images.Add(Url.Content("~/assets/images/tpehoc-001.png"));
            images.Add(Url.Content("~/assets/images/tpehoc-002.jpg"));
            images.Add(Url.Content("~/assets/images/tpehoc-003.jpg"));
            images.Add(Url.Content("~/assets/images/tpehoc-004.jpg"));
            //images.Add(Url.Content("~/assets/images/photo-1.jpg"));
            //images.Add(Url.Content("~/assets/images/photo-10.jpg"));
            //images.Add(Url.Content("~/assets/images/photo-6.jpg"));
            //images.Add(Url.Content("~/assets/images/photo-9.jpg"));
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

            if (ContactIdString != "密碼錯誤" && ContactIdString != "系統沒有設定密碼" && ContactIdString != "帳號錯誤")
            {
                Guid aContactGuid = new Guid(ContactIdString);

                String FullName = this.m_ToolUtilityClass.RetrieveEntityDynamics365("contact", aContactGuid).Attributes["fullname"].ToString();
                //String FullName = this.m_ToolUtilityClass.RetrieveEntityCrm2011("contact", aContactGuid).Attributes["fullname"].ToString();

                m_SmallGroupDataList.SetupSmallGroupData(FullName, aGalleryViewModel.Account, aGalleryViewModel.Password, DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek));

                //TempData["FullName"] = FullName;
                //TempData["Account"] = aGalleryViewModel.Account;
                //TempData["Password"] = aGalleryViewModel.Password;
                //TempData["SundayDate"] = DateTime.Now;

                String SerializedSmallGroupDataList = JsonConvert.SerializeObject(m_SmallGroupDataList);
                TempData["SmallGroupDataList"] = SerializedSmallGroupDataList;

                //SmallGroupDataList XXX_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SerializedSmallGroupDataList);

                //TempData["SmallGroupDataList"] = JsonConvert.SerializeObject(m_SmallGroupDataList);


                return Json(new { status = "1", message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
            }
            else
            {
                return Json(new { status = "2", message = ContactIdString, fullname = ContactIdString });
            }
        }
        #endregion
        #region 註冊帳號
        public IActionResult Register()
        {
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
        #region 小組長點名

        //[Route("/Home/SmallGroupReportView/{LoginParameter}")]
        //public ActionResult SmallGroupReportView(String LoginParameter)
        public ActionResult SmallGroupReportView()
        {
            String SmallGroupDataListString = (String)TempData.Peek("SmallGroupDataList");
            TempData.Keep("SmallGroupDataList");

            if (SmallGroupDataListString != null)
            {
                SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataListString);

                return View(m_SmallGroupDataList.m_SmallGroupData);
            }
            else
            {
                return RedirectToAction("Login");
            }
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

                return View(m_SmallGroupDataList.m_NewPersonFollowUpData);
            }
            else
            {
                return RedirectToAction("Login");
            }
        }
        #endregion
        #region 上傳、更換日期
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
        [HttpGet]
        public IActionResult UpdateDate(string SelectedDate)
        {


            //String FullName = (String)TempData.Peek("FullName");
            //TempData.Keep("FullName");
            //String Account = (String)TempData.Peek("Account");
            //TempData.Keep("Account");
            //String Password = (String)TempData.Peek("Password");
            //TempData.Keep("Password");
            //DateTime SundayDate = (DateTime)TempData.Peek("SundayDate");
            //TempData.Keep("SundayDate");

            //TempData["SundayDate"] = DateTime.Parse(SelectedDate).AddDays(-(int)DateTime.Now.DayOfWeek);

            String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");
            TempData.Keep("SmallGroupDataList");
            SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataList);

            //SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();
            DateTime aSelectDate = DateTime.Parse(SelectedDate);
            m_SmallGroupDataList.SetupSmallGroupData(m_SmallGroupDataList.m_FullName, m_SmallGroupDataList.m_Account, m_SmallGroupDataList.m_Password, aSelectDate.AddDays(-(int)aSelectDate.DayOfWeek));

            TempData["SmallGroupDataList"] = JsonConvert.SerializeObject(m_SmallGroupDataList);

            //this.m_SundayDate = DateTime.Parse(SelectedDate).AddDays(-(int)DateTime.Now.DayOfWeek);
            //m_SmallGroupDataList.SetupSmallGroupData();
            //return View(SmallGroupDataList.m_SmallGroupData);
            //return Json(new { status = "1", message = "成功上傳了...." });
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

                    return View(aWeeklyReportData.m_WeeklyReportViewModel);
                }
                else
                {
                    WeeklyReportData aWeeklyReportData = JsonConvert.DeserializeObject<WeeklyReportData>(WeeklyReportDataString);

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
        #region 新增新人
        public IActionResult NewPerson()
        {
            String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");
            if (SmallGroupDataList == null)
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

                return View(aNewPersonModel.PersonFormViewModel);
            }
            else
            {
                NewPersonModel aNewPersonModel = JsonConvert.DeserializeObject<NewPersonModel>(NewPersonString);

                return View(aNewPersonModel.PersonFormViewModel);

            }

        }

        [HttpPost]
        public IActionResult SaveNewPerson(PersonFormViewModel aPersonFormViewModel)
        {
            String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");
            TempData.Keep("SmallGroupDataList");
            SmallGroupDataList m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataList);

            String NewPersonString = (String)TempData.Peek("NewPerson");
            TempData.Keep("NewPerson");
            NewPersonModel aNewPersonModel = JsonConvert.DeserializeObject<NewPersonModel>(NewPersonString);

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
    }
}
