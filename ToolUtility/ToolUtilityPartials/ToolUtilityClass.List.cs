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
