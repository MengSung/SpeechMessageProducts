/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 付款收費單類型判斷輔助服務 此類別只理解 ChurchReport 的 CRM 欄位與奉獻/課程分類規則，不處理任何 MyPay、永豐或台新 provider protocol。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentFeeTypeHelper、FeeType。
 * - ======================================== 判斷收費單類型 ======================================== 【判斷邏輯】 1. 
 * 檢查是否有關聯課程（new_course_id）→ 課程類型 2. 檢查收費單名稱是否包含課程關鍵字 → 課程類型 3. 檢查課程名稱欄位是否有值 → 課程類型 4. 
 * 檢查奉獻類別代碼範圍（100000000~100000019）→ 奉獻類型 5. 預設為奉獻類型
 * - ======================================== 取得課程名稱 ======================================== 【取得順序】 1. 
 * 透過課程關聯（new_course_id）查詢課程實體的名稱 2. 使用收費單的課程名稱欄位（new_course_name） 3. 使用收費單本身的名稱（new_name） 4. 預設回傳「課程」
 * - ======================================== 取得奉獻類別名稱 ======================================== 【支援的奉獻類別】 - 
 * 100000010: 主日奉獻 - 100000000: 十一奉獻 - 100000002: 感恩奉獻 - 100000006: 建堂奉獻 - 100000007: 宣教奉獻 - 100000019: 愛心奉獻 - 
 * 100000008: 特別奉獻 【支援多種輸入方式】 1. 傳入 Entity：從 FormattedValues 或 OptionSetValue 取得 2. 傳入 int：直接對應類別代碼
 * 主要操作入口包含 DetermineFeeType、GetCourseName、GetDedicationCategoryName。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PaymentNotificationServiceWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/ChurchReportPaymentContextBuilderTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/MyPayControllerAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/PaymentProductServiceNamingTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/TspgControllerAdapterTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using Microsoft.Extensions.Logging;
using ToolUtilityNameSpace;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 付款收費單類型判斷輔助服務
    /// 此類別只理解 ChurchReport 的 CRM 欄位與奉獻/課程分類規則，不處理任何 MyPay、永豐或台新 provider protocol。
    /// </summary>
    public class PaymentFeeTypeHelper
    {
        private readonly ILogger<PaymentFeeTypeHelper> _logger;

        public PaymentFeeTypeHelper(ILogger<PaymentFeeTypeHelper> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 收費單類型列舉
        /// </summary>
        public enum FeeType
        {
            /// <summary>奉獻類型</summary>
            Dedication,
            /// <summary>課程類型</summary>
            Course,
            /// <summary>其他類型</summary>
            Other
        }

        /// <summary>
        /// ========================================
        /// 判斷收費單類型
        /// ========================================
        ///
        /// 【判斷邏輯】
        /// 1. 檢查是否有關聯課程（new_course_id）→ 課程類型
        /// 2. 檢查收費單名稱是否包含課程關鍵字 → 課程類型
        /// 3. 檢查課程名稱欄位是否有值 → 課程類型
        /// 4. 檢查奉獻類別代碼範圍（100000000~100000019）→ 奉獻類型
        /// 5. 預設為奉獻類型
        /// </summary>
        public FeeType DetermineFeeType(ToolUtilityClass utility, Entity feeEntity)
        {
            try
            {
                // 判斷 1：檢查課程關聯
                var courseId = utility.GetEntityLookupAttribute(feeEntity, "new_course_id");
                if (courseId != Guid.Empty)
                {
                    return FeeType.Course;
                }

                // 判斷 2：檢查收費單名稱是否包含課程關鍵字
                string feeName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? string.Empty;
                if (feeName.Contains("課程") ||
                    feeName.Contains("報名") ||
                    feeName.Contains("學費") ||
                    feeName.Contains("培訓") ||
                    feeName.Contains("研習"))
                {
                    return FeeType.Course;
                }

                // 判斷 3：檢查課程名稱欄位
                string courseName = utility.GetEntityStringAttribute(feeEntity, "new_course_name");
                if (!string.IsNullOrWhiteSpace(courseName))
                {
                    return FeeType.Course;
                }

                // 判斷 4：檢查奉獻類別代碼
                int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                if (categoryValue >= 100000000 && categoryValue <= 100000019)
                {
                    return FeeType.Dedication;
                }

                // 預設判斷：奉獻類型
                return FeeType.Dedication;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DetermineFeeType例外，預設奉獻");
                return FeeType.Dedication;
            }
        }

        /// <summary>
        /// ========================================
        /// 取得課程名稱
        /// ========================================
        ///
        /// 【取得順序】
        /// 1. 透過課程關聯（new_course_id）查詢課程實體的名稱
        /// 2. 使用收費單的課程名稱欄位（new_course_name）
        /// 3. 使用收費單本身的名稱（new_name）
        /// 4. 預設回傳「課程」
        /// </summary>
        public string GetCourseName(ToolUtilityClass utility, Entity feeEntity)
        {
            try
            {
                // 方法 1：從課程實體查詢
                var courseId = utility.GetEntityLookupAttribute(feeEntity, "new_course_id");
                if (courseId != Guid.Empty)
                {
                    var courseEntity = utility.RetrieveEntity("new_course", courseId);
                    if (courseEntity != null)
                    {
                        var name = utility.GetEntityStringAttribute(courseEntity, "new_name");
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name;
                        }
                    }
                }

                // 方法 2：從收費單的課程名稱欄位
                var courseNameField = utility.GetEntityStringAttribute(feeEntity, "new_course_name");
                if (!string.IsNullOrWhiteSpace(courseNameField))
                {
                    return courseNameField;
                }

                // 方法 3：從收費單名稱
                return utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "課程";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCourseName例外");
                return "課程";
            }
        }

        /// <summary>
        /// ========================================
        /// 取得奉獻類別名稱
        /// ========================================
        ///
        /// 【支援的奉獻類別】
        /// - 100000010: 主日奉獻
        /// - 100000000: 十一奉獻
        /// - 100000002: 感恩奉獻
        /// - 100000006: 建堂奉獻
        /// - 100000007: 宣教奉獻
        /// - 100000019: 愛心奉獻
        /// - 100000008: 特別奉獻
        ///
        /// 【支援多種輸入方式】
        /// 1. 傳入 Entity：從 FormattedValues 或 OptionSetValue 取得
        /// 2. 傳入 int：直接對應類別代碼
        /// </summary>
        public string GetDedicationCategoryName(Entity aFeeEntity)
        {
            try
            {
                // 方法 1: 優先使用 FormattedValues（最快速且最可靠）
                if (aFeeEntity.FormattedValues.Contains("new_category"))
                {
                    string displayText = aFeeEntity.FormattedValues["new_category"];
                    if (!string.IsNullOrEmpty(displayText))
                    {
                        _logger.LogDebug("使用 FormattedValues 取得奉獻類別: {Category}", displayText);
                        return displayText;
                    }
                }

                // 預設值
                _logger.LogWarning("無法從 Entity 取得奉獻類別，使用預設值");
                return "十一奉獻";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDedicationCategoryName(Entity) 發生錯誤");
                return "十一奉獻";
            }
        }

    }
}
