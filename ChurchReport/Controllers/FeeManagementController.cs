using ChurchReport.Models;
using ChurchReport.Tools;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
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
                InMemoryContext.FeeList.SetupLessonList(
                    InMemoryContext.FeeList.m_Account,
                    InMemoryContext.FeeList.m_Password
                );

                // 設定所有必要的 ViewBag 參數
                SetupBasicViewBag();
                SetMultiGroupLayoutParameter();

                // 設定 ViewBag 參數
                ViewBag.Result = InMemoryContext.FeeList.Result;
                ViewBag.FeeDataListCount = "繳費與點名無資料";  // 課程清單頁面不顯示「繳費」和「點名」選單
                ViewBag.DisplayNavigation = "顯示牧養回報項目";

                System.Diagnostics.Debug.WriteLine($"[LessonList] 課程清單載入完成 - 課程數量: {InMemoryContext.FeeList.LessonList?.Count ?? 0}");

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
                InMemoryContext.FeeList.SetupPresentFeeList(discipleLessonsId);

                // 設定所有必要的 ViewBag 參數
                SetupBasicViewBag();
                SetMultiGroupLayoutParameter();

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
                InMemoryContext.FeeList.SetupPresentFeeList(discipleLessonsId);

                // 設定所有必要的 ViewBag 參數
                SetupBasicViewBag();
                SetMultiGroupLayoutParameter();

                // 設定 ViewBag 參數
                ViewBag.PresentResult = InMemoryContext.FeeList.Result;
                ViewBag.DiscipleLessonsId = discipleLessonsId;

                // 設定為「已有資料」，讓「繳費」和「點名」選單顯示
                var feeDataCount = InMemoryContext.FeeList.FeeDataList?.Count ?? 0;
                ViewBag.FeeDataListCount = feeDataCount > 0 ? "繳費與點名已有資料" : "繳費與點名無資料";
                ViewBag.DisplayNavigation = "顯示牧養回報項目";

                // ? 設定欄位標題參數（用於 onCustomizeColumns 函數）
                InitializeColumnHeaders();

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
                if (InMemoryContext.FeeList.LessonList == null || InMemoryContext.FeeList.LessonList.Count == 0)
                {
                    InMemoryContext.FeeList.SetupLessonList(
                        InMemoryContext.FeeList.m_Account,
                        InMemoryContext.FeeList.m_Password
                    );
                }

                // 使用 DevExtreme DataSourceLoader 處理資料
                var result = DataSourceLoader.Load(InMemoryContext.FeeList.LessonList, loadOptions);

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
                if (!string.IsNullOrEmpty(discipleLessonsId))
                {
                    System.Diagnostics.Debug.WriteLine($"[GetFeeData] 呼叫 SetupPresentFeeList({discipleLessonsId})");
                    InMemoryContext.FeeList.SetupPresentFeeList(discipleLessonsId);
                }
                else if (InMemoryContext.FeeList.FeeDataList == null || InMemoryContext.FeeList.FeeDataList.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[GetFeeData] FeeDataList 為空，重新載入");
                    InMemoryContext.FeeList.SetupFeeDataList(
                        InMemoryContext.FeeList.m_Account,
                        InMemoryContext.FeeList.m_Password
                    );
                }

                var feeDataCount = InMemoryContext.FeeList.FeeDataList?.Count ?? 0;
                System.Diagnostics.Debug.WriteLine($"[GetFeeData] FeeDataList.Count={feeDataCount}");

                // 使用 DevExtreme DataSourceLoader 處理資料
                var result = DataSourceLoader.Load(InMemoryContext.FeeList.FeeDataList, loadOptions);

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
        /// </summary>
        [HttpPut]
        [Route("/FeeManagement/Api/UpdateFeeData")]
        public IActionResult UpdateFeeData(string key, string values)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateFeeData] 開始更新 - key={key}");

                // 找到要更新的 Fee 記錄
                var fee = InMemoryContext.FeeList.FeeDataList?.FirstOrDefault(f => f.StorLessonsId == key);

                if (fee == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateFeeData] 找不到記錄 - key={key}");
                    return BadRequest("找不到指定的繳費記錄");
                }

                // 使用 FeeList 的 PopulateObjectAndUpdateEntity 方法更新實體
                InMemoryContext.FeeList.PopulateObjectAndUpdateEntity(values, fee);

                System.Diagnostics.Debug.WriteLine($"[UpdateFeeData] 更新成功 - key={key}");

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
                System.Diagnostics.Debug.WriteLine($"[SaveBatch] 開始儲存 - aResult={aResult}");

                // 這裡可以添加額外的業務邏輯，例如發送通知或記錄日誌
                // 目前 UpdateFeeData 已經處理了實際的資料更新

                System.Diagnostics.Debug.WriteLine("[SaveBatch] 儲存成功");

                return Json(new
                {
                    status = "success",
                    message = "繳費資料已成功儲存"
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

        #endregion
    }
}
