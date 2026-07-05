// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Constants/CrmEntityColumns.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class CrmEntityColumns、class Contact、class List、class PresentRecord、class WeeklyReport、class DedicationBooking、class Fee、class StorLessons
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace ToolUtilityNameSpace.Constants
{
    /// <summary>
    /// CRM 實體標準欄位映射
    /// ? Phase 3.3: 提供標準欄位集合，避免重複定義
    ///
    /// 使用範例:
    /// var contact = optimizedQuery.RetrieveEntity("contact", id, ContactColumns.Basic);
    /// var contact = optimizedQuery.RetrieveEntity("contact", id, ContactColumns.Extended);
    /// </summary>
    public static class CrmEntityColumns
    {
        /// <summary>
        /// Contact (連絡人) 標準欄位
        /// </summary>
        public static class Contact
        {
            /// <summary>
            /// 基本欄位 (最常用)
            /// 用於: 列表顯示、基本資訊
            /// </summary>
            public static readonly string[] Basic =
            {
                "contactid",
                "fullname",
                "mobilephone"
            };

            /// <summary>
            /// 擴展欄位 (一般使用)
            /// 用於: 詳細資訊、編輯頁面
            /// </summary>
            public static readonly string[] Extended =
            {
                "contactid",
                "fullname",
                "mobilephone",
                "telephone2",          // 住家電話
                "emailaddress1",       // 電子郵件
                "address2_line1",      // 地址
                "customertypecode",    // 委身類型
                "gendercode",          // 性別
                "birthdate"            // 生日
            };

            /// <summary>
            /// 完整欄位 (詳細資訊)
            /// 用於: 完整編輯、匯出
            /// </summary>
            public static readonly string[] Full =
            {
                "contactid",
                "fullname",
                "lastname",
                "mobilephone",
                "telephone2",
                "company",             // 公司電話
                "emailaddress1",
                "address2_line1",
                "customertypecode",
                "gendercode",
                "birthdate",
                "familystatuscode",    // 婚姻狀態
                "new_spiriitual_identity", // 信仰狀態
                "new_enter_church_date",   // 進教會日期
                "new_industry",        // 職業
                "new_fb_account",      // Facebook
                "new_ig_account",      // Instagram
                "new_personal_id",     // 身分證字號
                "new_last_six_digit",  // 銀行帳戶後六碼
                "new_ntbt_ornot",      // 是否上傳國稅局
                "description",         // 描述
                "new_invitor",         // 邀請人
                "new_carers",          // 邀請人關係
                "new_cell_list_contact", // 主要小組
                "new_race_leader_contact", // 族系組長
                "parentcustomerid"     // 所屬教會
            };

            /// <summary>
            /// 新人跟進欄位
            /// 用於: 新人管理、跟進記錄
            /// </summary>
            public static readonly string[] FollowUp =
            {
                "contactid",
                "fullname",
                "mobilephone",
                "customertypecode",
                "new_enter_church_date",
                "new_start_tracking_date", // 開始關懷日期
                "description",
                "gendercode"
            };
        }

        /// <summary>
        /// List (名單) 標準欄位
        /// </summary>
        public static class List
        {
            /// <summary>
            /// 基本欄位
            /// </summary>
            public static readonly string[] Basic =
            {
                "listid",
                "listname",
                "purpose"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static readonly string[] Extended =
            {
                "listid",
                "listname",
                "purpose",
                "createdfromcode",
                "type",                    // 靜態/動態
                "new_app_named",           // APP點名
                "new_contact_family_leader_list",  // 小組長
                "new_contact_race_leager_list",    // 族系組長
                "new_familyhead_list",     // 小家長
                "statuscode",
                "statecode"
            };
        }

        /// <summary>
        /// new_present_record (出席記錄) 標準欄位
        /// </summary>
        public static class PresentRecord
        {
            /// <summary>
            /// 基本欄位
            /// </summary>
            public static readonly string[] Basic =
            {
                "new_present_recordid",
                "new_contact_new_present_record",  // 聯絡人
                "new_sunday_date",                 // 主日日期
                "new_sunday_present_this_week",    // 主日出席
                "new_group_present_this_week"      // 小組出席
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static readonly string[] Extended =
            {
                "new_present_recordid",
                "new_contact_new_present_record",
                "new_sunday_date",
                "new_group_date",
                "new_sunday_present_this_week",
                "new_group_present_this_week",
                "new_sunday_rate",            // 主日出席率
                "new_small_group_rate",       // 小組出席率
                "new_list_new_present_record", // 名單
                "new_groupleader_present_record", // 小組長
                "new_explanation",            // 附註
                "new_cell_hpone"              // 手機
            };

            /// <summary>
            /// 新人跟進欄位
            /// </summary>
            public static readonly string[] FollowUp =
            {
                "new_present_recordid",
                "new_contact_new_present_record",
                "new_sunday_date",
                "new_weeks",              // 週次
                "new_follow_up",          // 跟進方式
                "new_conclusion_choise",  // 跟進結果
                "new_next_step",          // 下一步驟
                "new_explanation",        // 跟進描述
                "new_groupleader_present_record"
            };
        }

        /// <summary>
        /// new_group_present_weekly_report (週報) 標準欄位
        /// </summary>
        public static class WeeklyReport
        {
            /// <summary>
            /// 基本欄位
            /// </summary>
            public static readonly string[] Basic =
            {
                "new_group_present_weekly_reportid",
                "new_name",
                "new_sunday_date",
                "new_list_group_present_weekly_report"  // 名單
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static readonly string[] Extended =
            {
                "new_group_present_weekly_reportid",
                "new_name",
                "new_sunday_date",
                "new_list_group_present_weekly_report",
                "new_small_group_place",     // 小組地點
                "new_small_group_time",      // 小組時間
                "new_sunday_present_rate",   // 主日出席率
                "new_group_present_rate",    // 小組出席率
                "new_sunday_present_number", // 主日出席人數
                "new_group_present_number",  // 小組出席人數
                "createdon",
                "statecode"
            };
        }

        /// <summary>
        /// new_dedication_booking (奉獻預約) 標準欄位
        /// </summary>
        public static class DedicationBooking
        {
            /// <summary>
            /// 基本欄位
            /// </summary>
            public static readonly string[] Basic =
            {
                "new_dedication_bookingid",
                "new_name",
                "new_contact_new_dedication_booking",  // 聯絡人
                "new_dedication_booking_status",       // 狀態
                "createdon"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static readonly string[] Extended =
            {
                "new_dedication_bookingid",
                "new_name",
                "new_contact_new_dedication_booking",
                "new_dedication_booking_status",
                "new_dedication_type",          // 奉獻類型
                "new_amount",                   // 金額
                "new_start_date",               // 開始日期
                "new_end_date",                 // 結束日期
                "createdon",
                "statecode"
            };
        }

        /// <summary>
        /// new_fee (費用) 標準欄位
        /// </summary>
        public static class Fee
        {
            /// <summary>
            /// 基本欄位
            /// </summary>
            public static readonly string[] Basic =
            {
                "new_feeid",
                "new_name",
                "new_dedication_booking_new_fee",  // 奉獻預約
                "new_paid_period",                 // 繳費期間
                "createdon"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static readonly string[] Extended =
            {
                "new_feeid",
                "new_name",
                "new_dedication_booking_new_fee",
                "new_paid_period",
                "new_amount",           // 金額
                "new_payment_status",   // 付款狀態
                "new_payment_date",     // 付款日期
                "new_payment_method",   // 付款方式
                "createdon",
                "statecode"
            };
        }

        /// <summary>
        /// new_stor_lessons (課程記錄) 標準欄位
        /// </summary>
        public static class StorLessons
        {
            /// <summary>
            /// 基本欄位
            /// </summary>
            public static readonly string[] Basic =
            {
                "new_stor_lessonsid",
                "new_contact_new_stor_lessons",       // 聯絡人
                "new_new_disciple_lessons_new_stor_les", // 課程
                "createdon"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static readonly string[] Extended =
            {
                "new_stor_lessonsid",
                "new_contact_new_stor_lessons",
                "new_new_disciple_lessons_new_stor_les",
                "new_fee",              // 費用
                "new_pay_date",         // 付款日期
                "new_current_complete", // 完成狀態
                "new_enroll_status",    // 報名狀態
                "createdon",
                "statuscode",
                "statecode"
            };
        }

        /// <summary>
        /// Account (教會) 標準欄位
        /// </summary>
        public static class Account
        {
            /// <summary>
            /// 基本欄位
            /// </summary>
            public static readonly string[] Basic =
            {
                "accountid",
                "name"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static readonly string[] Extended =
            {
                "accountid",
                "name",
                "telephone1",
                "address1_line1",
                "emailaddress1"
            };
        }

        /// <summary>
        /// Task (工作) 標準欄位
        /// </summary>
        public static class Task
        {
            /// <summary>
            /// 基本欄位
            /// </summary>
            public static readonly string[] Basic =
            {
                "activityid",
                "subject",
                "statecode"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static readonly string[] Extended =
            {
                "activityid",
                "subject",
                "description",
                "scheduledend",
                "prioritycode",
                "regardingobjectid",
                "createdby",
                "statecode",
                "createdon"
            };
        }
    }
}
