using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChurchReport.Services.ListManagement
{
    /// <summary>
    /// 名單管理服務介面
    /// 負責處理小組名單的查詢、關聯、成員管理等操作
    /// </summary>
    public interface IListManagementService
    {
        /// <summary>
        /// 根據小組名稱查詢名單實體
        /// </summary>
        /// <param name="groupName">小組名稱</param>
        /// <param name="contactId">查詢者的 Contact ID</param>
        /// <returns>找到的名單實體，若無則返回 null</returns>
        Entity GetListByGroupName(string groupName, Guid contactId);

        /// <summary>
        /// 將聯絡人加入到名單中
        /// </summary>
        /// <param name="contactId">聯絡人 ID</param>
        /// <param name="listEntity">名單實體</param>
        /// <returns>是否成功</returns>
        Task<bool> AddContactToListAsync(Guid contactId, Entity listEntity);

        /// <summary>
        /// 從名單中移除聯絡人
        /// </summary>
        /// <param name="contactId">聯絡人 ID</param>
        /// <param name="listEntity">名單實體</param>
        /// <returns>是否成功</returns>
        Task<bool> RemoveContactFromListAsync(Guid contactId, Entity listEntity);

        /// <summary>
        /// 取得登入者可管理的所有名單集合
        /// </summary>
        /// <param name="contactId">登入者的 Contact ID</param>
        /// <returns>名單集合</returns>
        EntityCollection GetManageableLists(Guid contactId);

        /// <summary>
        /// 判斷名單是否為靜態名單
        /// </summary>
        /// <param name="listEntity">名單實體</param>
        /// <returns>true: 靜態名單, false: 動態名單</returns>
        bool IsStaticList(Entity listEntity);
    }
}
