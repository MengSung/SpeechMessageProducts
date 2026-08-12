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

        /// <summary>
        /// 建立保留 legacy 相容行為的表單服務；不會自行建立 drain controller 或任何 connector。
        /// </summary>
        /// <param name="utility">既有 ToolUtility，由上游產品流程擁有。</param>
        public DonationDedicationFeeFormService(ToolUtilityClass utility)
            : this(utility, package01FeeReadClient: null, dynamicsAccess: null, package01FeeReadsEnabled: false)
        {
        }

        /// <summary>
        /// 建立可選 typed fee read 的表單服務。controller 必須是主 DI 擁有的 singleton，
        /// 僅保護受控 legacy fee ingress，不保存 contact、表單或使用者資料。
        /// </summary>
        /// <param name="utility">既有 ToolUtility。</param>
        /// <param name="package01FeeReadClient">可選 typed client。</param>
        /// <param name="dynamicsAccess">deployment-owned Dynamics 設定。</param>
        /// <param name="package01FeeReadsEnabled">Package01 feature flag 的既有解析結果。</param>
        /// <param name="legacyDrainController">可選 host-owned legacy drain controller。</param>
        public DonationDedicationFeeFormService(
            ToolUtilityClass utility,
            IPackage01FeeReadClient? package01FeeReadClient,
            IOptions<ProductDynamicsOptions>? dynamicsAccess,
            bool package01FeeReadsEnabled,
            LegacyToolUtilityDrainController? legacyDrainController = null)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
            _feeQueryService = new DonationFeeQueryService(
                _utility,
                package01FeeReadClient,
                dynamicsAccess,
                package01FeeReadsEnabled,
                legacyDrainController);
        }

        /// <summary>
        /// 取得本 session service 在建構時是否接到 deployment-owned Package01 typed read 路徑。
        /// </summary>
        /// <remarks>
        /// 這是唯讀的 route observation，不寫入設定、不開啟旗標，也不保存 contact、表單或
        /// 使用者資訊；controller 以它選擇既有 rollback-compatible legacy 或新的 typed read，
        /// 不允許任一 HTTP 呼叫改變此值。
        /// </remarks>
        public bool IsPackage01FeeReadEnabled => _feeQueryService.IsPackage01FeeReadEnabled;

        /// <summary>
        /// 在已完成 controller 伺服器端授權後，以 request-local typed DTO 讀取指定 contact 的
        /// 奉獻稽核資料。
        /// </summary>
        /// <param name="contactId">已授權的目標 contact locator；不在此層做 CRM Entity 補查。</param>
        /// <param name="cancellationToken">目前 HTTP request 的取消 token，原樣轉交 typed client。</param>
        /// <returns>不含付款表單、CRM Entity 或共享快取資料的獨立讀取結果。</returns>
        /// <exception cref="InvalidOperationException">Package01 未啟用時拒絕，避免默默 fallback。</exception>
        public Task<DonationFeeAuditReadResult> RetrieveFeeAuditByContactAsync(
            Guid contactId,
            CancellationToken cancellationToken = default)
        {
            if (!IsPackage01FeeReadEnabled)
            {
                throw new InvalidOperationException("Package01 fee audit reads are not enabled.");
            }

            return _feeQueryService.RetrieveFeeAuditByContactAsync(contactId, cancellationToken);
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
