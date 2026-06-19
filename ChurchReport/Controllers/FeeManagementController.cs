using ChurchReport.Diagnostics.Profiling;
using ChurchReport.Models;
using ChurchReport.Tools;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 課程繳費點名管理 Controller
    /// 負責處理課程清單、繳費、點名等相關功能
    /// </summary>
    public class FeeManagementController : BaseChurchController
    {
        #region 建構式

        /// <summary>
        /// FeeManagementController 建構函數 (使用 Dependency Injection)
        /// </summary>
        /// <param name="httpContextAccessor">HTTP 上下文存取器</param>
        /// <param name="memoryCache">記憶體快取</param>
        /// <param name="qpayService">金流服務</param>
        /// <param name="toolUtilityProvider">ToolUtility 提供者 (DI 注入)</param>
        /// <param name="connectionPool">CRM 連線池</param>
        public FeeManagementController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment qpayService,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
        : base(httpContextAccessor, memoryCache, qpayService, toolUtilityProvider, connectionPool)
        {
        }

        #endregion

        #region 視圖方法

        /// <summary>
        /// 顯示課程清單視圖
        /// 路徑: /FeeManagement/LessonList
        /// </summary>
        [Route("/FeeManagement/LessonList")]
        public IActionResult LessonList()
        {
            try
            {
                // 使用當前登入者的帳密設定課程清單
                EnsureLessonListLoaded("FeeManagement.LessonList.SetupLessonList");

                // 設定所有必要的 ViewBag 參數
                using (PerfPhase.Measure(HttpContext, "FeeManagement.LessonList.SetupBasicViewBag"))
                {
                    SetupBasicViewBag();
                }
                using (PerfPhase.Measure(HttpContext, "FeeManagement.LessonList.SetMultiGroupLayoutParameter"))
                {
                    SetMultiGroupLayoutParameter();
                }

                // 設定 ViewBag 參數
                ViewBag.Result = InMemoryContext.FeeList.Result;
                ViewBag.DisplayNavigation = "顯示牧養回報項目";

                var lessonCount = InMemoryContext.FeeList.LessonList?.Count ?? 0;
                
                System.Diagnostics.Debug.WriteLine($"[LessonList] 課程數量: {lessonCount}");

                // ? 智能判斷：根據課程數量決定顯示邏輯
                if (lessonCount == 1)
                {
                    // 只有一門課程：自動載入該課程資料，並顯示「點名」、「繳費」按鈕
                    var singleLesson = InMemoryContext.FeeList.LessonList[0];
                    var discipleLessonsId = singleLesson.DiscipleLessonsId;
                    
                    System.Diagnostics.Debug.WriteLine($"[LessonList] 只有一門課程，自動載入 - DiscipleLessonsId={discipleLessonsId}");
                    
                    // 載入該課程的繳費資料
                    EnsurePresentFeeListLoaded(discipleLessonsId, "FeeManagement.LessonList.SetupPresentFeeList");
                    
                    // 設定 DiscipleLessonsId，讓側邊欄能正確生成連結
                    ViewBag.DiscipleLessonsId = discipleLessonsId;
                    
                    // 設定為「已有資料」，讓「繳費」和「點名」選單顯示
                    var feeDataCount = InMemoryContext.FeeList.FeeDataList?.Count ?? 0;
                    ViewBag.FeeDataListCount = feeDataCount > 0 ? "繳費與點名已有資料" : "繳費與點名無資料";
                    
                    System.Diagnostics.Debug.WriteLine($"[LessonList] 單一課程模式 - 學員數={feeDataCount}, FeeDataListCount={ViewBag.FeeDataListCount}");
                }
                else if (lessonCount > 1)
                {
                    // 多門課程：只顯示「課程清單」按鈕，隱藏「點名」、「繳費」按鈕
                    ViewBag.FeeDataListCount = "繳費與點名無資料";
                    ViewBag.DiscipleLessonsId = null;
                    
                    System.Diagnostics.Debug.WriteLine($"[LessonList] 多課程模式 - 只顯示課程清單按鈕");
                }
                else
                {
                    // 沒有課程
                    ViewBag.FeeDataListCount = "繳費與點名無資料";
                    ViewBag.DiscipleLessonsId = null;
                    
                    System.Diagnostics.Debug.WriteLine($"[LessonList] 無課程");
                }

                return View(InMemoryContext.FeeList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LessonList] 發生錯誤: {ex.Message}");
                return HandleError(ex, "LessonList");
            }
        }

        /// <summary>
        /// 顯示課程繳費視圖
        /// 路徑: /FeeManagement/Fee/{discipleLessonsId}
        /// </summary>
        /// <param name="discipleLessonsId">課程ID</param>
        [Route("/FeeManagement/Fee/{discipleLessonsId}")]
        public IActionResult Fee(string discipleLessonsId)
        {
            try
            {
                if (string.IsNullOrEmpty(discipleLessonsId))
                {
                    System.Diagnostics.Debug.WriteLine("[Fee] 課程ID為空，重導向到課程清單");
                    return RedirectToAction("LessonList");
                }

                // 載入該課程的繳費資料
                EnsurePresentFeeListLoaded(discipleLessonsId, "FeeManagement.Fee.SetupPresentFeeList");

                // 設定所有必要的 ViewBag 參數
                using (PerfPhase.Measure(HttpContext, "FeeManagement.Fee.SetupBasicViewBag"))
                {
                    SetupBasicViewBag();
                }
                using (PerfPhase.Measure(HttpContext, "FeeManagement.Fee.SetMultiGroupLayoutParameter"))
                {
                    SetMultiGroupLayoutParameter();
                }

                // 設定 ViewBag 參數
                ViewBag.FeeResult = InMemoryContext.FeeList.Result;
                ViewBag.DiscipleLessonsId = discipleLessonsId;

                // 設定為「已有資料」，讓「繳費」和「點名」選單顯示
                var feeDataCount = InMemoryContext.FeeList.FeeDataList?.Count ?? 0;
                ViewBag.FeeDataListCount = feeDataCount > 0 ? "繳費與點名已有資料" : "繳費與點名無資料";
                ViewBag.DisplayNavigation = "顯示牧養回報項目";

                System.Diagnostics.Debug.WriteLine($"[Fee] 課程繳費載入完成 - DiscipleLessonsId={discipleLessonsId}, 學員數={feeDataCount}");

                return View(InMemoryContext.FeeList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Fee] 發生錯誤: {ex.Message}");
                return HandleError(ex, "Fee");
            }
        }

        /// <summary>
        /// 顯示課程點名視圖
        /// 路徑: /FeeManagement/Present/{discipleLessonsId}
        /// </summary>
        /// <param name="discipleLessonsId">課程ID</param>
        [Route("/FeeManagement/Present/{discipleLessonsId}")]
        public IActionResult Present(string discipleLessonsId)
        {
            try
            {
                if (string.IsNullOrEmpty(discipleLessonsId))
                {
                    System.Diagnostics.Debug.WriteLine("[Present] 課程ID為空，重導向到課程清單");
                    return RedirectToAction("LessonList");
                }

                // 載入該課程的點名資料
                EnsurePresentFeeListLoaded(discipleLessonsId, "FeeManagement.Present.SetupPresentFeeList");

                // 設定所有必要的 ViewBag 參數
                using (PerfPhase.Measure(HttpContext, "FeeManagement.Present.SetupBasicViewBag"))
                {
                    SetupBasicViewBag();
                }
                using (PerfPhase.Measure(HttpContext, "FeeManagement.Present.SetMultiGroupLayoutParameter"))
                {
                    SetMultiGroupLayoutParameter();
                }

                // 設定 ViewBag 參數
                ViewBag.PresentResult = InMemoryContext.FeeList.Result;
                ViewBag.DiscipleLessonsId = discipleLessonsId;

                // 設定為「已有資料」，讓「繳費」和「點名」選單顯示
                var feeDataCount = InMemoryContext.FeeList.FeeDataList?.Count ?? 0;
                ViewBag.FeeDataListCount = feeDataCount > 0 ? "繳費與點名已有資料" : "繳費與點名無資料";
                ViewBag.DisplayNavigation = "顯示牧養回報項目";

                // ? 設定欄位標題參數（用於 onCustomizeColumns 函數）
                //InitializeColumnHeaders();
                SetFeeManagerViewBag();

                System.Diagnostics.Debug.WriteLine($"[Present] 課程點名載入完成 - DiscipleLessonsId={discipleLessonsId}, 學員數={feeDataCount}");

                return View(InMemoryContext.FeeList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Present] 發生錯誤: {ex.Message}");
                return HandleError(ex, "Present");
            }
        }

        #endregion

        #region API 方法

        /// <summary>
        /// 載入課程清單 (DevExtreme DataGrid API)
        /// 路徑: /FeeManagement/Api/Lessons
        /// </summary>
        [HttpGet]
        [Route("/FeeManagement/Api/Lessons")]
        public IActionResult GetLessons(DataSourceLoadOptions loadOptions)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[GetLessons] 開始載入課程清單");

                // 確保課程清單已載入
                EnsureLessonListLoaded("FeeManagement.GetLessons.SetupLessonList");

                // 使用 DevExtreme DataSourceLoader 處理資料
                DevExtreme.AspNet.Data.ResponseModel.LoadResult result;
                using (PerfPhase.Measure(HttpContext, "FeeManagement.GetLessons.DataSourceLoader"))
                {
                    result = DataSourceLoader.Load(InMemoryContext.FeeList.LessonList, loadOptions);
                }

                System.Diagnostics.Debug.WriteLine($"[GetLessons] 載入完成 - totalCount={result.totalCount}");

                return Json(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetLessons] 發生錯誤: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[GetLessons] 錯誤堆疊: {ex.StackTrace}");

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
        /// 路徑: /FeeManagement/Api/FeeData
        /// </summary>
        [HttpGet]
        [Route("/FeeManagement/Api/FeeData")]
        public IActionResult GetFeeData(DataSourceLoadOptions loadOptions, string discipleLessonsId = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[GetFeeData] 開始載入 - discipleLessonsId={discipleLessonsId}");

                // 如果有指定課程ID，載入該課程的繳費資料
                // Security: re-scope to current session login before serving or loading any FeeData,
                // so a previous user's cached FeeDataList cannot be served after a same-session re-login.
                var (account, password) = CurrentLogin();
                InMemoryContext.FeeList.EnsureLoginScope(account, password);

                if (!string.IsNullOrEmpty(discipleLessonsId))
                {
                    System.Diagnostics.Debug.WriteLine($"[GetFeeData] 呼叫 EnsurePresentFeeListLoaded({discipleLessonsId})");
                    EnsurePresentFeeListLoaded(discipleLessonsId, "FeeManagement.GetFeeData.SetupPresentFeeList");
                }
                else if (InMemoryContext.FeeList.FeeDataList == null || InMemoryContext.FeeList.FeeDataList.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[GetFeeData] FeeDataList 為空，重新載入");
                    using (PerfPhase.Measure(HttpContext, "FeeManagement.GetFeeData.SetupFeeDataList"))
                    {
                        InMemoryContext.FeeList.SetupFeeDataList(account, password);
                    }
                }

                var feeDataCount = InMemoryContext.FeeList.FeeDataList?.Count ?? 0;
                System.Diagnostics.Debug.WriteLine($"[GetFeeData] FeeDataList.Count={feeDataCount}");

                // 使用 DevExtreme DataSourceLoader 處理資料
                DevExtreme.AspNet.Data.ResponseModel.LoadResult result;
                using (PerfPhase.Measure(HttpContext, "FeeManagement.GetFeeData.DataSourceLoader"))
                {
                    result = DataSourceLoader.Load(InMemoryContext.FeeList.FeeDataList, loadOptions);
                }

                System.Diagnostics.Debug.WriteLine($"[GetFeeData] 回傳結果 - totalCount={result.totalCount}, data count={((IEnumerable<object>)result.data).Count()}");

                return Json(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetFeeData] 發生錯誤: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[GetFeeData] 錯誤堆疊: {ex.StackTrace}");

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
        /// 路徑: /FeeManagement/Api/UpdateFeeData
        /// 注意: 此方法只更新記憶體中的資料，不會立即提交到資料庫
        /// 需要按下「上傳」按鈕觸發 SaveBatch 才會批次提交
        /// </summary>
        [HttpPut]
        [Route("/FeeManagement/Api/UpdateFeeData")]
        public IActionResult UpdateFeeData(string key, string values)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateFeeData] 開始更新 - key={key}");

                // 找到要更新的 Fee 記錄
                // Security: re-scope to current session login before reading/mutating cached FeeData,
                // so a previous user's stale FeeDataList cannot be looked up or edited after a re-login.
                var (account, password) = CurrentLogin();
                InMemoryContext.FeeList.EnsureLoginScope(account, password);

                var fee = InMemoryContext.FeeList.FeeDataList?.FirstOrDefault(f => f.StorLessonsId == key);

                if (fee == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateFeeData] 找不到記錄 - key={key}");
                    return BadRequest("找不到指定的繳費記錄");
                }

                // ? 使用 FeeList 的 PopulateObjectAndUpdateEntity 方法
                // 此方法會更新記憶體中的 Fee 物件，並記錄修改歷程，但不會立即提交到資料庫
                InMemoryContext.FeeList.PopulateObjectAndUpdateEntity(values, fee);

                System.Diagnostics.Debug.WriteLine(
                    $"[UpdateFeeData] 修改已記錄 - key={key}, " +
                    $"待處理修改數: {InMemoryContext.FeeList.ChangeHistory.GetPendingCount()}");

                return Ok();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateFeeData] 發生錯誤: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[UpdateFeeData] 錯誤堆疊: {ex.StackTrace}");

                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// 批次儲存繳費管理資料
        /// 路徑: /FeeManagement/Api/SaveBatch
        /// </summary>
        [HttpPost]
        [Route("/FeeManagement/Api/SaveBatch")]
        public IActionResult SaveBatch(string aResult)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[SaveBatch] 開始批次儲存");

                // ? 執行批次提交所有待處理的修改
                // Security: re-scope to current session login; on a re-login this clears the previous
                // user's pending ChangeHistory so SaveBatch cannot commit their edits to CRM.
                var (account, password) = CurrentLogin();
                InMemoryContext.FeeList.EnsureLoginScope(account, password);

                int successCount;
                using (PerfPhase.Measure(HttpContext, "FeeManagement.SaveBatch.CommitPendingChanges"))
                {
                    successCount = InMemoryContext.FeeList.CommitPendingChanges();
                }

                System.Diagnostics.Debug.WriteLine($"[SaveBatch] 批次儲存完成 - 成功更新 {successCount} 筆記錄");

                return Json(new
                {
                    status = "success",
                    message = $"繳費資料已成功儲存 ({successCount} 筆記錄已更新)",
                    count = successCount
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveBatch] 發生錯誤: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[SaveBatch] 錯誤堆疊: {ex.StackTrace}");

                return Json(new
                {
                    status = "error",
                    message = ex.Message
                });
            }
        }

        #endregion

        #region 私有輔助方法

        // Authoritative current-session login identity. Source = session keys set at login time
        // (_LoginAccount / _LoginPassword; LINE login = "LineIdLogin" / lineUserId).
        // Do NOT use FeeList.m_Account (self-comparison would make login-change detection vacuous).
        private (string account, string password) CurrentLogin()
            => (HttpContext?.Session?.GetString("_LoginAccount") ?? "",
                HttpContext?.Session?.GetString("_LoginPassword") ?? "");

        private void EnsureLessonListLoaded(string phaseName)
        {
            var (account, password) = CurrentLogin();

            // Security: re-scope to current login; clears the previous user's data on account switch.
            InMemoryContext.FeeList.EnsureLoginScope(account, password);

            if (InMemoryContext.FeeList.IsLessonListLoadedFor(account, password))
            {
                System.Diagnostics.Debug.WriteLine("[FeeManagement] Reuse LessonList for current login.");
                return;
            }

            using (PerfPhase.Measure(HttpContext, phaseName))
            {
                InMemoryContext.FeeList.SetupLessonList(account, password);
            }
        }

        private void EnsurePresentFeeListLoaded(string discipleLessonsId, string phaseName)
        {
            var (account, password) = CurrentLogin();

            // Security: re-scope first. On account switch EnsureLoginScope clears FeeDataList, so the
            // IsPresentFeeListLoadedFor reuse check below cannot match the previous user's data.
            InMemoryContext.FeeList.EnsureLoginScope(account, password);

            if (InMemoryContext.FeeList.IsPresentFeeListLoadedFor(discipleLessonsId))
            {
                System.Diagnostics.Debug.WriteLine($"[FeeManagement] Reuse PresentFeeList for discipleLessonsId={discipleLessonsId}");
                return;
            }

            using (PerfPhase.Measure(HttpContext, phaseName))
            {
                InMemoryContext.FeeList.SetupPresentFeeList(discipleLessonsId);
            }
        }

        /// <summary>
        /// 初始化欄位標題參數（用於點名視圖的 onCustomizeColumns 函數）
        /// </summary>
        private void InitializeColumnHeaders()
        {
            // 初始化所有欄位標題為空字串，避免 JavaScript 錯誤
            ViewBag.Colume9 = "";
            ViewBag.Colume10 = "";
            ViewBag.Colume11 = "";
            ViewBag.Colume12 = "";
            ViewBag.Colume13 = "";
            ViewBag.Colume14 = "";
            ViewBag.Colume15 = "";
            ViewBag.Colume16 = "";
            ViewBag.Colume17 = "";
            ViewBag.Colume18 = "";
            ViewBag.Colume19 = "";
            ViewBag.Colume20 = "";
            ViewBag.Colume21 = "";
            ViewBag.Colume22 = "";
            ViewBag.Colume23 = "";
            ViewBag.Colume24 = "";
            ViewBag.Colume25 = "";
            ViewBag.Colume26 = "";
            ViewBag.Colume27 = "";
            ViewBag.Colume28 = "";
            ViewBag.Colume29 = "";
            ViewBag.Colume30 = "";
            ViewBag.Colume31 = "";
            ViewBag.Colume32 = "";
            ViewBag.Colume33 = "";
            ViewBag.Colume34 = "";
            ViewBag.Colume35 = "";
            ViewBag.Colume36 = "";
            ViewBag.Colume37 = "";
            ViewBag.Colume38 = "";
            ViewBag.Colume39 = "";
            ViewBag.Colume40 = "";
            ViewBag.Colume41 = "";
            ViewBag.Colume42 = "";
            ViewBag.Colume43 = "";
        }
        public void SetFeeManagerViewBag()
        {
            try
            {
                // 確保 m_ClassName 已初始化
                if (InMemoryContext?.FeeList?.m_ClassName == null)
                {
                    System.Diagnostics.Debug.WriteLine("[SetFeeManagerViewBag] m_ClassName 未初始化，使用預設值");

                    // 如果 m_ClassName 為 null，建立新的 ClassName 實例（會使用預設值）
                    if (InMemoryContext?.FeeList != null)
                    {
                        InMemoryContext.FeeList.m_ClassName = new ClassName();
                    }
                    else
                    {
                        // 如果連 FeeList 都沒有，初始化所有欄位為預設值
                        InitializeColumnHeaders();
                        return;
                    }
                }

                ViewBag.Colume9 = InMemoryContext.FeeList.m_ClassName.Lesson1;
                ViewBag.Colume10 = InMemoryContext.FeeList.m_ClassName.Lesson2;
                ViewBag.Colume11 = InMemoryContext.FeeList.m_ClassName.Lesson3;
                ViewBag.Colume12 = InMemoryContext.FeeList.m_ClassName.Lesson4;
                ViewBag.Colume13 = InMemoryContext.FeeList.m_ClassName.Lesson5;
                ViewBag.Colume14 = InMemoryContext.FeeList.m_ClassName.Lesson6;
                ViewBag.Colume15 = InMemoryContext.FeeList.m_ClassName.Lesson7;
                ViewBag.Colume16 = InMemoryContext.FeeList.m_ClassName.Lesson8;
                ViewBag.Colume17 = InMemoryContext.FeeList.m_ClassName.Lesson9;
                ViewBag.Colume18 = InMemoryContext.FeeList.m_ClassName.Lesson10;
                ViewBag.Colume19 = InMemoryContext.FeeList.m_ClassName.Lesson11;
                ViewBag.Colume20 = InMemoryContext.FeeList.m_ClassName.Lesson12;
                ViewBag.Colume21 = InMemoryContext.FeeList.m_ClassName.Lesson13;
                ViewBag.Colume22 = InMemoryContext.FeeList.m_ClassName.Lesson14;
                ViewBag.Colume23 = InMemoryContext.FeeList.m_ClassName.Lesson15;

                ViewBag.Colume24 = InMemoryContext.FeeList.m_ClassName.Lesson16;
                ViewBag.Colume25 = InMemoryContext.FeeList.m_ClassName.Lesson17;
                ViewBag.Colume26 = InMemoryContext.FeeList.m_ClassName.Lesson18;
                ViewBag.Colume27 = InMemoryContext.FeeList.m_ClassName.Lesson19;
                ViewBag.Colume28 = InMemoryContext.FeeList.m_ClassName.Lesson20;
                ViewBag.Colume29 = InMemoryContext.FeeList.m_ClassName.Lesson21;
                ViewBag.Colume30 = InMemoryContext.FeeList.m_ClassName.Lesson22;
                ViewBag.Colume31 = InMemoryContext.FeeList.m_ClassName.Lesson23;
                ViewBag.Colume32 = InMemoryContext.FeeList.m_ClassName.Lesson24;
                ViewBag.Colume33 = InMemoryContext.FeeList.m_ClassName.Lesson25;
                ViewBag.Colume34 = InMemoryContext.FeeList.m_ClassName.Lesson26;
                ViewBag.Colume35 = InMemoryContext.FeeList.m_ClassName.Lesson27;
                ViewBag.Colume36 = InMemoryContext.FeeList.m_ClassName.Lesson28;
                ViewBag.Colume37 = InMemoryContext.FeeList.m_ClassName.Lesson29;
                ViewBag.Colume38 = InMemoryContext.FeeList.m_ClassName.Lesson30;

                ViewBag.Colume39 = InMemoryContext.FeeList.m_ClassName.HomeWorkA;
                ViewBag.Colume40 = InMemoryContext.FeeList.m_ClassName.HomeWorkB;
                ViewBag.Colume41 = InMemoryContext.FeeList.m_ClassName.HomeWorkC;
                ViewBag.Colume42 = InMemoryContext.FeeList.m_ClassName.HomeWorkD;
                ViewBag.Colume43 = InMemoryContext.FeeList.m_ClassName.HomeWorkE;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                ToolUtility.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "新莊靈糧堂: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
    }
}
