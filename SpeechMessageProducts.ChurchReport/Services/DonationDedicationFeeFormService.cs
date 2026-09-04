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

            Entity lineLoginContact = null;
            if (!string.IsNullOrWhiteSpace(userLineId))
            {
                lineLoginContact = _utility.RetrieveContactByLineId(userLineId);
            }
            else
            {
                lineLoginContact = fallbackContact;
            }

            // CRM 查無 contact 時必須 fail-closed。不可建立假的 contact，也不可把 null
            // 傳給 FillIdentity 或 FillFeeList，否則會拋例外，或把上一位使用者的模型留在畫面上。
            if (lineLoginContact == null)
            {
                return ClearModelForMissingContact(model);
            }

            FillIdentity(model, lineLoginContact, overwriteWhenClickTypeExists: true);
            model.NationId = _utility.GetEntityStringAttribute(ref lineLoginContact, "new_personal_id");
            model.Category = "十一奉獻";
            model.PayWay = "信用卡";
            model.DedicationDate = DateTime.Now;
            model.DedicateLocation = "好牧人";

            _feeQueryService.FillFeeList(model, lineLoginContact);
            return model;
        }

        /// <summary>
        /// 嘗試以 LINE user id 查詢 CRM contact。
        /// </summary>
        /// <param name="userLineId">只接受來自目前已驗證 LINE 流程的 user id。</param>
        /// <param name="contact">成功時為 CRM contact；失敗時為 null。</param>
        /// <returns>查到有效 contact 時為 true。</returns>
        /// <remarks>
        /// 此方法只回傳查詢結果，不保存任何跨 request 狀態；呼叫端取得 contact 後仍須由自己的
        /// request/session owner 管理其生命週期，避免把 scoped 資料提升到程序級快取。
        /// </remarks>
        public bool TryResolveLineContact(string userLineId, out Entity contact)
        {
            contact = null;
            if (string.IsNullOrWhiteSpace(userLineId))
            {
                return false;
            }

            contact = _utility.RetrieveContactByLineId(userLineId);
            return contact != null;
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

        /// <summary>
        /// 清除查無 contact 時的所有使用者識別與奉獻清單欄位。
        /// </summary>
        /// <remarks>
        /// 清除操作在同一個 request 內完成，並保留表單必要的非個資預設值；不建立背景工作、
        /// cache entry、連線或其他需要額外釋放的資源。這是防止同一個 Session 換 LINE 帳號後
        /// 沿用前一位使用者資料的最後一道防線。
        /// </remarks>
        private static DonationPaymentFormModel ClearModelForMissingContact(DonationPaymentFormModel model)
        {
            model.EnsureFormDefaults();
            model.FullName = string.Empty;
            model.Mobile = string.Empty;
            model.DedicationNumber = string.Empty;
            model.NationId = string.Empty;
            model.LastSixDigit = string.Empty;
            model.DedicationFeeList.Clear();
            model.SameNameList.Clear();
            model.TotalAmount = 0;
            return model;
        }
    }
}
