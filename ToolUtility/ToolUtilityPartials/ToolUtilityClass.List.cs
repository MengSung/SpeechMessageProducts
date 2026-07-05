// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ToolUtilityPartials/ToolUtilityClass.List.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityClass
// 主要成員：RetrieveMemberListCollectionByListId、RetrieveMemberListCollectionByListIdDynamics365、RetrieveMemberListCollectionByListIdCrm2011、RetrieveDynamicMemberList、RetrieveDynamicMemberListDynamics365、RetrieveDynamicMemberListCrm2011、GetAllMemberDataFromList、AddMembersToMarketingList、RemoveMembersToMarketingList、AddMembersToMarketingListAsync
// 引用命名空間：Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Client、System、System.Collections、System.Collections.Generic、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace
{
    /// <summary>
    /// ToolUtilityClass - 名單操作 (Partial Class 3/10)
    /// 包含：名單成員管理、批次操作
    /// </summary>
    public partial class ToolUtilityClass
    {
        #region 名單查詢方法
        public EntityCollection RetrieveMemberListCollectionByListId(Guid aListId)
            => _facade.RetrieveMemberListCollectionByListId(aListId);

        public EntityCollection RetrieveMemberListCollectionByListId(ref IOrganizationService aOrganizationService, Guid aListId)
            => _facade.RetrieveMemberListCollectionByListId(ref aOrganizationService, aListId);

        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(ref OrganizationServiceProxy aOrganizationService, Guid aListId)
            => _facade.RetrieveMemberListCollectionByListIdDynamics365(ref aOrganizationService, aListId);

        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(Guid aListId)
            => _facade.RetrieveMemberListCollectionByListId(aListId);

        public EntityCollection RetrieveMemberListCollectionByListIdCrm2011(ref IOrganizationService aOrganizationService, Guid aListId)
            => _facade.RetrieveMemberListCollectionByListIdCrm2011(ref aOrganizationService, aListId);

        public EntityCollection RetrieveDynamicMemberList(string strList)
            => _facade.RetrieveDynamicMemberList(strList);

        public EntityCollection RetrieveDynamicMemberList(IOrganizationService service, string strList)
            => _facade.RetrieveDynamicMemberList(service, strList);

        public EntityCollection RetrieveDynamicMemberListDynamics365(OrganizationServiceProxy service, Guid strList)
            => _facade.RetrieveDynamicMemberListDynamics365(service, strList);

        public EntityCollection RetrieveDynamicMemberListDynamics365(ref OrganizationServiceProxy service, Guid aListId)
            => _facade.RetrieveDynamicMemberListDynamics365(service, aListId);

        public EntityCollection RetrieveDynamicMemberListDynamics365(Guid aListId)
            => _facade.RetrieveDynamicMemberList(aListId);

        public EntityCollection RetrieveDynamicMemberListCrm2011(IOrganizationService service, string strList)
            => _facade.RetrieveDynamicMemberListCrm2011(service, strList);

        public EntityCollection RetrieveDynamicMemberList(Guid aListId)
            => _facade.RetrieveDynamicMemberList(aListId);

        public EntityCollection RetrieveDynamicMemberList(ref IOrganizationService service, Guid aListId)
            => _facade.RetrieveDynamicMemberList(ref service, aListId);

        public EntityCollection RetrieveDynamicMemberListCrm2011(ref IOrganizationService service, Guid aListId)
            => _facade.RetrieveDynamicMemberListCrm2011(ref service, aListId);

        public ArrayList GetAllMemberDataFromList(Guid ListEntityId)
            => _facade.GetAllMemberDataFromList(ListEntityId);
        #endregion

        #region 名單成員管理 - 同步方法
        public void AddMembersToMarketingList(Guid thisListGuid, List<Guid> memberGuidList, ref IOrganizationService gCRMService)
        {
            try
            {
                _facade.AddMembersToMarketingList(thisListGuid, memberGuidList, ref gCRMService);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public void RemoveMembersToMarketingList(Guid aListGuid, Guid MemberGuid, ref IOrganizationService gCRMService)
        {
            try
            {
                _facade.RemoveMembersToMarketingList(aListGuid, MemberGuid, ref gCRMService);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public void AddMembersToMarketingList(Guid thisListGuid, List<Guid> memberGuidList)
        {
            try
            {
                _facade.AddMembersToMarketingList(thisListGuid, memberGuidList);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public void RemoveMembersToMarketingList(Guid aListGuid, Guid MemberGuid)
        {
            try
            {
                _facade.RemoveMembersToMarketingList(aListGuid, MemberGuid);
            }
            catch (Exception e)
            {
                throw e;
            }
        }
        #endregion

        #region 名單成員管理 - 非同步方法
        public async Task<int> AddMembersToMarketingListAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int batchSize = 50,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                return await _facade.AddMembersToMarketingListAsync(listGuid, memberGuidList, batchSize, cancellationToken);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public async Task<int> AddMembersToMarketingListUsingSdkAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int maxBatchSize = 1000,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                return await _facade.AddMembersToMarketingListUsingSdkAsync(
                    listGuid, memberGuidList, m_Crm2011OrganizationService, maxBatchSize, cancellationToken);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public async Task<int> RemoveMembersFromMarketingListAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int batchSize = 50,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                return await _facade.RemoveMembersFromMarketingListAsync(listGuid, memberGuidList, batchSize, cancellationToken);
            }
            catch (Exception e)
            {
                throw e;
            }
        }
        #endregion
    }
}
