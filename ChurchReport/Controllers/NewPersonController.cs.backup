using ChurchReport.Models;
using ChurchReport.Tools;
using ChurchReport.ViewModels;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Threading.Tasks;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 新人關懷與追蹤控制器
    /// 處理新人追蹤、關懷、指派小組等功能
    /// </summary>
    public class NewPersonController : BaseChurchController
    {
        #region 建構函式

        public NewPersonController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService,
            IToolUtilityProvider toolUtilityProvider)
            : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider)
        {
        }

        #endregion

        #region 新人跟進關懷主頁面

        /// <summary>
        /// 新人跟進關懷列表頁面
        /// 顯示需要關懷的新人清單
        /// </summary>
        [HttpGet]
        [Route("/NewPerson/FollowUpView")]
        public IActionResult NewPersonFollowUpView()
        {
            try
            {
                SetupNewPersonFollowUpViewBag();

                return View(InMemoryContext.SmallGroupDataList.m_NewPersonFollowUpData);
            }
            catch (Exception e)
            {
                return HandleError(e, "NewPersonFollowUpView");
            }
        }

        /// <summary>
        /// 設定新人跟進頁面的 ViewBag
        /// </summary>
        private void SetupNewPersonFollowUpViewBag()
        {
            SetupBasicViewBag();
            SetMultiGroupLayoutParameter();

            ViewBag.ListId = InMemoryContext.ListManager.ActiveListId;
        }

        #endregion

        #region 資料載入

        /// <summary>
        /// 載入新人跟進資料
        /// 用於 DevExtreme DataGrid 的資料來源
        /// </summary>
        /// <param name="id">清單ID</param>
        /// <param name="loadOptions">載入選項(分頁、排序、篩選)</param>
        [HttpGet]
        public object LoadNewPersonFollowUp(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsureNewPersonDataLoaded(id);

                var tasks = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_NewPersonFollowUpData.Members;

                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadNewPersonFollowUp");
            }
        }

        /// <summary>
        /// 確保新人跟進資料已載入
        /// </summary>
        private void EnsureNewPersonDataLoaded(string id)
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
        /// 新增新人跟進記錄
        /// </summary>
        /// <param name="values">JSON 格式的資料</param>
        [HttpPost]
        public IActionResult InsertNewPresentRecord(string values)
        {
            try
            {
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_NewPersonFollowUpData.InsertMember(values);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "InsertNewPresentRecord");
            }
        }

        /// <summary>
        /// 更新新人跟進記錄
        /// 包含指派小組、轉介、關懷記錄等操作
        /// </summary>
        /// <param name="key">記錄識別碼</param>
        /// <param name="values">更新的欄位值(JSON)</param>
        [HttpPut]
        public IActionResult UpdateNewPresentRecord(string key, string values)
        {
            try
            {
                // 更新新人跟進資料
                UpdateNewPersonFollowUpData(key, values);

                // 更新全部成員資料
                UpdateAllMemberData(key, values);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateNewPresentRecord");
            }
        }

        /// <summary>
        /// 更新新人跟進關懷資料
        /// </summary>
        private void UpdateNewPersonFollowUpData(string key, string values)
        {
            InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                .m_SmallGroupDataList.m_NewPersonFollowUpData.UpdateMember(key, values);
        }

        /// <summary>
        /// 更新全部成員資料
        /// </summary>
        private void UpdateAllMemberData(string key, string values)
        {
            InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                .m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values);
        }

        /// <summary>
        /// 刪除新人跟進記錄
        /// 同時從多個資料集中移除
        /// </summary>
        /// <param name="key">記錄識別碼</param>
        [HttpDelete]
        public IActionResult DeleteNewPresentRecord(string key)
        {
            try
            {
                var dataList = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList;

                // 從各個資料集中刪除
                dataList.m_SmallGroupData.DeleteMember(key);
                dataList.m_NewPersonFollowUpData.DeleteMember(key);
                dataList.m_SmallGroupData.DeleteMember(key);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "DeleteNewPresentRecord");
            }
        }

        #endregion

        #region 資料儲存

        /// <summary>
        /// 儲存新人跟進資料
        /// </summary>
        /// <param name="aResult">儲存結果</param>
        [HttpPost]
        public IActionResult SaveNewPersonFollowUp(string aResult)
        {
            try
            {
                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveNewPersonFollowUp");
            }
        }

        #endregion

        #region 新增新人

        /// <summary>
        /// 新增新人頁面
        /// 提供新人基本資料輸入表單
        /// </summary>
        [HttpGet]
        [Route("/NewPerson/AddNewPerson")]
        [Route("/NewPerson/NewPerson")]
        public IActionResult NewPerson()
        {
            try
            {
                SetupNewPersonViewBag();
                SetupNewPersonGroupArray();

                return View(InMemoryContext.NewPersonModel.m_PersonFormViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "NewPerson");
            }
        }

        /// <summary>
        /// 設定新增新人頁面的 ViewBag
        /// </summary>
        private void SetupNewPersonViewBag()
        {
            SetupBasicViewBag();
            SetMultiGroupLayoutParameter();
            SetupNewPersonPosition();
        }

        /// <summary>
        /// 設定新人要加入的小組位置
        /// </summary>
        private void SetupNewPersonPosition()
        {
            var multiGroupList = InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData;

            if (multiGroupList.Count == 1)
            {
                // 單一小組 - 不需設定
            }
            else
            {
                string multiGroupIndex = ViewBag.MultiGroupIndex;

                if (multiGroupIndex == "HybridView")
                {
                    InMemoryContext.NewPersonModel.m_PersonFormViewModel.Position =
                        InMemoryContext.ListManager.ActiveListId;
                }
                else if (multiGroupIndex == "SingleMultiGroupView")
                {
                    InMemoryContext.NewPersonModel.m_PersonFormViewModel.Position = "";
                }
                else
                {
                    InMemoryContext.NewPersonModel.m_PersonFormViewModel.Position = "";
                }
            }
        }

        /// <summary>
        /// 設定可選擇的小組陣列
        /// </summary>
        private void SetupNewPersonGroupArray()
        {
            InMemoryContext.NewPersonModel.SetupGroupArray(
                InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData,
                InMemoryContext.ListManager.ActiveListId,
                InMemoryContext.ListManager.LoginType);
        }

        /// <summary>
        /// 儲存新增的新人資料
        /// </summary>
        /// <param name="aPersonFormViewModel">新人表單 ViewModel</param>
        [HttpPost]
        public IActionResult SaveNewPerson(PersonFormViewModel aPersonFormViewModel)
        {
            try
            {
                // 驗證必填欄位
                if (string.IsNullOrEmpty(aPersonFormViewModel.Phone))
                {
                    return Json(new { status = "2", message = "新增新人必須要有行動電話" });
                }

                // 上傳新人資料到 CRM
                string result = UploadNewPersonToCrm(aPersonFormViewModel);

                if (result.Contains("成功"))
                {
                    // 新增成功後的處理
                    HandleSuccessfulNewPersonCreation(aPersonFormViewModel);

                    // 重設表單
                    ResetNewPersonForm();

                    return Json(new { status = "1", message = result });
                }
                else
                {
                    // 新增失敗後的處理
                    ResetNewPersonForm();

                    return Json(new { status = "2", message = result });
                }
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveNewPerson");
            }
        }

        /// <summary>
        /// 上傳新人資料到 CRM
        /// </summary>
        private string UploadNewPersonToCrm(PersonFormViewModel viewModel)
        {
            return InMemoryContext.NewPersonModel.UploadNewPerson(
                InMemoryContext.ListManager.m_Account,
                InMemoryContext.ListManager.m_Password,
                viewModel);
        }

        /// <summary>
        /// 處理新人新增成功後的邏輯
        /// </summary>
        private void HandleSuccessfulNewPersonCreation(PersonFormViewModel viewModel)
        {
            // 如果有指派小組，則加入到小組成員清單
            if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport != null &&
                viewModel.Position != "0")
            {
                viewModel.PresentRecordId = InMemoryContext.NewPersonModel.m_NewContact.PresentRecordId;

                Task.Factory.StartNew(() =>
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList
                        .AddNewPersonToMember(viewModel),
                    TaskCreationOptions.LongRunning);
            }
        }

        /// <summary>
        /// 重設新人表單
        /// </summary>
        private void ResetNewPersonForm()
        {
            InMemoryContext.NewPersonModel.ResetPersonFormViewModel(
                InMemoryContext.NewPersonModel.m_PersonFormViewModel);
        }

        #endregion

        #region 小組指派查詢

        /// <summary>
        /// 取得可指派的小組清單
        /// 用於 Lookup 下拉選單
        /// </summary>
        /// <param name="loadOptions">載入選項</param>
        [HttpGet]
        public object AssignSmallGroupGet(DataSourceLoadOptions loadOptions)
        {
            try
            {
                return DataSourceLoader.Load(
                    InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData,
                    loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "AssignSmallGroupGet");
            }
        }

        #endregion
    }
}
