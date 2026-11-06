/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 奉獻收費單查詢與轉換服務。 這個服務處理的是 CRM new_fee 實體與 ChurchReport 奉獻頁面模型之間的轉換， 因此必須留在 ChurchReport。
 * 通用金流專案只知道付款結果，不應該知道奉獻收費單欄位、 OptionSet 值或奉獻類別顯示邏輯。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationFeeQueryService。
 * - 將 CRM 查出的奉獻收費單填入表單模型，並同步更新總金額。
 * - 將收費單清單投影成既有 AJAX endpoint 會序列化的匿名物件形狀。
 * - 將 CRM new_pay_way OptionSet 值轉成 ChurchReport 畫面文字。
 * - 優先使用 CRM FormattedValues 的顯示文字；查不到時回到既有預設「十一奉獻」。
 * 主要操作入口包含 FillFeeList、ToAjaxRows、ConvertPayWay、MapFee、ConvertCategory。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Payments/DonationPaymentServiceExtractionTests.cs、
 * SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationBookingService.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationDedicationFeeFormService.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationKeyInDedicationService.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 奉獻收費單查詢與轉換服務。
    ///
    /// 這個服務處理的是 CRM <c>new_fee</c> 實體與 ChurchReport 奉獻頁面模型之間的轉換，
    /// 因此必須留在 ChurchReport。通用金流專案只知道付款結果，不應該知道奉獻收費單欄位、
    /// OptionSet 值或奉獻類別顯示邏輯。
    /// </summary>
    public sealed class DonationFeeQueryService
    {
        private readonly ToolUtilityClass _utility;

        public DonationFeeQueryService(ToolUtilityClass utility)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
        }

        /// <summary>
        /// 將 CRM 查出的奉獻收費單填入表單模型，並同步更新總金額。
        /// </summary>
        public void FillFeeList(DonationPaymentFormModel model, Entity contact)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(contact);

            var fullName = model.FullName;
            var contactId = contact.Id.ToString();
            EntityCollection feeEntities = _utility.RetrieveDedicationFeeByDateFetchXml(
                fullName,
                contactId,
                model.QueryStartDate,
                model.QueryEndDate);

            System.Diagnostics.Trace.WriteLine(
                $"[DEDQUERY] FullName={fullName} Start={model.QueryStartDate:yyyy-MM-dd} End={model.QueryEndDate:yyyy-MM-dd} Returned={feeEntities.Entities.Count}");

            model.TotalAmount = 0;
            model.DedicationFeeList = feeEntities.Entities
                .Select(MapFee)
                .ToList();

            foreach (var fee in model.DedicationFeeList)
            {
                model.TotalAmount += fee.Amount;
            }
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
