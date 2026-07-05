// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationContactService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DonationContactService
// 主要成員：CreateContact、FilterByFullName、FilterByNationId、FilterByMobile、UpdateMissingFields
// 引用命名空間：System、ChurchReport.ViewModel、Microsoft.Xrm.Sdk、ToolUtilityNameSpace
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using ChurchReport.ViewModel;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 奉獻流程專用的 CRM contact 操作服務。
    ///
    /// 這個類別刻意留在 ChurchReport 專案，而不是抽到 SpeechMessage.Payments 或
    /// SpeechMessage.Payments.AspNetCore，因為它直接認得 ChurchReport 的 CRM schema，
    /// 例如 contact.new_personal_id、contact.pager、contact.mobilephone，以及奉獻者登入時
    /// 「只補空欄位、不覆蓋既有資料」的產品規則。
    ///
    /// DonationPaymentManager 只負責協調流程；真正建立、篩選、補齊奉獻者 contact 的細節集中在這裡，
    /// 讓未來閱讀或修改 CRM contact 規則時，不需要在大型付款協調器裡搜尋。
    /// </summary>
    public sealed class DonationContactService
    {
        private readonly ToolUtilityClass _utility;

        public DonationContactService(ToolUtilityClass utility)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
        }

        /// <summary>
        /// 依照官網奉獻登入頁輸入的資料建立 ChurchReport contact。
        ///
        /// 這裡只建立 ChurchReport 需要的 CRM 欄位，不處理任何金流 provider 參數；
        /// 金流核心不應知道奉獻者資料如何落到 Dynamics 365 contact。
        /// </summary>
        public Entity CreateContact(GalleryViewModel viewModel)
        {
            var contact = new Entity("contact");

            _utility.SetEntityStringAttribute(ref contact, "lastname", viewModel.FullName);
            _utility.SetEntityStringAttribute(ref contact, "mobilephone", viewModel.Mobile);
            _utility.SetEntityStringAttribute(ref contact, "address2_line1", viewModel.Address);
            _utility.SetEntityStringAttribute(ref contact, "new_personal_id", viewModel.NationId);
            _utility.SetOptionSetAttribute(ref contact, "customertypecode", 100000000);
            _utility.SetEntityStringAttribute(ref contact, "description", "透過官網奉獻付款流程建立的新朋友");

            Guid contactId = _utility.CreateEntity(contact);
            return _utility.RetrieveEntity("contact", contactId);
        }

        /// <summary>
        /// 從 CRM 查詢結果中找出姓名完全相同的 contact。
        /// </summary>
        public Entity FilterByFullName(GalleryViewModel viewModel, EntityCollection contacts)
        {
            foreach (Entity contact in contacts.Entities)
            {
                if (_utility.GetEntityStringAttribute(contact, "fullname") == viewModel.FullName)
                {
                    return contact;
                }
            }

            return null;
        }

        /// <summary>
        /// 從 CRM 查詢結果中用身分證字號比對 contact。
        ///
        /// 舊資料可能把奉獻編號放在 pager，因此仍同時比對 new_personal_id 與 pager；
        /// 這是 ChurchReport 的歷史資料相容規則，不屬於通用金流核心。
        /// </summary>
        public Entity FilterByNationId(GalleryViewModel viewModel, EntityCollection contacts)
        {
            foreach (Entity contact in contacts.Entities)
            {
                if (_utility.GetEntityStringAttribute(contact, "new_personal_id") == viewModel.NationId ||
                    _utility.GetEntityStringAttribute(contact, "pager") == viewModel.NationId)
                {
                    return contact;
                }
            }

            return null;
        }

        /// <summary>
        /// 從 CRM 查詢結果中用行動電話比對 contact。
        ///
        /// 電話號碼先透過 ToolUtilityClass.FilterDigit 正規化，只比較數字，避免
        /// 空白、破折號、括號等格式差異造成同一支電話比對失敗。
        /// </summary>
        public Entity FilterByMobile(GalleryViewModel viewModel, EntityCollection contacts)
        {
            foreach (Entity contact in contacts.Entities)
            {
                if (_utility.FilterDigit(_utility.GetEntityStringAttribute(contact, "mobilephone")) ==
                    _utility.FilterDigit(viewModel.Mobile))
                {
                    return contact;
                }
            }

            return null;
        }

        /// <summary>
        /// 只補齊 contact 缺少的欄位，不覆蓋 CRM 既有資料。
        ///
        /// 奉獻者可能已經由同工維護過較完整的 CRM 資料，所以官網登入流程只能補空值。
        /// 這個保守規則避免使用者在網頁輸入的資料意外覆蓋正式 CRM 主檔。
        /// </summary>
        public void UpdateMissingFields(GalleryViewModel viewModel, ref Entity contact)
        {
            bool shouldUpdate = false;

            if (_utility.GetEntityStringAttribute(ref contact, "mobilephone") == string.Empty)
            {
                _utility.SetEntityStringAttribute(ref contact, "mobilephone", viewModel.Mobile);
                shouldUpdate = true;
            }

            if (_utility.GetEntityStringAttribute(ref contact, "new_personal_id") == string.Empty)
            {
                _utility.SetEntityStringAttribute(ref contact, "new_personal_id", viewModel.NationId);
                shouldUpdate = true;
            }

            if (_utility.GetEntityStringAttribute(ref contact, "pager") == string.Empty)
            {
                _utility.SetEntityStringAttribute(ref contact, "pager", viewModel.NationId);
                shouldUpdate = true;
            }

            if (shouldUpdate)
            {
                _utility.UpdateEntity(ref contact);
            }
        }
    }
}
