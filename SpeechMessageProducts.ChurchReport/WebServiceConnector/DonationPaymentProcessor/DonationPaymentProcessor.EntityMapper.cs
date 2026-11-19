/*
 * 檔案整體說明
 *
 * 目的與定位：金流處理器 - 實體欄位映射模組 【職責】 - CRM 欄位映射 - OptionSet 值設定 - 奉獻類別映射 - 收入類別判斷 - 會計科目設定 【設計原則】 - 策略模式：不同類別的映射策略 -
 * 查找表模式：使用字典快速映射
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentProcessor。奉獻類別映射只接受已由登入教會清單解析的領域值，
 * 並於候選 Entity 同時設定正式 lookup 與相容 OptionSet 欄位；不再依名稱或 metadata 猜測身分。
 * 主要操作入口包含 SetFeePayCategory、SetPayCategory、GetCategoryValueByDisplayText、SetIncomeCategory、SetAccountingCode、
 * SetPayMethod、SetPayStatus。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * P6-07公開邊界：DonationPaymentProcessor改為產品內部宣告；外部輸出與既有資源清理契約不變。
 */
using Microsoft.Xrm.Sdk;
using ChurchReport.Services;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;
using System;
using System.Collections.Generic;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 金流處理器 - 實體欄位映射模組
    ///
    /// 【職責】
    /// - CRM 欄位映射
    /// - OptionSet 值設定
    /// - 奉獻類別映射
    /// - 收入類別判斷
    /// - 會計科目設定
    ///
    /// 【設計原則】
    /// - 策略模式：不同類別的映射策略
    /// - 查找表模式：使用字典快速映射
    /// </summary>
    /// <remarks>P6-07限於本產品內部使用；既有資料形狀、呼叫順序與資源所有權不變，不作為跨組件API。</remarks>
    internal partial class DonationPaymentProcessor
    {
        #region ===== 奉獻類別映射 =====

        /// <summary>
        /// 設定已驗證的奉獻類別正式 lookup 與既有必填選項欄位。
        /// </summary>
        public void SetFeePayCategory(OfferingCategory category, ref Entity aFeeEntity)
        {
            DonationOfferingCategoryResolver.ApplyToEntity(
                aFeeEntity,
                category,
                "new_offeringcategoryid",
                "new_category");
        }

        /// <summary>
        /// 設定已驗證奉獻類別的 lookup 與指定相容欄位。
        /// </summary>
        public void SetPayCategory(OfferingCategory category, string attributeName, ref Entity aFeeEntity)
        {
            string lookupAttribute = attributeName == "new_dedication_category"
                ? "new_offeringcategoryid"
                : throw new ArgumentException("不支援的奉獻類別相容欄位。", nameof(attributeName));
            DonationOfferingCategoryResolver.ApplyToEntity(
                aFeeEntity,
                category,
                lookupAttribute,
                attributeName);
        }

        #endregion

        #region ===== 收入類別映射 =====

        /// <summary>
        /// 設定收入類別
        /// </summary>
        public void SetIncomeCategory(string value, ref Entity aFeeEntity)
        {
            // ? 修復：檢查 null 或空白字串
            if (string.IsNullOrWhiteSpace(value))
            {
                System.Diagnostics.Debug.WriteLine("[SetIncomeCategory] 奉獻類別為空，使用預設值（經常費收入）");
                ToolUtility.SetEntityStringAttribute(aFeeEntity, "new_income_category", "經常費收入");
                return;
            }

            var regularIncomeTypes = new HashSet<string>
            {
                "十一奉獻", "主日奉獻", "聖餐獻金", "節期獻金",
                "感恩奉獻", "特別奉獻", "利息收入", "對內獻金", "其他收入"
            };

            if (regularIncomeTypes.Contains(value))
            {
                if (value != "特別奉獻")
                {
                    ToolUtility.SetEntityStringAttribute(aFeeEntity, "new_income_category", "經常費收入");
                }
                else
                {
                    // 處理特別奉獻：根據是否有 others 欄位判斷
                    var others = ToolUtility.GetEntityStringAttribute(aFeeEntity, "new_others");
                    var incomeCategory = !string.IsNullOrEmpty(others) ? "專帳收入" : "經常費收入";
                    ToolUtility.SetEntityStringAttribute(aFeeEntity, "new_income_category", incomeCategory);
                }
            }
            else
            {
                ToolUtility.SetEntityStringAttribute(aFeeEntity, "new_income_category", "專帳收入");
            }
        }

        #endregion

        #region ===== 會計科目映射 =====

        /// <summary>
        /// 設定會計科目（暫時保留，但建議使用動態映射）
        /// </summary>
        public void SetAccountingCode(OfferingCategory category, ref Entity aFeeEntity)
        {
            if (!string.IsNullOrWhiteSpace(category?.AccountingCode))
            {
                ToolUtility.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", category.AccountingCode);
            }
        }

        #endregion

        #region ===== 付款方式映射 =====

        /// <summary>
        /// 設定付款方式
        /// </summary>
        public void SetPayMethod(string value, ref Entity aFeeEntity)
        {
            // ? 修復：檢查 null 或空白字串，避免 Dictionary.ContainsKey(null) 拋出異常
            if (string.IsNullOrWhiteSpace(value))
            {
                System.Diagnostics.Debug.WriteLine("[SetPayMethod] 付款方式為空，使用預設值（未知 = 100000004）");
                ToolUtility.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000004);
                return;
            }

            var payMethodMap = new Dictionary<string, int>
            {
                { "現金", 100000000 },
                { "信用卡", 100000001 },
                { "ATM轉帳/匯款", 100000002 },
                { "未知", 100000004 },
                { "LinePay", 100000005 },
                { "銀行轉帳", 100000006 },
                { "行動支付", 100000007 },
                { "銀聯卡", 100000008 },
                { "超商付款", 100000004 }
            };

            var payMethod = payMethodMap.ContainsKey(value)
                ? payMethodMap[value]
                : 100000004; // 預設為未知

            System.Diagnostics.Debug.WriteLine($"[SetPayMethod] 設定付款方式: {value} -> {payMethod}");
            ToolUtility.SetOptionSetAttribute(aFeeEntity, "new_pay_way", payMethod);
        }

        #endregion

        #region ===== 付款狀態映射 =====

        /// <summary>
        /// 設定付款狀態
        /// </summary>
        public void SetPayStatus(string value, ref Entity aFeeEntity)
        {
            // ? 修復：檢查 null 或空白字串，避免 Dictionary.ContainsKey(null) 拋出異常
            if (string.IsNullOrWhiteSpace(value))
            {
                System.Diagnostics.Debug.WriteLine("[SetPayStatus] 付款狀態為空，使用預設值（新建立 = 100000000）");
                ToolUtility.SetOptionSetAttribute(aFeeEntity, "new_pay_status", 100000000);
                return;
            }

            var payStatusMap = new Dictionary<string, int>
            {
                { "新建立", 100000000 },
                { "信用卡已繳費", 100000001 },
                { "ATM轉帳/匯款已繳費", 100000002 },
                { "現金已繳費", 100000003 },
                { "銀行轉帳已繳費", 100000004 }
            };

            var payStatus = payStatusMap.ContainsKey(value)
                ? payStatusMap[value]
                : 100000000; // 預設為新建立

            System.Diagnostics.Debug.WriteLine($"[SetPayStatus] 設定付款狀態: {value} -> {payStatus}");
            ToolUtility.SetOptionSetAttribute(aFeeEntity, "new_pay_status", payStatus);
        }

        #endregion
    }
}
