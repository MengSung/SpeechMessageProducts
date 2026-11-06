/*
 * 檔案整體說明
 *
 * 目的與定位：聯絡人服務介面 負責處理聯絡人的建立、查詢、更新等操作
 *
 * 主要功能與流程：
 * 本檔宣告 IContactService、ContactCreationResult。
 * 主要操作入口包含 Success、Failure。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/CoreTargetFrameworkTests.cs、
 * ToolUtility.Tests/DataOperations/ContactCharacterizationTests.cs、
 * ToolUtility.Tests/DataOperations/ContactSafetyTests.cs、
 * SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs、
 * ToolUtility.Core/ContactOperations/ContactDataService.cs 追查目前工作樹中的合作／使用位置。
 */
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
