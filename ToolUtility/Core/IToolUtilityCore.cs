using Microsoft.Xrm.Sdk;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.Core
{
    /// <summary>
    /// ToolUtility 核心介面
    /// 定義所有基礎操作的抽象，遵循 Interface Segregation Principle (ISP)
    /// </summary>
    public interface IToolUtilityCore
    {
        #region 組織服務屬性
        /// <summary>
        /// 取得 CRM 組織服務實例
        /// </summary>
        IOrganizationService OrganizationService { get; }
        #endregion

        #region Entity 基礎操作
        /// <summary>
        /// 建立實體
        /// </summary>
        Guid CreateEntity(Entity entity);

        /// <summary>
        /// 更新實體
        /// </summary>
        void UpdateEntity(ref Entity entity);

        /// <summary>
        /// 刪除實體
        /// </summary>
        void DeleteEntity(string entityName, Guid entityId);

        /// <summary>
        /// 檢索單一實體
        /// </summary>
        Entity RetrieveEntity(string entityName, Guid entityId);
        #endregion

        #region 聯絡人操作
        /// <summary>
        /// 透過 LINE ID 檢索聯絡人
        /// </summary>
        Entity RetrieveContactByLineId(string lineId);

        /// <summary>
        /// 透過姓名檢索聯絡人集合
        /// </summary>
        EntityCollection RetrieveContactCollectionByName(string contactFullName);
        #endregion

        #region 名單操作
        /// <summary>
        /// 批次添加成員到行銷名單 (非同步)
        /// </summary>
        Task<int> AddMembersToMarketingListAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int batchSize = 50,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 取得名單所有成員
        /// </summary>
        ArrayList GetAllMemberDataFromList(Guid listEntityId);
        #endregion
    }
}
