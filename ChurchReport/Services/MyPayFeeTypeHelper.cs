using System;
using Microsoft.Extensions.Logging;
using ToolUtilityNameSpace;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.Services
{
    /// <summary>
    /// MyPay 收費單類型判斷輔助服務
    /// 負責判斷收費單類型及取得相關資訊
    /// </summary>
    public class MyPayFeeTypeHelper
    {
        private readonly ILogger<MyPayFeeTypeHelper> _logger;

        public MyPayFeeTypeHelper(ILogger<MyPayFeeTypeHelper> logger)
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
        /// - 100000008: 特別獻金
        /// </summary>
        public string GetDedicationCategoryName(int categoryValue)
        {
            switch (categoryValue)
            {
                case 100000010: return "主日奉獻";
                case 100000000: return "十一奉獻";
                case 100000002: return "感恩奉獻";
                case 100000006: return "建堂奉獻";
                case 100000007: return "宣教奉獻";
                case 100000019: return "愛心奉獻";
                case 100000008: return "特別獻金";
                default: return "奉獻";
            }
        }
    }
}
