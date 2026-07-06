// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/ListManagement/IListManagementService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IListManagementService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：Microsoft.Xrm.Sdk、System、System.Collections.Generic、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
