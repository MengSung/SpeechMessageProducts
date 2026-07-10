// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/ApiControllers/SpiritLeaderLookupController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class SpiritLeaderLookupController
// 主要成員：Get、SetupSpiritLeaderList
// 引用命名空間：DevExtreme.AspNet.Data、DevExtreme.AspNet.Mvc、Microsoft.AspNetCore.Mvc、System、System.Linq、ChurchReport.Models、System.Collections.Generic、ChurchReport.WebServiceConnector
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Linq;

using ChurchReport.Models;
using System.Collections.Generic;

using ChurchReport.WebServiceConnector;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers.ApiControllers
{
    [Authorize]
    [Route("api/[controller]/[action]")]
    public class SpiritLeaderLookupController : BaseChurchController
    {
        public SpiritLeaderLookupController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
        }

        [HttpGet]
        public object Get(String id, DataSourceLoadOptions loadOptions)
        {
            if (!CanAccessSpiritLeaderList(id))
            {
                return Forbid();
            }

            SpiritLeaderList aSpiritLeaderList = SetupSpiritLeaderList(id);

            return DataSourceLoader.Load(aSpiritLeaderList.SpiritLeaders, loadOptions);
        }

        private bool CanAccessSpiritLeaderList(string listEntityId)
        {
            EnsureSpiritLeaderListsLoaded();
            return CanAccessRequestedList(
                InMemoryContext?.ListManager?.ActiveListId,
                InMemoryContext?.ListManager?.m_MultiGroupList?.m_WeeklyReportRecordListData?.Select(record => record.ListEntityId),
                listEntityId);
        }

        private void EnsureSpiritLeaderListsLoaded()
        {
            var listManager = InMemoryContext?.ListManager;
            if (listManager == null)
            {
                return;
            }

            var loaded = listManager.m_MultiGroupList?.m_WeeklyReportRecordListData;
            if ((loaded == null || loaded.Count == 0) && !string.IsNullOrEmpty(listManager.m_Password))
            {
                listManager.SetupListManager(
                    listManager.m_Account,
                    listManager.m_Password,
                    listManager.m_SelectDate != default ? listManager.m_SelectDate : DateTime.Now);
            }
        }

        public static bool CanAccessRequestedListForTesting(
            string activeListId,
            IEnumerable<string> groupListIds,
            string requestedListId)
        {
            return CanAccessRequestedList(activeListId, groupListIds, requestedListId);
        }

        private static bool CanAccessRequestedList(
            string activeListId,
            IEnumerable<string> groupListIds,
            string requestedListId)
        {
            if (!Guid.TryParse(requestedListId, out var requestedListGuid) || requestedListGuid == Guid.Empty)
            {
                return false;
            }

            if (Guid.TryParse(activeListId, out var activeListGuid) && activeListGuid == requestedListGuid)
            {
                return true;
            }

            if (groupListIds == null)
            {
                return false;
            }

            return groupListIds.Any(listEntityId =>
                Guid.TryParse(listEntityId, out var recordListGuid) &&
                recordListGuid == requestedListGuid);
        }


        public SpiritLeaderList SetupSpiritLeaderList(String ListEntityId)
        {
            SpiritLeaderList aSpiritLeaderList = new SpiritLeaderList();

            aSpiritLeaderList.SpiritLeaders = new List<SpiritLeader>();

            if (ListEntityId != null)
            {
                DownloadHappyGroup aDownloadHappyGroup = new DownloadHappyGroup();

                String ShepherdLeader = aDownloadHappyGroup.GetSpiritLeaderListString(ListEntityId);

                if (ShepherdLeader != null && ShepherdLeader != "")
                {
                    String[] SpiritLeaderArray = ShepherdLeader.Split(',');

                    for (int i = 0; i < SpiritLeaderArray.Length; i++)
                    {
                        if (SpiritLeaderArray[i] != "")
                        {
                            aSpiritLeaderList.SpiritLeaders.Add
                            (
                                new SpiritLeader
                                {
                                    ID = i + 1,
                                    Name = SpiritLeaderArray[i]
                                }
                            );
                        }
                    }
                }
            }

            return aSpiritLeaderList;
        }

    }

}

