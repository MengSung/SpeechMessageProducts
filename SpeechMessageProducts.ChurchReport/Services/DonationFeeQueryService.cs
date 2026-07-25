// ============================================================================
// 檔案：ChurchReport/Services/DonationFeeQueryService.cs
// 目的：奉獻收費單查詢與畫面模型轉換。
//
// 保母教學：
// - 預設仍走舊 ToolUtility 路徑，確保現有環境不中斷。
// - 若注入 IPackage01FeeReadClient 且 DynamicsAccess:Package01FeeReadsEnabled=true，
//   則改走 no-SDK Package 1 受控操作。
// - 產品仍只得到 DedicationFee 畫面模型，不直接碰 CRM Entity（新路徑）。
// - 舊路徑暫時仍依賴 ToolUtility/Entity，這是遷移期兼容，不是最終態。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 奉獻收費單查詢與轉換服務。
    /// </summary>
    public sealed class DonationFeeQueryService
    {
        private readonly ToolUtilityClass _utility;
        private readonly IPackage01FeeReadClient? _package01FeeReadClient;
        private readonly ProductDynamicsOptions? _dynamicsAccess;
        private readonly bool _package01Enabled;

        public DonationFeeQueryService(ToolUtilityClass utility)
            : this(utility, package01FeeReadClient: null, dynamicsAccess: null)
        {
        }

        public DonationFeeQueryService(
            ToolUtilityClass utility,
            IPackage01FeeReadClient? package01FeeReadClient,
            IOptions<ProductDynamicsOptions>? dynamicsAccess,
            bool package01FeeReadsEnabled = false)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
            _package01FeeReadClient = package01FeeReadClient;
            _dynamicsAccess = dynamicsAccess?.Value;
            _package01Enabled = package01FeeReadsEnabled && package01FeeReadClient is not null;
        }

        /// <summary>
        /// 將 CRM 查出的奉獻收費單填入表單模型，並同步更新總金額。
        /// </summary>
        public void FillFeeList(DonationPaymentFormModel model, Entity contact)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(contact);

            var fullName = model.FullName;
            var contactId = contact.Id;

            if (_package01Enabled)
            {
                FillFeeListViaPackage01(model, contactId, fullName);
                return;
            }

            // ---- 舊路徑（遷移期兼容）----
            EntityCollection feeEntities = _utility.RetrieveDedicationFeeByDateFetchXml(
                fullName,
                contactId.ToString(),
                model.QueryStartDate,
                model.QueryEndDate);

            System.Diagnostics.Trace.WriteLine(
                $"[DEDQUERY-LEGACY] FullName={fullName} Start={model.QueryStartDate:yyyy-MM-dd} End={model.QueryEndDate:yyyy-MM-dd} Returned={feeEntities.Entities.Count}");

            model.TotalAmount = 0;
            model.DedicationFeeList = feeEntities.Entities
                .Select(MapFee)
                .ToList();

            foreach (var fee in model.DedicationFeeList)
            {
                model.TotalAmount += fee.Amount;
            }
        }

        private void FillFeeListViaPackage01(DonationPaymentFormModel model, Guid contactId, string? fullName)
        {
            var profileAlias = _dynamicsAccess?.ProfileAlias;
            if (string.IsNullOrWhiteSpace(profileAlias))
            {
                throw new InvalidOperationException(
                    "DynamicsAccess:ProfileAlias is required when Package01 fee reads are enabled.");
            }

            // WorkloadSubjectId 使用產品部署識別，不是終端使用者 LINE/session。
            const string workloadSubjectId = "church-report-service";

            var rows = _package01FeeReadClient!
                .RetrieveDedicationFeesByContactDateRangeAsync(
                    profileAlias,
                    workloadSubjectId,
                    contactId,
                    model.QueryStartDate,
                    model.QueryEndDate,
                    fullName)
                .GetAwaiter()
                .GetResult();

            System.Diagnostics.Trace.WriteLine(
                $"[DEDQUERY-P01] FullName={fullName} Start={model.QueryStartDate:yyyy-MM-dd} End={model.QueryEndDate:yyyy-MM-dd} Returned={rows.Count}");

            model.TotalAmount = 0;
            model.DedicationFeeList = rows.Select(MapFeeDto).ToList();
            foreach (var fee in model.DedicationFeeList)
            {
                model.TotalAmount += fee.Amount;
            }
        }

        private static DedicationFee MapFeeDto(SpeechMessage.Dynamics.ProductClient.Models.FeeRecordDto dto)
        {
            var amount = dto.Amount;
            if (amount < int.MinValue) amount = int.MinValue;
            if (amount > int.MaxValue) amount = int.MaxValue;

            return new DedicationFee
            {
                DedicationDate = (dto.CreatedOn ?? DateTimeOffset.MinValue).LocalDateTime,
                PayDate = (dto.PayDate ?? DateTimeOffset.MinValue).LocalDateTime,
                Amount = Convert.ToInt32(amount),
                PayWay = !string.IsNullOrWhiteSpace(dto.PayWayLabel)
                    ? dto.PayWayLabel!
                    : ConvertPayWay(dto.PayWayOption ?? -1),
                Category = !string.IsNullOrWhiteSpace(dto.CategoryLabel)
                    ? dto.CategoryLabel!
                    : "十一奉獻",
                Others = dto.Others ?? string.Empty,
                PaidPeriod = dto.PaidPeriod ?? string.Empty
            };
        }

        /// <summary>
        /// 將收費單清單投影成既有 AJAX endpoint 會序列化的匿名物件形狀。
        /// </summary>
        public static List<object> ToAjaxRows(IEnumerable<DedicationFee> fees)
        {
            return fees.Select(f => new
            {
                f.Category,
                f.DedicationDate,
                f.PayDate,
                f.PayWay,
                f.Amount,
                f.PaidPeriod,
                f.Others
            }).ToList<object>();
        }

        /// <summary>
        /// 將 CRM new_pay_way OptionSet 值轉成 ChurchReport 畫面文字。
        /// </summary>
        public static string ConvertPayWay(int optionSetValue)
        {
            return optionSetValue switch
            {
                100000000 => "現金",
                100000001 => "信用卡",
                100000002 => "ATM轉帳",
                100000003 => "超商付款",
                100000005 => "LinePay",
                100000006 => "銀行轉帳",
                100000007 => "行動支付",
                100000008 => "銀聯卡",
                _ => "未知"
            };
        }

        private DedicationFee MapFee(Entity feeEntity)
        {
            var fee = new DedicationFee
            {
                DedicationDate = _utility.GetEntityDateTimeAttribute(feeEntity, "createdon").ToLocalTime(),
                PayDate = _utility.GetEntityDateTimeAttribute(feeEntity, "new_pay_date").ToLocalTime(),
                Amount = Convert.ToInt32(_utility.GetEntityMoneyAttribute(feeEntity, "new_fee_really_paid").Value),
                PayWay = ConvertPayWay(_utility.GetOptionSetAttribute(feeEntity, "new_pay_way")),
                Category = ConvertCategory(feeEntity),
                Others = _utility.GetEntityStringAttribute(feeEntity, "new_others"),
                PaidPeriod = _utility.GetEntityStringAttribute(feeEntity, "new_paid_period")
            };

            return fee;
        }

        /// <summary>
        /// 優先使用 CRM FormattedValues 的顯示文字；查不到時回到既有預設「十一奉獻」。
        /// </summary>
        public static string ConvertCategory(Entity feeEntity)
        {
            try
            {
                if (feeEntity.FormattedValues.Contains("new_category"))
                {
                    string displayText = feeEntity.FormattedValues["new_category"];
                    if (!string.IsNullOrEmpty(displayText))
                    {
                        return displayText;
                    }
                }

                return "十一奉獻";
            }
            catch
            {
                return "十一奉獻";
            }
        }
    }
}
