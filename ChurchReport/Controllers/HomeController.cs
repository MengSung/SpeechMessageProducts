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
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;
using Microsoft.AspNetCore.Hosting;

namespace ChurchReport.Controllers
{
    public class HomeController : BaseChurchController
    {
        #region 建構式
        /// <summary>
        /// HomeController 建構函數 (使用 Dependency Injection)
        /// </summary>
        /// <param name="httpContextAccessor">HTTP 上下文存取器</param>
        /// <param name="memoryCache">記憶體快取</param>
        /// <param name="qpayService">金流服務</param>
        /// <param name="toolUtilityProvider">ToolUtility 提供者 (DI 注入)</param>
        /// <param name="connectionPool">CRM 連線池</param>
        /// <param name="inMemoryContext">記憶體資料上下文 (DI 注入)</param>
        public HomeController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment qpayService,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            IInMemoryDataContext inMemoryContext)
        : base(httpContextAccessor, memoryCache, qpayService, toolUtilityProvider, connectionPool, inMemoryContext)
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
            var env = HttpContext.RequestServices.GetService(typeof(IWebHostEnvironment)) as IWebHostEnvironment;
            using (var authController = new AuthenticationController(
                HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
                HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
                HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
                HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
                HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool,
                env))
            {
                return await authController.ProcessLogin(aGalleryViewModel);
            }
        }

        // 註：原本 /Home/LineIdLoginView 會 302 轉址到 /Authentication/LineIdLoginView，
        // 但這會讓 liff.login() 的 redirect_uri 變成 /Authentication/...，與 LIFF Endpoint(/home/...)
        // 不符而造成 LINE 400「invalid url」。現改由 AuthenticationController.LineIdLoginView 直接掛
        // /Home/ 路由就地渲染（網址維持 /home/...），故此處不再需要轉址方法。

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
        /// 向後相容: 繞過驗證直接導向首頁
        /// </summary>
        [Route("/Home/SkipAuth")]
        public IActionResult SkipAuth()
        {
            return RedirectToAction("Index", "Home");
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/DediationLineLoginView 重導向到 /Dedication/DedianLineLoginView
        /// </summary>
        [Route("/Home/DediationLineLoginView/{LineIdLoginViewPatameter}")]
        public IActionResult DediationLineLoginViewRedirect(string LineIdLoginViewPatameter)
        {
            return RedirectToAction("DediationLineLoginView", "Dedication", new { LineIdLoginViewPatameter });
        }

        /// <summary>
        /// 向後相容: 處理舊的 /Home/SaveUserLineId POST 請求
        /// </summary>
        [HttpPost]
        [Route("/Home/SaveUserLineId")]
        public async Task<IActionResult> SaveUserLineIdRedirect(string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            var env = HttpContext.RequestServices.GetService(typeof(IWebHostEnvironment)) as IWebHostEnvironment;
            using (var authController = new AuthenticationController(
                HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
                HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
                HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
                HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
                HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool,
                env))
            {
                return await authController.SaveUserLineId(UserLineId, GroupId, RoomId, ViewType);
            }
        }

        /// <summary>
        /// 向後相容: 處理舊的 /Home/SetupUserLineId POST 請求（奉獻用）
        /// </summary>
        [HttpPost]
        [Route("/Home/SetupUserLineId")]
        public async Task<IActionResult> SetupUserLineIdRedirect(string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            // ? 使用 using 確保 Controller 被正確釋放，避免記憶體洩漏
            using (var dedicationController = new DedicationController(
                HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
                HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
                HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
                HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
                HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool,
                HttpContext.RequestServices.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration)) as Microsoft.Extensions.Configuration.IConfiguration))
            {
                // ? 使用 await 調用非同步方法
                return await dedicationController.SetupUserLineId(UserLineId, GroupId, RoomId, ViewType);
            }
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/LineLiffView 重導向到 /Authentication/LineLiffView
        /// </summary>
        [Route("/Home/LineLiffView/{LineIdLoginViewPatameter?}")]
        public IActionResult LineLiffViewRedirect(string LineIdLoginViewPatameter)
        {
            return RedirectToAction("LineLiffView", "Authentication", new { LineIdLoginViewPatameter });
        }

        /// <summary>
        /// 向後相容: 處理舊的 /Home/ProcessLineBinding POST 請求
        /// </summary>
        [HttpPost]
        [Route("/Home/ProcessLineBinding")]
        public async Task<IActionResult> ProcessLineBindingRedirect(LineBindingViewModel model)
        {
            var env = HttpContext.RequestServices.GetService(typeof(IWebHostEnvironment)) as IWebHostEnvironment;
            using (var authController = new AuthenticationController(
                HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
                HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
                HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
                HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
                HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool,
                env))
            {
                return await authController.ProcessLineBinding(model);
            }
        }

        /// <summary>
        /// 向後相容: 處理舊的 /Home/SaveUserId POST 請求
        /// </summary>
        [HttpPost]
        [Route("/Home/SaveUserId")]
        public async Task<IActionResult> SaveUserIdRedirect(
            string UserLineId,
            string GroupId,
            string RoomId,
            string ViewType,
            string DisplayName = "",
            string PictureUrl = "",
            string StatusMessage = "")
        {
            var env = HttpContext.RequestServices.GetService(typeof(IWebHostEnvironment)) as IWebHostEnvironment;
            using (var authController = new AuthenticationController(
                HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
                HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
                HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
                HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
                HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool,
                env))
            {
                return await authController.SaveUserId(UserLineId, GroupId, RoomId, ViewType, DisplayName, PictureUrl, StatusMessage);
            }
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/PresentFeeListView 重導向到 /FeeManagement/LessonList
        /// </summary>
        [Route("/Home/PresentFeeListView")]
        [Route("/Home/PresentFeeListView/{DiscipleLessonsId}")]
        public IActionResult PresentFeeListViewRedirect(string DiscipleLessonsId = null)
        {
            // ? 統一重導向到 LessonList，由 LessonList 自動判斷顯示邏輯
            return RedirectToAction("LessonList", "FeeManagement");
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/FeeView 重導向到 /FeeManagement/Fee
        /// </summary>
        [Route("/Home/FeeView")]
        [Route("/Home/FeeView/{DiscipleLessonsId}")]
        public IActionResult FeeViewRedirect(string DiscipleLessonsId = null)
        {
            if (string.IsNullOrEmpty(DiscipleLessonsId))
            {
                return RedirectToAction("LessonList", "FeeManagement");
            }
            return RedirectToAction("Fee", "FeeManagement", new { discipleLessonsId = DiscipleLessonsId });
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/PresentView 重導向到 /FeeManagement/Present
        /// </summary>
        [Route("/Home/PresentView")]
        [Route("/Home/PresentView/{DiscipleLessonsId}")]
        public IActionResult PresentViewRedirect(string DiscipleLessonsId = null)
        {
            if (string.IsNullOrEmpty(DiscipleLessonsId))
            {
                return RedirectToAction("LessonList", "FeeManagement");
            }
            return RedirectToAction("Present", "FeeManagement", new { discipleLessonsId = DiscipleLessonsId });
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/LoadLessonList API 重導向到 /FeeManagement/Api/Lessons
        /// </summary>
        [HttpGet]
        [Route("/Home/LoadLessonList")]
        public IActionResult LoadLessonListRedirect([FromQuery] DataSourceLoadOptions loadOptions)
        {
            return RedirectToAction("GetLessons", "FeeManagement", loadOptions);
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/LoadFeeDataList API 重導向到 /FeeManagement/Api/FeeData
        /// </summary>
        [HttpGet]
        [Route("/Home/LoadFeeDataList")]
        public IActionResult LoadFeeDataListRedirect([FromQuery] DataSourceLoadOptions loadOptions, string DiscipleLessonsId = null)
        {
            return RedirectToAction("GetFeeData", "FeeManagement", new { discipleLessonsId = DiscipleLessonsId });
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/UpdateFeeDataList API 重導向到 /FeeManagement/Api/UpdateFeeData
        /// </summary>
        [HttpPut]
        [Route("/Home/UpdateFeeDataList")]
        public IActionResult UpdateFeeDataListRedirect(string key, string values)
        {
            return RedirectToAction("UpdateFeeData", "FeeManagement", new { key, values });
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/SaveFeeManager API 重導向到 /FeeManagement/Api/SaveBatch
        /// </summary>
        [HttpPost]
        [Route("/Home/SaveFeeManager")]
        public IActionResult SaveFeeManagerRedirect(string aResult)
        {
            return RedirectToAction("SaveBatch", "FeeManagement", new { aResult });
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/DedicationFeeView 重導向到 /Dedication/DedicationFeeView
        /// </summary>
        [Route("/Home/DedicationFeeView")]
        public IActionResult DedicationFeeViewRedirect()
        {
            return RedirectToAction("DedicationFeeView", "Dedication");
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/DedicationFeeViewWeb 重導向到 /Dedication/DedicationFeeViewWeb
        /// </summary>
        [Route("/Home/DedicationFeeViewWeb")]
        public IActionResult DedicationFeeViewWebRedirect()
        {
            return RedirectToAction("DedicationFeeViewWeb", "Dedication");
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/KeyInDedicationFeeView 重導向到 /Dedication/KeyInDedicationFeeView
        /// </summary>
        [Route("/Home/KeyInDedicationFeeView")]
        public IActionResult KeyInDedicationFeeViewRedirect()
        {
            return RedirectToAction("KeyInDedicationFeeView", "Dedication");
        }

        /// <summary>
        /// 向後相容: 將舊的 /Home/KeyInDedicationFeeViewWeb 重導向到 /Dedication/KeyInDedicationFeeViewWeb
        /// </summary>
        [Route("/Home/KeyInDedicationFeeViewWeb")]
        public IActionResult KeyInDedicationFeeViewWebRedirect()
        {
            return RedirectToAction("KeyInDedicationFeeViewWeb", "Dedication");
        }

        #endregion

        #region ? Phase 3.2: 快取效能測試端點

        /// <summary>
        /// 測試 ChurchListDataProcessor 的快取效能
        /// 訪問 URL: /Home/TestCachePerformance
        /// </summary>
        [Route("/Home/TestCachePerformance")]
        public IActionResult TestCachePerformance()
        {
            try
            {
                // 從 DI 取得帶快取的 ChurchListDataProcessor
                var cacheService = HttpContext.RequestServices.GetService(typeof(ToolUtility.Caching.CrmCacheService))
                    as ToolUtility.Caching.CrmCacheService;

                var processor = new ChurchListDataProcessor(cacheService);
                var monitor = new CachePerformanceMonitor();

                // 測試用的假設 ContactId（請替換為實際的測試帳號 ID）
                // 您可以從資料庫中選擇一個真實的 Contact ID
                var testContactId = GetTestContactId(); // 需要實作這個方法

                if (testContactId == Guid.Empty)
                {
                    return Content("請先設定測試用的 Contact ID", "text/plain");
                }

                string report = "快取效能測試報告\n";
                report += "==========================================\n\n";

                // 測試 1: QueryListByContactId
                monitor.StartFirstCall("QueryListByContactId");
                var result1 = TestQueryListByContactId(processor, testContactId);
                monitor.EndFirstCall();

                // 第二次呼叫（應該從快取取得）
                monitor.StartSecondCall();
                var result2 = TestQueryListByContactId(processor, testContactId);
                monitor.EndSecondCall();

                report += monitor.GetPerformanceReport();
                report += "\n\n";

                // 清除快取以進行下一個測試
                cacheService?.InvalidateAsync($"list_query_{testContactId}_vice_family_leader").Wait();

                return Content(report, "text/plain; charset=utf-8");
            }
            catch (Exception ex)
            {
                return Content($"測試發生錯誤: {ex.Message}\n\n{ex.StackTrace}", "text/plain; charset=utf-8");
            }
        }

        /// <summary>
        /// 取得測試用的 Contact ID
        /// 建議：從 Session 或設定檔取得，或使用固定的測試帳號
        /// </summary>
        private Guid GetTestContactId()
        {
            // 方法 1: 從 Session 取得目前登入的使用者
            var contactIdStr = HttpContext.Session.GetString("ContactID");
            if (!string.IsNullOrEmpty(contactIdStr) && Guid.TryParse(contactIdStr, out var contactId))
            {
                return contactId;
            }

            // 方法 2: 使用預設測試帳號（請替換為實際的測試帳號 GUID）
            // return new Guid("YOUR-TEST-CONTACT-ID-HERE");

            return Guid.Empty;
        }

        /// <summary>
        /// 測試查詢方法（模擬實際使用場景）
        /// </summary>
        private int TestQueryListByContactId(ChurchListDataProcessor processor, Guid contactId)
        {
            var churchRoot = new ChurchRoot();
            var raceLeaderArray = new List<string>();
            var areaLeaderArray = new List<string>();
            var raceLeaderSmallGroupArray = new List<string>();
            var churchSmallGroupArray = new List<string>();

            // 執行實際的查詢（這會觸發快取邏輯）
            var result = processor.GetChurchListData(
                contactId.ToString(),
                "LineIdLogin",  // 使用 LineId 登入模式
                ref churchRoot,
                ref raceLeaderArray,
                ref areaLeaderArray,
                ref raceLeaderSmallGroupArray,
                ref churchSmallGroupArray
            );

            return result?.AreaLeaderList?.Count ?? 0;
        }

        #endregion

        #region 資格設定

        /// <summary>
        /// 顯示資格設定頁面（信仰狀態、性別、生日、身分證字號）
        /// 路由: /Home/QualificationView/{lineIdLoginViewParameter}
        /// </summary>
        /// <param name="lineIdLoginViewParameter">LINE 使用者識別碼或編碼的 URL 參數</param>
        [HttpGet]
        [Route("/Home/QualificationView/{lineIdLoginViewParameter}")]
        public IActionResult QualificationView(string lineIdLoginViewParameter)
        {
            try
            {
                var viewModel = InMemoryContext.LineBindingViewModel;

                // ─── 設定輪播圖片清單（Gallery 跑馬燈必要）───
                viewModel.Images = new System.Collections.Generic.List<string>
                {
                    Url.Content("~/assets/images/church-001.jpg"),
                    Url.Content("~/assets/images/church-002.jpg")
                };

                // ─── 設定 LIFF ID（View 中 liff.init() 必要）───
                TempData["Proponent"] = lineIdLoginViewParameter;
                ViewBag.LiffId = lineIdLoginViewParameter; // 同步 ViewBag，確保 View 可靠讀取 LIFF ID

                // 使用參數查詢聯絡人資訊
                if (!string.IsNullOrEmpty(lineIdLoginViewParameter))
                {
                    viewModel.EncodeUrl = lineIdLoginViewParameter;

                    // CS0206: 屬性不能直接傳 ref，需要使用暫存區域變數
                    string faithStatus = viewModel.FaithStatus ?? string.Empty;
                    string genderCode = viewModel.GenderCode ?? string.Empty;
                    DateTime birthDate = viewModel.BirthDate;
                    string personalId = viewModel.PersonalId ?? string.Empty;

                    viewModel.GetContactInfomation(
                        lineIdLoginViewParameter,
                        ref faithStatus,
                        ref genderCode,
                        ref birthDate,
                        ref personalId
                    );

                    viewModel.FaithStatus = faithStatus;
                    viewModel.GenderCode = genderCode;
                    viewModel.BirthDate = birthDate;
                    viewModel.PersonalId = personalId;
                }

                return View(viewModel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HomeController.QualificationView] 錯誤: {ex.Message}");
                return View(InMemoryContext.LineBindingViewModel);
            }
        }

        /// <summary>
        /// 取得資格設定資料（信仰狀態、性別、生日、身分證字號），由 LIFF JavaScript 透過 AJAX 呼叫。
        /// 使用真實的 LINE UserId 查詢 CRM 聯絡人並將結果存入 Session 快取。
        /// </summary>
        /// <param name="UserLineId">LINE 用戶真實 UserId（由 liff.getProfile() 取得）</param>
        /// <param name="GroupId">LINE 群組 ID</param>
        /// <param name="RoomId">LINE 聊天室 ID</param>
        /// <param name="ViewType">開啟頁面方式</param>
        [HttpPost]
        [Route("/Home/GetQualificationData")]
        public IActionResult GetQualificationData(string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                var viewModel = InMemoryContext.LineBindingViewModel;

                string faithStatus = string.Empty;
                string genderCode = string.Empty;
                DateTime birthDate = default;
                string personalId = string.Empty;

                viewModel.GetContactInfomation(UserLineId, ref faithStatus, ref genderCode, ref birthDate, ref personalId);

                viewModel.FaithStatus = faithStatus;
                viewModel.GenderCode = genderCode;
                viewModel.BirthDate = birthDate;
                viewModel.PersonalId = personalId;
                viewModel.LineUserId = UserLineId;

                return Json(new
                {
                    faithStatus = viewModel.FaithStatus,
                    genderCode = viewModel.GenderCode,
                    birthDate = viewModel.BirthDate,
                    personalId = viewModel.PersonalId
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HomeController.GetQualificationData] 錯誤: {ex.Message}");
                return Json(new { error = ex.Message });
            }
        }

        /// <summary>
        /// 儲存資格設定資料（信仰狀態、性別、生日、身分證字號）
        /// </summary>
        /// <param name="aLineBindingViewModel">表單資料（包含 LineUserId）</param>
        [HttpPost]
        [Route("/Home/SaveQualificationData")]
        public IActionResult SaveQualificationData(LineBindingViewModel aLineBindingViewModel)
        {
            try
            {
                var viewModel = InMemoryContext.LineBindingViewModel;

                viewModel.FaithStatus = aLineBindingViewModel.FaithStatus;
                viewModel.GenderCode = aLineBindingViewModel.GenderCode;
                viewModel.BirthDate = aLineBindingViewModel.BirthDate;
                viewModel.PersonalId = aLineBindingViewModel.PersonalId;

                viewModel.UpdateContactInfomation(
                    aLineBindingViewModel.LineUserId,
                    viewModel.FaithStatus,
                    viewModel.GenderCode,
                    viewModel.BirthDate,
                    viewModel.PersonalId
                );

                return Json(new { status = "1", message = "資料儲存成功！" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HomeController.SaveQualificationData] 錯誤: {ex.Message}");
                return Json(new { status = "0", message = $"儲存失敗：{ex.Message}" });
            }
        }

        #endregion

        #region QPay 登入

        /// <summary>
        /// 顯示 QPay 登入頁面
        /// </summary>
        [HttpGet]
        [Route("/Home/QPayLogin")]
        public IActionResult QPayLogin()
        {
            return RedirectToAction("Index", "QPayLogin");
        }

        /// <summary>
        /// 處理 QPay 登入表單提交
        /// </summary>
        [HttpPost]
        [Route("/Home/ProcessQPayLogin")]
        public IActionResult ProcessQPayLogin(GalleryViewModel model)
        {
            using (var controller = new QPayLoginController(
                HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
                HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
                HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
                HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
                HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool))
            {
                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = this.HttpContext
                };

                return controller.ProcessQPayLogin(model);
            }
        }

        #endregion

        #region VisitorCard 來賓登記

        /// <summary>
        /// 顯示來賓登記頁面 (VisitorCard)
        /// 支援 /Home/VisitorCard 及 /Home/VisitorCard/{liffId} 兩條路由
        /// </summary>
        [HttpGet]
        [Route("/Home/VisitorCard")]
        [Route("/Home/VisitorCard/{liffId}")]
        public IActionResult VisitorCard(string? liffId = null)
        {
            try
            {
                SetupBasicViewBag();
                ViewBag.LiffId = liffId ?? string.Empty;

                // 設定 Gallery 圖片清單（與 LineLiffView 相同）
                var model = new LineBindingViewModel
                {
                    Images = new List<string>
                    {
                        Url.Content("~/assets/images/church-001.jpg"),
                        Url.Content("~/assets/images/church-002.jpg"),
                        Url.Content("~/assets/images/church-003.jpg")
                    }
                };

                return View(model);
            }
            catch (Exception e)
            {
                return HandleError(e, "VisitorCard");
            }
        }

        /// <summary>
        /// 處理來賓卡表單送出
        /// 將來賓資料儲存至 CRM 資料庫
        /// 回傳 status=1 成功，status=0 失敗
        /// </summary>
        [HttpPost]
        [Route("/Home/ProcessVisitorCard")]
        public async Task<IActionResult> ProcessVisitorCard(LineBindingViewModel model)
        {
            try
            {
                var env = HttpContext.RequestServices.GetService(typeof(IWebHostEnvironment)) as IWebHostEnvironment;
                using (var authController = new AuthenticationController(
                    HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
                    HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
                    HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
                    HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
                    HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool,
                    env))
                {
                    authController.ControllerContext = new ControllerContext
                    {
                        HttpContext = this.HttpContext
                    };
                    return await authController.ProcessLineBinding(model);
                }
            }
            catch (Exception e)
            {
                return HandleError(e, "ProcessVisitorCard");
            }
        }

        #endregion
    }
}
