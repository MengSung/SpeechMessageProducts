// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/Contact/IContactService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IContactService、class ContactCreationResult
// 主要成員：Success、Failure、IsSuccess、Message、ContactId、AssociatedListEntity
// 引用命名空間：ChurchReport.Models.CrmTransmitModule、Microsoft.Xrm.Sdk、System、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models.CrmTransmitModule;
using Microsoft.Xrm.Sdk;
using System;
using System.Threading.Tasks;

namespace ChurchReport.Services.Contact
{
    /// <summary>
    /// 聯絡人服務介面
    /// 負責處理聯絡人的建立、查詢、更新等操作
    /// </summary>
    public interface IContactService
    {
        /// <summary>
        /// 根據手機號碼搜尋聯絡人
        /// </summary>
        /// <param name="fullName">姓名</param>
        /// <param name="mobilePhone">手機號碼</param>
        /// <returns>找到的聯絡人實體，若無則返回 null</returns>
        Entity SearchByMobilePhone(string fullName, string mobilePhone);

        /// <summary>
        /// 建立新聯絡人
        /// </summary>
        /// <param name="newContact">新聯絡人資料</param>
        /// <param name="accountPasswordData">登入者帳密資料</param>
        /// <returns>建立結果訊息</returns>
        Task<ContactCreationResult> CreateContactAsync(NewContact newContact, AccountPasswordData accountPasswordData);

        /// <summary>
        /// 將現有聯絡人加入到指定名單
        /// </summary>
        /// <param name="existingContact">現有聯絡人實體</param>
        /// <param name="targetGroupName">目標小組名稱</param>
        /// <param name="accountPasswordData">登入者帳密資料</param>
        /// <returns>操作結果訊息</returns>
        Task<string> AddContactToListAsync(Entity existingContact, string targetGroupName, AccountPasswordData accountPasswordData);

        /// <summary>
        /// 檢查聯絡人是否已在其他小組中
        /// </summary>
        /// <param name="contact">聯絡人實體</param>
        /// <returns>若已在小組中則返回該小組實體，否則返回 null</returns>
        Entity GetContactCurrentGroup(Entity contact);

        /// <summary>
        /// 更新聯絡人的負責人（Owner）
        /// </summary>
        /// <param name="contactId">聯絡人 ID</param>
        /// <param name="ownerId">負責人 ID</param>
        void AssignOwner(Guid contactId, Guid ownerId);
    }

    /// <summary>
    /// 聯絡人建立結果
    /// </summary>
    public class ContactCreationResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 結果訊息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 新建立的聯絡人 ID
        /// </summary>
        public Guid? ContactId { get; set; }

        /// <summary>
        /// 關聯的小組實體
        /// </summary>
        public Entity AssociatedListEntity { get; set; }

        public static ContactCreationResult Success(string message, Guid contactId, Entity listEntity)
        {
            return new ContactCreationResult
            {
                IsSuccess = true,
                Message = message,
                ContactId = contactId,
                AssociatedListEntity = listEntity
            };
        }

        public static ContactCreationResult Failure(string message)
        {
            return new ContactCreationResult
            {
                IsSuccess = false,
                Message = message
            };
        }
    }
}
