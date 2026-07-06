// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/Converters/FollowUpConverter.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class FollowUpConverter
// 主要成員：WeekPickerToIndex、IndexToWeekPicker、NumberToWeekPicker、NumberToWeekIndex、ResultPickerToIndex、IndexToResultPicker、NextStepPickerToIndex、IndexToNextStepPicker、OptionToIndex、IndexToOptionPicker
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace ChurchReport.WebServiceConnector.Converters
{
    /// <summary>
    /// 新人跟進相關的數值與文字轉換器
    /// 遵循 Linus 代碼原則：單一職責、小型函數
    /// </summary>
    public static class FollowUpConverter
    {
        #region 週次轉換 (文字 -> 數值)

        /// <summary>
        /// 將週次文字轉換為 CRM OptionSet 數值
        /// </summary>
        public static int WeekPickerToIndex(string followUpWeek)
        {
            return followUpWeek switch
            {
                "一" => 100000000,
                "二" => 100000001,
                "三" => 100000002,
                "四" => 100000003,
                "五" => 100000004,
                "六" => 100000005,
                "七" => 100000006,
                "八" => 100000007,
                "九" => 100000008,
                "十" => 100000009,
                "十一" => 100000010,
                "十二" => 100000011,
                "十三" => 100000012,
                "十四" => 100000013,
                "十五" => 100000014,
                "十六" => 100000015,
                "十七" => 100000016,
                "十八" => 100000017,
                "十九" => 100000018,
                "二十" => 100000019,
                _ => 100000008 // 預設值
            };
        }

        #endregion

        #region 週次轉換 (數值 -> 文字)

        /// <summary>
        /// 將 CRM OptionSet 數值轉換為週次文字
        /// </summary>
        public static string IndexToWeekPicker(int optionValue)
        {
            return optionValue switch
            {
                100000000 => "一",
                100000001 => "二",
                100000002 => "三",
                100000003 => "四",
                100000004 => "五",
                100000005 => "六",
                100000006 => "七",
                100000007 => "八",
                100000009 => "九",
                100000010 => "十",
                100000011 => "十一",
                100000012 => "十二",
                100000013 => "十三",
                100000014 => "十四",
                100000015 => "十五",
                100000016 => "十六",
                100000017 => "十七",
                100000018 => "十八",
                100000019 => "十九",
                100000020 => "二十",
                100000008 => "未選擇",
                _ => "."
            };
        }

        /// <summary>
        /// 將數字週次轉換為中文週次
        /// </summary>
        public static string NumberToWeekPicker(int weekNumber)
        {
            return weekNumber switch
            {
                1 => "一",
                2 => "二",
                3 => "三",
                4 => "四",
                5 => "五",
                6 => "六",
                7 => "七",
                8 => "八",
                9 => "九",
                10 => "十",
                11 => "十一",
                12 => "十二",
                13 => "十三",
                14 => "十四",
                15 => "十五",
                16 => "十六",
                17 => "十七",
                18 => "十八",
                19 => "十九",
                20 => "二十",
                _ => "二十"
            };
        }

        /// <summary>
        /// 將數字週次轉換為 CRM OptionSet 數值
        /// </summary>
        public static int NumberToWeekIndex(int weekNumber)
        {
            return weekNumber switch
            {
                1 => 100000000,
                2 => 100000001,
                3 => 100000002,
                4 => 100000003,
                5 => 100000004,
                6 => 100000005,
                7 => 100000006,
                8 => 100000007,
                9 => 100000008,
                10 => 100000009,
                11 => 100000010,
                12 => 100000011,
                13 => 100000012,
                14 => 100000013,
                15 => 100000014,
                16 => 100000015,
                17 => 100000016,
                18 => 100000017,
                19 => 100000018,
                20 => 100000019,
                _ => 100000007
            };
        }

        #endregion

        #region 跟進結果轉換

        /// <summary>
        /// 將跟進結果文字轉換為 CRM OptionSet 數值
        /// </summary>
        public static int ResultPickerToIndex(string followUpResult)
        {
            return followUpResult switch
            {
                "請選擇" => 100000000,
                "熱情回應" => 100000001,
                "渴慕認識信仰" => 100000002,
                "沒聯絡上" => 100000003,
                "反應冷淡" => 100000004,
                "考慮中，繼續跟進" => 100000005,
                "入小組" => 100000006,
                "來主日" => 100000007,
                "轉介" => 100000008,
                "其他" => 100000009,
                _ => 100000000
            };
        }

        /// <summary>
        /// 將 CRM OptionSet 數值轉換為跟進結果文字
        /// </summary>
        public static string IndexToResultPicker(int optionValue)
        {
            return optionValue switch
            {
                100000000 => "請選擇",
                100000001 => "熱情回應",
                100000002 => "渴慕認識信仰",
                100000003 => "沒聯絡上",
                100000004 => "反應冷淡",
                100000005 => "考慮中，繼續跟進",
                100000006 => "入小組",
                100000007 => "來主日",
                100000008 => "轉介",
                100000009 => "其他",
                _ => ""
            };
        }

        #endregion

        #region 下一步驟轉換

        /// <summary>
        /// 將下一步驟文字轉換為 CRM OptionSet 數值
        /// </summary>
        public static int NextStepPickerToIndex(string followUpNextStep)
        {
            return followUpNextStep switch
            {
                "請選擇" => 100000000,
                "繼續跟進" => 100000001,
                "轉介" => 100000002,
                _ => 100000000
            };
        }

        /// <summary>
        /// 將 CRM OptionSet 數值轉換為下一步驟文字
        /// </summary>
        public static string IndexToNextStepPicker(int optionValue)
        {
            return optionValue switch
            {
                100000000 => "請選擇",
                100000001 => "繼續跟進",
                100000002 => "轉介",
                _ => ""
            };
        }

        #endregion

        #region 跟進方式轉換

        /// <summary>
        /// 將跟進方式文字轉換為 CRM OptionSet 數值
        /// </summary>
        public static int OptionToIndex(string followUpOption)
        {
            return followUpOption switch
            {
                "電話" => 100000000,
                "探訪" => 100000001,
                "Line/FB" => 100000002,
                "出遊/吃飯" => 100000003,
                "懷鄉/其他課程" => 100000004,
                "約談" => 100000005,
                "沒跟進" => 100000006,
                "其他" => 100000007,
                _ => 100000000
            };
        }

        /// <summary>
        /// 將 CRM OptionSet 數值轉換為跟進方式文字
        /// </summary>
        public static string IndexToOptionPicker(int optionValue)
        {
            return optionValue switch
            {
                100000000 => "電話",
                100000001 => "探訪",
                100000002 => "Line/FB",
                100000003 => "出遊/吃飯",
                100000004 => "懷鄉/其他課程",
                100000005 => "約談",
                100000006 => "沒跟進",
                100000007 => "其他",
                _ => ""
            };
        }

        #endregion

        #region 幸福小組主題轉換

        /// <summary>
        /// 將 CRM OptionSet 數值轉換為幸福小組主題
        /// </summary>
        public static string IndexToTopic(int optionValue)
        {
            return optionValue switch
            {
                100000000 => "預備週",
                100000001 => "真幸福",
                100000002 => "真相大白",
                100000003 => "萬世巨星",
                100000004 => "幸福連線",
                100000005 => "當上帝來敲門",
                100000006 => "十字架的勝利",
                100000007 => "釋放與自由",
                100000008 => "幸福的教會",
                _ => ""
            };
        }

        #endregion
    }
}
