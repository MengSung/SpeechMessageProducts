// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationDedicationFeeFormService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DonationDedicationFeeFormService
// 主要成員：FillFromLineId、FillFromContact、GetFeesByContactId、FillIdentity
// 引用命名空間：System、System.Collections.Generic、ChurchReport.Models、Microsoft.Xrm.Sdk、ToolUtilityNameSpace
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using ChurchReport.Models;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 奉獻查詢表單刷新服務。
    ///
    /// 這個服務負責把 contact 欄位投影到 DonationPaymentFormModel，並重新查詢 new_fee 奉獻收費單清單。
    /// 它是 ChurchReport UI/CRM 表單流程，不是金流 provider 核心能力；未來其他產品可建立自己的
    /// 表單刷新服務，只共用 SpeechMessage.Payments 的付款建立與 callback 抽象。
    /// </summary>
    public sealed class DonationDedicationFeeFormService
    {
        private readonly ToolUtilityClass _utility;
        private readonly DonationFeeQueryService _feeQueryService;

        public DonationDedicationFeeFormService(ToolUtilityClass utility)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
            _feeQueryService = new DonationFeeQueryService(_utility);
        }

        public DonationPaymentFormModel FillFromLineId(
            DonationPaymentFormModel model,
            string userLineId,
            Entity fallbackContact)
        {
            ArgumentNullException.ThrowIfNull(model);

            Entity lineLoginContact = userLineId != null
                ? _utility.RetrieveContactByLineId(userLineId)
                : fallbackContact ?? new Entity("contact");

            FillIdentity(model, lineLoginContact, overwriteWhenClickTypeExists: true);
            model.NationId = _utility.GetEntityStringAttribute(ref lineLoginContact, "new_personal_id");
            model.Category = "十一奉獻";
            model.PayWay = "信用卡";
            model.DedicationDate = DateTime.Now;
            model.DedicateLocation = "好牧人";

            _feeQueryService.FillFeeList(model, lineLoginContact);
            return model;
        }

        public DonationPaymentFormModel FillFromContact(DonationPaymentFormModel model, Entity lineLoginContact)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(lineLoginContact);

            FillIdentity(
                model,
                lineLoginContact,
                overwriteWhenClickTypeExists: model.ClickType == null);

            _feeQueryService.FillFeeList(model, lineLoginContact);
            return model;
        }

        /// <summary>
        /// 取得指定 contact 的奉獻收費單 AJAX rows。
        ///
        /// 這個方法把 contactId 解析、CRM 讀取、model 刷新與 rows 投影包成單一入口，
        /// 讓 DonationPaymentManager 不需要再知道這些步驟的順序。
        /// </summary>
        public List<object> GetFeesByContactId(string contactId, DonationPaymentFormModel model)
        {
            if (string.IsNullOrWhiteSpace(contactId))
            {
                return new List<object>();
            }

            if (!Guid.TryParse(contactId, out Guid id))
            {
                return new List<object>();
            }

            Entity contactEntity = _utility.RetrieveEntity("contact", id);
            if (contactEntity == null)
            {
                return new List<object>();
            }

            FillFromContact(model, contactEntity);
            return DonationFeeQueryService.ToAjaxRows(model.DedicationFeeList);
        }

        private void FillIdentity(
            DonationPaymentFormModel model,
            Entity contact,
            bool overwriteWhenClickTypeExists)
        {
            if (!overwriteWhenClickTypeExists)
            {
                return;
            }

            model.FullName = _utility.GetEntityStringAttribute(ref contact, "fullname");
            model.Mobile = _utility.GetEntityStringAttribute(ref contact, "mobilephone");
            model.DedicationNumber = _utility.GetEntityStringAttribute(ref contact, "pager");
            model.Ntbt = _utility.GetEntityBoolAttribute(ref contact, "new_ntbt_ornot")
                ? "願意上傳國稅局"
                : "不願意上傳國稅局";
        }
    }
}
