using ChurchReport.Models;
using ChurchReport.Tools;
using ChurchReport.ViewModel;
using ChurchReport.ViewModels;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器
    /// 負責處理小組回報、整合式回報、多組回報等相關功能
    /// </summary>
    public class SmallGroupController : BaseChurchController
    {
        #region 建構函式

        public SmallGroupController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService)
            : base(httpContextAccessor, memoryCache, paymentService)
        {
        }

        #endregion

        #region 多小組回報

        /// <summary>
        /// 多小組回報主頁面
        /// 顯示多個小組的統計資訊與管理功能
        /// </summary>
        /// <param name="LoginParameter">登入參數(AccountPassword 或 LineId)</param>
        [Route("/SmallGroup/MultiGroupView/{LoginParameter}")]
        public IActionResult MultiGroupView(string LoginParameter)
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
                    return HandleLineLogin(LoginParameter);
                }
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
        /// 提供單一小組的完整回報功能(點名、代禱、統計)
        /// </summary>
        /// <param name="LoginParameter">登入參數或清單ID</param>
        [Route("/SmallGroup/IntegrateView/{LoginParameter}")]
        public IActionResult IntegrateView(string LoginParameter)
        {
            try
            {
                SetupViewBagForSmallGroup();
                SetupIntegrateViewData(LoginParameter);

                if (LoginParameter != "AccountPassword")
                {
                    return HandleIntegrateViewLogin(LoginParameter);
                }
                else if (LoginParameter == "jquery.js")
                {
                    ViewBag.LoginType = "個人登入";
                    return Ok();
                }
                else
                {
                    return HandleLineLogin(LoginParameter);
                }
            }
            catch (Exception e)
            {
                return HandleError(e, "IntegrateView");
            }
        }

        /// <summary>
        /// 設定整合式頁面資料
        /// </summary>
        private void SetupIntegrateViewData(string loginParameter)
        {
            // 判斷是否需要載入整合資料
            bool shouldLoadData = ShouldLoadIntegrateData(loginParameter);

            if (shouldLoadData)
            {
                string listId = DetermineListId(loginParameter);
                InMemoryContext.ListManager.SetupIntegrateData(listId);
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

            // 多小組統計點擊進入
            if (displayViewType == "MultiGroupView")
            {
                return weeklyReport == null || !weeklyReport.LoadFlag;
            }

            return false;
        }

        /// <summary>
        /// 決定要載入的清單ID
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
        /// </summary>
        /// <param name="key">成員識別碼</param>
        /// <param name="values">更新的欄位值(JSON)</param>
        [HttpPut]
        public IActionResult UpdateSmallGroupPresentRecord(string key, string values)
        {
            try
            {
                var dataList = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList;

                // 使用 Task.Factory.StartNew 進行非同步更新
                Task.Factory.StartNew(() =>
                    dataList.m_SmallGroupData.UpdateMember(key, values),
                    TaskCreationOptions.LongRunning);

                Task.Factory.StartNew(() =>
                    dataList.m_AllMemeberData.UpdateMember(key, values),
                    TaskCreationOptions.LongRunning);

                return Ok();
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
        /// 儲存整合式小組回報資料
        /// 包含出席資料、幸福小組資訊、小組暫停狀態
        /// </summary>
        /// <param name="WeeklyReportData">週報資料(JSON)</param>
        /// <param name="HappyWeekIndex">幸福小組第幾週</param>
        /// <param name="HappyWeekTopic">幸福小組主題</param>
        /// <param name="CheckBox">小組是否暫停</param>
        [HttpPost]
        public async Task<IActionResult> SaveIntegrate(
            string WeeklyReportData,
            string HappyWeekIndex,
            string HappyWeekTopic,
            string CheckBox)
        {
            try
            {
                // 驗證幸福小組必填欄位
                if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.ListEntityName.Contains("幸福"))
                {
                    var validationResult = ValidateHappyGroupFields(HappyWeekIndex, HappyWeekTopic);
                    if (validationResult != null) return validationResult;
                }

                bool pauseCheckBox = CheckBox == "true";

                // 異步上傳資料
                Task.Factory.StartNew(() =>
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
                    ), TaskCreationOptions.LongRunning);

                // 清理已轉介或指派的成員
                CleanupTransferredMembers();

                return Json(new { status = "1", message = "成功上傳了.... !太棒了!" });
            }
            catch (Exception e)
            {
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

        #region 幸福小組欄位更新

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
        /// 處理 LINE 登入
        /// </summary>
        private IActionResult HandleLineLogin(string lineUserId)
        {
            try
            {
                string fullName = ToolUtility.RetrieveContactEntityByLineUserId(lineUserId)
                    .Attributes["fullname"].ToString();

                LineMessagingProcessorClass lineProcessor = new LineMessagingProcessorClass();

                if (fullName.EndsWith("(Line)"))
                {
                    lineProcessor.NotifyLineBinding(lineUserId);
                    return RedirectToAction("Login", "Home");
                }
                else
                {
                    InMemoryContext.SetupSmallGroupData(
                        fullName, "LineIdLogin", lineUserId, DateTime.Now, true);

                    SetupViewBagForSmallGroup();

                    EnsureIntegrateDataLoaded(lineUserId);

                    // 明確指定 View 路徑 (暫時使用 Home 資料夾中的 View)
                    return View("~/Views/Home/IntegrateView.cshtml", InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport);
                }
            }
            catch (Exception e)
            {
                return HandleError(e, "HandleLineLogin");
            }
        }

        #endregion
    }
}
