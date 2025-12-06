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
        public HomeController(
            IHttpContextAccessor httpContextAccessor, 
            IMemoryCache memoryCache, 
            IPayment qpayService,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
        : base(httpContextAccessor, memoryCache, qpayService, toolUtilityProvider, connectionPool)
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
            // ? 使用 using 確保 Controller 被正確釋放，避免記憶體洩漏
            using (var authController = new AuthenticationController(
                HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
                HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
                HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
                HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
                HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool))
            {
                return await authController.ProcessLogin(aGalleryViewModel);
            }
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
            // ? 使用 using 確保 Controller 被正確釋放，避免記憶體洩漏
            using (var authController = new AuthenticationController(
                HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
                HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
                HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
                HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
                HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool))
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
                HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool))
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
            // ? 使用 using 確保 Controller 被正確釋放，避免記憶體洩漏
            using (var authController = new AuthenticationController(
                HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
                HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
                HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
                HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
                HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool))
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
            // ? 使用 using 確保 Controller 被正確釋放，避免記憶體洩漏
            using (var authController = new AuthenticationController(
                HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
                HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
                HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
                HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
                HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool))
            {
                return await authController.SaveUserId(UserLineId, GroupId, RoomId, ViewType, DisplayName, PictureUrl, StatusMessage);
            }
        }
        
        #endregion

        #region 課程繳費點名視圖

        /// <summary>
        /// 顯示課程繳費點名清單視圖
        /// 路徑: /Home/PresentFeeListView
        /// </summary>
        /// <param name="DiscipleLessonsId">課程ID (選填)</param>
        [Route("/Home/PresentFeeListView")]
        [Route("/Home/PresentFeeListView/{DiscipleLessonsId}")]
        public IActionResult PresentFeeListView(string DiscipleLessonsId = null)
        {
            try
            {
                // 設定需要點名的課程清單
                if (!string.IsNullOrEmpty(DiscipleLessonsId))
                {
                    InMemoryContext.FeeList.SetupPresentFeeList(DiscipleLessonsId);
                }
                else
                {
                    // 如果沒有指定課程ID，使用當前登入者的帳密設定繳費清單
                    InMemoryContext.FeeList.SetupFeeDataList(
                        InMemoryContext.FeeList.m_Account, 
                        InMemoryContext.FeeList.m_Password
                    );
                }

                // 設定 ViewBag 參數
                ViewBag.Result = InMemoryContext.FeeList.Result;
                ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
                
                // ? 修復：將整數轉換為字串，以符合 _Layout.cshtml 中的字串比較
                var feeDataCount = InMemoryContext.FeeList.FeeDataList?.Count ?? 0;
                ViewBag.FeeDataListCount = feeDataCount > 0 ? "繳費與點名已有資料" : "繳費與點名無資料";

                return View(InMemoryContext.FeeList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PresentFeeListView] 發生錯誤: {ex.Message}");
                return HandleError(ex, "PresentFeeListView");
            }
        }

        /// <summary>
        /// 顯示課程繳費視圖 (支援特定課程ID)
        /// 路徑: /Home/FeeView/{DiscipleLessonsId}
        /// </summary>
        /// <param name="DiscipleLessonsId">課程ID</param>
        [Route("/Home/FeeView")]
        [Route("/Home/FeeView/{DiscipleLessonsId}")]
        public IActionResult FeeView(string DiscipleLessonsId = null)
        {
            try
            {
                // 如果有指定課程ID，載入該課程的繳費資料
                if (!string.IsNullOrEmpty(DiscipleLessonsId))
                {
                    InMemoryContext.FeeList.SetupPresentFeeList(DiscipleLessonsId);
                }
                else
                {
                    // 否則使用當前登入者的帳密設定繳費清單
                    InMemoryContext.FeeList.SetupFeeDataList(
                        InMemoryContext.FeeList.m_Account,
                        InMemoryContext.FeeList.m_Password
                    );
                }

                // 設定 ViewBag 參數
                ViewBag.FeeResult = InMemoryContext.FeeList.Result;
                ViewBag.DiscipleLessonsId = DiscipleLessonsId;

                return View(InMemoryContext.FeeList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FeeView] 發生錯誤: {ex.Message}");
                return HandleError(ex, "FeeView");
            }
        }

        /// <summary>
        /// 顯示課程點名視圖
        /// 路徑: /Home/PresentView/{DiscipleLessonsId}
        /// </summary>
        /// <param name="DiscipleLessonsId">課程ID</param>
        [Route("/Home/PresentView")]
        [Route("/Home/PresentView/{DiscipleLessonsId}")]
        public IActionResult PresentView(string DiscipleLessonsId = null)
        {
            try
            {
                // 如果有指定課程ID，載入該課程的繳費資料
                if (!string.IsNullOrEmpty(DiscipleLessonsId))
                {
                    InMemoryContext.FeeList.SetupPresentFeeList(DiscipleLessonsId);
                }
                else
                {
                    // 否則使用當前登入者的帳密設定繳費清單
                    InMemoryContext.FeeList.SetupFeeDataList(
                        InMemoryContext.FeeList.m_Account,
                        InMemoryContext.FeeList.m_Password
                    );
                }

                // 設定 ViewBag 參數
                ViewBag.PresentResult = InMemoryContext.FeeList.Result;
                ViewBag.DiscipleLessonsId = DiscipleLessonsId;

                return View(InMemoryContext.FeeList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PresentView] 發生錯誤: {ex.Message}");
                return HandleError(ex, "PresentView");
            }
        }

        #endregion

        #region 課程繳費點名 API

        /// <summary>
        /// 載入課程清單 (DevExtreme DataGrid API)
        /// 路徑: /Home/LoadLessonList
        /// </summary>
        [HttpGet]
        [Route("/Home/LoadLessonList")]
        public IActionResult LoadLessonList(DataSourceLoadOptions loadOptions)
        {
            try
            {
                // 確保課程清單已載入
                if (InMemoryContext.FeeList.LessonList == null || InMemoryContext.FeeList.LessonList.Count == 0)
                {
                    // 重新載入課程清單
                    InMemoryContext.FeeList.SetupLessonList(
                        InMemoryContext.FeeList.m_Account,
                        InMemoryContext.FeeList.m_Password
                    );
                }

                // 使用 DevExtreme DataSourceLoader 處理資料
                var result = DataSourceLoader.Load(InMemoryContext.FeeList.LessonList, loadOptions);

                return Json(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadLessonList] 發生錯誤: {ex.Message}");
                
                // 返回空結果而不是錯誤，避免前端顯示異常
                return Json(new
                {
                    data = new List<Lesson>(),
                    totalCount = 0
                });
            }
        }

        /// <summary>
        /// 載入繳費資料清單 (DevExtreme DataGrid API)
        /// 路徑: /Home/LoadFeeDataList
        /// </summary>
        [HttpGet]
        [Route("/Home/LoadFeeDataList")]
        public IActionResult LoadFeeDataList(DataSourceLoadOptions loadOptions, string DiscipleLessonsId = null)
        {
            try
            {
                // 如果有指定課程ID，載入該課程的繳費資料
                if (!string.IsNullOrEmpty(DiscipleLessonsId))
                {
                    InMemoryContext.FeeList.SetupPresentFeeList(DiscipleLessonsId);
                }
                else if (InMemoryContext.FeeList.FeeDataList == null || InMemoryContext.FeeList.FeeDataList.Count == 0)
                {
                    // 否則重新載入繳費清單
                    InMemoryContext.FeeList.SetupFeeDataList(
                        InMemoryContext.FeeList.m_Account,
                        InMemoryContext.FeeList.m_Password
                    );
                }

                // 使用 DevExtreme DataSourceLoader 處理資料
                var result = DataSourceLoader.Load(InMemoryContext.FeeList.FeeDataList, loadOptions);

                return Json(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadFeeDataList] 發生錯誤: {ex.Message}");
                
                // 返回空結果
                return Json(new
                {
                    data = new List<Fee>(),
                    totalCount = 0
                });
            }
        }

        /// <summary>
        /// 更新繳費資料 (DevExtreme DataGrid API)
        /// 路徑: /Home/UpdateFeeDataList
        /// </summary>
        [HttpPut]
        [Route("/Home/UpdateFeeDataList")]
        public IActionResult UpdateFeeDataList(string key, string values)
        {
            try
            {
                // 找到要更新的 Fee 記錄
                var fee = InMemoryContext.FeeList.FeeDataList?.FirstOrDefault(f => f.StorLessonsId == key);
                
                if (fee == null)
                {
                    return BadRequest("找不到指定的繳費記錄");
                }

                // 使用 FeeList 的 PopulateObjectAndUpdateEntity 方法更新實體
                InMemoryContext.FeeList.PopulateObjectAndUpdateEntity(values, fee);

                return Ok();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateFeeDataList] 發生錯誤: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// 儲存繳費管理資料
        /// 路徑: /Home/SaveFeeManager
        /// </summary>
        [HttpPost]
        [Route("/Home/SaveFeeManager")]
        public IActionResult SaveFeeManager(string aResult)
        {
            try
            {
                // 記錄儲存操作
                System.Diagnostics.Debug.WriteLine($"[SaveFeeManager] 儲存繳費資料: {aResult}");

                // 這裡可以添加額外的業務邏輯，例如發送通知或記錄日誌
                // 目前 UpdateFeeDataList 已經處理了實際的資料更新

                return Json(new
                {
                    status = "success",
                    message = "繳費資料已成功儲存"
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveFeeManager] 發生錯誤: {ex.Message}");
                return Json(new
                {
                    status = "error",
                    message = ex.Message
                });
            }
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
    }
}
