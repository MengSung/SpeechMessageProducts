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
        #region 幣構函式

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
        /// ? 修復：支援多小組模式，按各小組分別顯示組員資訊
        /// ? 修復：避免覆蓋 WeeklyReport，改用直接查詢
        /// ? 添加詳細調試輸出
        /// </summary>
        /// <param name="id">清單ID</param>
        /// <param name="loadOptions">載入選項(分頁、排序、篩選)</param>
        [HttpGet]
        public object LoadMaintainPersonInfomation(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                // ? 完整的 null 檢查鏈
                if (InMemoryContext.ListManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("[LoadMaintainPersonInfomation] ListManager is null");
                    return DataSourceLoader.Load(new System.Collections.Generic.List<Member>(), loadOptions);
                }

                var allMembers = new System.Collections.Generic.List<Member>();

                // ? 檢查是否為多小組模式
                string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
                bool integrateFlag = IsIntegrateDataLoaded();

                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] id={id}, displayViewType={displayViewType}, integrateFlag={integrateFlag}");

                if (displayViewType == "MultiGroupView" && !integrateFlag)
                {
                    // ? 多小組模式：從各小組載入資料（不覆蓋原有資料）
                    var multiGroupList = InMemoryContext.ListManager.m_MultiGroupList;
                    
                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 進入多小組模式");
                    
                    if (multiGroupList != null && multiGroupList.m_WeeklyReportRecordListData != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 小組數量: {multiGroupList.m_WeeklyReportRecordListData.Count}");
                        
                        // 取得 ToolUtility 實例
                        var toolUtility = ToolUtility;
                        
                        int groupIndex = 0;
                        foreach (var groupRecord in multiGroupList.m_WeeklyReportRecordListData)
                        {
                            groupIndex++;
                            System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 處理第 {groupIndex} 個小組: {groupRecord.Name}, ListId: {groupRecord.ListEntityId}");
                            
                            try
                            {
                                // ? 直接從 CRM 查詢該小組的成員，不使用 SetupIntegrateData
                                System.Guid listGuid;
                                if (System.Guid.TryParse(groupRecord.ListEntityId, out listGuid))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 開始查詢小組 {groupRecord.Name} 的成員...");
                                    
                                    // 查詢名單成員
                                    var memberCollection = toolUtility.RetrieveMemberListCollectionByListId(listGuid);
                                    
                                    if (memberCollection != null && memberCollection.Entities != null)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 小組 {groupRecord.Name} 找到 {memberCollection.Entities.Count} 個成員");
                                        
                                        int memberIndex = 0;
                                        foreach (var memberEntity in memberCollection.Entities)
                                        {
                                            memberIndex++;
                                            try
                                            {
                                                // 取得聯絡人 ID
                                                var contactId = toolUtility.GetEntityLookupAttribute(memberEntity, "entityid");
                                                
                                                if (contactId != System.Guid.Empty)
                                                {
                                                    // 查詢聯絡人詳細資訊
                                                    var contactEntity = toolUtility.RetrieveEntity("contact", contactId);
                                                    
                                                    if (contactEntity != null)
                                                    {
                                                        var fullName = toolUtility.GetEntityStringAttribute(contactEntity, "fullname");
                                                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]   成員 {memberIndex}: {fullName}");
                                                        
                                                        // 建立 Member 物件
                                                        var member = new Member
                                                        {
                                                            SmallGroupName = groupRecord.Name,
                                                            FullName = fullName,
                                                            Phone = toolUtility.GetEntityStringAttribute(contactEntity, "mobilephone"),
                                                            Address = toolUtility.GetEntityStringAttribute(contactEntity, "address2_line1"),
                                                            ContactId = contactId.ToString()
                                                        };
                                                
                                                        // 取得會員身分
                                                        if (contactEntity.Contains("new_membership_status"))
                                                        {
                                                            var statusValue = toolUtility.GetOptionSetAttribute(contactEntity, "new_membership_status");
                                                            member.Status = GetMembershipStatusText(statusValue);
                                                        }
                                                
                                                        // 取得信仰狀態
                                                        if (contactEntity.Contains("new_spiritual_identity"))
                                                        {
                                                            var spiritualIdentity = toolUtility.GetOptionSetAttribute(contactEntity, "new_spiritual_identity");
                                                            member.SpiritualIdentity = GetSpiritualIdentityText(spiritualIdentity);
                                                        }
                                                
                                                        // 取得生日
                                                        if (contactEntity.Contains("birthdate"))
                                                        {
                                                            member.BirthDate = toolUtility.GetEntityDateTimeAttribute(contactEntity, "birthdate");
                                                        }
                                                
                                                        // 取得裝備狀態
                                                        if (contactEntity.Contains("new_equipment_status"))
                                                        {
                                                            member.EquipmentStatus = toolUtility.GetEntityStringAttribute(contactEntity, "new_equipment_status");
                                                        }
                                                
                                                        allMembers.Add(member);
                                                    }
                                                }
                                            }
                                            catch (Exception memberEx)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 處理成員時發生錯誤: {memberEx.Message}");
                                            }
                                        }
                                        
                                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 小組 {groupRecord.Name} 處理完成，累計成員數: {allMembers.Count}");
                                    }
                                    else
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 小組 {groupRecord.Name} 沒有成員資料");
                                    }
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 無法解析 ListEntityId: {groupRecord.ListEntityId}");
                                }
                            }
                            catch (Exception ex)
                            {
                                // 記錄該小組載入失敗，但繼續處理其他小組
                                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 載入小組 {groupRecord.Name} 失敗: {ex.Message}");
                                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 錯誤堆疊: {ex.StackTrace}");
                            }
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 所有小組處理完成，總成員數: {allMembers.Count}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] multiGroupList or m_WeeklyReportRecordListData is null");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 使用單一小組模式");
                    
                    // ? 單一小組模式或 IntegrateView 模式：原有邏輯
                    EnsurePersonReportDataLoaded(id);

                    var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
                    if (weeklyReport?.m_SmallGroupDataList?.m_AllMemeberData?.Members != null)
                    {
                        allMembers = weeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members;
                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 單一小組模式載入 {allMembers.Count} 個成員");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 最終返回 {allMembers.Count} 個成員");
                
                // 返回資料
                return DataSourceLoader.Load(allMembers, loadOptions);
            }
            catch (Exception e)
            {
                // 記錄詳細錯誤資訊
                var errorDetails = new System.Text.StringBuilder();
                errorDetails.AppendLine($"LoadMaintainPersonInfomation 錯誤: {e.Message}");
                errorDetails.AppendLine($"錯誤堆疊: {e.StackTrace}");
                errorDetails.AppendLine($"ListManager is null: {InMemoryContext.ListManager == null}");
                
                if (InMemoryContext.ListManager != null)
                {
                    errorDetails.AppendLine($"DisplayViewType: {InMemoryContext.ListManager.GetDisplayViewType()}");
                    errorDetails.AppendLine($"IntegrateFlag: {IsIntegrateDataLoaded()}");
                }

                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 發生錯誤:\n{errorDetails}");

                return HandleError(e, errorDetails.ToString());
            }
        }

        /// <summary>
        /// 將信仰狀態選項值轉換為文字
        /// </summary>
        private string GetSpiritualIdentityText(int optionValue)
        {
            switch (optionValue)
            {
                case 100000000:
                    return "基督徒";
                case 100000001:
                    return "已決志";
                case 100000002:
                    return "慕道友";
                case 100000003:
                    return "未信主";
                default:
                    return "-未知-";
            }
        }

        /// <summary>
        /// 將會員身分選項值轉換為文字
        /// </summary>
        private string GetMembershipStatusText(int optionValue)
        {
            switch (optionValue)
            {
                case 100000000:
                    return "牧師師母";
                case 100000001:
                    return "區牧";
                case 100000002:
                    return "小區長";
                case 100000003:
                    return "小組長";
                case 100000004:
                    return "副小組長";
                case 100000005:
                    return "核心同工";
                case 100000006:
                    return "小組組員";
                case 100000007:
                    return "未入組";
                case 100000008:
                    return "新朋友";
                case 100000009:
                    return "外教會";
                case 100000010:
                    return "結案";
                default:
                    return "";
            }
        }

        /// <summary>
        /// 確保個人回報資料已載入
        /// ? 添加 null 檢查，防止連鎖失敗
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
        /// </summary>
        /// <param name="key">記錄識別碼</param>
        /// <param name="values">更新的欄位值(JSON)</param>
        [HttpPut]
        public IActionResult UpdatePersonReport(string key, string values)
        {
            try
            {
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_AllMemeberData.UpdateMember(key, values);

                return Ok();
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
        /// 儲存個人回報資料 (DataGrid 方式)
        /// </summary>
        /// <param name="WeeklyReportData">週報資料(JSON)</param>
        [HttpPost]
        public IActionResult SavePersonReport(string WeeklyReportData)
        {
            try
            {
                Task.Factory.StartNew(() =>
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData(
                        InMemoryContext.ListManager.m_SelectDate,
                        InMemoryContext.ListManager.m_Account,
                        InMemoryContext.ListManager.m_Password,
                        InMemoryContext.ListManager.LoginType,
                        InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                        WeeklyReportData,
                        "", "", false
                    ), TaskCreationOptions.LongRunning);

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (Exception e)
            {
                return HandleError(e, "SavePersonReport");
            }
        }

        /// <summary>
        /// 儲存個人回報表單資料 (Form 方式)
        /// 用於個人出席、代禱事項的表單提交
        /// </summary>
        /// <param name="aPersonalReportViewModel">個人回報 ViewModel</param>
        [HttpPost]
        public IActionResult SavePersonalReportForm(PersonalReportViewModel aPersonalReportViewModel)
        {
            try
            {
                var allMemberData = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_AllMemeberData;

                if (allMemberData?.Members != null)
                {
                    // 個人回報且已加入小組
                    SavePersonalReportWithSmallGroup(aPersonalReportViewModel);
                }
                else
                {
                    // 個人回報但未加入小組
                    SavePersonalReportWithoutSmallGroup(aPersonalReportViewModel);
                }

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (Exception e)
            {
                return HandleError(e, "SavePersonalReportForm");
            }
        }

        /// <summary>
        /// 儲存已加入小組的個人回報
        /// </summary>
        private void SavePersonalReportWithSmallGroup(PersonalReportViewModel viewModel)
        {
            InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                .GetPersonalReportViewModelResult(viewModel);

            Task.Factory.StartNew(() =>
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData(
                    InMemoryContext.ListManager.m_SelectDate,
                    InMemoryContext.ListManager.m_Account,
                    InMemoryContext.ListManager.m_Password,
                    InMemoryContext.ListManager.LoginType,
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                    "不需更新小組日誌",
                    "", "", false
                ), TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// 儲存未加入小組的個人回報
        /// </summary>
        private void SavePersonalReportWithoutSmallGroup(PersonalReportViewModel viewModel)
        {
            // 建立局部變數以支援 ref 參數
            var toolUtility = ToolUtility;
            InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                .SavePersonalReportForm(ref toolUtility, viewModel);
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

                // ? 設定 ViewBag.ListId - 用於多小組模式下的資料載入
                // 在多小組模式下，需要傳遞特殊的識別碼
                if (InMemoryContext.ListManager != null)
                {
                    var displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
                    bool integrateFlag = IsIntegrateDataLoaded();

                    if (displayViewType == "MultiGroupView" && !integrateFlag)
                    {
                        // 多小組模式：使用特殊識別碼
                        ViewBag.ListId = "MULTIGROUP_MODE";
                    }
                    else
                    {
                        // 單一小組模式：使用實際的 ListId
                        ViewBag.ListId = InMemoryContext.ListManager.ActiveListId ?? "";
                    }
                }
                else
                {
                    ViewBag.ListId = "";
                }

                // ? 根據登入類型設定不同的資料，添加 null 檢查
                if (InMemoryContext.ListManager != null &&
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport != null &&
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList != null &&
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData != null)
                {
                    return View(InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData);
                }
                else
                {
                    // 返回空的 SmallGroupData
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
