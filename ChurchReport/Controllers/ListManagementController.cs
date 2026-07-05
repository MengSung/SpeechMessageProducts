// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/ListManagementController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class ListManagementController
// 主要成員：ChurchRoot、SetupListManagementViewBag、LoadChurchRoot、LoadListManagementList、LoadListManagementSmallGroup、LoadListManagementMember、LoadLookupList、PostRacerListManagementMember、AddRaceLeader、DeleteRaceLeader
// 引用命名空間：ChurchReport.Models、ChurchReport.Tools、DevExtreme.AspNet.Data、DevExtreme.AspNet.Mvc、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Caching.Memory、System
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Tools;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 名單管理控制器
    /// 處理教會組織架構管理 (區牧、區長、小組、成員)
    /// </summary>
    public class ListManagementController : BaseChurchController
    {
        #region 建構函式

        public ListManagementController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
        }

        #endregion

        #region 名單管理主頁面

        /// <summary>
        /// 教會組織架構(名單管理)主頁面
        /// 顯示區牧 -> 區長 -> 小組 -> 成員 的樹狀結構
        /// </summary>
        [Route("/ListManagement/ChurchRoot")]
        public IActionResult ChurchRoot()
        {
            try
            {
                SetupListManagementViewBag();

                // 設定名單管理資料
                InMemoryContext.ListManagementDataManager.SetupListManagementData(
                    InMemoryContext.AppointmentsListManager.m_Account,
                    InMemoryContext.AppointmentsListManager.m_Password);

                ViewBag.ListManagementType = InMemoryContext.ListManagementDataManager.ListManagementType;

                return View();
            }
            catch (Exception e)
            {
                return HandleError(e, "ChurchRoot");
            }
        }

        /// <summary>
        /// 設定名單管理頁面的 ViewBag
        /// </summary>
        private void SetupListManagementViewBag()
        {
            SetupBasicViewBag();
            SetMultiGroupLayoutParameter();
        }

        #endregion

        #region 資料載入

        /// <summary>
        /// 載入區牧清單(最上層)
        /// </summary>
        [HttpGet]
        public object LoadChurchRoot(DataSourceLoadOptions loadOptions)
        {
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
                EnsureCorrectUserData();

                if (InMemoryContext.ListManagementDataManager.m_ChurchRoot != null)
                {
                    return DataSourceLoader.Load(
                        InMemoryContext.ListManagementDataManager.m_ChurchRoot.AreaLeaderList,
                        loadOptions);
                }
                return null;
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadChurchRoot");
            }
        }

        /// <summary>
        /// 載入區長清單(第二層)
        /// </summary>
        /// <param name="id">區牧 ID</param>
        [HttpGet]
        public object LoadListManagementList(string id, DataSourceLoadOptions loadOptions)
        {
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
                EnsureCorrectUserData();

                if (InMemoryContext.ListManagementDataManager.m_ChurchRoot != null)
                {
                    var tasks = InMemoryContext.ListManagementDataManager.m_ChurchRoot.AreaLeaderList
                        .Where(e => e.AreaLeaderEntityId == id)
                        .Select(e => e.RaceLeaderList)
                        .FirstOrDefault();

                    return DataSourceLoader.Load(tasks, loadOptions);
                }
                return null;
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadListManagementList");
            }
        }

        /// <summary>
        /// 載入小組清單(第三層)
        /// </summary>
        /// <param name="id">區長 ID</param>
        [HttpGet]
        public object LoadListManagementSmallGroup(string id, DataSourceLoadOptions loadOptions)
        {
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
                EnsureCorrectUserData();

                AreaLeader areaLeader = InMemoryContext.ListManagementDataManager
                    .GetAreaLeaderByRaceLeaderId(id);

                if (areaLeader != null)
                {
                    var tasks = areaLeader.RaceLeaderList
                        .Where(e => e.RaceLeaderEntityId == id)
                        .Select(e => e.SmallGroupList)
                        .FirstOrDefault();

                    return DataSourceLoader.Load(tasks, loadOptions);
                }
                return null;
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadListManagementSmallGroup");
            }
        }

        /// <summary>
        /// 載入小組成員清單(第四層)
        /// </summary>
        /// <param name="id">小組 ID</param>
        [HttpGet]
        public object LoadListManagementMember(string id, DataSourceLoadOptions loadOptions)
        {
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
                EnsureCorrectUserData();

                RaceLeader raceLeader = InMemoryContext.ListManagementDataManager
                    .GetRaceLeaderBySmallGroupId(id);

                if (raceLeader != null)
                {
                    var tasks = raceLeader.SmallGroupList
                        .Where(e => e.SmallGroupId == id)
                        .Select(e => e.ContactMemberList)
                        .FirstOrDefault();

                    return DataSourceLoader.Load(tasks, loadOptions);
                }
                return null;
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadListManagementMember");
            }
        }

        /// <summary>
        /// 載入 Lookup 下拉選單資料
        /// </summary>
        /// <param name="id">資料類型 (換區牧/換區長/指派小組)</param>
        [HttpGet]
        public object LoadLookupList(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                // 修改為 C# 7.3 支援的 switch statement
                switch (id)
                {
                    case "換區牧":
                        return InMemoryContext.ListManagementDataManager.m_AreaLeaderArray;
                    case "換區長":
                        return InMemoryContext.ListManagementDataManager.m_RaceLeaderArray;
                    case "指派至本牧區小組":
                        return InMemoryContext.ListManagementDataManager.m_RaceLeaderSmallGroupArray;
                    case "指派至教會小組":
                        return InMemoryContext.ListManagementDataManager.m_ChurchSmallGroupArray;
                    default:
                        return null;
                }
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadLookupList");
            }
        }

        #endregion

        #region 區長 CRUD 操作

        /// <summary>
        /// 新增區長
        /// </summary>
        [HttpPost]
        public IActionResult PostRacerListManagementMember(string values)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.AddRacerListManagementElement(
                    values,
                    InMemoryContext.PersonalInfomationModel.m_LoginContact);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "PostRacerListManagementMember");
            }
        }

        /// <summary>
        /// 新增區長 (含 CRM 同步)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AddRaceLeader(string key, string values)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.AddRacerOnRowInserting(key, values);

                return Json(new
                {
                    status = InMemoryContext.ListManagementDataManager.m_AddController.Status,
                    RaceEntityId = InMemoryContext.ListManagementDataManager.m_AddController.EntityId,
                    parentid = InMemoryContext.ListManagementDataManager.m_AddController.ParentEntityId,
                    message = InMemoryContext.ListManagementDataManager.m_AddController.Result
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "AddRaceLeader");
            }
        }

        /// <summary>
        /// 刪除區長
        /// </summary>
        [HttpDelete]
        public void DeleteRaceLeader(string key)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.DeleteRaceLeaderByEntityId(key);
            }
            catch (Exception e)
            {
                HandleError(e, "DeleteRaceLeader");
            }
        }

        #endregion

        #region 小組 CRUD 操作

        /// <summary>
        /// 新增小組
        /// </summary>
        [HttpPost]
        public IActionResult PostSmallGroupAction(string values)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.AddSmallGroupManagementElement(values);
                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "PostSmallGroupAction");
            }
        }

        /// <summary>
        /// 新增小組 (含 CRM 同步)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AddSmallGroup(
            string MasterParentID,
            string SmallGroupName,
            string SmallGroupLeaderName)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.AddSmalllGroupOnRowInserted(
                    MasterParentID,
                    SmallGroupName,
                    SmallGroupLeaderName);

                return Json(new
                {
                    status = InMemoryContext.ListManagementDataManager.m_AddController.Status,
                    RaceEntityId = InMemoryContext.ListManagementDataManager.m_AddController.EntityId,
                    parentid = InMemoryContext.ListManagementDataManager.m_AddController.ParentEntityId,
                    message = InMemoryContext.ListManagementDataManager.m_AddController.Result
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "AddSmallGroup");
            }
        }

        /// <summary>
        /// 更新小組資訊
        /// </summary>
        [HttpPut]
        public IActionResult UpdateListManagementSmallGroup(string key, string values)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.UpdateSmallGroupManagementElement(key, values);
                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateListManagementSmallGroup");
            }
        }

        /// <summary>
        /// 更新小組 (含 CRM 同步)
        /// </summary>
        [HttpPut]
        public async Task<IActionResult> UpdateSmallGroup(string key, string values)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.UpdateSmalllGroupOnRowUpdated(key, values);

                return Json(new
                {
                    status = InMemoryContext.ListManagementDataManager.m_AddController.Status,
                    parentid = InMemoryContext.ListManagementDataManager.m_AddController.ParentEntityId,
                    message = InMemoryContext.ListManagementDataManager.m_AddController.Result
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateSmallGroup");
            }
        }

        /// <summary>
        /// 刪除小組
        /// </summary>
        [HttpDelete]
        public void DeleteSmallGroup(string key)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.DeleteSmallGroupByEntityId(key);
            }
            catch (Exception e)
            {
                HandleError(e, "DeleteSmallGroup");
            }
        }

        #endregion

        #region 成員 CRUD 操作

        /// <summary>
        /// 新增成員
        /// </summary>
        [HttpPost]
        public IActionResult PostContactAction(string values)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.AddContactManagementElement(
                    values,
                    InMemoryContext.ListManager.m_Account,
                    InMemoryContext.ListManager.m_Password);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "PostContactAction");
            }
        }

        /// <summary>
        /// 新增成員 (含 CRM 同步)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AddContact(
            string MasterParentID,
            string FulllName,
            string Status,
            string MobilePhone)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.AddContactOnRowInserted(
                    MasterParentID,
                    FulllName,
                    Status,
                    MobilePhone,
                    InMemoryContext.ListManager.m_Account,
                    InMemoryContext.ListManager.m_Password);

                return Json(new
                {
                    status = InMemoryContext.ListManagementDataManager.m_AddController.Status,
                    RaceEntityId = InMemoryContext.ListManagementDataManager.m_AddController.EntityId,
                    parentid = InMemoryContext.ListManagementDataManager.m_AddController.ParentEntityId,
                    message = InMemoryContext.ListManagementDataManager.m_AddController.Result
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "AddContact");
            }
        }

        /// <summary>
        /// 更新成員資訊
        /// </summary>
        [HttpPut]
        public IActionResult UpdateListManagementContactMember(string key, string values)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.UpdateContactMemberManagementElement(key, values);
                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateListManagementContactMember");
            }
        }

        /// <summary>
        /// 更新成員 (含 CRM 同步)
        /// </summary>
        [HttpPut]
        public async Task<IActionResult> UpdateContactMember(string key, string values)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.UpdateContactMemberOnRowUpdated(key, values);

                return Json(new
                {
                    status = InMemoryContext.ListManagementDataManager.m_AddController.Status,
                    parentid = InMemoryContext.ListManagementDataManager.m_AddController.ParentEntityId,
                    message = InMemoryContext.ListManagementDataManager.m_AddController.Result
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateContactMember");
            }
        }

        /// <summary>
        /// 刪除成員
        /// </summary>
        [HttpDelete]
        public void DeleteContact(string key)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.DeleteContactByEntityId(key);
            }
            catch (Exception e)
            {
                HandleError(e, "DeleteContact");
            }
        }

        #endregion

        #region 批次操作

        /// <summary>
        /// 刪除名單元素 (通用)
        /// </summary>
        [HttpDelete]
        public void DeleteListManagement(string key)
        {
            try
            {
                InMemoryContext.ListManagementDataManager.DeleteListManamement(key);
            }
            catch (Exception e)
            {
                HandleError(e, "DeleteListManagement");
            }
        }

        /// <summary>
        /// 儲存所有變更
        /// </summary>
        [HttpPost]
        public IActionResult SaveListManagement()
        {
            try
            {
                InMemoryContext.HappyGroupDataManager.SaveActiveHappyGroup();
                InMemoryContext.HappyGroupDataManager.InitialHappyGroupData(
                    ref InMemoryContext.HappyGroupDataManager.m_ActiveHappyGroupListClass);

                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveListManagement");
            }
        }

        #endregion

        #region 查詢輔助方法

        /// <summary>
        /// 依小組名稱搜尋並返回 Refresh ID
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SaveListManagementContactMember(string key, string values)
        {
            try
            {
                string refreshId = InMemoryContext.ListManagementDataManager
                    .SearchSmallGroupByName(values);

                return Json(new { status = "1", RefreshId = refreshId, message = "查詢成功" });
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveListManagementContactMember");
            }
        }

        /// <summary>
        /// 依區長名稱搜尋並返回 Refresh ID
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SaveListManagementSmallGroup(string key, string values)
        {
            try
            {
                string refreshId = InMemoryContext.ListManagementDataManager
                    .SearchRaceLeaderByName(values);

                return Json(new { status = "1", RefreshId = refreshId, message = "查詢成功" });
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveListManagementSmallGroup");
            }
        }

        #endregion
    }
}
