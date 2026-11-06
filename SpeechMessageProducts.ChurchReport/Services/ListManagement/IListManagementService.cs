/*
 * 檔案整體說明
 *
 * 目的與定位：名單管理服務介面 負責處理小組名單的查詢、關聯、成員管理等操作
 *
 * 主要功能與流程：
 * 本檔宣告 IListManagementService。
 * 資料／選項欄位包含 IListManagementService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs 追查目前工作樹中的合作／使用位置。
 */
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
