using ChurchReport.Models;
using ChurchReport.Tools;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;
using LineMessagingProcessor;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器
    /// 負責處理小組週報、整合視圖、多組週報等相關功能
    /// </summary>
    public class SmallGroupController : BaseChurchController
    {
        #region 建構函式

        public SmallGroupController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider, connectionPool)
        {
        }

        #endregion

        #region 多小組回報

        /// <summary>
        /// 多小組回報主頁面
        /// 顯示多個小組的統計資訊與管理功能
        /// </summary>
        /// <param name="LoginParameter">登入參數(AccountPassword 或 LineId)</param>
        /// <param name="cancellationToken">取消標記</param>
        [Route("/SmallGroup/MultiGroupView/{LoginParameter}")]
        public async Task<IActionResult> MultiGroupView(
            string LoginParameter,
            CancellationToken cancellationToken = default)
        {
            try
            {
                SetupViewBagForSmallGroup();
                ViewBag.ListId = InMemoryContext.ListManager.ActiveListId;

                if (LoginParameter != "AccountPassword")
                {
                    return HandleMultiGroupLogin(LoginParameter);
                }
                else if (LoginParameter == "jquery.js")
                {
                    ViewBag.LoginType = "個人登入";
                    return Ok();
                }
                else
                {
                    // ? 使用非同步 HandleLineLogin
                    return await HandleLineLogin(LoginParameter, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception e)
            {
                return HandleError(e, "MultiGroupView");
            }
        }

        /// <summary>
        /// 處理多小組登入邏輯
        /// </summary>
        private IActionResult HandleMultiGroupLogin(string loginParameter)
        {
            string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();

            if (displayViewType == "MultiGroupView")
            {
                // 清除整合頁面資料
                if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport != null)
                {
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag = false;
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport = null;
                }

                // 設定多組資料
                if (InMemoryContext.ListManager.InitialFlag)
                {
                    InMemoryContext.ListManager.SetupListManager(
                        InMemoryContext.ListManager.m_Account,
                        InMemoryContext.ListManager.m_Password,
                        InMemoryContext.ListManager.m_SelectDate);
                }
                else
                {
                    InMemoryContext.ListManager.InitialFlag = true;
                }

                // 明確指定 View 路徑 (暫時使用 Home 資料夾中的 View)
                return View("~/Views/Home/MultiGroupView.cshtml", InMemoryContext.ListManager);
            }
            else
            {
                // 單一小組，跳轉到整合式回報
                return RedirectToAction("IntegrateView", "SmallGroup");
            }
        }

        #endregion

        #region 整合式小組長點名

        /// <summary>
        /// 整合式小組回報頁面
        /// 提供單一小組的詳細回報功能(點名、禱告、統計)
        /// </summary>
        /// <param name="LoginParameter">登入參數或清單ID</param>
        /// <param name="cancellationToken">取消權杖</param>
        [Route("/SmallGroup/IntegrateView/{LoginParameter}")]
        public async Task<IActionResult> IntegrateView(
            string LoginParameter,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // ? 先設置資料，再設置 ViewBag
                SetupIntegrateViewData(LoginParameter);
                
                // ? 關鍵修復：確保從 MultiGroupView 點擊小組後，ViewBag.MultiGroupIndex 保持為 HybridView
                // 這樣「回報統計」和「小組回報」選項都會顯示
                SetupViewBagForSmallGroup();

                if (LoginParameter != "AccountPassword")
                {
                    return HandleIntegrateViewLogin(LoginParameter);
                }
                else if (LoginParameter == "jquery.js")
                {
                    ViewBag.LoginType = "個人回報";
                    return Ok();
                }
                else
                {
                    // ? 使用非同步 HandleLineLogin
                    return await HandleLineLogin(LoginParameter, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception e)
            {
                return HandleError(e, "IntegrateView");
            }
        }

        /// <summary>
        /// 設定整合視圖資料
        /// </summary>
        private void SetupIntegrateViewData(string loginParameter)
        {
            // 判斷是否需要載入整合資料
            bool shouldLoadData = ShouldLoadIntegrateData(loginParameter);

            if (shouldLoadData)
            {
                string listId = DetermineListId(loginParameter);
                
                // ? 關鍵：載入指定小組的資料
                // 這會設置 InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag = true
                // 讓 IsIntegrateDataLoaded() 返回 true
                InMemoryContext.ListManager.SetupIntegrateData(listId);
                
                // ? 更新 ActiveListId 為當前選擇的小組
                InMemoryContext.ListManager.ActiveListId = listId;
            }

            ViewBag.ListId = InMemoryContext.ListManager.ActiveListId;
            ViewBag.SpiritualLeaderListId = InMemoryContext.ListManager.ActiveListId;
        }

        /// <summary>
        /// 判斷是否需要載入整合資料
        /// </summary>
        private bool ShouldLoadIntegrateData(string loginParameter)
        {
            var displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
            var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;

            // ? 修正：當從多小組統計進入時，或是資料尚未載入時，都需要重新載入
            if (displayViewType == "MultiGroupView")
            {
                // 多小組模式：總是需要載入指定小組的資料
                return true;
            }

            // 其他情況：檢查是否已載入
            return weeklyReport == null || !weeklyReport.LoadFlag;
        }

        /// <summary>
        /// 處理整合式頁面登入
        /// </summary>
        private IActionResult HandleIntegrateViewLogin(string loginParameter)
        {
            if (InMemoryContext.ListManager.LoginType == "個人回報")
            {
                return RedirectToAction("PersonalInfomationView", "Personal");
            }

            // 明確指定 View 路徑 (暫時使用 Home 資料夾中的 View)
            return View("~/Views/Home/IntegrateView.cshtml", InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport);
        }

        #endregion

        #region 資料載入 API

        /// <summary>
        /// 載入整合式頁面的小組成員資料
        /// 用於 DevExtreme DataGrid 的資料來源
        /// </summary>
        /// <param name="id">清單ID</param>
        /// <param name="loadOptions">載入選項(分頁、排序、篩選)</param>
        [HttpGet]
        public object LoadIntegrate(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsureIntegrateDataLoaded(id);

                var tasks = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_SmallGroupData.Members;

                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadIntegrate");
            }
        }

        /// <summary>
        /// 載入圖表資料
        /// 用於 DevExtreme Chart 的資料來源
        /// </summary>
        /// <param name="WeeklyReportId">週報ID</param>
        /// <param name="loadOptions">載入選項</param>
        [HttpGet]
        [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "WeeklyReportId" })]
        public object GetChartDataList(string WeeklyReportId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                // 確保整合資料已載入
                if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport == null ||
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_WeeklyReportChart == null ||
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_WeeklyReportChart.m_ChartDataList == null)
                {
                    return DataSourceLoader.Load(new List<ChartData>(), loadOptions);
                }

                var chartData = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_WeeklyReportChart.m_ChartDataList;

                return DataSourceLoader.Load(chartData, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "GetChartDataList");
            }
        }

        /// <summary>
        /// 載入多小組圓餅圖資料
        /// 用於 MultiGroupView 的 PieChart 資料來源
        /// </summary>
        /// <param name="WeeklyReportId">週報ID</param>
        /// <param name="loadOptions">載入選項</param>
        [HttpGet]
        [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "WeeklyReportId" })]
        public object GetMultiGroupChartDataList(string WeeklyReportId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                // 確保多組資料已載入
                if (InMemoryContext.ListManager.m_MultiGroupChartDataList == null ||
                    InMemoryContext.ListManager.m_MultiGroupChartDataList.m_MultiGroupChartDataList == null)
                {
                    return DataSourceLoader.Load(new List<MultiGroupChartData>(), loadOptions);
                }

                var chartData = InMemoryContext.ListManager.m_MultiGroupChartDataList.m_MultiGroupChartDataList;

                return DataSourceLoader.Load(chartData, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "GetMultiGroupChartDataList");
            }
        }

        /// <summary>
        /// 載入多小組列表資料
        /// 用於 MultiGroupView 的 DataGrid 資料來源
        /// </summary>
        /// <param name="id">清單ID</param>
        /// <param name="loadOptions">載入選項</param>
        [HttpGet]
        [ResponseCache(Duration = 60, VaryByQueryKeys = new[] { "id" })]
        public object AssignSmallGroupGet(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                // 確保多組資料已載入
                if (InMemoryContext.ListManager.m_MultiGroupList == null ||
                    InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData == null)
                {
                    return DataSourceLoader.Load(new List<WeeklyReportRecord>(), loadOptions);
                }

                var weeklyReportRecords = InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData;

                return DataSourceLoader.Load(weeklyReportRecords, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "AssignSmallGroupGet");
            }
        }

        /// <summary>
        /// 確保整合資料已載入
        /// </summary>
        private void EnsureIntegrateDataLoaded(string id)
        {
            var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;

            if (weeklyReport == null || !weeklyReport.LoadFlag)
            {
                InMemoryContext.ListManager.SetupIntegrateData(id);
            }
        }

        #endregion

        #region CRUD 操作

        /// <summary>
        /// 新增出席記錄
        /// </summary>
        /// <param name="values">JSON 格式的成員資料</param>
        [HttpPost]
        public IActionResult InsertPresentRecord(string values)
        {
            try
            {
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_SmallGroupData.InsertMember(values);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "InsertPresentRecord");
            }
        }

        /// <summary>
        /// 更新小組出席記錄
        /// 同時更新小組資料和全部成員資料
        /// ? 已改造為正確的並行模式
        /// </summary>
        /// <param name="key">成員識別碼</param>
        /// <param name="values">更新數據(JSON)</param>
        /// <param name="cancellationToken">取消標記</param>
        [HttpPut]
        public async Task<IActionResult> UpdateSmallGroupPresentRecord(
            string key, 
            string values,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var dataList = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList;

                // ? 並行更新兩個資料集
                var task1 = Task.Run(() => 
                    dataList.m_SmallGroupData.UpdateMember(key, values), 
                    cancellationToken);
                
                var task2 = Task.Run(() => 
                    dataList.m_AllMemeberData.UpdateMember(key, values), 
                    cancellationToken);

                // ? 等待所有更新完成
                await Task.WhenAll(task1, task2).ConfigureAwait(false);

                return Ok();
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499); // Client Closed Request
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateSmallGroupPresentRecord");
            }
        }

        /// <summary>
        /// 刪除出席記錄
        /// 同時從多個資料集中移除該成員
        /// </summary>
        /// <param name="key">成員識別碼</param>
        [HttpDelete]
        public IActionResult DeletePresentRecord(string key)
        {
            try
            {
                var dataList = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList;

                // 先刪除全部資料中的成員
                Member deletedMember = dataList.m_AllMemeberData.DeleteMember(key);

                if (deletedMember != null)
                {
                    // 上傳刪除資訊到 CRM
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.DeleteMemberData(
                        InMemoryContext.ListManager.m_Account,
                        InMemoryContext.ListManager.m_Password,
                        deletedMember
                    );
                }

                // 從各個資料集中移除
                dataList.m_SmallGroupData.DeleteMember(key);
                dataList.m_NewPersonFollowUpData.DeleteMember(key);
                dataList.m_HappyGroup.DeleteMember(key);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "DeletePresentRecord");
            }
        }

        #endregion

        #region 資料儲存

        /// <summary>
        /// 儲存整合視圖週報資料
        /// 包含出席狀況、快樂小組資訊、計劃暫停等資料
        /// ? 改為 Fire-and-Forget 模式，立即回應使用者，在背景處理上傳
        /// </summary>
        /// <param name="WeeklyReportData">週報資料(JSON)</param>
        /// <param name="HappyWeekIndex">快樂小組第幾週</param>
        /// <param name="HappyWeekTopic">快樂小組主題</param>
        /// <param name="CheckBox">計劃是否暫停</param>
        /// <param name="cancellationToken">取消標記</param>
        [HttpPost]
        public IActionResult SaveIntegrate(
            string WeeklyReportData,
            string HappyWeekIndex,
            string HappyWeekTopic,
            string CheckBox,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 驗證快樂小組欄位
                if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.ListEntityName.Contains("快樂"))
                {
                    var validationResult = ValidateHappyGroupFields(HappyWeekIndex, HappyWeekTopic);
                    if (validationResult != null) return validationResult;
                }

                bool pauseCheckBox = CheckBox == "true";

                // ? Fire-and-Forget：在背景執行上傳，不等待完成
                // 立即回應使用者，避免長時間等待
                _ = Task.Run(async () =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 開始背景上傳...");
                        
                        // 在背景執行上傳
                        InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData(
                            InMemoryContext.ListManager.m_SelectDate,
                            InMemoryContext.ListManager.m_Account,
                            InMemoryContext.ListManager.m_Password,
                            InMemoryContext.ListManager.LoginType,
                            InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                            WeeklyReportData,
                            HappyWeekIndex,
                            HappyWeekTopic,
                            pauseCheckBox
                        );

                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 背景上傳完成");

                        // 上傳完成後才清理
                        CleanupTransferredMembers();
                        
                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 清理完成");
                    }
                    catch (Exception ex)
                    {
                        // 背景任務的錯誤記錄到 Debug 輸出
                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 背景上傳失敗: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 錯誤堆疊:\n{ex.StackTrace}");
                        
                        // 記錄到追蹤日誌
                        try
                        {
                            ToolUtility?.TraceByLevel(1, 1, 
                                $"SaveIntegrate 背景上傳失敗: {ex.Message}\n{ex.StackTrace}");
                        }
                        catch
                        {
                            // 追蹤失敗不影響
                        }
                    }
                }, cancellationToken);

                // ? 立即回應使用者，不等待上傳完成
                System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 立即回應使用者，背景處理中...");
                return Json(new { status = "1", message = "資料已送出，正在背景上傳中..." });
            }
            catch (OperationCanceledException)
            {
                return Json(new { status = "0", message = "操作已取消" });
            }
            catch (Exception e)
            {
                // 僅在啟動背景任務失敗時才返回錯誤
                System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 啟動失敗: {e.Message}");
                return HandleError(e, "SaveIntegrate");
            }
        }

        /// <summary>
        /// 驗證幸福小組必填欄位
        /// </summary>
        private JsonResult ValidateHappyGroupFields(string weekIndex, string topic)
        {
            if (string.IsNullOrEmpty(weekIndex) && string.IsNullOrEmpty(topic))
            {
                return Json(new { status = "2", message = "幸福小組必須填寫第幾週和主題" });
            }
            if (string.IsNullOrEmpty(weekIndex))
            {
                return Json(new { status = "2", message = "幸福小組必須填寫第幾週" });
            }
            if (string.IsNullOrEmpty(topic))
            {
                return Json(new { status = "2", message = "幸福小組必須填寫主題" });
            }
            return null;
        }

        /// <summary>
        /// 清理已轉介或指派到其他小組的成員
        /// </summary>
        private void CleanupTransferredMembers()
        {
            var smallGroupMembers = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                .m_SmallGroupDataList.m_SmallGroupData.Members;

            if (smallGroupMembers != null)
            {
                // 清理小組資料
                RemoveTransferredMembers(smallGroupMembers);

                // 清理新人跟進資料
                var newPersonMembers = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_NewPersonFollowUpData.Members;
                RemoveTransferredMembers(newPersonMembers);
            }
        }

        /// <summary>
        /// 從清單中移除已轉介的成員
        /// </summary>
        private void RemoveTransferredMembers(System.Collections.Generic.List<Member> members)
        {
            int count = members.Count;
            int index = 0;

            for (int i = 0; i < count; i++)
            {
                if (ShouldRemoveMember(members[index]))
                {
                    members.RemoveAt(index);
                }
                else
                {
                    index++;
                }
            }
        }

        /// <summary>
        /// 判斷成員是否應該被移除
        /// </summary>
        private bool ShouldRemoveMember(Member member)
        {
            return (!string.IsNullOrEmpty(member.AssignedGroup)) ||
                   (member.FollowUpNextStep == "轉介");
        }

        #endregion

        #region 幸福小組週次更新

        /// <summary>
        /// 更新幸福小組週次
        /// </summary>
        /// <param name="HappyWeekIndex">第幾週</param>
        [HttpPost]
        public IActionResult UpdateHappyWeekIndex(string HappyWeekIndex)
        {
            try
            {
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.HappyWeekIndex = HappyWeekIndex;
                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateHappyWeekIndex");
            }
        }

        /// <summary>
        /// 更新幸福小組主題
        /// </summary>
        /// <param name="HappyWeekTopic">主題內容</param>
        [HttpPost]
        public IActionResult UpdateHappyWeekTopic(string HappyWeekTopic)
        {
            try
            {
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.HappyWeekTopic = HappyWeekTopic;
                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateHappyWeekTopic");
            }
        }

        /// <summary>
        /// 更新多小組檢視的日期
        /// 當使用者在 MultiGroupView 中更改日期時調用
        /// </summary>
        /// <param name="SelectedDate">選擇的日期 (格式: yyyy/M/d)</param>
        [HttpGet]
        public IActionResult UpdateDate(string SelectedDate)
        {
            try
            {
                // 解析日期
                if (!DateTime.TryParseExact(SelectedDate, 
                    new[] { "yyyy/M/d", "yyyy/MM/dd", "yyyy-MM-dd" }, 
                    CultureInfo.InvariantCulture, 
                    DateTimeStyles.None, 
                    out DateTime selectedDateTime))
                {
                    return Json(new { success = false, message = "日期格式錯誤" });
                }

                // 更新選擇的日期
                InMemoryContext.ListManager.m_SelectDate = selectedDateTime;

                // 重新設置 ListManager 以載入新日期的資料
                InMemoryContext.ListManager.SetupListManager(
                    InMemoryContext.ListManager.m_Account,
                    InMemoryContext.ListManager.m_Password,
                    selectedDateTime);

                return Json(new { 
                    success = true, 
                    message = "日期更新成功" 
                });
            }
            catch (Exception e)
            {
                return Json(new { 
                    success = false, 
                    message = $"日期更新失敗: {e.Message}" 
                });
            }
        }

        /// <summary>
        /// 更新綜合報表日期
        /// 當使用者在 IntegrateView 中更改小組日期時調用
        /// ? 修復：確保更新日期後，保持載入當前選擇的小組，而不是跳回第一個小組
        /// </summary>
        /// <param name="SelectedDate">選擇的日期 (格式: yyyy/M/d)</param>
        [HttpGet]
        public IActionResult UpdateIntegrateDate(string SelectedDate)
        {
            try
            {
                // 解析日期
                if (!DateTime.TryParseExact(SelectedDate, 
                    new[] { "yyyy/M/d", "yyyy/MM/dd", "yyyy-MM-dd" }, 
                    CultureInfo.InvariantCulture, 
                    DateTimeStyles.None, 
                    out DateTime selectedDateTime))
                {
                    return Json(new { success = false, message = "日期格式錯誤" });
                }

                // ? 關鍵修復：先保存當前選擇的小組 ID
                // 避免 SetupListManager 重設為第一個小組
                string currentListId = InMemoryContext.ListManager.ActiveListId;
                
                System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 當前小組 ID: {currentListId}");
                System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 更新日期: {selectedDateTime:yyyy/M/d}");

                // 更新選擇的日期
                InMemoryContext.ListManager.m_SelectDate = selectedDateTime;

                // ? 重要：SetupListManager 會重新載入多小組列表
                // 但可能會重設 ActiveListId 為第一個小組
                InMemoryContext.ListManager.SetupListManager(
                    InMemoryContext.ListManager.m_Account,
                    InMemoryContext.ListManager.m_Password,
                    selectedDateTime);

                // ? 修復：恢復之前選擇的小組 ID
                // 確保不會跳回第一個小組
                if (!string.IsNullOrEmpty(currentListId))
                {
                    InMemoryContext.ListManager.ActiveListId = currentListId;
                    System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 恢復小組 ID: {currentListId}");
                }
                else
                {
                    // 如果沒有保存的 ID，使用當前的 ActiveListId
                    currentListId = InMemoryContext.ListManager.ActiveListId;
                    System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 使用新的小組 ID: {currentListId}");
                }

                // ? 重新載入當前選擇的小組的資料（不是第一個小組）
                InMemoryContext.ListManager.SetupIntegrateData(currentListId);

                System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 完成載入小組: {currentListId}");

                // 返回當前小組的 ID（不是第一個小組）
                return Json(new { 
                    success = true, 
                    ActiveListId = currentListId,
                    message = "日期更新成功" 
                });
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 錯誤: {e.Message}");
                System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 堆疊: {e.StackTrace}");
                
                return Json(new { 
                    success = false, 
                    message = $"日期更新失敗: {e.Message}" 
                });
            }
        }

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// 設定小組頁面的 ViewBag 參數
        /// </summary>
        private void SetupViewBagForSmallGroup()
        {
            ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
            ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
            ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
            ViewBag.HappyType = InMemoryContext.HappyGroupDataManager.HappyType;

            SetupFeeDataListCount();
            SetMultiGroupLayoutParameter();

            ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "不是單純行事曆";
            ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "顯示牧養回報項目";
            ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;
        }

        /// <summary>
        /// 設定繳費點名資料數量
        /// </summary>
        private void SetupFeeDataListCount()
        {
            if (InMemoryContext.FeeList.FeeDataList != null &&
                InMemoryContext.FeeList.FeeDataList.Count > 0)
            {
                ViewBag.FeeDataListCount = "繳費與點名已有資料";
            }
            else
            {
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
            }
        }

        /// <summary>
        /// 確定要載入的清單ID
        /// </summary>
        private string DetermineListId(string loginParameter)
        {
            if (loginParameter == "undefined" || loginParameter == "IntegrateView")
            {
                return InMemoryContext.ListManager.ActiveListId;
            }
            return loginParameter;
        }

        /// <summary>
        /// 處理 LINE 登入
        /// ? 已改造為非同步模式
        /// </summary>
        private async Task<IActionResult> HandleLineLogin(
            string lineUserId, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                // ? 使用非同步查詢
                var contactTask = Task.Run(() => 
                    ToolUtility.RetrieveContactEntityByLineUserId(lineUserId),
                    cancellationToken);

                var contact = await contactTask.ConfigureAwait(false);

                if (contact == null)
                {
                    return BadRequest("找不到對應的連絡人");
                }

                string fullName = contact.Attributes["fullname"].ToString();

                if (fullName.EndsWith("(Line)"))
                {
                    // ? 非同步通知
                    var lineProcessor = new LineMessagingProcessorClass();
                    await Task.Run(() => 
                        lineProcessor.NotifyLineBinding(lineUserId),
                        cancellationToken).ConfigureAwait(false);
                    
                    return RedirectToAction("Login", "Authentication");
                }
                else
                {
                    // ? 並行初始化
                    var setupDataTask = Task.Run(() => 
                        InMemoryContext.SetupSmallGroupData(
                            fullName, "LineIdLogin", lineUserId, DateTime.Now, true),
                        cancellationToken);
                    
                    var setupViewBagTask = Task.Run(() => 
                        SetupViewBagForSmallGroup(), 
                        cancellationToken);
                    
                    var ensureDataTask = Task.Run(() => 
                        EnsureIntegrateDataLoaded(lineUserId),
                        cancellationToken);
                    
                    // ? 等待所有初始化完成
                    await Task.WhenAll(setupDataTask, setupViewBagTask, ensureDataTask)
                        .ConfigureAwait(false);

                    return View("~/Views/Home/IntegrateView.cshtml", 
                        InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport);
                }
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception e)
            {
                return HandleError(e, "HandleLineLogin");
            }
        }

        #endregion
    }
}
