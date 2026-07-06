// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/ViewModels/GalleryViewModel.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class GalleryViewModel、class RegisterViewModel、class LineBindingViewModel
// 主要成員：GetContactInfomation、UpdateContactInfomation、GetFaithStatus、GetFaithStatusIndex、GetGenderCode、GetGenderCodeIndex、Images、Account、Password、FullName
// 引用命名空間：Microsoft.Xrm.Sdk、System、System.Collections.Generic、System.Linq、ToolUtilityNameSpace、ToolUtilityNameSpace.Factory
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;

namespace ChurchReport.ViewModel
{
    public class GalleryViewModel
    {
        public IEnumerable<string> Images { get; set; }
        public string Account { get; set; } = "";
        public string Password { get; set; } = "";
        public string FullName { get; set; } = ""; // 姓名
        public string Mobile { get; set; } = "";   // 手機
        public string Address { get; set; } = "";   // 地址
        public string NationId { get; set; } = ""; // 身分證字號
    }
    public class RegisterViewModel
    {
        public IEnumerable<string> Images { get; set; }
        public string FullName { get; set; }
        public string Mobile { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
    }
    public class LineBindingViewModel
    {
        #region 成員資料

        // 透過 Factory 取得 ToolUtilityClass 單一實例
        private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

        public Entity m_Contact;

        public IEnumerable<string> Images { get; set; }
        public string DisplayName { get; set; }
        public string DisplayId { get; set; }
        public string GroupId { get; set; }
        public string RoomId { get; set; }
        public string LineUserId { get; set; }
        public string ViewType { get; set; }
        public string UserDisplayName { get; set; }
        public string FullName { get; set; }
        public string OtherName { get; set; }
        public string Mobile { get; set; }
        public string EncodeUrl { get; set; }
        public string BindingResult { get; set; }

        public string FaithStatus { get; set; } //信仰狀態
        public String GenderCode { get; set; }  // 性別
        public DateTime BirthDate { get; set; }   // 生日
        public string PersonalId { get; set; } //身分證字號

        public string PictureUrl { get; set; }
        public string StatusMessage { get; set; }

        public String Gender { get; set; }          // 性別
        public String Status { get; set; }          // 新人信仰狀態

        #endregion

        #region 工具區
        public void GetContactInfomation(String UserLineId, ref String FaithStatus, ref String GenderCode, ref DateTime BirthDate, ref String PersonalId )
        {
            m_Contact = this.m_ToolUtilityClass.RetrieveContactByLineId(UserLineId);
            if (m_Contact == null)
            {
                return;
            }
            else
            {
                FaithStatus = GetFaithStatus(m_Contact);

                GenderCode = GetGenderCode(m_Contact);

                BirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref m_Contact, "birthdate");

                PersonalId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "new_personal_id");
            }
        }

        public void UpdateContactInfomation(string lineUserId, String FaithStatus, String GenderCode, DateTime BirthDate, String PersonalId)
        {
            if (m_Contact == null)
            {
                if (string.IsNullOrEmpty(lineUserId))
                    throw new InvalidOperationException("m_Contact 未設定且 lineUserId 為空，無法儲存資料。");

                m_Contact = this.m_ToolUtilityClass.RetrieveContactByLineId(lineUserId);

                if (m_Contact == null)
                    throw new InvalidOperationException($"找不到 LINE UserId [{lineUserId}] 對應的聯絡人，無法儲存資料。");
            }

            this.m_ToolUtilityClass.SetOptionSetAttribute(ref this.m_Contact, "new_spiriitual_identity", GetFaithStatusIndex(FaithStatus));
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref this.m_Contact, "gendercode", GetGenderCodeIndex(GenderCode));
            this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref this.m_Contact, "birthdate", BirthDate.ToLocalTime());
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref this.m_Contact, "new_personal_id", PersonalId);

            this.m_ToolUtilityClass.UpdateEntity(ref this.m_Contact);
        }


        // ✅ 改為使用 OptionSetMetadataService 動態查詢 (數值 -> 文字)
        public String GetFaithStatus(Entity aContact)
        {
            int FaithStatusIndex = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "new_spiriitual_identity");

            try
            {
                // ✅ 使用 OptionSetMetadataService 動態查詢
                var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                    m_ToolUtilityClass.m_Crm2011OrganizationService,
                    null, // Logger (可選)
                    new Microsoft.Extensions.Caching.Memory.MemoryCache(
                        new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())
                );

                // 從 Dynamics 365 取得顯示文字
                string displayText = optionSetService.GetOptionSetText("contact", "new_spiriitual_identity", FaithStatusIndex);

                System.Diagnostics.Debug.WriteLine($"[GalleryViewModel.GetFaithStatus] 輸入值: {FaithStatusIndex}, 回傳文字: {displayText}");

                return displayText;
            }
            catch (Exception ex)
            {
                // 如果動態查詢失敗，使用備用的硬編碼對應表
                System.Diagnostics.Debug.WriteLine($"[GalleryViewModel.GetFaithStatus] 動態查詢失敗，使用備用對應表: {ex.Message}");

                return "-未知-";
            }
        }

        // ✅ 改為使用 OptionSetMetadataService 動態反向查詢 (文字 -> 數值)
        public int GetFaithStatusIndex(String FaithStatus)
        {
            try
            {
                // ✅ 使用 OptionSetMetadataService 動態反向查詢
                var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                    m_ToolUtilityClass.m_Crm2011OrganizationService,
                    null, // Logger (可選)
                    new Microsoft.Extensions.Caching.Memory.MemoryCache(
                        new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())
                );

                // 從 Dynamics 365 取得對應的數值
                int faithStatusValue = optionSetService.GetOptionSetValue(
                    "contact",
                    "new_spiriitual_identity",
                    FaithStatus
                );

                System.Diagnostics.Debug.WriteLine($"[GalleryViewModel.GetFaithStatusIndex] 輸入文字: {FaithStatus}, 回傳值: {faithStatusValue}");

                return faithStatusValue;
            }
            catch (KeyNotFoundException ex)
            {
                // 找不到對應的選項，使用備用硬編碼邏輯
                System.Diagnostics.Debug.WriteLine($"[GalleryViewModel.GetFaithStatusIndex] 動態查詢失敗，使用備用對應表: {ex.Message}");

                return 100000004; //-未知- 對應到"-未知-"
            }
            catch (Exception ex)
            {
                // 其他錯誤，記錄並使用預設值
                System.Diagnostics.Debug.WriteLine($"[GalleryViewModel.GetFaithStatusIndex] 發生錯誤: {ex.Message}");
                return 100000001; // 預設為"基督徒"
            }
        }

        public String GetGenderCode(Entity aContact)
        {
            int GenderCodeIndex = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "gendercode");

            switch (GenderCodeIndex)
            {
                case 100000000:
                    return "未知";
                case 200000:
                    return "男性";
                case 200001:
                    return "女性";
                default:
                    return "未知";
            }

        }

        public int GetGenderCodeIndex(String GenderCode)
        {
            switch (GenderCode)
            {
                case "未知":
                    return 100000000;
                case "男性":
                    return 200000;
                case "女性":
                    return 200001;
                default:
                    return 100000000;
            }

        }

        #endregion

    }
}