// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/Contact/Impl/ContactService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ContactService
// 主要成員：SearchByMobilePhone、CreateContactAsync、SetupNewContactAttributes、SetupRelationships、SetupCommitmentType、SetCommitmentTypeByText、SetupDates、SetupGender、SetupOptionSets、SetupAdditionalInfo
// 引用命名空間：ChurchReport.Domain.Constants、ChurchReport.Models.CrmTransmitModule、ChurchReport.Services.Contact、ChurchReport.Services.ListManagement、ChurchReport.Services.PresentRecord、ChurchReport.Utilities、ChurchReport.WebServiceConnector、Microsoft.Extensions.Logging
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Domain.Constants;
using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.Services.Contact;
using ChurchReport.Services.ListManagement;
using ChurchReport.Services.PresentRecord;
using ChurchReport.Utilities;
using ChurchReport.WebServiceConnector;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using System;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;

namespace ChurchReport.Services.Contact.Impl
{
    /// <summary>
    /// 聯絡人服務實作
    /// 遵循單一職責原則，僅負責聯絡人相關的 CRUD 操作
    /// </summary>
    public class ContactService : IContactService
    {
        private readonly ToolUtilityClass _toolUtility;
        private readonly IListManagementService _listManagementService;
        private readonly IPresentRecordService _presentRecordService;
        private readonly ILogger<ContactService> _logger;
        private readonly LineNotifyUtility _lineNotifyUtility;

        public ContactService(
            ToolUtilityClass toolUtility,
            IListManagementService listManagementService,
            IPresentRecordService presentRecordService,
            ILogger<ContactService> logger)
        {
            _toolUtility = toolUtility ?? throw new ArgumentNullException(nameof(toolUtility));
            _listManagementService = listManagementService ?? throw new ArgumentNullException(nameof(listManagementService));
            _presentRecordService = presentRecordService ?? throw new ArgumentNullException(nameof(presentRecordService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _lineNotifyUtility = new LineNotifyUtility();
        }

        #region 搜尋聯絡人

        /// <summary>
        /// 根據手機號碼搜尋聯絡人
        /// </summary>
        public Entity SearchByMobilePhone(string fullName, string mobilePhone)
        {
            try
            {
                _logger.LogInformation("開始搜尋聯絡人: 姓名={FullName}, 手機={MobilePhone}", fullName, mobilePhone);

                // 標準化手機號碼（移除非數字字元）
                string normalizedMobilePhone = OptionSetConverter.NormalizePhoneNumber(mobilePhone);

                // 查詢同名聯絡人
                EntityCollection contactCollection = _toolUtility.RetrieveContactEntityByFullNameCollection(fullName);

                if (contactCollection == null || contactCollection.Entities.Count == 0)
                {
                    _logger.LogInformation("未找到同名聯絡人: {FullName}", fullName);
                    return null;
                }

                // 比對手機號碼
                foreach (Entity contactEntity in contactCollection.Entities)
                {
                    string existingMobilePhone = _toolUtility.GetEntityStringAttribute(contactEntity, "mobilephone");
                    string normalizedExistingPhone = OptionSetConverter.NormalizePhoneNumber(existingMobilePhone);

                    if (normalizedMobilePhone == normalizedExistingPhone && !string.IsNullOrEmpty(normalizedMobilePhone))
                    {
                        _logger.LogInformation("找到匹配的聯絡人: ContactId={ContactId}", contactEntity.Id);
                        return contactEntity;
                    }
                }

                _logger.LogInformation("同名聯絡人存在，但手機號碼不匹配");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜尋聯絡人時發生錯誤: 姓名={FullName}, 手機={MobilePhone}", fullName, mobilePhone);
                throw;
            }
        }

        #endregion

        #region 建立新聯絡人

        /// <summary>
        /// 建立新聯絡人
        /// </summary>
        public async Task<ContactCreationResult> CreateContactAsync(NewContact newContact, AccountPasswordData accountPasswordData)
        {
            try
            {
                _logger.LogInformation("開始建立新聯絡人: 姓名={Name}, 小組={GroupName}", newContact.Name, newContact.GroupName);

                // 1. 取得登入者實體
                Entity loginContact = await GetLoginContactAsync(accountPasswordData);
                if (loginContact == null)
                {
                    return ContactCreationResult.Failure("無法取得登入者資訊");
                }

                // 2. 查詢目標小組名單
                Entity listEntity = _listManagementService.GetListByGroupName(newContact.GroupName, loginContact.Id);
                if (listEntity == null)
                {
                    string errorMsg = $"無法找到小組名單：{newContact.GroupName}，請確認小組名稱是否正確";
                    _logger.LogWarning(errorMsg);
                    return ContactCreationResult.Failure(errorMsg);
                }

                // 3. 建立新聯絡人實體
                Entity newContactEntity = new Entity("contact");
                SetupNewContactAttributes(ref newContactEntity, newContact, loginContact, listEntity.Id);

                // 4. 關聯主要小組
                _toolUtility.SetEntityLookUpAttribute(ref newContactEntity, "new_cell_list_contact", "list", listEntity.Id);

                // 5. 在 CRM 中建立聯絡人
                Guid newContactId = _toolUtility.CreateEntity(newContactEntity);
                _logger.LogInformation("成功建立聯絡人: ContactId={ContactId}", newContactId);

                // 6. 指派負責人
                Guid ownerId = _toolUtility.GetOwnerId(loginContact);
                if (ownerId != Guid.Empty)
                {
                    Entity createdContact = _toolUtility.RetrieveEntity("contact", newContactId);
                    _toolUtility.AssignOwner("contact", createdContact, ownerId);
                }

                // 7. 將聯絡人加入至小組名單
                await _listManagementService.AddContactToListAsync(newContactId, listEntity);

                // 8. 建立出席記錄
                await _presentRecordService.CreatePresentRecordAsync(listEntity, newContactId, newContact.GroupName);

                // 9. 發送 LINE 通知
                string loginContactName = _toolUtility.GetEntityStringAttribute(loginContact, "fullname");
                string successMessage = $"{loginContactName} 成功建立新人並且加入 {newContact.Name} 到 {newContact.GroupName}小組中";

                _lineNotifyUtility.SendAddNewPersonResultLine(successMessage, listEntity);
                _lineNotifyUtility.SendListMemberLine(listEntity);

                return ContactCreationResult.Success(successMessage, newContactId, listEntity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "建立新聯絡人時發生錯誤: 姓名={Name}", newContact.Name);
                return ContactCreationResult.Failure($"建立新聯絡人失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 設定新聯絡人的屬性
        /// </summary>
        private void SetupNewContactAttributes(ref Entity newContactEntity, NewContact newContact, Entity loginContact, Guid listEntityId)
        {
            // 基本資料
            _toolUtility.SetEntityStringAttribute(ref newContactEntity, "lastname", newContact.Name);
            _toolUtility.SetEntityStringAttribute(ref newContactEntity, "mobilephone", newContact.MobilePhone);
            _toolUtility.SetEntityStringAttribute(ref newContactEntity, "telephone2", newContact.HomePhone);
            _toolUtility.SetEntityStringAttribute(ref newContactEntity, "address2_line1", newContact.Address);

            // 關聯欄位
            SetupRelationships(ref newContactEntity, loginContact);

            // 委身類型
            SetupCommitmentType(ref newContactEntity, newContact, listEntityId);

            // 日期欄位
            SetupDates(ref newContactEntity, newContact);

            // 性別
            SetupGender(ref newContactEntity, newContact.Gender);

            // 婚姻狀態和信仰狀態（使用 OptionSetMetadataService 動態查詢）
            SetupOptionSets(ref newContactEntity, newContact);

            // 其他資訊
            SetupAdditionalInfo(ref newContactEntity, newContact, loginContact);
        }

        private void SetupRelationships(ref Entity newContactEntity, Entity loginContact)
        {
            // 關聯所屬教會
            Guid accountId = _toolUtility.GetEntityLookupAttribute(ref loginContact, "parentcustomerid");
            if (accountId != Guid.Empty)
            {
                _toolUtility.SetEntityLookUpAttribute(ref newContactEntity, "parentcustomerid", "account", accountId);
            }

            // 關聯族系組長
            Guid raceLeaderId = _toolUtility.GetEntityLookupAttribute(ref loginContact, "new_race_leader_contact");
            if (raceLeaderId != Guid.Empty)
            {
                _toolUtility.SetEntityLookUpAttribute(ref newContactEntity, "new_race_leader_contact", "contact", raceLeaderId);
            }

            // 關聯邀請人
            Guid contactId = loginContact.Id;
            if (contactId != Guid.Empty)
            {
                _toolUtility.SetEntityLookUpAttribute(ref newContactEntity, "new_invitnewperson_contact", "contact", contactId);
            }
        }

        private void SetupCommitmentType(ref Entity newContactEntity, NewContact newContact, Guid listEntityId)
        {
            if (listEntityId == Guid.Empty)
            {
                SetCommitmentTypeByText(ref newContactEntity, newContact.CustomerTypeCode);
                return;
            }

            // 檢查是否為幸福小組
            Entity listEntity = _toolUtility.RetrieveEntity("list", listEntityId);
            string listName = _toolUtility.GetEntityStringAttribute(listEntity, "listname");

            if (listName.Contains("幸福"))
            {
                _toolUtility.SetOptionSetAttribute(ref newContactEntity, "customertypecode", CommitmentType.HappyBest);
            }
            else
            {
                SetCommitmentTypeByText(ref newContactEntity, newContact.CustomerTypeCode);
            }
        }

        private void SetCommitmentTypeByText(ref Entity newContactEntity, string customerTypeText)
        {
            int commitmentType = customerTypeText switch
            {
                string s when s.Contains("小組組員") => CommitmentType.SmallGroupMember,
                string s when s.Contains("新朋友") => CommitmentType.NewFriend,
                _ => CommitmentType.NewFriend // 預設為新朋友
            };

            _toolUtility.SetOptionSetAttribute(ref newContactEntity, "customertypecode", commitmentType);
        }

        private void SetupDates(ref Entity newContactEntity, NewContact newContact)
        {
            // 生日
            if (newContact.BirthDate != new DateTime(1919, 1, 1) && newContact.BirthDate.Year != 1919)
            {
                _toolUtility.SetEntityDateTimeAttribute(ref newContactEntity, "birthdate", newContact.BirthDate);
            }

            // 首次參加教會主日日期
            if (newContact.FirstChurchDate.Year > 1000)
            {
                _toolUtility.SetEntityDateTimeAttribute(ref newContactEntity, "new_enter_church_date", newContact.FirstChurchDate);
            }

            // 首次參加活動日期
            if (newContact.FirstActionDate.Year > 1000)
            {
                _toolUtility.SetEntityDateTimeAttribute(ref newContactEntity, "new_recently_visitchurch_date", newContact.FirstActionDate);
            }
        }

        private void SetupGender(ref Entity newContactEntity, string gender)
        {
            if (gender == "男性")
            {
                _toolUtility.SetOptionSetAttribute(ref newContactEntity, "gendercode", Gender.Male);
            }
            else if (gender == "女性")
            {
                _toolUtility.SetOptionSetAttribute(ref newContactEntity, "gendercode", Gender.Female);
            }
        }

        private void SetupOptionSets(ref Entity newContactEntity, NewContact newContact)
        {
            // 婚姻狀態
            if (!string.IsNullOrWhiteSpace(newContact.MerrageState))
            {
                try
                {
                    var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                        _toolUtility.m_Crm2011OrganizationService,
                        null,
                        new Microsoft.Extensions.Caching.Memory.MemoryCache(
                            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())
                    );

                    int familyStatusValue = optionSetService.GetOptionSetValue("contact", "familystatuscode", newContact.MerrageState);
                    _toolUtility.SetOptionSetAttribute(ref newContactEntity, "familystatuscode", familyStatusValue);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "設定婚姻狀態失敗，使用預設值");
                    _toolUtility.SetOptionSetAttribute(ref newContactEntity, "familystatuscode", 100000001); // 未知
                }
            }

            // 信仰狀態
            if (!string.IsNullOrWhiteSpace(newContact.FaithStatus))
            {
                try
                {
                    var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                        _toolUtility.m_Crm2011OrganizationService,
                        null,
                        new Microsoft.Extensions.Caching.Memory.MemoryCache(
                            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())
                    );

                    int spiritualIdentityValue = optionSetService.GetOptionSetValue("contact", "new_spiriitual_identity", newContact.FaithStatus);
                    _toolUtility.SetOptionSetAttribute(ref newContactEntity, "new_spiriitual_identity", spiritualIdentityValue);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "設定信仰狀態失敗，使用預設值");
                    _toolUtility.SetOptionSetAttribute(ref newContactEntity, "new_spiriitual_identity", 100000004); // 未知
                }
            }
        }

        private void SetupAdditionalInfo(ref Entity newContactEntity, NewContact newContact, Entity loginContact)
        {
            // 來源
            _toolUtility.SetEntityStringAttribute(ref newContactEntity, "new_coming_reason", newContact.Source);

            // 邀請人相關資訊
            _toolUtility.SetEntityStringAttribute(ref newContactEntity, "new_invitor", newContact.Introducer);
            _toolUtility.SetEntityStringAttribute(ref newContactEntity, "assistantphone", newContact.IntroducerPhone);
            _toolUtility.SetEntityStringAttribute(ref newContactEntity, "new_carers", newContact.IntroducerRelation);
            _toolUtility.SetEntityStringAttribute(ref newContactEntity, "new_invitor_group", newContact.IntroducerGroup);

            // 職業及專長
            _toolUtility.SetEntityStringAttribute(ref newContactEntity, "new_industry", newContact.Industry);

            // 描述（記錄建立者）
            string creatorName = _toolUtility.GetEntityStringAttribute(ref loginContact, "fullname");
            string description = $"{creatorName} 透過網頁回報建立的新人{Environment.NewLine}{newContact.Note}";
            _toolUtility.SetEntityStringAttribute(ref newContactEntity, "description", description);

            // 裝備狀態
            string equipmentStatus = _toolUtility.GetEntityStringAttribute(ref newContactEntity, "new_equipment_status");
            if (string.IsNullOrEmpty(equipmentStatus))
            {
                _toolUtility.SetEntityStringAttribute(ref newContactEntity, "new_equipment_status", "尚未裝備");
            }
        }

        #endregion

        #region 將現有聯絡人加入名單

        /// <summary>
        /// 將現有聯絡人加入到指定名單
        /// </summary>
        public async Task<string> AddContactToListAsync(Entity existingContact, string targetGroupName, AccountPasswordData accountPasswordData)
        {
            try
            {
                _logger.LogInformation("開始將聯絡人加入名單: ContactId={ContactId}, 目標小組={GroupName}",
                    existingContact.Id, targetGroupName);

                // 1. 取得登入者實體
                Entity loginContact = await GetLoginContactAsync(accountPasswordData);
                string loginContactName = _toolUtility.GetEntityStringAttribute(loginContact, "fullname");
                string existContactName = _toolUtility.GetEntityStringAttribute(existingContact, "fullname");

                // 2. 檢查是否已在其他小組
                Entity currentGroup = GetContactCurrentGroup(existingContact);

                // 3. 取得目標小組
                Entity targetList = _listManagementService.GetListByGroupName(targetGroupName, loginContact.Id);
                if (targetList == null)
                {
                    return $"無法找到目標小組：{targetGroupName}";
                }

                // 4. 根據當前狀態執行不同邏輯
                if (currentGroup == null)
                {
                    // 尚未在任何小組中，直接加入
                    return await AddContactToNewListAsync(existingContact, targetList, targetGroupName, loginContact);
                }
                else
                {
                    // 已在其他小組中，需要判斷是否允許轉組
                    string currentGroupName = _toolUtility.GetEntityStringAttribute(currentGroup, "listname");

                    if (currentGroupName.Contains("新人") || currentGroupName.Contains("關懷"))
                    {
                        // 從新人關懷小組轉出，允許
                        return await TransferContactBetweenListsAsync(existingContact, currentGroup, targetList, targetGroupName, loginContact);
                    }
                    else
                    {
                        // 已在正式小組中，不允許隨意轉組
                        string warningMessage = $"{loginContactName} 想要加入 {existContactName} 到 {targetGroupName}小組中，" +
                                              $"但是{existContactName}已經在 {currentGroupName} 小組了!";

                        _lineNotifyUtility.SendAddNewPersonResultLine(warningMessage, currentGroup);
                        _lineNotifyUtility.SendListMemberLine(currentGroup);
                        _lineNotifyUtility.SendAddNewPersonResultLine(warningMessage, targetList);
                        _lineNotifyUtility.SendListMemberLine(targetList);

                        return warningMessage;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "將聯絡人加入名單時發生錯誤");
                throw;
            }
        }

        private async Task<string> AddContactToNewListAsync(Entity contact, Entity targetList, string groupName, Entity loginContact)
        {
            // 加入新名單
            await _listManagementService.AddContactToListAsync(contact.Id, targetList);

            // 建立出席記錄
            await _presentRecordService.CreatePresentRecordAsync(targetList, contact.Id, groupName);

            // 更新關聯
            _toolUtility.SetEntityLookUpAttribute(ref contact, "new_cell_list_contact", "list", targetList.Id);
            _toolUtility.UpdateEntity(ref contact);

            // 指派負責人
            Guid ownerId = _toolUtility.GetOwnerId(loginContact);
            if (ownerId != Guid.Empty)
            {
                _toolUtility.AssignOwner("contact", contact, ownerId);
            }

            // 發送通知
            string loginContactName = _toolUtility.GetEntityStringAttribute(loginContact, "fullname");
            string existContactName = _toolUtility.GetEntityStringAttribute(contact, "fullname");
            string successMessage = $"{loginContactName} 仍然成功的加入 {existContactName} 到 {groupName}小組中";

            _lineNotifyUtility.SendAddNewPersonResultLine(successMessage, targetList);
            _lineNotifyUtility.SendListMemberLine(targetList);

            return $"新增的新人在資料庫已經存在，但是 {successMessage}";
        }

        private async Task<string> TransferContactBetweenListsAsync(Entity contact, Entity fromList, Entity toList, string toGroupName, Entity loginContact)
        {
            // 加入新名單
            await _listManagementService.AddContactToListAsync(contact.Id, toList);

            // 從舊名單移除
            await _listManagementService.RemoveContactFromListAsync(contact.Id, fromList);

            // 建立新的出席記錄
            await _presentRecordService.CreatePresentRecordAsync(toList, contact.Id, toGroupName);

            // 更新關聯
            _toolUtility.SetEntityLookUpAttribute(ref contact, "new_cell_list_contact", "list", toList.Id);
            _toolUtility.UpdateEntity(ref contact);

            // 指派負責人
            Guid ownerId = _toolUtility.GetOwnerId(loginContact);
            if (ownerId != Guid.Empty)
            {
                _toolUtility.AssignOwner("contact", contact, ownerId);
            }

            // 發送通知
            string loginContactName = _toolUtility.GetEntityStringAttribute(loginContact, "fullname");
            string existContactName = _toolUtility.GetEntityStringAttribute(contact, "fullname");
            string successMessage = $"{loginContactName} 仍然成功的加入 {existContactName} 到 {toGroupName}小組中";

            _lineNotifyUtility.SendAddNewPersonResultLine(successMessage, toList);
            _lineNotifyUtility.SendListMemberLine(toList);

            return $"新增的新人在資料庫已經存在，但是 {successMessage}";
        }

        #endregion

        #region 輔助方法

        /// <summary>
        /// 取得登入者的聯絡人實體
        /// </summary>
        private Task<Entity> GetLoginContactAsync(AccountPasswordData accountPasswordData)
        {
            Entity loginContact;
            if (accountPasswordData.Account != "LineIdLogin")
            {
                loginContact = _toolUtility.RetrieveContactEntityByAccountNumber(accountPasswordData.Account, accountPasswordData.Password);
            }
            else
            {
                loginContact = _toolUtility.RetrieveContactEntityByLineUserId(accountPasswordData.Password);
            }

            return Task.FromResult(loginContact);
        }

        public Entity GetContactCurrentGroup(Entity contact)
        {
            try
            {
                EntityCollection lists = _toolUtility.QueryListOfContactManyToMany(contact.Id);

                foreach (Entity listEntity in lists.Entities)
                {
                    bool isAppNamed = _toolUtility.GetEntityBoolAttribute(listEntity, "new_app_named");
                    if (isAppNamed)
                    {
                        return listEntity;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查詢聯絡人當前小組時發生錯誤");
                throw;
            }
        }

        public void AssignOwner(Guid contactId, Guid ownerId)
        {
            Entity contact = _toolUtility.RetrieveEntity("contact", contactId);
            _toolUtility.AssignOwner("contact", contact, ownerId);
        }

        #endregion
    }
}
