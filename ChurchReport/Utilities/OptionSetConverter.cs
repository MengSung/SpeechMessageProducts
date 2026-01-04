using ChurchReport.Domain.Constants;
using System;
using System.Text.RegularExpressions;

namespace ChurchReport.Utilities
{
    /// <summary>
    /// 選項集轉換工具
    /// 負責將 OptionSet 的數值與文字進行雙向轉換
    /// </summary>
    public class OptionSetConverter
    {
        private static readonly Regex DigitsOnly = new Regex(@"[^\d]");

        #region 跟進週次轉換

        /// <summary>
        /// 將中文週次轉換為 OptionSet 值
        /// </summary>
        public static int ChineseWeekToOptionSetValue(string chineseWeek)
        {
            return chineseWeek switch
            {
                "一" => FollowUpWeek.Week1,
                "二" => FollowUpWeek.Week2,
                "三" => FollowUpWeek.Week3,
                "四" => FollowUpWeek.Week4,
                "五" => FollowUpWeek.Week5,
                "六" => FollowUpWeek.Week6,
                "七" => FollowUpWeek.Week7,
                "八" => FollowUpWeek.Week8,
                "九" => FollowUpWeek.Week9,
                "十" => FollowUpWeek.Week10,
                "十一" => FollowUpWeek.Week11,
                "十二" => FollowUpWeek.Week12,
                "十三" => FollowUpWeek.Week13,
                "十四" => FollowUpWeek.Week14,
                "十五" => FollowUpWeek.Week15,
                "十六" => FollowUpWeek.Week16,
                "十七" => FollowUpWeek.Week17,
                "十八" => FollowUpWeek.Week18,
                "十九" => FollowUpWeek.Week19,
                "二十" => FollowUpWeek.Week20,
                _ => FollowUpWeek.Week8 // 預設第8週
            };
        }

        /// <summary>
        /// 將 OptionSet 值轉換為中文週次
        /// </summary>
        public static string OptionSetValueToChineseWeek(int optionSetValue)
        {
            return FollowUpWeek.WeekCodeToChineseNumber(optionSetValue);
        }

        #endregion

        #region 跟進結果轉換

        /// <summary>
        /// 將跟進結果文字轉換為 OptionSet 值
        /// </summary>
        public static int FollowUpResultTextToValue(string resultText)
        {
            return resultText switch
            {
                "請選擇" => FollowUpResult.PleaseSelect,
                "熱情回應" => FollowUpResult.EnthusiasticResponse,
                "渴慕認識信仰" => FollowUpResult.EagerToKnowFaith,
                "沒聯絡上" => FollowUpResult.NoContact,
                "反應冷淡" => FollowUpResult.ColdResponse,
                "考慮中，繼續跟進" => FollowUpResult.Considering,
                "入小組" => FollowUpResult.JoinedSmallGroup,
                "來主日" => FollowUpResult.AttendedSunday,
                "轉介" => FollowUpResult.Transferred,
                "其他" => FollowUpResult.Other,
                _ => FollowUpResult.PleaseSelect
            };
        }

        /// <summary>
        /// 將 OptionSet 值轉換為跟進結果文字
        /// </summary>
        public static string FollowUpResultValueToText(int optionSetValue)
        {
            return FollowUpResult.GetDisplayName(optionSetValue);
        }

        #endregion

        #region 跟進下一步驟轉換

        /// <summary>
        /// 將跟進下一步驟文字轉換為 OptionSet 值
        /// </summary>
        public static int FollowUpNextStepTextToValue(string nextStepText)
        {
            return nextStepText switch
            {
                "請選擇" => FollowUpNextStep.PleaseSelect,
                "繼續跟進" => FollowUpNextStep.ContinueFollowUp,
                "轉介" => FollowUpNextStep.Transfer,
                _ => FollowUpNextStep.PleaseSelect
            };
        }

        /// <summary>
        /// 將 OptionSet 值轉換為跟進下一步驟文字
        /// </summary>
        public static string FollowUpNextStepValueToText(int optionSetValue)
        {
            return FollowUpNextStep.GetDisplayName(optionSetValue);
        }

        #endregion

        #region 委身類型轉換

        /// <summary>
        /// 取得委身類型的簡化顯示名稱
        /// 用於統計報告中
        /// </summary>
        public static string GetSimplifiedCommitmentType(int commitmentTypeCode)
        {
            return commitmentTypeCode switch
            {
                CommitmentType.NewFriend => "新朋友",
                CommitmentType.NotJoinedGroup => "未入組",
                CommitmentType.ExternalChurch => "未入組", // 外教會歸類為未入組
                CommitmentType.SmallGroupMember => "小組組員",
                _ => "小組組員"
            };
        }

        #endregion

        #region 電話號碼處理

        /// <summary>
        /// 移除電話號碼中的非數字字元
        /// </summary>
        public static string NormalizePhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return string.Empty;

            return DigitsOnly.Replace(phoneNumber, "");
        }

        #endregion

        #region 文字處理

        /// <summary>
        /// 判斷字串是否有意義（非空、非預設值）
        /// </summary>
        public static bool HasMeaningfulValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // 排除常見的無意義預設值
            string[] meaninglessValues = { ".", "請選擇", "無", "未知", "-" };
            foreach (var meaningless in meaninglessValues)
            {
                if (value.Equals(meaningless, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 建立帶標題的字串（僅在有意義時）
        /// </summary>
        /// <param name="title">標題</param>
        /// <param name="content">內容</param>
        /// <returns>格式化的字串，若內容無意義則返回空字串</returns>
        public static string BuildLabeledString(string title, string content)
        {
            if (!HasMeaningfulValue(content))
                return string.Empty;

            return $"{title}{content}{Environment.NewLine}";
        }

        #endregion
    }
}
