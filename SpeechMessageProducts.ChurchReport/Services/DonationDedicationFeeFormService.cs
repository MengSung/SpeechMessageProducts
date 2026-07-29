// ============================================================================
// 檔案：ChurchReport/Services/DonationDedicationFeeFormService.cs
// 目的：奉獻查詢表單刷新（identity + fee list）。
//
// 保母教學：
// - contact 讀取目前仍走 ToolUtility（尚未在 Package 1 範圍）。
// - fee list 可透過 DonationFeeQueryService 切到 Package 1 no-SDK 路徑。
// - 建構時可注入 IPackage01FeeReadClient；沒注入就維持舊行為。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Models;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 奉獻查詢表單刷新服務。
    /// </summary>
    public sealed class DonationDedicationFeeFormService
    {
        private readonly ToolUtilityClass _utility;
        private readonly DonationFeeQueryService _feeQueryService;

        public DonationDedicationFeeFormService(ToolUtilityClass utility)
            : this(utility, package01FeeReadClient: null, dynamicsAccess: null, package01FeeReadsEnabled: false)
        {
        }

        public DonationDedicationFeeFormService(
            ToolUtilityClass utility,
            IPackage01FeeReadClient? package01FeeReadClient,
            IOptions<ProductDynamicsOptions>? dynamicsAccess,
            bool package01FeeReadsEnabled)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
            _feeQueryService = new DonationFeeQueryService(
                _utility,
                package01FeeReadClient,
                dynamicsAccess,
                package01FeeReadsEnabled);
        }

        public async Task<DonationPaymentFormModel> FillFromLineIdAsync(
            DonationPaymentFormModel model,
            string userLineId,
            Entity fallbackContact,
            CancellationToken cancellationToken = default)
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

            await _feeQueryService.FillFeeListAsync(model, lineLoginContact, cancellationToken)
                .ConfigureAwait(false);
            return model;
        }

        public async Task<DonationPaymentFormModel> FillFromContactAsync(
            DonationPaymentFormModel model,
            Entity lineLoginContact,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(lineLoginContact);

            FillIdentity(
                model,
                lineLoginContact,
                overwriteWhenClickTypeExists: model.ClickType == null);

            await _feeQueryService.FillFeeListAsync(model, lineLoginContact, cancellationToken)
                .ConfigureAwait(false);
            return model;
        }

        /// <summary>
        /// 取得指定 contact 的奉獻收費單 AJAX rows。
        /// </summary>
        public async Task<List<object>> GetFeesByContactIdAsync(
            string contactId,
            DonationPaymentFormModel model,
            CancellationToken cancellationToken = default)
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

            await FillFromContactAsync(model, contactEntity, cancellationToken).ConfigureAwait(false);
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
