// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/EquipmentController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class EquipmentController
// 主要成員：EquipmentView、LoadEquipmentList、LoadEquipmentContact、LoadEquipmentStorLessons、UpdateEquipmentStatus、AddEquipmentLesson、ExportEquipmentReport、GetEquipmentSummary
// 引用命名空間：ChurchReport.Diagnostics.Profiling、ChurchReport.Models、ChurchReport.Tools、DevExtreme.AspNet.Data、DevExtreme.AspNet.Mvc、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Caching.Memory
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Diagnostics.Profiling;
using ChurchReport.Models;
using ChurchReport.Services;
using ChurchReport.Tools;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 裝備狀態管理控制器
    /// 處理小組成員的裝備/訓練狀態管理功能
    /// </summary>
    public class EquipmentController : BaseChurchController
    {
        #region 建構函式

        /// <summary>
        /// EquipmentController 建構函數 (使用 Dependency Injection)
        /// </summary>
        /// <param name="httpContextAccessor">HTTP 上下文存取器</param>
        /// <param name="memoryCache">記憶體快取</param>
        /// <param name="paymentService">金流服務</param>
        /// <param name="toolUtilityProvider">ToolUtility 提供者 (DI 注入)</param>
        /// <param name="connectionPool">CRM 連線池</param>
        public EquipmentController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
        }

        #endregion

        #region 裝備狀態主頁面

        /// <summary>
        /// 裝備狀態檢視頁面
        /// 顯示小組成員的裝備/訓練狀態
        /// </summary>
        [HttpGet]
        [Route("/Equipment/EquipmentView")]
        public IActionResult EquipmentView()
        {
            using var perfPhase = PerfPhase.Measure(HttpContext, "Equipment.EquipmentView");

            try
            {
                using (PerfPhase.Measure(HttpContext, "Equipment.EquipmentView.SetupBasicViewBag"))
                {
                    SetupBasicViewBag();
                }
                using (PerfPhase.Measure(HttpContext, "Equipment.EquipmentView.SetMultiGroupLayoutParameter"))
                {
                    SetMultiGroupLayoutParameter();
                }

                // 建立裝備資料 - 返回包含小組的模型
                var equipmentData = new EquipmenSmallGroup
                {
                    SmallGroupName = ViewBag.LoginFullName ?? "小組",
                    LoginUserId = InMemoryContext.ListManager.m_Account,
                    SmallGroupListEntityId = InMemoryContext.ListManager.ActiveListId,
                    EquipmentContactList = new List<EquipmentContact>()
                };

                return View(equipmentData);
            }
            catch (Exception e)
            {
                return HandleError(e, "EquipmentView");
            }
        }

        #endregion

        #region 裝備資料載入

        /// <summary>
        /// 載入裝備小組清單資料
        /// 用於主 DataGrid - 返回 EquipmenSmallGroup 清單
        /// </summary>
        /// <param name="id">清單ID</param>
        /// <param name="loadOptions">載入選項(分頁、排序、篩選)</param>
        [HttpGet]
        public object LoadEquipmentList(string id, DataSourceLoadOptions loadOptions)
        {
            using var perfPhase = PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentList");

            try
            {
                // ========================================
                // ✅ 關鍵修復：驗證 Session 並確保資料正確
                // ========================================
                //EnsureCorrectUserData();

                // 確保資料已載入
                if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport == null ||
                    !InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag)
                {
                    using (PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentList.SetupIntegrateData"))
                    {
                        InMemoryContext.ListManager.SetupIntegrateData(id);
                    }
                }

                // 為每個小組建立 EquipmenSmallGroup 對象
                var equipmentGroups = new List<EquipmenSmallGroup>();

                // 如果是多小組，為每個小組建立一個項目
                if (InMemoryContext.ListManager.m_MultiGroupList?.m_WeeklyReportRecordListData != null)
                {
                    foreach (var group in InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData)
                    {
                        equipmentGroups.Add(new EquipmenSmallGroup
                        {
                            SmallGroupName = group.Name,
                            SmallGroupListEntityId = group.ListEntityId,
                            LoginUserId = InMemoryContext.ListManager.m_Account,
                            EquipmentContactList = new List<EquipmentContact>()
                        });
                    }
                }
                else
                {
                    // 單一小組的情況
                    equipmentGroups.Add(new EquipmenSmallGroup
                    {
                        SmallGroupName = InMemoryContext.ListManager.LoginFullName ?? "小組",
                        SmallGroupListEntityId = InMemoryContext.ListManager.ActiveListId,
                        LoginUserId = InMemoryContext.ListManager.m_Account,
                        EquipmentContactList = new List<EquipmentContact>()
                    });
                }

                using (PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentList.DataSourceLoader"))
                {
                    return DataSourceLoader.Load(equipmentGroups, loadOptions);
                }
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadEquipmentList");
            }
        }

        /// <summary>
        /// 載入裝備聯絡人清單資料
        /// 用於 master-detail 的 DataGrid - 返回 EquipmentContact 清單
        /// </summary>
        /// <param name="id">小組清單ID</param>
        /// <param name="loadOptions">載入選項</param>
        [HttpGet]
        public object LoadEquipmentContact(string id, DataSourceLoadOptions loadOptions)
        {
            using var perfPhase = PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentContact");

            try
            {
                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] ===== 開始載入聯絡人 =====");
                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] 請求小組ID: {id}");
                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] 目前ActiveListId: {InMemoryContext.ListManager.ActiveListId}");

                // ========================================
                // ✅ 關鍵修復：驗證 Session 並確保資料正確
                // ========================================
                using (PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentContact.EnsureCorrectUserData"))
                {
                    EnsureCorrectUserData();
                }

                // 強制重新載入資料以確保正確性
                // 原因: 多小組切換時，ActiveListId 可能因為非同步請求導致不一致
                bool needReload = false;

                if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] 原因: m_ListSmallGroupWeeklyReport 為 null");
                    needReload = true;
                }
                else if (!InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] 原因: LoadFlag 為 false");
                    needReload = true;
                }
                else if (InMemoryContext.ListManager.ActiveListId != id)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] 原因: ActiveListId 不匹配");
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact]   目前: {InMemoryContext.ListManager.ActiveListId}");
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact]   請求: {id}");
                    needReload = true;
                }

                if (needReload)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] >>> 執行重新載入資料 <<<");
                    using (PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentContact.SetupIntegrateData"))
                    {
                        InMemoryContext.ListManager.SetupIntegrateData(id);
                    }
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] >>> 載入完成，新的ActiveListId: {InMemoryContext.ListManager.ActiveListId}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] 使用現有快取資料");
                }

                var members = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    ?.m_SmallGroupDataList?.m_AllMemeberData?.Members
                    ?? new List<Member>();

                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] 取得成員數量: {members.Count}");

                // 額外驗證: 確保載入的資料確實屬於請求的小組
                if (InMemoryContext.ListManager.ActiveListId != id)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] ?? 警告: 載入後 ActiveListId 仍不匹配!");
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact]   目前: {InMemoryContext.ListManager.ActiveListId}");
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact]   預期: {id}");

                    // 再次強制載入
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] >>> 再次強制載入資料 <<<");
                    using (PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentContact.SetupIntegrateDataRetry"))
                    {
                        InMemoryContext.ListManager.SetupIntegrateData(id);
                    }

                    members = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                        ?.m_SmallGroupDataList?.m_AllMemeberData?.Members
                        ?? new List<Member>();

                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] 再次載入後成員數量: {members.Count}");
                }

                // 轉換為 EquipmentContact 清單
                var equipmentList = members.Select(m => new EquipmentContact
                {
                    ContactFullName = m.FullName,
                    EquipmentStatus = m.EquipmentStatus,
                    EquipmentContactId = m.PresentRecordId,
                    SmallGroupName = InMemoryContext.ListManager.LoginFullName ?? "",
                    SmallGroupListEntityId = id,
                    StorLessonsList = new List<EquipmentStorLessons>()
                }).ToList();

                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] 返回聯絡人數量: {equipmentList.Count}");
                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] 小組名稱: {InMemoryContext.ListManager.LoginFullName}");

                // 輸出前 3 個成員名稱用於驗證
                if (equipmentList.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] 前 3 個成員:");
                    for (int i = 0; i < Math.Min(3, equipmentList.Count); i++)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact]   {i + 1}. {equipmentList[i].ContactFullName}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] ===== 載入聯絡人完成 =====\n");

                using (PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentContact.DataSourceLoader"))
                {
                    return DataSourceLoader.Load(equipmentList, loadOptions);
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] ? 錯誤: {e.Message}");
                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentContact] 堆疊追蹤: {e.StackTrace}");
                return HandleError(e, "LoadEquipmentContact");
            }
        }

        /// <summary>
        /// 載入裝備課程清單資料，用於第三層 master-detail 的 DataGrid。Package01 關閉時維持
        /// legacy fullname 查詢；開啟時只傳 null 名稱及 <see cref="HttpContext.RequestAborted"/> 到
        /// 非同步 typed projection，避免以 SDK 補查或同步等待延長 request/session 生命週期。所有
        /// 資料僅建立在此 action 的區域集合；取消或失敗不會寫入 shared member state 或切換 fallback。
        /// </summary>
        /// <param name="id">既有成員清單中的 PresentRecordId；找不到、無 contact 或未載入時回傳空集合。</param>
        /// <param name="loadOptions">僅套用於本次 request-local 課程顯示集合的 DevExtreme 載入選項。</param>
        /// <returns>可供 DataGrid 使用的裝備課程資料或既有安全錯誤回應。</returns>
        [HttpGet]
        public async Task<object> LoadEquipmentStorLessons(string id, DataSourceLoadOptions loadOptions)
        {
            using var perfPhase = PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentStorLessons");

            try
            {
                // ========================================
                // ✅ 使用基底控制器的統一驗證方法
                // ========================================
                // 教學說明：
                // 基底控制器已經提供了完整的驗證方法，包含：
                // - Session 和 ListManager 密碼一致性檢查
                // - LINE ID 恢復機制
                // - 安全的日誌記錄（隱藏敏感資訊）
                using (PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentStorLessons.EnsureCorrectUserData"))
                {
                    EnsureCorrectUserData();
                }

                // 確保資料已載入
                if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport == null ||
                    !InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 資料未載入，id={id}");
                    using (PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentStorLessons.EmptyDataSourceLoader"))
                    {
                        return DataSourceLoader.Load(new List<EquipmentStorLessons>(), loadOptions);
                    }
                }

                // 從成員列表中找到對應的聯絡人
                var members = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    ?.m_SmallGroupDataList?.m_AllMemeberData?.Members
                    ?? new List<Member>();

                var member = members.FirstOrDefault(m => m.PresentRecordId == id);

                if (member == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 找不到成員，id={id}");
                    using (PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentStorLessons.EmptyDataSourceLoader"))
                    {
                        return DataSourceLoader.Load(new List<EquipmentStorLessons>(), loadOptions);
                    }
                }

                // 檢查 ContactId 是否存在
                if (string.IsNullOrEmpty(member.ContactId))
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 警告: ContactId 為空，FullName={member.FullName}, PresentRecordId={member.PresentRecordId}");
                    using (PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentStorLessons.EmptyDataSourceLoader"))
                    {
                        return DataSourceLoader.Load(new List<EquipmentStorLessons>(), loadOptions);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 查詢課程記錄: ContactName={member.FullName}, ContactId={member.ContactId}");

                // Package01 關閉時服務保留既有 FetchXML 行為；開啟時資料已在 connector 以 lesson link
                // 封閉投影，不可為補全姓名或階段再取回 CRM Entity，否則會破壞本批 no-SDK 邊界。
                IReadOnlyList<StorLessonProjection> projections;
                using (PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentStorLessons.QueryService"))
                {
                    var configuration = HttpContext.RequestServices.GetService<IConfiguration>();
                    var queryService = new StorLessonQueryService(ToolUtility, configuration ?? new ConfigurationBuilder().Build());
                    projections = await queryService.GetByContactAsync(
                        queryService.IsPackage01Enabled ? null : member.FullName,
                        member.ContactId,
                        HttpContext.RequestAborted).ConfigureAwait(false);
                }

                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 查詢結果: Count={projections.Count}");

                var lessonsList = new List<EquipmentStorLessons>();
                foreach (var row in projections)
                {
                    var lessonItem = new EquipmentStorLessons
                    {
                        StorLessonsEntityId = row.StorLessonsEntityId,
                        DiscipleLessonsName = row.DiscipleLessonsName,
                        StageName = row.StageName,
                        CurrentComplete = row.CurrentComplete,
                        DiscipleLessonsDateTime = row.DiscipleLessonsDateTime
                    };

                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 課程: {lessonItem.DiscipleLessonsName}, 階段: {lessonItem.StageName}, 日期: {lessonItem.DiscipleLessonsDateTime:yyyy-MM-dd}");
                    lessonsList.Add(lessonItem);
                }

                if (lessonsList.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 警告: 該聯絡人({member.FullName})沒有課程記錄，或課程的 new_classification 不是 100000000/100000001");
                }

                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 最終返回課程數量: {lessonsList.Count}");
                using (PerfPhase.Measure(HttpContext, "Equipment.LoadEquipmentStorLessons.DataSourceLoader"))
                {
                    return DataSourceLoader.Load(lessonsList, loadOptions);
                }
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                // 客戶端已中止時不建立錯誤回應或延長任何 projection／例外的生命週期；重新擲出讓
                // ASP.NET Core 與既有 Data8 取消／lease cleanup owner 完成確定性釋放。
                throw;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 錯誤: {e.Message}");
                return HandleError(e, "LoadEquipmentStorLessons");
            }
        }

        #endregion

        #region 裝備資料操作

        /// <summary>
        /// 更新裝備狀態
        /// </summary>
        /// <param name="contactId">聯絡人ID</param>
        /// <param name="equipmentStatus">裝備狀態</param>
        [HttpPost]
        public IActionResult UpdateEquipmentStatus(string contactId, string equipmentStatus)
        {
            try
            {
                // 實作更新裝備狀態邏輯
                // await InMemoryContext.EquipmentDataManager.UpdateEquipmentStatus(contactId, equipmentStatus);

                return Json(new { status = "1", message = "裝備狀態已更新" });
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateEquipmentStatus");
            }
        }

        /// <summary>
        /// 新增課程記錄
        /// </summary>
        /// <param name="contactId">聯絡人ID</param>
        /// <param name="lessonName">課程名稱</param>
        /// <param name="stageName">階段名稱</param>
        /// <param name="lessonDate">課程日期</param>
        [HttpPost]
        public IActionResult AddEquipmentLesson(
            string contactId,
            string lessonName,
            string stageName,
            DateTime lessonDate)
        {
            try
            {
                // 實作新增課程記錄邏輯
                // await InMemoryContext.EquipmentDataManager.AddLesson(contactId, lessonName, stageName, lessonDate);

                return Json(new { status = "1", message = "課程記錄已新增" });
            }
            catch (Exception e)
            {
                return HandleError(e, "AddEquipmentLesson");
            }
        }

        /// <summary>
        /// 匯出裝備報表
        /// 產生 Excel 格式的裝備狀態統計報表
        /// </summary>
        /// <param name="groupId">小組ID</param>
        [HttpGet]
        public IActionResult ExportEquipmentReport(string groupId)
        {
            try
            {
                // 實作匯出報表邏輯
                // var reportData = await InMemoryContext.EquipmentDataManager.GenerateReport(groupId);

                return Json(new { status = "1", message = "報表已產生" });
            }
            catch (Exception e)
            {
                return HandleError(e, "ExportEquipmentReport");
            }
        }

        #endregion

        #region 統計資訊

        /// <summary>
        /// 取得裝備統計摘要
        /// 包含完成人數、進行中人數等統計資訊
        /// </summary>
        /// <param name="groupId">小組ID</param>
        [HttpGet]
        public IActionResult GetEquipmentSummary(string groupId)
        {
            try
            {
                // 實作統計邏輯
                var summary = new
                {
                    totalMembers = 0,
                    completedMembers = 0,
                    inProgressMembers = 0,
                    notStartedMembers = 0
                };

                return Json(new { status = "1", data = summary });
            }
            catch (Exception e)
            {
                return HandleError(e, "GetEquipmentSummary");
            }
        }

        #endregion
    }
}
