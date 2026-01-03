using Microsoft.Xrm.Sdk;
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
    public partial class QPayProcessor
    {
        #region ===== 奉獻類別映射 =====

        /// <summary>
        /// 設定奉獻類別（使用動態 OptionSet 查詢）
        /// </summary>
        public void SetFeePayCategory(string value, ref Entity aFeeEntity)
        {
            try
            {
                var categoryValue = GetCategoryValueByDisplayText(value);
                ToolUtility.SetOptionSetAttribute(aFeeEntity, "new_category", categoryValue);
                
                System.Diagnostics.Debug.WriteLine($"[SetFeePayCategory] 設定奉獻類別: {value} -> {categoryValue}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SetFeePayCategory] 錯誤: {ex.Message}，使用預設值（十一奉獻）");
                ToolUtility.SetOptionSetAttribute(aFeeEntity, "new_category", 100000000);
            }
        }

        /// <summary>
        /// 設定付款類別（通用方法）
        /// </summary>
        public void SetPayCategory(string value, string attributeName, ref Entity aFeeEntity)
        {
            try
            {
                var categoryValue = GetCategoryValueByDisplayText(value);
                ToolUtility.SetOptionSetAttribute(aFeeEntity, attributeName, categoryValue);
                
                System.Diagnostics.Debug.WriteLine($"[SetPayCategory] 設定 {attributeName}: {value} -> {categoryValue}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SetPayCategory] 錯誤: {ex.Message}，使用預設值（十一奉獻）");
                ToolUtility.SetOptionSetAttribute(aFeeEntity, attributeName, 100000000);
            }
        }

        /// <summary>
        /// 根據顯示文字取得對應的 OptionSet 值（動態查詢）
        /// </summary>
        private int GetCategoryValueByDisplayText(string displayText)
        {
            try
            {
                if (string.IsNullOrEmpty(displayText))
                {
                    System.Diagnostics.Debug.WriteLine("[GetCategoryValueByDisplayText] 輸入為空，使用預設值（十一奉獻 = 100000000）");
                    return 100000000;
                }

                // 使用 OptionSetMetadataService 動態查詢
                int categoryValue = OptionSetService.GetOptionSetValue(
                    entityName: "new_fee",
                    attributeName: "new_category",
                    displayText: displayText.Trim(),
                    defaultValue: 100000000
                );

                System.Diagnostics.Debug.WriteLine($"[GetCategoryValueByDisplayText] {displayText} → {categoryValue}");
                return categoryValue;
            }
            catch (KeyNotFoundException)
            {
                System.Diagnostics.Debug.WriteLine($"[GetCategoryValueByDisplayText] 找不到對應的奉獻類別: {displayText}，使用預設值");
                return 100000000;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetCategoryValueByDisplayText] 錯誤: {ex.Message}，使用預設值");
                return 100000000;
            }
        }

        #endregion

        #region ===== 收入類別映射 =====

        /// <summary>
        /// 設定收入類別
        /// </summary>
        public void SetIncomeCategory(string value, ref Entity aFeeEntity)
        {
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
        public void SetAccountingCode(string value, ref Entity aFeeEntity)
        {
            var accountingCodeMap = new Dictionary<string, string>
            {
                { "十一奉獻", "4111100" },
                { "建堂奉獻", "4113100" },
                { "感恩奉獻", "4112100" },
                { "其他奉獻", "4119100" },
                { "指定奉獻", "4115100" },
                { "宣教奉獻", "4114100" },
                { "慈惠奉獻", "4116100" },
                { "特別奉獻", "4117100" }
            };

            var accountingCode = accountingCodeMap.ContainsKey(value) 
                ? accountingCodeMap[value] 
                : "4111100"; // 預設為十一奉獻

            ToolUtility.SetEntityStringAttribute(aFeeEntity, "new_accounting_code", accountingCode);
        }

        #endregion

        #region ===== 付款方式映射 =====

        /// <summary>
        /// 設定付款方式
        /// </summary>
        public void SetPayMethod(string value, ref Entity aFeeEntity)
        {
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
                : 100000000; // 預設為現金

            ToolUtility.SetOptionSetAttribute(aFeeEntity, "new_pay_way", payMethod);
        }

        #endregion

        #region ===== 付款狀態映射 =====

        /// <summary>
        /// 設定付款狀態
        /// </summary>
        public void SetPayStatus(string value, ref Entity aFeeEntity)
        {
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

            ToolUtility.SetOptionSetAttribute(aFeeEntity, "new_pay_status", payStatus);
        }

        #endregion
    }
}
