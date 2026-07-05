// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ListOperations/IListService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IListService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System、System.Collections.Generic、System.Threading、System.Threading.Tasks、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Client
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;

namespace ToolUtilityNameSpace.ListOperations
{
    public interface IListService
    {
        #region 同步方法 (向下相容)

        // SDK-based member management (recommended for bulk operations)
        void AddMembers(Guid listGuid, List<Guid> memberGuidList);
        void AddMembersUsingSdk(Guid listGuid, List<Guid> memberGuidList, IOrganizationService service);
        void RemoveMember(Guid listGuid, Guid memberGuid);
        void RemoveMemberUsingSdk(Guid listGuid, Guid memberGuid, IOrganizationService service);

        // Member list retrieval
        EntityCollection RetrieveMemberListCollectionByListId(Guid listId);
        EntityCollection RetrieveMemberListCollectionByListIdUsingService(IOrganizationService externalService, Guid listId);
        EntityCollection RetrieveMemberListCollectionByListIdUsingProxy(IOrganizationService externalService, Guid listId);

        // Dynamic member list retrieval
        EntityCollection RetrieveDynamicMemberList(Guid listId);
        EntityCollection RetrieveDynamicMemberListUsingService(IOrganizationService externalService, Guid listId);
        EntityCollection RetrieveDynamicMemberListUsingProxy(IOrganizationService externalService, Guid listId);

        // New: list entity fetches
        EntityCollection RetrieveLists();
        EntityCollection RetrieveSmallGroupLists();

        EntityCollection QueryListByContactId(Guid contactId, string associationName);
        System.Collections.ArrayList GetAllMemberDataFromList(Guid listEntityId);

        /// <summary>
        /// 根據名單名稱查詢名單實體
        /// </summary>
        Entity RetrieveListEntityByName(string listName);

        /// <summary>
        /// 根據連絡人查詢所屬的名單
        /// </summary>
        EntityCollection RetrieveListByContact(string contactName);

        /// <summary>
        /// 根據族群領袖查詢名單
        /// </summary>
        EntityCollection RetrieveListByRacerLeader(string contactName, string contactId);

        #endregion

        #region 非同步批量操作 (新增 - Phase 2.3)

        /// <summary>
        /// 批量並行添加成員到名單 (非同步)
        /// 使用批次 + 並行處理，大幅提升效能
        /// </summary>
        /// <param name="listGuid">名單ID</param>
        /// <param name="memberGuidList">成員ID列表</param>
        /// <param name="batchSize">批次大小 (預設50)</param>
        /// <param name="cancellationToken">取消標記</param>
        /// <returns>成功添加的成員數</returns>
        Task<int> AddMembersAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int batchSize = 50,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量並行移除名單成員 (非同步)
        /// </summary>
        /// <param name="listGuid">名單ID</param>
        /// <param name="memberGuidList">成員ID列表</param>
        /// <param name="batchSize">批次大小 (預設50)</param>
        /// <param name="cancellationToken">取消標記</param>
        /// <returns>成功移除的成員數</returns>
        Task<int> RemoveMembersAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int batchSize = 50,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 使用 CRM SDK 批量添加成員 (非同步)
        /// 使用 AddListMembersListRequest 一次添加多個成員
        /// </summary>
        /// <param name="listGuid">名單ID</param>
        /// <param name="memberGuidList">成員ID列表</param>
        /// <param name="service">CRM 服務</param>
        /// <param name="maxBatchSize">最大批次大小 (CRM API 限制，通常為1000)</param>
        /// <param name="cancellationToken">取消標記</param>
        /// <returns>成功添加的成員數</returns>
        Task<int> AddMembersUsingSdkAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            IOrganizationService service,
            int maxBatchSize = 1000,
            CancellationToken cancellationToken = default);

        #endregion
    }
}
