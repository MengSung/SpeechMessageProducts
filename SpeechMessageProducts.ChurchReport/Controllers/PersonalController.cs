// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/PersonalController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class PersonalController
// 主要成員：PersonalReport、SetupPersonalReportViewModel、SetupPersonalReportViewBag、SetupPersonalGroupPosition、LoadPersonReport、LoadMaintainPersonInfomation、CreateMaintainContactColumnSet、CreateMaintainMemberFromContact、ApplyMaintainContactFields、GetOptionSetMetadataService
// 引用命名空間：ChurchReport.Diagnostics.Profiling、ChurchReport.Models、ChurchReport.Tools、ChurchReport.ViewModels、DevExtreme.AspNet.Data、DevExtreme.AspNet.Mvc、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Diagnostics.Profiling;
using ChurchReport.Models;
using ChurchReport.Tools;
using ChurchReport.ViewModels;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 個人資訊管理控制器
    /// 處理個人資料維護、個人回報等功能
    /// </summary>
    public partial class PersonalController : BaseChurchController
    {
        private static readonly IMemoryCache FallbackOptionSetMetadataCache =
            new MemoryCache(new MemoryCacheOptions());

        /// <summary>
        /// 建立背景工作專用的 DI scope。背景工作可能在 HTTP request 結束後仍執行，
        /// 因此不得捕獲 request scope 的 ToolUtility 或 CRM 連線；scope 由背景工作
        /// 擁有，並在工作結束時由 using 確定性釋放，以防止跨 request 資源洩漏。
        /// </summary>
        private readonly IServiceScopeFactory _scopeFactory;

        private ChurchReport.Services.OptionSetMetadataService _optionSetMetadataService;

        #region 幣構函式

        /// <summary>
        /// 建立個人資訊控制器。
        /// 控制器本身只持有目前 request 的服務；長時間執行的背景工作會透過
        /// <see cref="IServiceScopeFactory"/> 建立獨立 scope，讓 ToolUtility 與 CRM
        /// 連線在背景工作結束時確定性釋放，不會跨 request 或跨使用者重用。
        /// </summary>
        /// <param name="httpContextAccessor">目前 HTTP request 的上下文存取器。</param>
        /// <param name="memoryCache">網站共用的記憶體快取。</param>
        /// <param name="toolUtilityProvider">目前 request scope 的 ToolUtility 提供者。</param>
        /// <param name="connectionPool">CRM 連線池，負責租約的取得與歸還。</param>
        /// <param name="scopeFactory">建立背景工作獨立 DI scope 的工廠。</param>
        public PersonalController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            IServiceScopeFactory scopeFactory)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
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
                // ========================================
                // ✅ 關鍵修復：驗證 Session 並確保資料正確
                // ========================================
                EnsureCorrectUserData();

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
            using var perfPhase = PerfPhase.Measure(HttpContext, "Personal.LoadMaintain");

            // ========================================
            // ✅ 關鍵修復：驗證 Session 並確保資料正確
            // ========================================
            using (PerfPhase.Measure(HttpContext, "Personal.LoadMaintain.EnsureCorrectUserData"))
            {
                EnsureCorrectUserData();
            }

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
                    using (PerfPhase.Measure(HttpContext, "Personal.LoadMaintain.GetDisplayViewType"))
                    {
                        displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
                    }
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
                    using (PerfPhase.Measure(HttpContext, "Personal.LoadMaintain.IsIntegrateDataLoaded"))
                    {
                        integrateFlag = IsIntegrateDataLoaded();
                    }
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
                    using var multiGroupPhase = PerfPhase.Measure(HttpContext, "Personal.LoadMaintain.MultiGroup");

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
                                    EntityCollection memberCollection;
                                    using (PerfPhase.Measure(HttpContext, "Personal.LoadMaintain.MultiGroup.RetrieveMemberListCollection"))
                                    {
                                        memberCollection = toolUtility.RetrieveMemberListCollectionByListId(listGuid);
                                    }

                                    if (memberCollection != null && memberCollection.Entities != null)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 小組 {groupRecord.Name} 找到 {memberCollection.Entities.Count} 個成員");

                                        var contactIds = memberCollection.Entities
                                            .Select(memberEntity => toolUtility.GetEntityLookupAttribute(memberEntity, "entityid"))
                                            .Where(contactId => contactId != Guid.Empty)
                                            .ToList();

                                        var contactsById = RetrieveMaintainContactsByIds(
                                            contactIds,
                                            "Personal.LoadMaintain.MultiGroup.ContactRetrieveBatch");

                                        int memberIndex = 0;
                                        foreach (var memberEntity in memberCollection.Entities)
                                        {
                                            memberIndex++;
                                            try
                                            {
                                                var contactId = toolUtility.GetEntityLookupAttribute(memberEntity, "entityid");
                                                if (contactId == Guid.Empty)
                                                {
                                                    continue;
                                                }

                                                if (!contactsById.TryGetValue(contactId, out var contactEntity))
                                                {
                                                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 找不到 contact: {contactId}");
                                                    continue;
                                                }

                                                var member = CreateMaintainMemberFromContact(contactEntity, contactId, groupRecord.Name);
                                                allMembers.Add(member);

                                                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]   成員 {memberIndex}: {member.FullName}");
                                                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]   ✅ 成功加入成員: {member.FullName}");
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
                    using var singleGroupPhase = PerfPhase.Measure(HttpContext, "Personal.LoadMaintain.SingleGroup");

                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 使用單一小組模式");

                    // ✅ 單一小組模式：改用與多小組相同的查詢邏輯
                    using (PerfPhase.Measure(HttpContext, "Personal.LoadMaintain.SingleGroup.EnsurePersonReportDataLoaded"))
                    {
                        EnsurePersonReportDataLoaded(id);
                    }

                    var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;

                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] weeklyReport is null: {weeklyReport == null}");

                    if (weeklyReport?.m_SmallGroupDataList?.m_AllMemeberData?.Members != null)
                    {
                        var members = weeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members;
                        System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 單一小組模式原有資料: {members.Count} 個成員");

                        // ✅ 修復：為單一小組模式也重新查詢完整欄位
                        var contactIds = members
                            .Where(member => !string.IsNullOrEmpty(member.ContactId) && Guid.TryParse(member.ContactId, out _))
                            .Select(member => Guid.Parse(member.ContactId))
                            .ToList();

                        var contactsById = RetrieveMaintainContactsByIds(
                            contactIds,
                            "Personal.LoadMaintain.SingleGroup.ContactRetrieveBatch");

                        foreach (var member in members)
                        {
                            try
                            {
                                if (string.IsNullOrEmpty(member.ContactId) || !Guid.TryParse(member.ContactId, out var contactGuid))
                                {
                                    continue;
                                }

                                if (!contactsById.TryGetValue(contactGuid, out var contactEntity))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation] 單一小組找不到 contact: {contactGuid}");
                                    continue;
                                }

                                ApplyMaintainContactFields(member, contactEntity);
                                System.Diagnostics.Debug.WriteLine($"[LoadMaintainPersonInfomation]     單一小組-成員: {member.FullName}");
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
                using (PerfPhase.Measure(HttpContext, "Personal.LoadMaintain.DataSourceLoader"))
                {
                    return DataSourceLoader.Load(allMembers, loadOptions);
                }
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

        private static ColumnSet CreateMaintainContactColumnSet()
        {
            return new ColumnSet(
                "contactid",
                "fullname",
                "mobilephone",
                "address2_line1",
                "birthdate",
                "customertypecode",
                "new_spiriitual_identity",
                "new_equipment_status");
        }

        private Dictionary<Guid, Entity> RetrieveMaintainContactsByIds(IEnumerable<Guid> contactIds, string phaseName)
        {
            var contactIdList = contactIds
                .Where(contactId => contactId != Guid.Empty)
                .Distinct()
                .ToList();

            var contactsById = new Dictionary<Guid, Entity>();
            if (contactIdList.Count == 0)
            {
                return contactsById;
            }

            const int batchSize = 500;
            for (int offset = 0; offset < contactIdList.Count; offset += batchSize)
            {
                var batchIds = contactIdList
                    .Skip(offset)
                    .Take(batchSize)
                    .Select(contactId => (object)contactId)
                    .ToArray();

                var query = new QueryExpression("contact")
                {
                    ColumnSet = CreateMaintainContactColumnSet()
                };
                query.Criteria.AddCondition("contactid", ConditionOperator.In, batchIds);

                EntityCollection contactCollection;
                using (PerfPhase.Measure(HttpContext, phaseName))
                {
                    contactCollection = OrganizationService.RetrieveMultiple(query);
                }

                foreach (var contactEntity in contactCollection.Entities)
                {
                    contactsById[contactEntity.Id] = contactEntity;
                }
            }

            return contactsById;
        }

        private Member CreateMaintainMemberFromContact(Entity contactEntity, Guid contactId, string smallGroupName)
        {
            var member = new Member
            {
                SmallGroupName = smallGroupName,
                ContactId = contactId.ToString()
            };

            ApplyMaintainContactFields(member, contactEntity);
            return member;
        }

        private void ApplyMaintainContactFields(Member member, Entity contactEntity)
        {
            if (member == null || contactEntity == null)
            {
                return;
            }

            var toolUtility = ToolUtility;

            member.FullName = toolUtility.GetEntityStringAttribute(contactEntity, "fullname");
            member.Phone = toolUtility.GetEntityStringAttribute(contactEntity, "mobilephone");
            member.Address = toolUtility.GetEntityStringAttribute(contactEntity, "address2_line1");
            member.EquipmentStatus = toolUtility.GetEntityStringAttribute(contactEntity, "new_equipment_status");

            if (contactEntity.Contains("customertypecode"))
            {
                var statusValue = toolUtility.GetOptionSetAttribute(contactEntity, "customertypecode");
                member.Status = GetMembershipStatusText(statusValue);
            }
            else
            {
                member.Status = "";
            }

            if (contactEntity.Contains("new_spiriitual_identity"))
            {
                var spiritualValue = toolUtility.GetOptionSetAttribute(contactEntity, "new_spiriitual_identity");
                member.SpiritualIdentity = GetSpiritualIdentityText(spiritualValue);
            }
            else
            {
                member.SpiritualIdentity = "";
            }

            if (contactEntity.Contains("birthdate"))
            {
                var birthDate = toolUtility.GetEntityDateTimeAttribute(contactEntity, "birthdate");
                member.BirthDate = birthDate != DateTime.MinValue && birthDate.Year > 1
                    ? birthDate
                    : DateTime.MinValue;
            }
            else
            {
                member.BirthDate = DateTime.MinValue;
            }
        }

        private ChurchReport.Services.OptionSetMetadataService GetOptionSetMetadataService()
        {
            if (_optionSetMetadataService != null)
            {
                return _optionSetMetadataService;
            }

            var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache
                ?? FallbackOptionSetMetadataCache;

            _optionSetMetadataService = new ChurchReport.Services.OptionSetMetadataService(
                OrganizationService,
                null,
                memoryCache);

            return _optionSetMetadataService;
        }

        /// <summary>
        /// 將信仰狀態選項值轉換為文字
        /// ✅ 改為使用 OptionSetMetadataService 動態查詢
        /// </summary>
        private string GetSpiritualIdentityText(int optionValue)
        {
            try
            {
                // ✅ 使用 OptionSetMetadataService 動態查詢
                var optionSetService = GetOptionSetMetadataService();

                // 從 Dynamics 365 取得顯示文字
                string displayText = optionSetService.GetOptionSetText("contact", "new_spiriitual_identity", optionValue);

                System.Diagnostics.Debug.WriteLine($"[GetSpiritualIdentityText] 輸入值: {optionValue}, 回傳文字: {displayText}");

                return displayText;
            }
            catch (Exception ex)
            {
                // 如果動態查詢失敗，使用備用的硬編碼對應表
                System.Diagnostics.Debug.WriteLine($"[GetSpiritualIdentityText] 動態查詢失敗，使用備用對應表: {ex.Message}");
                return "-未知-";
            }
        }

        /// <summary>
        /// 將會員身分選項值轉換為文字
        /// ✅ 改為使用 OptionSetMetadataService 動態查詢
        /// </summary>
        private string GetMembershipStatusText(int optionValue)
        {
            try
            {
                // ✅ 使用 OptionSetMetadataService 動態查詢
                var optionSetService = GetOptionSetMetadataService();

                // 從 Dynamics 365 取得顯示文字
                string displayText = optionSetService.GetOptionSetText("contact", "customertypecode", optionValue);

                System.Diagnostics.Debug.WriteLine($"[GetMembershipStatusText] 輸入值: {optionValue}, 回傳文字: {displayText}");

                return displayText;
            }
            catch (Exception ex)
            {
                // 如果動態查詢失敗，使用備用的硬編碼對應表
                System.Diagnostics.Debug.WriteLine($"[GetMembershipStatusText] 動態查詢失敗，使用備用對應表: {ex.Message}");

                return "-未知-";
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
        /// ✅ 已整合大頭照上傳功能
        /// </summary>
        [HttpGet]
        [Route("/Personal/PersonalInfomationView")]
        [Route("/Personal/InfomationView")]
        public IActionResult PersonalInfomationView()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[PersonalInfomationView] 開始載入個人資訊頁面");

                SetupPersonalInfoViewBag();

                InMemoryContext.PersonalInfomationModel.SetPersonalInfomationViewModel();

                // ========================================
                // ✅ 設定 ContactId（用於大頭照上傳功能）
                // ========================================
                var loginContact = InMemoryContext?.PersonalInfomationModel?.m_LoginContact;
                if (loginContact != null)
                {
                    InMemoryContext.PersonalInfomationModel.m_PersonalInfomationViewModel.ContactId = loginContact.Id;
                    System.Diagnostics.Debug.WriteLine($"[PersonalInfomationView] ✅ ContactId 已設定: {loginContact.Id}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[PersonalInfomationView] ⚠️ 無法取得 loginContact，ContactId 未設定");
                }

                // ========================================
                // ✅ 使用新的 View（含大頭照上傳功能）
                // ========================================
                System.Diagnostics.Debug.WriteLine("[PersonalInfomationView] 使用 PersonalInfomationViewWithImage View");
                return View("PersonalInfomationViewWithImage",
                    InMemoryContext.PersonalInfomationModel.m_PersonalInfomationViewModel);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[PersonalInfomationView] ❌ 發生錯誤: {e.Message}");
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

                // ✅ Fire-and-Forget：在背景執行上傳，不等待完成
                // 立即回應使用者，避免長時間等待
                var memberCount = members.Count;
                _ = Task.Run(() =>
                {
                    ToolUtilityClass toolUtility = null;
                    try
                    {
                        // 背景工作擁有獨立 scope；request 結束後仍可安全使用自己的
                        // ToolUtility 與 CRM lease，工作離開 using 範圍時由 DI 釋放。
                        using var scope = _scopeFactory.CreateScope();
                        var toolUtilityProvider = scope.ServiceProvider.GetRequiredService<IToolUtilityProvider>();
                        toolUtility = toolUtilityProvider.GetToolUtility();

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
                            // catch 位於 using scope 之外；不可再使用已釋放的 Scoped ToolUtility。
                            ToolUtilityClass.TraceByLevelStatic(1, 1,
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

        #region API - 取得會員身分清單 (動態從 OptionSet 取得)

        /// <summary>
        /// 取得會員身分清單 (從 contact.customertypecode OptionSet 動態取得)
        /// URL: /Personal/Api/GetMembershipStatusList
        /// </summary>
        [HttpGet]
        [Route("/Personal/Api/GetMembershipStatusList")]
        public IActionResult GetMembershipStatusList()
        {
            try
            {
                // ✅ 使用 OptionSetMetadataService 動態取得會員身分清單
                var optionSetService = GetOptionSetMetadataService();

                // 從 Dynamics 365 取得所有選項的對應表
                var mapping = optionSetService.GetOptionSetMapping("contact", "customertypecode");

                // 提取顯示文字清單
                var statusList = mapping.Keys.ToList();

                System.Diagnostics.Debug.WriteLine($"[GetMembershipStatusList] 取得 {statusList.Count} 個會員身分選項");

                return Json(new
                {
                    success = true,
                    data = statusList,
                    count = statusList.Count
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetMembershipStatusList] 錯誤: {ex.Message}");

                // 備用硬編碼清單
                var fallbackList = new System.Collections.Generic.List<string>
                {
                    "牧師師母", "區牧", "小區長", "小組長", "副小組長",
                    "核心同工", "小組組員", "未入組", "新朋友", "外教會", "結案"
                };

                return Json(new
                {
                    success = true,
                    data = fallbackList,
                    count = fallbackList.Count,
                    warning = "使用備用清單"
                });
            }
        }

        /// <summary>
        /// 取得信仰狀態清單 (從 contact.new_spiriitual_identity OptionSet 動態取得)
        /// URL: /Personal/Api/GetSpiritualIdentityList
        /// </summary>
        [HttpGet]
        [Route("/Personal/Api/GetSpiritualIdentityList")]
        public IActionResult GetSpiritualIdentityList()
        {
            try
            {
                // ✅ 使用 OptionSetMetadataService 動態取得信仰狀態清單
                var optionSetService = GetOptionSetMetadataService();

                // 從 Dynamics 365 取得所有選項的對應表
                var mapping = optionSetService.GetOptionSetMapping("contact", "new_spiriitual_identity");

                // 提取顯示文字清單
                var identityList = mapping.Keys.ToList();

                System.Diagnostics.Debug.WriteLine($"[GetSpiritualIdentityList] 取得 {identityList.Count} 個信仰狀態選項");

                return Json(new
                {
                    success = true,
                    data = identityList,
                    count = identityList.Count
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetSpiritualIdentityList] 錯誤: {ex.Message}");

                // 備用硬編碼清單
                var fallbackList = new System.Collections.Generic.List<string>
                {
                    "-未知-", "基督徒", "已決志", "慕道友", "未信主"
                };

                return Json(new
                {
                    success = true,
                    data = fallbackList,
                    count = fallbackList.Count,
                    warning = "使用備用清單"
                });
            }
        }

        /// <summary>
        /// 取得探訪清單 (從 new_present_record.new_visit OptionSet 動態取得)
        /// URL: /Personal/Api/GetVisitList
        /// </summary>
        [HttpGet]
        [Route("/Personal/Api/GetVisitList")]
        public IActionResult GetVisitList()
        {
            try
            {
                var optionSetService = GetOptionSetMetadataService();

                var mapping = optionSetService.GetOptionSetMapping("new_present_record", "new_visit");
                var visitList = mapping.Keys.ToList();

                System.Diagnostics.Debug.WriteLine($"[GetVisitList] 取得 {visitList.Count} 個探訪選項");

                return Json(new
                {
                    success = true,
                    data = visitList,
                    count = visitList.Count
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetVisitList] 錯誤: {ex.Message}");

                return Json(new
                {
                    success = true,
                    data = new System.Collections.Generic.List<string>(),
                    count = 0,
                    warning = "使用空白清單"
                });
            }
        }

        /// <summary>
        /// 取得婚姻狀態清單 (從 contact.familystatuscode OptionSet 動態取得)
        /// URL: /Personal/Api/GetFamilyStatusList
        /// </summary>
        [HttpGet]
        [Route("/Personal/Api/GetFamilyStatusList")]
        public IActionResult GetFamilyStatusList()
        {
            try
            {
                // ✅ 使用 OptionSetMetadataService 動態取得婚姻狀態清單
                var optionSetService = GetOptionSetMetadataService();

                // 從 Dynamics 365 取得所有選項的對應表
                var mapping = optionSetService.GetOptionSetMapping("contact", "familystatuscode");

                // 提取顯示文字清單
                var statusList = mapping.Keys.ToList();

                System.Diagnostics.Debug.WriteLine($"[GetFamilyStatusList] 取得 {statusList.Count} 個婚姻狀態選項");

                return Json(new
                {
                    success = true,
                    data = statusList,
                    count = statusList.Count
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetFamilyStatusList] 錯誤: {ex.Message}");

                // 備用硬編碼清單
                var fallbackList = new System.Collections.Generic.List<string>
                {
                    "未知", "已婚", "未婚", "離異", "喪偶", "單身", "單親", "失婚", "婚姻", "離婚"
                };

                return Json(new
                {
                    success = true,
                    data = fallbackList,
                    count = fallbackList.Count,
                    warning = "使用備用清單"
                });
            }
        }

        #endregion
    }
}
