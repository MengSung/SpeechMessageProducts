// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Core/Constants/CrmEntityColumns.cs
// 所屬區塊：供 net462 與 net10 共用的純 Dataverse 欄位結構目錄，不依賴產品 host。
// 檔案責任：集中保存各 CRM 實體查詢會使用的固定欄位名稱與順序，避免各呼叫端複製不同版本。
// 主要型別：CrmEntityColumns，以及 Contact、List、PresentRecord、WeeklyReport、DedicationBooking、Fee、StorLessons、Account、Task 九個分類。
// 主要成員：21 組公開欄位複本存取器；每次讀取都回傳新的 caller-owned 陣列，不共享可變陣列。
// 引用命名空間：System
// 閱讀路徑：依實體分類確認欄位 logical name；實際查詢、授權、連線與例外處理由各 host 負責。
// 維護重點：私有模板只保存有限的結構字串；不可直接公開模板，也不可依姓名、租戶或請求建立共享可變狀態。
// 行為保護：golden 測試鎖定 21 組欄位及其順序；修改欄位需先確認消費者契約並同步更新特性證據。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace ToolUtilityNameSpace.Constants
{
    /// <summary>
    /// 提供各 Dataverse 實體既有查詢使用的固定欄位集合；每組欄位名稱與順序都由 golden 契約保護。
    /// </summary>
    /// <remarks>
    /// 類別只保存有限的結構欄位名稱，不持有 CRM Entity、服務連線、使用者、教會或租戶資料。
    /// 私有靜態模板只供 getter 複製；每次 getter 都建立新的陣列交給單一呼叫端，
    /// 因此呼叫端可為 SDK 查詢調整自己的陣列，而不會污染同時或後續操作。
    /// 例如：<c>new ColumnSet(CrmEntityColumns.Contact.Basic)</c>。
    /// </remarks>
    public static class CrmEntityColumns
    {
        /// <summary>
        /// Contact (連絡人) 標準欄位
        /// </summary>
        public static class Contact
        {
            private static readonly string[] BasicColumns = {
                "contactid",
                "fullname",
                "mobilephone"
            };

            /// <summary>
            /// 基本欄位 (最常用)
            /// 用於: 列表顯示、基本資訊
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
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
            /// 擴展欄位 (一般使用)
            /// 用於: 詳細資訊、編輯頁面
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();

            private static readonly string[] FullColumns = {
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
            /// 完整欄位 (詳細資訊)
            /// 用於: 完整編輯、匯出
            /// </summary>
            public static string[] Full => (string[])FullColumns.Clone();

            private static readonly string[] FollowUpColumns = {
                "contactid",
                "fullname",
                "mobilephone",
                "customertypecode",
                "new_enter_church_date",
                "new_start_tracking_date", // 開始關懷日期
                "description",
                "gendercode"
            };

            /// <summary>
            /// 新人跟進欄位
            /// 用於: 新人管理、跟進記錄
            /// </summary>
            public static string[] FollowUp => (string[])FollowUpColumns.Clone();
        }

        /// <summary>
        /// List (名單) 標準欄位
        /// </summary>
        public static class List
        {
            private static readonly string[] BasicColumns = {
                "listid",
                "listname",
                "purpose"
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
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

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }

        /// <summary>
        /// new_present_record (出席記錄) 標準欄位
        /// </summary>
        public static class PresentRecord
        {
            private static readonly string[] BasicColumns = {
                "new_present_recordid",
                "new_contact_new_present_record",  // 聯絡人
                "new_sunday_date",                 // 主日日期
                "new_sunday_present_this_week",    // 主日出席
                "new_group_present_this_week"      // 小組出席
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
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
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();

            private static readonly string[] FollowUpColumns = {
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

            /// <summary>
            /// 新人跟進欄位
            /// </summary>
            public static string[] FollowUp => (string[])FollowUpColumns.Clone();
        }

        /// <summary>
        /// new_group_present_weekly_report (週報) 標準欄位
        /// </summary>
        public static class WeeklyReport
        {
            private static readonly string[] BasicColumns = {
                "new_group_present_weekly_reportid",
                "new_name",
                "new_sunday_date",
                "new_list_group_present_weekly_report"  // 名單
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
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

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }

        /// <summary>
        /// new_dedication_booking (奉獻預約) 標準欄位
        /// </summary>
        public static class DedicationBooking
        {
            private static readonly string[] BasicColumns = {
                "new_dedication_bookingid",
                "new_name",
                "new_contact_new_dedication_booking",  // 聯絡人
                "new_dedication_booking_status",       // 狀態
                "createdon"
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
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

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }

        /// <summary>
        /// new_fee (費用) 標準欄位
        /// </summary>
        public static class Fee
        {
            private static readonly string[] BasicColumns = {
                "new_feeid",
                "new_name",
                "new_dedication_booking_new_fee",  // 奉獻預約
                "new_paid_period",                 // 繳費期間
                "createdon"
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
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

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }

        /// <summary>
        /// new_stor_lessons (課程記錄) 標準欄位
        /// </summary>
        public static class StorLessons
        {
            private static readonly string[] BasicColumns = {
                "new_stor_lessonsid",
                "new_contact_new_stor_lessons",       // 聯絡人
                "new_new_disciple_lessons_new_stor_les", // 課程
                "createdon"
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
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

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }

        /// <summary>
        /// Account (教會) 標準欄位
        /// </summary>
        public static class Account
        {
            private static readonly string[] BasicColumns = {
                "accountid",
                "name"
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
                "accountid",
                "name",
                "telephone1",
                "address1_line1",
                "emailaddress1"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }

        /// <summary>
        /// Task (工作) 標準欄位
        /// </summary>
        public static class Task
        {
            private static readonly string[] BasicColumns = {
                "activityid",
                "subject",
                "statecode"
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
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

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }
    }
}
