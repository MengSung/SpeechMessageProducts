using ChurchReport.Models;
using ChurchReport.Tools;
using ChurchReport.ViewModels;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 個人資訊管理控制器
    /// 處理個人資料維護、個人回報等功能
    /// </summary>
    public class PersonalController : BaseChurchController
    {
        #region 建構函式

        public PersonalController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
        : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider, connectionPool)
        {
        }

        #endregion

        #region 個人回報主頁面

        /// <summary>
        /// 個人回報主頁面
        /// 顯示個人出席記錄和代禱事項表單
        /// </summary>
        [HttpGet]
        [Route("/Personal/PersonalReport")]
        public IActionResult PersonalReport()
        {
            try
            {
                SetupPersonalReportViewBag();
                SetupPersonalReportViewModel();

                return View(InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_PersonalReportViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "PersonalReport");
            }
        }

        /// <summary>
        /// 設定個人回報頁面的 ViewModel
        /// </summary>
        private void SetupPersonalReportViewModel()
        {
            // 建立局部變數以支援 ref 參數
            var toolUtility = ToolUtility;
            InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.SetPersonalReportViewModel(
                ref toolUtility,
                InMemoryContext.PersonalInfomationModel.m_LoginContact);
        }

        /// <summary>
        /// 設定個人回報頁面的 ViewBag
        /// </summary>
        private void SetupPersonalReportViewBag()
        {
            SetupBasicViewBag();
            SetMultiGroupLayoutParameter();

            // 設定小組選擇位置
            SetupPersonalGroupPosition();
        }

        /// <summary>
        /// 設定個人所屬小組位置
        /// </summary>
        private void SetupPersonalGroupPosition()
        {
            var multiGroupList = InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData;

            if (multiGroupList.Count == 1)
            {
                InMemoryContext.NewPersonModel.m_PersonFormViewModel.Position =
                    multiGroupList.First().ListEntityId;
            }
            else
            {
                string multiGroupIndex = ViewBag.MultiGroupIndex;

                if (multiGroupIndex == "HybridView")
                {
                    InMemoryContext.NewPersonModel.m_PersonFormViewModel.Position =
                        InMemoryContext.ListManager.ActiveListId;
                }
                else
                {
                    InMemoryContext.NewPersonModel.m_PersonFormViewModel.Position = "";
                }
            }
        }

        #endregion

        #region 資料載入

        /// <summary>
        /// 載入個人回報資料
        /// 用於 DevExtreme DataGrid 的資料來源
        /// </summary>
        /// <param name="id">清單ID</param>
        /// <param name="loadOptions">載入選項(分頁、排序、篩選)</param>
        [HttpGet]
        public object LoadPersonReport(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsurePersonReportDataLoaded(id);

                var tasks = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_AllMemeberData.Members;

                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadPersonReport");
            }
        }

        /// <summary>
        /// 載入維護個人資訊資料
        /// 用於 MaintainPersonInfomationView 的 DataGrid 資料來源
        /// </summary>
        /// <param name="id">清單ID</param>
        /// <param name="loadOptions">載入選項(分頁、排序、篩選)</param>
        [HttpGet]
        public object LoadMaintainPersonInfomation(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsurePersonReportDataLoaded(id);

                var tasks = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_AllMemeberData.Members;

                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadMaintainPersonInfomation");
            }
        }

        /// <summary>
        /// 確保個人回報資料已載入
        /// </summary>
        private void EnsurePersonReportDataLoaded(string id)
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
        /// 新增個人回報記錄
        /// </summary>
        /// <param name="values">JSON 格式的資料</param>
        [HttpPost]
        public IActionResult InsertPersonReport(string values)
        {
            try
            {
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_AllMemeberData.InsertMember(values);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "InsertPersonReport");
            }
        }

        /// <summary>
        /// 更新個人回報記錄
        /// ? 已改造為非同步模式
        /// </summary>
        /// <param name="key">記錄識別碼</param>
        /// <param name="values">更新數據(JSON)</param>
        /// <param name="cancellationToken">取消標記</param>
        [HttpPut]
        public async Task<IActionResult> UpdatePersonReport(
            string key, 
            string values,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // ? 使用非同步更新
                await Task.Run(() =>
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                        .m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values),
                    cancellationToken).ConfigureAwait(false);

                return Ok();
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdatePersonReport");
            }
        }

        /// <summary>
        /// 刪除個人回報記錄
        /// </summary>
        /// <param name="key">記錄識別碼</param>
        [HttpDelete]
        public IActionResult DeletePersonReport(string key)
        {
            try
            {
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_AllMemeberData.DeleteMember(key);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "DeletePersonReport");
            }
        }

        #endregion

        #region 資料儲存

        /// <summary>
        /// 儲存個人回報資料 (DataGrid 模式)
        /// ? 已改造為正確的非同步模式
        /// </summary>
        /// <param name="WeeklyReportData">週報資料(JSON)</param>
        /// <param name="cancellationToken">取消標記</param>
        [HttpPost]
        public async Task<IActionResult> SavePersonReport(
            string WeeklyReportData,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // ? 使用 await 等待上傳完成
                await Task.Run(() =>
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData(
                        InMemoryContext.ListManager.m_SelectDate,
                        InMemoryContext.ListManager.m_Account,
                        InMemoryContext.ListManager.m_Password,
                        InMemoryContext.ListManager.LoginType,
                        InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                        WeeklyReportData,
                        "", "", false
                    ), cancellationToken).ConfigureAwait(false);

                return Json(new { status = "1", message = "資料成功上傳了...." });
            }
            catch (OperationCanceledException)
            {
                return Json(new { status = "0", message = "操作已取消" });
            }
            catch (Exception e)
            {
                return HandleError(e, "SavePersonReport");
            }
        }

        /// <summary>
        /// 儲存個人回報資料表單 (Form 模式)
        /// 用於個人出席、代禱事項等資料的表單提交
        /// ? 已改造為正確的非同步模式
        /// </summary>
        /// <param name="aPersonalReportViewModel">個人回報 ViewModel</param>
        /// <param name="cancellationToken">取消標記</param>
        [HttpPost]
        public async Task<IActionResult> SavePersonalReportForm(
            PersonalReportViewModel aPersonalReportViewModel,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var allMemberData = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_AllMemeberData;

                if (allMemberData?.Members != null)
                {
                    // 個人回報且已加入小組
                    await SavePersonalReportWithSmallGroupAsync(aPersonalReportViewModel, cancellationToken);
                }
                else
                {
                    // 個人回報但未加入小組
                    await SavePersonalReportWithoutSmallGroupAsync(aPersonalReportViewModel, cancellationToken);
                }

                return Json(new { status = "1", message = "資料成功上傳了...." });
            }
            catch (OperationCanceledException)
            {
                return Json(new { status = "0", message = "操作已取消" });
            }
            catch (Exception e)
            {
                return HandleError(e, "SavePersonalReportForm");
            }
        }

        /// <summary>
        /// 儲存已加入小組的個人回報
        /// ? 已改造為非同步模式
        /// </summary>
        private async Task SavePersonalReportWithSmallGroupAsync(
            PersonalReportViewModel viewModel,
            CancellationToken cancellationToken)
        {
            // 處理 ViewModel 結果
            await Task.Run(() =>
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .GetPersonalReportViewModelResult(viewModel),
                cancellationToken).ConfigureAwait(false);

            // ? 使用 await 等待上傳完成
            await Task.Run(() =>
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData(
                    InMemoryContext.ListManager.m_SelectDate,
                    InMemoryContext.ListManager.m_Account,
                    InMemoryContext.ListManager.m_Password,
                    InMemoryContext.ListManager.LoginType,
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                    "個人更新小組回報",
                    "", "", false
                ), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 儲存未加入小組的個人回報
        /// ? 已改造為非同步模式
        /// </summary>
        private async Task SavePersonalReportWithoutSmallGroupAsync(
            PersonalReportViewModel viewModel,
            CancellationToken cancellationToken)
        {
            // 建立臨時變數以避免 ref 參數
            var toolUtility = ToolUtility;
            
            await Task.Run(() =>
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .SavePersonalReportForm(ref toolUtility, viewModel),
                cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region 個人資訊管理

        /// <summary>
        /// 個人資料管理畫面
        /// 顯示與編輯個人基本資料
        /// </summary>
        [HttpGet]
        [Route("/Personal/PersonalInfomationView")]
        [Route("/Personal/InfomationView")]
        public IActionResult PersonalInfomationView()
        {
            try
            {
                SetupPersonalInfoViewBag();

                InMemoryContext.PersonalInfomationModel.SetPersonalInfomationViewModel();

                return View(InMemoryContext.PersonalInfomationModel.m_PersonalInfomationViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "PersonalInfomationView");
            }
        }

        /// <summary>
        /// 設定個人資訊頁面的 ViewBag
        /// </summary>
        private void SetupPersonalInfoViewBag()
        {
            SetupBasicViewBag();
            SetMultiGroupLayoutParameter();
            SetupPersonalGroupPosition();
        }

        /// <summary>
        /// 儲存個人資訊
        /// </summary>
        /// <param name="aPersonalInfomationViewModel">個人資訊 ViewModel</param>
        [HttpPost]
        public IActionResult SavePersonalInfomation(PersonalInfomationViewModel aPersonalInfomationViewModel)
        {
            try
            {
                string result = InMemoryContext.PersonalInfomationModel.UploadPersonalInfomation(
                    InMemoryContext.ListManager.m_Account,
                    InMemoryContext.ListManager.m_Password,
                    aPersonalInfomationViewModel);

                return Json(new { status = "1", message = result });
            }
            catch (Exception e)
            {
                return HandleError(e, "SavePersonalInfomation");
            }
        }

        /// <summary>
        /// 個人資訊維護畫面
        /// 用於維護個人資訊，顯示地圖、資料網格，並允許上傳更新
        /// </summary>
        [HttpGet]
        [Route("/Personal/MaintainPersonInfomationView")]
        [Route("/Personal/MaintainInfomationView")]
        public IActionResult MaintainPersonInfomationView()
        {
            try
            {
                SetupPersonalInfoViewBag();

                // 根據登入類型設定不同的資料
                if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport != null)
                {
                    return View(InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData);
                }
                else
                {
                    return View(new SmallGroupData());
                }
            }
            catch (Exception e)
            {
                return HandleError(e, "MaintainPersonInfomationView");
            }
        }

        #endregion
    }
}
