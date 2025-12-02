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
        /// ✅ 修復：多小組模式下，無論是從「回報統計」還是「小組回報」點擊「組員資訊」，都顯示所有小組成員
        /// ✅ 修復：明確指定要查詢的欄位，確保會員身分和信仰狀態正確顯示
        /// ✅ 修復：單一小組模式也使用相同的查詢邏輯，確保會員身分和信仰狀態正確顯示
        /// </summary>
        /// <param name="id">清單ID</param>
        /// <param name="loadOptions">載入選項(分頁、排序、篩線)</param>
        [HttpGet]
        public object LoadMaintainPersonInfomation(string id, DataSourceLoadOptions loadOptions)
        {
            // ✅ 診斷日誌:確認方法被呼叫
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] ⭐ 方法被呼叫了!");
            System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] id 參數: [{id ?? "NULL"}]");
            System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] loadOptions 是否為 null: {loadOptions == null}");
            System.Diagnostics.Debug.WriteLine("========================================");
            
            try
            {
                // ✅ 檢查點 1: InMemoryContext
                System.Diagnostics.Debug.WriteLine("[LoadMaintainPersonInfomation] 檢查點 1: 開始檢查 InMemoryContext");
                
                if (InMemoryContext == null)
                {
                    System.Diagnostics.Debug.WriteLine("[LoadMaintainPersonInfomation] ❌ InMemoryContext is null");
                    return DataSourceLoader.Load(new System.Collections.Generic.List<Member>(), loadOptions);
                }
                
                System.Diagnostics.Debug.WriteLine("[LoadMaintainPersonInfomation] ✅ InMemoryContext 存在");
                
                // ✅ 檢查點 2: ListManager
                System.Diagnostics.Debug.WriteLine("[LoadMaintainPersonInfomation] 檢查點 2: 開始檢查 ListManager");
                
                if (InMemoryContext.ListManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("[LoadMaintainPersonInfomation] ❌ ListManager is null");
                    return DataSourceLoader.Load(new System.Collections.Generic.List<Member>(), loadOptions);
                }
                
                System.Diagnostics.Debug.WriteLine("[LoadMaintainPersonInfomation] ✅ ListManager 存在");

                var allMembers = new System.Collections.Generic.List<Member>();

                // ✅ 檢查點 3: GetDisplayViewType
                System.Diagnostics.Debug.WriteLine("[LoadMaintainPersonInfomation] 檢查點 3: 呼叫 GetDisplayViewType");
                
                string displayViewType = null;
                try
                {
                    displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] ✅ displayViewType = {displayViewType}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] ❌ GetDisplayViewType 失敗: {ex.Message}");
                    return DataSourceLoader.Load(new System.Collections.Generic.List<Member>(), loadOptions);
                }
                
                // ✅ 檢查點 4: IsIntegrateDataLoaded
                System.Diagnostics.Debug.WriteLine("[LoadMaintainPersonInfomation] 檢查點 4: 呼叫 IsIntegrateDataLoaded");
                
                bool integrateFlag = false;
                try
                {
                    integrateFlag = IsIntegrateDataLoaded();
                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] ✅ integrateFlag = {integrateFlag}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] ❌ IsIntegrateDataLoaded 失敗: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] id={id}, displayViewType={displayViewType}, integrateFlag={integrateFlag}");

                // ✅ 關鍵修復：只要是多小組環境（displayViewType == "MultiGroupView"），
                // 就應該載入所有小組的成員，不管 integrateFlag 的值
                if (displayViewType == "MultiGroupView")
                {
                    // ✅ 多小組模式：從各小組載入資料
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
                                // ✅ 直接從 CRM 查詢該小組的成員
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
                                                    // ✅ 查詢標準欄位和正確的自訂欄位
                                                    var columnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(
                                                        "contactid",
                                                        "fullname",
                                                        "mobilephone",
                                                        "address2_line1",
                                                        "birthdate",
                                                        "customertypecode", // ✅ 會員身分
                                                        "new_spiriitual_identity", // ✅ 信仰狀態 (注意拼字)
                                                        "new_equipment_status" // ✅ 裝備狀態欄位
                                                    );
                                                    
                                                    var contactEntity = toolUtility.m_Crm2011OrganizationService.Retrieve("contact", contactId, columnSet);
                                                
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
                                                            ContactId = contactId.ToString(),
                                                            EquipmentStatus = "" // ❌ 欄位不存在
                                                        };
                                                
                                                        // ✅ 取得會員身分
                                                        if (contactEntity.Contains("customertypecode"))
                                                        {
                                                            var statusValue = toolUtility.GetOptionSetAttribute(contactEntity, "customertypecode");
                                                            member.Status = GetMembershipStatusText(statusValue);
                                                            System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]     會員身分: {member.Status} (值: {statusValue})");
                                                        }
                                                        else
                                                        {
                                                            member.Status = "";
                                                            System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]     會員身分: 欄位不存在");
                                                        }
                                                        
                                                        // ✅ 取得信仰狀態
                                                        if (contactEntity.Contains("new_spiriitual_identity"))
                                                        {
                                                            var spiritualValue = toolUtility.GetOptionSetAttribute(contactEntity, "new_spiriitual_identity");
                                                            member.SpiritualIdentity = GetSpiritualIdentityText(spiritualValue);
                                                            System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]     信仰狀態: {member.SpiritualIdentity} (值: {spiritualValue})");
                                                        }
                                                        else
                                                        {
                                                            member.SpiritualIdentity = "";
                                                            System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]     信仰狀態: 欄位不存在");
                                                        }

                                                        // ✅裝備狀態
                                                        member.EquipmentStatus = toolUtility.GetEntityStringAttribute(contactEntity, "new_equipment_status");

                                                        // ✅ 取得生日
                                                        if (contactEntity.Contains("birthdate"))
                                                        {
                                                            member.BirthDate = toolUtility.GetEntityDateTimeAttribute(contactEntity, "birthdate");
                                                        }
                                                
                                                        allMembers.Add(member);
                                                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]   ✅ 成功加入成員: {fullName}");
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
                    
                    // ✅ 單一小組模式：改用與多小組相同的查詢邏輯
                    EnsurePersonReportDataLoaded(id);

                    var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
                    
                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] weeklyReport is null: {weeklyReport == null}");
                    
                    if (weeklyReport?.m_SmallGroupDataList?.m_AllMemeberData?.Members != null)
                    {
                        var members = weeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members;
                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 單一小組模式原有資料: {members.Count} 個成員");
                        
                        // ✅ 修復：為單一小組模式也重新查詢完整欄位
                        var toolUtility = ToolUtility;
                        
                        foreach (var member in members)
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(member.ContactId))
                                {
                                    System.Guid contactGuid;
                                    if (System.Guid.TryParse(member.ContactId, out contactGuid))
                                    {
                                        // ✅ 查詢標準欄位和正確的自訂欄位
                                        var columnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(
                                            "contactid",
                                            "fullname",
                                            "mobilephone",
                                            "address2_line1",
                                            "birthdate",
                                            "customertypecode", // ✅ 會員身分
                                            "new_spiriitual_identity", // ✅ 信仰狀態 (注意拼字)
                                            "new_equipment_status" // ✅ 裝備狀態欄位
                                        );
                                        
                                        var contactEntity = toolUtility.m_Crm2011OrganizationService.Retrieve("contact", contactGuid, columnSet);
                                        
                                        if (contactEntity != null)
                                        {
                                            // ✅ 更新會員身分
                                            if (contactEntity.Contains("customertypecode"))
                                            {
                                                var statusValue = toolUtility.GetOptionSetAttribute(contactEntity, "customertypecode");
                                                member.Status = GetMembershipStatusText(statusValue);
                                                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]     單一小組-會員身分: {member.Status}");
                                            }
                                            else
                                            {
                                                member.Status = "";
                                            }
                                            

                                            
                                            // ✅ 更新信仰狀態
                                            if (contactEntity.Contains("new_spiriitual_identity"))
                                            {
                                                var spiritualValue = toolUtility.GetOptionSetAttribute(contactEntity, "new_spiriitual_identity");
                                                member.SpiritualIdentity = GetSpiritualIdentityText(spiritualValue);
                                                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]     單一小組-信仰狀態: {member.SpiritualIdentity}");
                                            }
                                            else
                                            {
                                                member.SpiritualIdentity = "";
                                            }

                                            // ✅裝備狀態
                                            member.EquipmentStatus = toolUtility.GetEntityStringAttribute(contactEntity, "new_equipment_status");

                                            // ✅ 取得生日
                                            if (contactEntity.Contains("birthdate"))
                                            {
                                                member.BirthDate = toolUtility.GetEntityDateTimeAttribute(contactEntity, "birthdate");
                                            }

                                            System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]     單一小組-成員: {member.FullName}");
                                        }
                                    }
                                }
                            }
                            catch (Exception memberEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 單一小組模式-處理成員 {member.FullName} 時發生錯誤: {memberEx.Message}");
                            }
                        }
                        
                        // ✅ 修復：正確賦值 allMembers
                        allMembers = members;
                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 單一小組模式載入 {allMembers.Count} 個成員（已更新完整欄位）");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 單一小組模式：無可用的成員資料");
                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]   weeklyReport: {weeklyReport != null}");
                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]   m_SmallGroupDataList: {weeklyReport?.m_SmallGroupDataList != null}");
                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]   m_AllMemeberData: {weeklyReport?.m_SmallGroupDataList?.m_AllMemeberData != null}");
                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]   Members: {weeklyReport?.m_SmallGroupDataList?.m_AllMemeberData?.Members != null}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 總計返回 {allMembers.Count} 筆資料");
                
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
                case 100000004:
                    return "-未知-";
                case 100000001:
                    return "基督徒";
                case 100000002:
                    return "已決志";
                case 100000005:
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
            // ✅ 診斷日誌
            System.Diagnostics.Debug.WriteLine($"[GetMembershipStatusText] 輸入值: {optionValue}");
            
            switch (optionValue)
            {
                case 100000006:
                    return "牧師師母";
                case 100000002:
                    return "區牧";
                case 100000003:
                    return "小區長";
                case 100000008:
                    return "小組長";
                case 100000009:
                    return "副小組長";
                case 100000012:
                    return "核心同工";
                case 1:
                    return "小組組員";
                case 100000005:
                    return "幸福BEST";
                case 100000004:
                    return "未入組";
                case 100000000:
                    return "新朋友";
                case 100000007:
                    return "外教會";
                case 100000001:
                    return "結案";
                default:
                    System.Diagnostics.Debug.WriteLine($"[GetMembershipStatusText] ⚠️ 未知的值: {optionValue}");
                    return "未知";

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
        /// 將會員身分文字轉換為選項值
        /// </summary>
        private int GetMembershipStatusValue(string statusText)
        {
            switch (statusText)
            {
                case "牧師師母":
                    return 100000000;
                case "區牧":
                    return 100000001;
                case "小區長":
                    return 100000002;
                case "小組長":
                    return 100000003;
                case "副小組長":
                    return 100000004;
                case "核心同工":
                    return 100000005;
                case "小組組員":
                    return 100000006;
                case "未入組":
                    return 100000007;
                case "新朋友":
                    return 100000008;
                case "外教會":
                    return 100000009;
                case "結案":
                    return 100000010;
                default:
                    return -1;
            }
        }

        /// <summary>
        /// 將信仰狀態文字轉換為選項值
        /// </summary>
        private int GetSpiritualIdentityValue(string spiritualText)
        {
            switch (spiritualText)
            {
                case "基督徒":
                    return 100000000;
                case "已決志":
                    return 100000001;
                case "慕道友":
                    return 100000002;
                case "未信主":
                    return 100000003;
                default:
                    return -1;
            }
        }

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
        /// 儲存維護個人資訊（組員資料）
        /// 用於 MaintainPersonInfomationView 的上傳按鈕
        /// ✅ 改為 Fire-and-Forget 模式，立即回應使用者，在背景處理上傳
        /// ✅ 修復：在進入背景任務前先取得 ToolUtility 實例
        /// </summary>
        /// <param name="aResult">組員資料 JSON 字串</param>
        [HttpPost]
        public IActionResult SaveMaintainPersonInfomation(string aResult)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 開始處理");
                System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 資料長度: {aResult?.Length ?? 0}");

                if (string.IsNullOrWhiteSpace(aResult))
                {
                    return Json(new { status = "0", message = "沒有資料需要上傳" });
                }

                // 解析 JSON 資料（快速驗證）
                System.Collections.Generic.List<Member> members = null;
                
                try
                {
                    members = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<Member>>(aResult);
                }
                catch (Newtonsoft.Json.JsonException jsonEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] JSON 解析錯誤: {jsonEx.Message}");
                    
                    // 嘗試修復常見的 JSON 格式問題
                    try
                    {
                        var fixedJson = aResult.Replace("'", "\"");
                        members = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<Member>>(fixedJson);
                        System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] JSON 修復成功");
                    }
                    catch (Exception retryEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] JSON 修復失敗: {retryEx.Message}");
                        return Json(new { status = "0", message = $"JSON 格式錯誤: {jsonEx.Message}" });
                    }
                }

                if (members == null || members.Count == 0)
                {
                    return Json(new { status = "0", message = "沒有有效的資料需要上傳" });
                }

                System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 成功解析到 {members.Count} 筆資料");

                // ✅ 關鍵修復：在進入背景任務前先取得 ToolUtility 實例
                // 因為在背景執行緒中無法安全訪問 Controller 的實例成員
                var toolUtility = ToolUtility;
                
                if (toolUtility == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] ToolUtility 為 null，無法執行上傳");
                    return Json(new { status = "0", message = "系統錯誤：ToolUtility 未初始化" });
                }

                // ✅ Fire-and-Forget：在背景執行上傳，不等待完成
                // 立即回應使用者，避免長時間等待
                var memberCount = members.Count;
                _ = Task.Run(() =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 開始背景上傳 {memberCount} 筆資料...");

                        int successCount = 0;
                        int errorCount = 0;
                        int skippedCount = 0;
                        var errors = new System.Collections.Generic.List<string>();

                        // 逐筆更新成員資料
                        foreach (var member in members)
                        {
                            try
                            {
                                if (string.IsNullOrWhiteSpace(member.ContactId))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 跳過無 ContactId 的成員: {member.FullName}");
                                    skippedCount++;
                                    continue;
                                }

                                System.Guid contactGuid;
                                if (!System.Guid.TryParse(member.ContactId, out contactGuid))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 無效的 ContactId: {member.ContactId}");
                                    errorCount++;
                                    errors.Add($"{member.FullName}: 無效的聯絡人 ID");
                                    continue;
                                }

                                // ✅ 重新從 CRM 取得最新的聯絡人實體
                                var contactEntity = toolUtility.RetrieveEntity("contact", contactGuid);

                                if (contactEntity == null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 找不到聯絡人: {contactGuid}");
                                    errorCount++;
                                    errors.Add($"{member.FullName}: 找不到聯絡人記錄");
                                    continue;
                                }

                                // ✅ 建立要更新的實體（只包含要變更的欄位）
                                var entityToUpdate = new Microsoft.Xrm.Sdk.Entity("contact", contactGuid);
                                bool hasChanges = false;

                                // ✅ 更新行動電話（改進空值處理）
                                var currentPhone = contactEntity.Contains("mobilephone") 
                                    ? (contactEntity.GetAttributeValue<string>("mobilephone") ?? "")
                                    : "";
                                var newPhone = member.Phone ?? "";
                                

                                // 移除空白字元後比較
                                currentPhone = currentPhone.Trim();
                                newPhone = newPhone.Trim();
                                
                                if (!string.IsNullOrEmpty(newPhone) && !string.Equals(currentPhone, newPhone, StringComparison.OrdinalIgnoreCase))
                                {
                                    entityToUpdate["mobilephone"] = newPhone;
                                    hasChanges = true;
                                    System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] {member.FullName}: 更新電話 [{currentPhone}] -> [{newPhone}]");
                                }

                                // ✅ 更新地址（改進空值處理）
                                var currentAddress = contactEntity.Contains("address2_line1") 
                                    ? (contactEntity.GetAttributeValue<string>("address2_line1") ?? "")
                                    : "";
                                var newAddress = member.Address ?? "";
                                

                                // 移除空白字元後比較
                                currentAddress = currentAddress.Trim();
                                newAddress = newAddress.Trim();
                                
                                if (!string.IsNullOrEmpty(newAddress) && !string.Equals(currentAddress, newAddress, StringComparison.OrdinalIgnoreCase))
                                {
                                    entityToUpdate["address2_line1"] = newAddress;
                                    hasChanges = true;
                                    System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] {member.FullName}: 更新地址 [{currentAddress}] -> [{newAddress}]");
                                }

                                // ✅ 更新生日（改進日期比對）
                                if (member.BirthDate != DateTime.MinValue && member.BirthDate.Year > 1900)
                                {
                                    var currentBirthDate = contactEntity.Contains("birthdate") 
                                        ? (contactEntity.GetAttributeValue<DateTime?>("birthdate") ?? DateTime.MinValue)
                                        : DateTime.MinValue;

                                    // 轉換為本地時間並只比較日期部分
                                    if (currentBirthDate != DateTime.MinValue)
                                    {
                                        currentBirthDate = currentBirthDate.ToLocalTime();
                                    }
                                    
                                    if (currentBirthDate == DateTime.MinValue || currentBirthDate.Date != member.BirthDate.Date)
                                    {
                                        entityToUpdate["birthdate"] = member.BirthDate;
                                        hasChanges = true;
                                        System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] {member.FullName}: 更新生日 [{currentBirthDate:yyyy-MM-dd}] -> [{member.BirthDate:yyyy-MM-dd}]");
                                    }
                                }

                                // ✅ 如果有變更，則更新
                                if (hasChanges)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 準備更新 {member.FullName} 的資料到 CRM...");
                                    toolUtility.UpdateEntity(entityToUpdate);
                                    successCount++;
                                    System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] ✅ 成功更新: {member.FullName}");
                                }
                                else
                                {
                                    skippedCount++;
                                    System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] ⏭️ 無變更，跳過: {member.FullName}");
                                }
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                var errorMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                                errors.Add($"{member.FullName}: {errorMsg}");
                                System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] ❌ 更新失敗: {member.FullName}, 錯誤: {errorMsg}");
                            }
                        }

                        // 組合完成訊息
                        var message = $"背景處理完成！成功更新: {successCount} 筆";
                        if (skippedCount > 0)
                        {
                            message += $", 無變更: {skippedCount} 筆";
                        }
                        if (errorCount > 0)
                        {
                            message += $", 失敗: {errorCount} 筆";
                            if (errors.Count > 0)
                            {
                                message += "\n錯誤詳情:\n" + string.Join("\n", errors.Take(5));
                                if (errors.Count > 5)
                                {
                                    message += $"\n...以及其他 {errors.Count - 5} 個錯誤";
                                }
                            }
                        }

                        System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] {message}");
                    }
                    catch (Exception ex)
                    {
                        // 背景任務的錯誤記錄到 Debug 輸出
                        System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 背景上傳失敗: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 錯誤堆疊:\n{ex.StackTrace}");
                        
                        // 記錄到追蹤日誌
                        try
                        {
                            toolUtility?.TraceByLevel(1, 1, 
                                $"SaveMaintainPersonInfomation 背景上傳失敗: {ex.Message}\n{ex.StackTrace}");
                        }
                        catch
                        {
                            // 追蹤失敗不影響
                        }
                    }
                });

                // ✅ 立即回應使用者，不等待上傳完成
                System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 立即回應使用者，背景處理 {memberCount} 筆資料中...");
                return Json(new { status = "1", message = $"已送出 {memberCount} 筆資料，正在背景上傳中..." });
            }
            catch (Exception e)
            {
                var errorMessage = $"啟動上傳失敗: {e.Message}";
                System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 發生錯誤: {e.Message}");
                System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 錯誤堆疊:\n{e.StackTrace}");
                
                if (e.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 內部錯誤: {e.InnerException.Message}");
                    errorMessage += $" (內部錯誤: {e.InnerException.Message})";
                }
                
                return Json(new { status = "0", message = errorMessage });
            }
        }

        /// <summary>
        /// 更新單筆維護個人資訊
        /// 用於 DataGrid 的儲存按鈕（單筆更新）        
        /// </summary>
        /// <param name="key">ContactId</param>
        /// <param name="values">更新的欄位值(JSON)</param>
        [HttpPut]
        public IActionResult UpdateMaintainPersonInfomation(string key, string values)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateMaintainPersonInfomation] key={key}, values={values}");

                if (string.IsNullOrWhiteSpace(key))
                {
                    return BadRequest("缺少 ContactId");
                }

                System.Guid contactGuid;
                if (!System.Guid.TryParse(key, out contactGuid))
                {
                    return BadRequest("無效的 ContactId");
                }

                // 解析更新的欄位
                var updateValues = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>>(values);

                if (updateValues == null || updateValues.Count == 0)
                {
                    return Ok(); // 沒有變更
                }

                // 取得 ToolUtility 實例
                var toolUtility = ToolUtility;

                // 建立要更新的實體（只包含要變更的欄位）
                var entityToUpdate = new Microsoft.Xrm.Sdk.Entity("contact", contactGuid);
                bool hasChanges = false;

                // 更新行動電話
                if (updateValues.ContainsKey("Phone"))
                {
                    var phoneValue = updateValues["Phone"].GetString();
                    if (!string.IsNullOrWhiteSpace(phoneValue))
                    {
                        entityToUpdate["mobilephone"] = phoneValue;
                        hasChanges = true;
                        System.Diagnostics.Debug.WriteLine($"[UpdateMaintainPersonInfomation] 更新電話: {phoneValue}");
                    }
                }

                // 更新地址
                if (updateValues.ContainsKey("Address"))
                {
                    var addressValue = updateValues["Address"].GetString();
                    if (!string.IsNullOrWhiteSpace(addressValue))
                    {
                        entityToUpdate["address2_line1"] = addressValue;
                        hasChanges = true;
                        System.Diagnostics.Debug.WriteLine($"[UpdateMaintainPersonInfomation] 更新地址: {addressValue}");
                    }
                }

                // 更新生日
                if (updateValues.ContainsKey("BirthDate"))
                {
                    var birthDateString = updateValues["BirthDate"].GetString();
                    if (!string.IsNullOrWhiteSpace(birthDateString) && DateTime.TryParse(birthDateString, out DateTime birthDate))
                    {
                        if (birthDate.Year > 1900)
                        {
                            entityToUpdate["birthdate"] = birthDate;
                            hasChanges = true;
                            System.Diagnostics.Debug.WriteLine($"[UpdateMaintainPersonInfomation] 更新生日: {birthDate:yyyy-MM-dd}");
                        }
                    }
                }

                // ✅ 跳過會員身分和信仰狀態的更新（欄位不存在）
                if (updateValues.ContainsKey("Status") || updateValues.ContainsKey("SpiritualIdentity"))
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateMaintainPersonInfomation] 跳過會員身分/信仰狀態更新（欄位不存在）");
                }

                // 如果有變更，則更新
                if (hasChanges)
                {
                    toolUtility.UpdateEntity(entityToUpdate);
                    System.Diagnostics.Debug.WriteLine($"[UpdateMaintainPersonInfomation] 成功更新: {contactGuid}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateMaintainPersonInfomation] 無變更: {contactGuid}");
                }

                return Ok();
            }
            catch (Exception e)
            {
                var errorMsg = e.InnerException != null ? e.InnerException.Message : e.Message;
                System.Diagnostics.Debug.WriteLine($"[UpdateMaintainPersonInfomation] 發生錯誤: {errorMsg}");
                System.Diagnostics.Debug.WriteLine($"[UpdateMaintainPersonInfomation] 錯誤堆疊:\n{e.StackTrace}");
                return StatusCode(500, $"更新失敗: {errorMsg}");
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

                // ✅ 設定 ViewBag.ListId - 用於多小組模式下的資料載入
                if (InMemoryContext.ListManager != null)
                {
                    var displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
                    bool integrateFlag = IsIntegrateDataLoaded();

                    System.Diagnostics.Debug.WriteLine($"[MaintainPersonInfomationView] displayViewType={displayViewType}, integrateFlag={integrateFlag}");

                    if (displayViewType == "MultiGroupView")
                    {
                        // ✅ 多小組模式：使用特殊識別碼
                        ViewBag.ListId = "MULTIGROUP_MODE";
                        System.Diagnostics.Debug.WriteLine($"[MaintainPersonInfomationView] 設定 ListId = MULTIGROUP_MODE");
                    }
                    else
                    {
                        // ✅ 單一小組模式：使用實際的 ListId
                        var activeListId = InMemoryContext.ListManager.ActiveListId;
                        
                        if (!string.IsNullOrWhiteSpace(activeListId))
                        {
                            ViewBag.ListId = activeListId;
                            System.Diagnostics.Debug.WriteLine($"[MaintainPersonInfomationView] 設定 ListId = {activeListId}");
                        }
                        else
                        {
                            // ✅ 如果 ActiveListId 為空，嘗試從 MultiGroupList 取得第一個小組的 ListId
                            var multiGroupList = InMemoryContext.ListManager.m_MultiGroupList;
                            if (multiGroupList != null && 
                                multiGroupList.m_WeeklyReportRecordListData != null && 
                                multiGroupList.m_WeeklyReportRecordListData.Count > 0)
                            {
                                ViewBag.ListId = multiGroupList.m_WeeklyReportRecordListData[0].ListEntityId;
                                System.Diagnostics.Debug.WriteLine($"[MaintainPersonInfomationView] 從 MultiGroupList 取得 ListId = {ViewBag.ListId}");
                            }
                            else
                            {
                                // ✅ 最後的備選方案：使用 MULTIGROUP_MODE
                                ViewBag.ListId = "MULTIGROUP_MODE";
                                System.Diagnostics.Debug.WriteLine($"[MaintainPersonInfomationView] 使用備選方案 ListId = MULTIGROUP_MODE");
                            }
                        }
                    }
                }
                else
                {
                    ViewBag.ListId = "";
                    System.Diagnostics.Debug.WriteLine($"[MaintainPersonInfomationView] ListManager is null, 設定 ListId = 空字串");
                }

                // ✅ 根據登入類型設定不同的資料，添加 null 檢查
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
