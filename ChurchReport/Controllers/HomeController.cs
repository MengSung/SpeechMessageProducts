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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Converters;
using System.IO;

namespace ChurchReport.Controllers
{
    public class HomeController : Controller
    {
        #region 資料區
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        private InMemoryDataContextSmallGroup m_InMemoryDataContextSmallGroup;
        #endregion
        #region 初始化
        public HomeController(IHttpContextAccessor httpContextAccessor, IMemoryCache memoryCache)
        {
            m_InMemoryDataContextSmallGroup = ContextDictionary.GetInMemoryDataContextSmallGroup(httpContextAccessor, memoryCache);
        }
        #endregion
        #region 登入帳號
        public IActionResult Login()
        {
            var images = new List<string>();
            images.Add(Url.Content("~/assets/images/pastor-gary-kuo.png"));
            images.Add(Url.Content("~/assets/images/page2-img2.png"));
            images.Add(Url.Content("~/assets/images/page2-img3.png"));
            images.Add(Url.Content("~/assets/images/page2-img4.png"));
            images.Add(Url.Content("~/assets/images/page2-img5.png"));
            return View(new GalleryViewModel
            {
                Images = images
            });
        }
        [HttpPost] 
        public IActionResult ProcessLogin(GalleryViewModel aGalleryViewModel)
        {
            String ContactIdString = m_ToolUtilityClass.RetrieveContactByAccountNumber(aGalleryViewModel.Account, aGalleryViewModel.Password);

            if (ContactIdString != "密碼錯誤" && ContactIdString != "系統沒有設定密碼" && ContactIdString != "帳號錯誤")
            {
                Guid aContactGuid = new Guid(ContactIdString);

                String FullName = this.m_ToolUtilityClass.RetrieveEntityDynamics365("contact", aContactGuid).Attributes["fullname"].ToString();

                // 設定一般小組資料
                m_InMemoryDataContextSmallGroup.SetupSmallGroupData(FullName, aGalleryViewModel.Account, aGalleryViewModel.Password, DateTime.Now, true);

                // 設定週報資料
                m_InMemoryDataContextSmallGroup.SetupWeeklyReport(aGalleryViewModel.Account, aGalleryViewModel.Password, m_InMemoryDataContextSmallGroup.m_SmallGroupDataList.m_SundayDate );

                // 設定幸福小組資料
                m_InMemoryDataContextSmallGroup.SetupHappyGroupData(aGalleryViewModel.Account, aGalleryViewModel.Password);

                // 設定繳費與報名資料
                //m_InMemoryDataContextSmallGroup.SetupFeeListAccountAndPassword(FullName, aGalleryViewModel.Account, aGalleryViewModel.Password);
                m_InMemoryDataContextSmallGroup.SetupFeeList(FullName, aGalleryViewModel.Account, aGalleryViewModel.Password );

                if (m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType == "小組長" && m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單" )
                {
                    // 小組長回報，而且有幸福小組
                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.m_SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.m_SmallGroupDataList.m_FullName;

                    ViewBag.HappyType = "有幸福小組名單";
                    return Json(new { status = "1", message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                }
                else if (m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType == "小組長" && m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "沒幸福小組名單" )
                {
                    // 小組長回報，沒有幸福小組
                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

                    ViewBag.HappyType = "沒幸福小組名單";
                    return Json(new { status = "1", message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                }
                else if (m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType != "小組長" && m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "沒幸福小組名單" )
                {
                    // 個人回報，不是小組長，沒有幸福小組
                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

                    ViewBag.HappyType = "沒幸福小組名單";
                    return Json(new { status = "1", message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                }
                else if (m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType != "小組長" && m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單" )
                {
                    // 單純幸福小組長回報
                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

                    ViewBag.HappyType = "有幸福小組名單";
                    return Json(new { status = "2", message = "歡迎" + FullName + "登入成功!", fullname = FullName, account = aGalleryViewModel.Account, password = aGalleryViewModel.Password });
                }
                else
                {
                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;
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
                #region 用小組長回報網頁登入
                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

                    if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單" )
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }

                    return View(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_SmallGroupData);
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
                    m_InMemoryDataContextSmallGroup.SetupSmallGroupData(FullName, "LineIdLogin", LoginParameter, DateTime.Now, true);

                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

                    if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }

                    return View(m_InMemoryDataContextSmallGroup.SmallGroupDataList);
                }
                #endregion
            }
        }
        [HttpPost]
        public IActionResult SaveSmallGroup(String aResult)
        {
            //string json = @"[
            //    { 'Id':1,'FullName':'吳連碧','Status':'小組長','SmallGroupName':'0201 連碧小組 - 主日出席率:50 % 小組出席率:0 %','SectionName':'0201 連碧小組 - 主日出席率:50 % 小組出席率:0 %','PrayItem':'未填','Sunday':'false','SmallGroup':'true','StateID1':'2','Number1':'4','StateID2':'1','Number2':'2','Picture':'../../ images / employees / 01.png','Shepherd':'null',},
            //    ]";

            m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_SmallGroupData.Members.Clear();
            m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_SmallGroupData.Members = JsonConvert.DeserializeObject<List<Member>>(aResult);

            m_InMemoryDataContextSmallGroup.SmallGroupDataList.TransferToMemberInfomationPackage(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_SmallGroupData);
            m_InMemoryDataContextSmallGroup.SmallGroupDataList.UploadMemberInfomationPackage();

            return Json(new { status = "1", message = "成功上傳了...." });
        }
        #endregion
        #region 新人跟進關懷
        [HttpGet]
        public ActionResult NewPersonFollowUpView()
        {
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }
                return View(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_NewPersonFollowUpData);
        }
        [HttpPost]
        public IActionResult SaveNewPersonFollowUp(String aResult)
        {
            m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_NewPersonFollowUpData.Members.Clear();
            m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_NewPersonFollowUpData.Members = JsonConvert.DeserializeObject<List<Member>>(aResult);

            m_InMemoryDataContextSmallGroup.SmallGroupDataList.TransferToMemberInfomationPackage(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_NewPersonFollowUpData);
            m_InMemoryDataContextSmallGroup.SmallGroupDataList.UploadMemberInfomationPackage();
            return Json(new { status = "1", message = "成功上傳了...." });
        }
        #endregion
        #region 基本資料維護
        [HttpGet]
        public ActionResult MaintainPersonInfomationView()
        {
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                return View(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_AllMemeberData);
        }
        [HttpPost]
        public IActionResult SavePersonInfomation(String aResult)
        {
            m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_AllMemeberData.Members.Clear();
            m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_AllMemeberData.Members = JsonConvert.DeserializeObject<List<Member>>(aResult);

            m_InMemoryDataContextSmallGroup.SmallGroupDataList.TransferToMemberInfomationPackage(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_AllMemeberData);
            m_InMemoryDataContextSmallGroup.SmallGroupDataList.UploadMemberInfomationPackage();

            return Json(new { status = "1", message = "成功上傳了...." });
        }
        #endregion
        #region 更換日期
        [HttpGet]
        public IActionResult UpdateDate(string SelectedDate)
        {
            #region 小組 主日 點名
            //SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();
            //DateTime aSelectDate = DateTime.Parse(SelectedDate);
            DateTime aSelectDate = DateTime.Parse(SelectedDate).ToLocalTime();
            m_InMemoryDataContextSmallGroup.SmallGroupDataList.SetupSmallGroupData(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName, m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_Account, m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_Password, aSelectDate, false);
            #endregion

            #region 小組日誌
            m_InMemoryDataContextSmallGroup.WeeklyReportData.SetupWeeklyReport(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_Account, m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_Password, m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_SundayDate);
            #endregion

            return Ok();

        }
        #endregion
        #region 週報管理

        public IActionResult WeeklyReport()
        {
                if (m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReport.WeeklyReportContent == "尚未初始化")
                {
                    m_InMemoryDataContextSmallGroup.WeeklyReportData.SetupWeeklyReport(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_Account, m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_Password, m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_SundayDate);

                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

                    if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }

                    return View(m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReportViewModel);
                }
                else
                {

                    ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報
                    ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

                    if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                    {
                        ViewBag.HappyType = "有幸福小組名單";
                    }
                    else
                    {
                        ViewBag.HappyType = "沒幸福小組名單";
                    }

                    return View(m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReportViewModel);
                }
        }
        [HttpPost]
        public IActionResult SaveWeeklyReport(WeeklyReportViewModel aWeeklyReportViewModel)
        {
            if (m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_Account != "")
            {
                // 先上傳小組點名資料，萬一沒有先上傳小組點名，則仍然可以上傳小組日誌，因為在後台會建立新增周報
                m_InMemoryDataContextSmallGroup.SmallGroupDataList.UploadMemberInfomationPackage();

                // 給上傳用的
                m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReport.WeeklyReportContent = aWeeklyReportViewModel.WeeklyReportData;
                m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReport.PresentContent = aWeeklyReportViewModel.WeeklyReportAnalysis;

                // 給網頁顯示用的
                m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReportViewModel.WeeklyReportData = aWeeklyReportViewModel.WeeklyReportData;
                m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReportViewModel.WeeklyReportAnalysis = aWeeklyReportViewModel.WeeklyReportAnalysis;

                m_InMemoryDataContextSmallGroup.WeeklyReportData.UploadWeeklyReport(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_Account, m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_Password, m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_SundayDate, m_InMemoryDataContextSmallGroup.WeeklyReportData.m_WeeklyReport);
            }

            return Json(new { status = "1", message = "成功上傳了...." });
            //return Json(new { status = "2", message = "密碼錯誤...." });
        }

        [Route("/Home/InputReport/{FullName}")]
        public IActionResult InputReport(String FullName)
        {
            return View((object)FullName);
        }

        #endregion
        #region 幸福小組回報
        public ActionResult HappyGroup()
        {
            ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;
            ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

            if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
            {
                ViewBag.SpiritLeaderList = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList.SpiritLeaderList;
                ViewBag.ListEntityId = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList.ListEntityId;

                ViewBag.HappyGroupName = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupWeeklyReportList.HappyGroupName;

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
        public object LoadHappyGroupList( DataSourceLoadOptions loadOptions )
        {
            if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupListClass != null)
            {

                return DataSourceLoader.Load(m_InMemoryDataContextSmallGroup.HappyGroupDataManager.m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass, loadOptions);

            }
            else { return null; }
        }

        [HttpGet]
        public object LoadHappyWeeklyReport(string id, DataSourceLoadOptions loadOptions)
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

        [HttpGet]
        public object LoadBest(string id, DataSourceLoadOptions loadOptions)
        {
            HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass = m_InMemoryDataContextSmallGroup.HappyGroupDataManager.GetHappyGroupWeeklyReportListClassByWeeklyReportId(id);

            if ( aHappyGroupWeeklyReportListClass != null)
            {
                //var tasks = SampleData_001.DataGridEmployees.Where(e => e.ID == id).Select(e => e.Tasks).FirstOrDefault();
                var tasks = aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList.Where(e => e.HappyGroupWeeklyReportId == id).Select(e => e.BestRecordList).FirstOrDefault();

                return DataSourceLoader.Load(tasks, loadOptions);
            }
            else { return null; }

        }

        // POST api/values
        [HttpPost]
        public IActionResult PostBest(string values)
        {
            // 新增週報或是BEST

            m_InMemoryDataContextSmallGroup.HappyGroupDataManager.AddActiveHappyGroup(values);

            return Ok();
        }

        // PUT api/values/5
        [HttpPut]
        public IActionResult PutBest(string key, string values)
        {
            // 修改週報或是BEST
            //m_InMemoryDataContextSmallGroup.HappyGroupDataManager.UpdateUpdatedMasterOrDetail(key, values);
            m_InMemoryDataContextSmallGroup.HappyGroupDataManager.UpdateActiveHappyGroup(key, values);

            return Ok();
        }

        // DELETE api/values/5
        [HttpDelete]
        public void DeleteBest(string key)
        {
            // 刪除週報或是BEST
            //Dictionary < string, string> aDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(key);
            //m_HappyGroupDataManager.UpdateActiveHappyGroup(aDictionary["BestRecordId"], values);
            m_InMemoryDataContextSmallGroup.HappyGroupDataManager.DeleteActiveHappyGroup(key);
            return;
        }


        [HttpPost]
        public IActionResult SaveHappyGroup()
        {
            // 上傳至雲端系統資料庫
            String SerializedHappyGroupDataManager = (String)TempData.Peek("HappyGroupDataManager");

            m_InMemoryDataContextSmallGroup.HappyGroupDataManager.SaveActiveHappyGroup();

            return Json(new { status = "1", message = "成功上傳了...." });
        }

        #endregion
        #region 課程繳費與點名
        public ActionResult FeeManagerView()
        //public ActionResult SmallGroupReportView()
        {
            #region 用小組長回報網頁登入
            ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報
            ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

            if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
            {
                ViewBag.HappyType = "有幸福小組名單";
            }
            else
            {
                ViewBag.HappyType = "沒幸福小組名單";
            }

            // 設定繳費與報名資料
            //m_InMemoryDataContextSmallGroup.SetupFeeList();

            return View(m_InMemoryDataContextSmallGroup.FeeList);

            #endregion

        }
        [HttpPost]
        public IActionResult SaveFeeManager(String aResult)
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

            m_InMemoryDataContextSmallGroup.SmallGroupDataList.TransferToMemberInfomationPackage(m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_SmallGroupData);
            m_InMemoryDataContextSmallGroup.SmallGroupDataList.UploadMemberInfomationPackage();

            return Json(new { status = "1", message = "成功上傳了...." });
        }

        //public static T FromJSON<T>(this string str)
        //{
        //    var serializer = new JsonSerializer { DateFormatString = "dd-MM-yyyy" };
        //    return serializer.Deserialize<T>(new JsonTextReader(new StringReader(str)));
        //}

        [HttpGet]
        public object LoadFeeDataList(DataSourceLoadOptions loadOptions)
        {
            // 上課紀錄單過濾掉上完十課的
            // 下載對課單紀錄，含對課中及完整清單
            //m_InMemoryDataContext.ClassSheetManager.LoadReportDiscipleLessonsList();

            //loadOptions.Filter = new List<object>(new object[] { "DiscipleLessonsStatusCode", "<>", "對完十課" });

            return DataSourceLoader.Load(m_InMemoryDataContextSmallGroup.FeeList.FeeDataList, loadOptions);
        }

        [HttpPut]
        public IActionResult UpdateFeeDataList(string key, string values)
        {
            Fee aFee = m_InMemoryDataContextSmallGroup.FeeList.FeeDataList.First(a => a.StorLessonsId == key);

            // 更新後台資料庫
            //m_InMemoryDataContextSmallGroup.FeeList.UpdateEntity(key, values, aFee);

            // 更新前台顯示的網頁及更新後台資料庫
            m_InMemoryDataContextSmallGroup.FeeList.PopulateObjectAndUpdateEntity(values, aFee);

            return Ok();
        }

        #endregion
        #region 行事曆
        public ActionResult Scheduler()
        {
            //String SmallGroupDataList = (String)TempData.Peek("SmallGroupDataList");

            //SmallGroupDataList m_SmallGroupDataList;
            //if (SmallGroupDataList != null)
            //{
            //    TempData.Keep("SmallGroupDataList");
            //    m_SmallGroupDataList = JsonConvert.DeserializeObject<SmallGroupDataList>(SmallGroupDataList);
            //}
            //else
            //{
            //    return RedirectToAction("Login");
            //}

            ViewBag.HappyType = "有幸福小組名單";

            AppointmentsList aAppointmentsList = new AppointmentsList();

            ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;
            ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

            return View(aAppointmentsList);
        }
        #endregion
        #region 新增新人
        public IActionResult NewPerson()
        {
                ViewBag.LoginType = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_MemberInfomationPackage.m_LoginType;// 看是小組長還是個人回報
                ViewBag.LoginFullName = m_InMemoryDataContextSmallGroup.SmallGroupDataList.m_FullName;

                if (m_InMemoryDataContextSmallGroup.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    ViewBag.HappyType = "有幸福小組名單";
                }
                else
                {
                    ViewBag.HappyType = "沒幸福小組名單";
                }

                return View(m_InMemoryDataContextSmallGroup.NewPersonModel.m_PersonFormViewModel);
        }

        [HttpPost]
        public IActionResult SaveNewPerson(PersonFormViewModel aPersonFormViewModel)
        {
            if(aPersonFormViewModel.Phone == "" || aPersonFormViewModel.Phone == null)
            {
                return Json(new { status = "2", message = "新增新人必須要有行動電話" });
            }

            // 上傳至系統
            String Result = m_InMemoryDataContextSmallGroup.m_NewPersonModel.UploadNewPerson( m_InMemoryDataContextSmallGroup.SmallGroupDataList, aPersonFormViewModel );

            if (aPersonFormViewModel.Position == "0" || aPersonFormViewModel.Position == "1" || aPersonFormViewModel.Position == "2" || aPersonFormViewModel.Position == "3" || aPersonFormViewModel.Position == "4" || aPersonFormViewModel.Position == "5" || aPersonFormViewModel.Position == "6" || aPersonFormViewModel.Position == "7" || aPersonFormViewModel.Position == "8" || aPersonFormViewModel.Position == "9" || aPersonFormViewModel.Position == "10" )
            {
                int GroupIndex = Convert.ToInt32(aPersonFormViewModel.Position);

                // 幸福小組長上傳新人有可能沒有所屬小組可選
                if (m_InMemoryDataContextSmallGroup.m_SmallGroupDataList.m_AssignSmallGroupList.AssignSmallGroupListData.Count > 0)
                {
                    aPersonFormViewModel.Position = m_InMemoryDataContextSmallGroup.m_SmallGroupDataList.m_AssignSmallGroupList.AssignSmallGroupListData[Convert.ToInt32(aPersonFormViewModel.Position)].Name;
                }
            }

            m_InMemoryDataContextSmallGroup.SmallGroupDataList.AddNewPersonToSmallGroup(aPersonFormViewModel);


            if (Result.Contains("成功"))
            {
                return Json(new { status = "1", message = Result });
            }
            else
            {
                return Json(new { status = "2", message = Result });
            }
        }

        [HttpGet]
        public object AssignSmallGroupGet(DataSourceLoadOptions loadOptions)
        {
            return DataSourceLoader.Load(m_InMemoryDataContextSmallGroup.m_SmallGroupDataList.m_AssignSmallGroupList.AssignSmallGroupListData, loadOptions);
        }
        [HttpGet]
        public ActionResult AssignSmallGroupGetType(DataSourceLoadOptions loadOptions)
        {
            return Content(JsonConvert.SerializeObject(DataSourceLoader.Load(m_InMemoryDataContextSmallGroup.m_SmallGroupDataList.m_AssignSmallGroupList.AssignSmallGroupListData, loadOptions)), "application/json");
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

                //LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                //aLineMessagingProcessorClass.SendMessage(UserId, "001: " + UserId);

                if (aContact != null)
                {
                    //await SendMessage(UserId, "002");
                    //aLineMessagingProcessorClass.SendMessage(UserId, "002: " + UserId);

                    Entity aEntity = new Entity("letter");
                    m_ToolUtilityClass.SetEntityStringAttribute(ref aEntity, "subject", Message);
                    m_ToolUtilityClass.SetEntityLookUpAttribute(ref aEntity, "regardingobjectid", "contact", aContact.Id);
                    m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aEntity, "scheduledend", DateTime.Now);
                    m_ToolUtilityClass.SetEntityBoolAttribute(ref aEntity, "directioncode", false);

                    //aLineMessagingProcessorClass.SendMessage(UserId, "003: " + UserId);
                    //設定訊息種類為文字 
                    m_ToolUtilityClass.SetOptionSetAttribute(ref aEntity, "new_message_category", OptionSetValueOfMessageType);

                    //aLineMessagingProcessorClass.SendMessage(UserId, "004: " + UserId);
                    Entity Fromparty = new Entity("activityparty");

                    //aLineMessagingProcessorClass.SendMessage(UserId, "005: " + UserId);
                    Fromparty["partyid"] = new EntityReference("contact", aContact.Id);

                    //aLineMessagingProcessorClass.SendMessage(UserId, "006: " + UserId);
                    aEntity["from"] = new Entity[] { Fromparty };

                    //aLineMessagingProcessorClass.SendMessage(UserId, "007: " + UserId);
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
                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "台中思恩堂豐富教會 : 綁定錯誤 => " +  ErrorString);

                throw e;
            }
        }

        #endregion
    }
}
