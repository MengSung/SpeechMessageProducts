namespace ChurchReport.Domain.Constants
{
    /// <summary>
    /// 委身類型常數
    /// 定義 Dynamics 365 中的 customertypecode 選項集值
    /// </summary>
    public static class CommitmentType
    {
        /// <summary>
        /// 小組組員
        /// </summary>
        public const int SmallGroupMember = 1;

        /// <summary>
        /// 新朋友
        /// </summary>
        public const int NewFriend = 100000000;

        /// <summary>
        /// 未入組
        /// </summary>
        public const int NotJoinedGroup = 100000004;

        /// <summary>
        /// 幸福 Best
        /// </summary>
        public const int HappyBest = 100000005;

        /// <summary>
        /// 外教會
        /// </summary>
        public const int ExternalChurch = 100000007;

        /// <summary>
        /// 未入組結案
        /// </summary>
        public const int NotJoinedGroupClosed = 100000008;

        /// <summary>
        /// 取得委身類型的顯示名稱
        /// </summary>
        public static string GetDisplayName(int commitmentTypeCode)
        {
            return commitmentTypeCode switch
            {
                SmallGroupMember => "小組組員",
                NewFriend => "新朋友",
                NotJoinedGroup => "未入組",
                HappyBest => "幸福 Best",
                ExternalChurch => "外教會",
                NotJoinedGroupClosed => "未入組結案",
                _ => "未知"
            };
        }

        /// <summary>
        /// 判斷是否為需要關懷的對象（新朋友或未入組）
        /// </summary>
        public static bool RequiresCare(int commitmentTypeCode)
        {
            return commitmentTypeCode == NewFriend || commitmentTypeCode == NotJoinedGroup;
        }

        /// <summary>
        /// 判斷是否應列入出席率統計
        /// </summary>
        public static bool ShouldCountInAttendance(int commitmentTypeCode)
        {
            return commitmentTypeCode != NewFriend 
                && commitmentTypeCode != NotJoinedGroup 
                && commitmentTypeCode != ExternalChurch;
        }
    }

    /// <summary>
    /// 性別常數
    /// </summary>
    public static class Gender
    {
        public const int Male = 200000;
        public const int Female = 200001;

        public static string GetDisplayName(int genderCode)
        {
            return genderCode switch
            {
                Male => "男性",
                Female => "女性",
                _ => "未知"
            };
        }
    }

    /// <summary>
    /// 跟進週次常數
    /// </summary>
    public static class FollowUpWeek
    {
        public const int Week1 = 100000000;
        public const int Week2 = 100000001;
        public const int Week3 = 100000002;
        public const int Week4 = 100000003;
        public const int Week5 = 100000004;
        public const int Week6 = 100000005;
        public const int Week7 = 100000006;
        public const int Week8 = 100000007;
        public const int Week9 = 100000008;
        public const int Week10 = 100000009;
        public const int Week11 = 100000010;
        public const int Week12 = 100000011;
        public const int Week13 = 100000012;
        public const int Week14 = 100000013;
        public const int Week15 = 100000014;
        public const int Week16 = 100000015;
        public const int Week17 = 100000016;
        public const int Week18 = 100000017;
        public const int Week19 = 100000018;
        public const int Week20 = 100000019;

        /// <summary>
        /// 將數字轉換為週次選項集值
        /// </summary>
        public static int NumberToWeekCode(int weekNumber)
        {
            if (weekNumber < 1 || weekNumber > 20) return Week8; // 預設第8週
            return Week1 + (weekNumber - 1);
        }

        /// <summary>
        /// 將週次選項集值轉換為中文
        /// </summary>
        public static string WeekCodeToChineseNumber(int weekCode)
        {
            int weekNumber = weekCode - Week1 + 1;
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
                _ => "."
            };
        }

        /// <summary>
        /// 將數字轉換為中文週次
        /// </summary>
        public static string NumberToChineseWeek(int weekNumber)
        {
            if (weekNumber < 1 || weekNumber > 20) return "二十";
            return WeekCodeToChineseNumber(NumberToWeekCode(weekNumber));
        }
    }

    /// <summary>
    /// 跟進結果常數
    /// </summary>
    public static class FollowUpResult
    {
        public const int PleaseSelect = 100000000;
        public const int EnthusiasticResponse = 100000001;
        public const int EagerToKnowFaith = 100000002;
        public const int NoContact = 100000003;
        public const int ColdResponse = 100000004;
        public const int Considering = 100000005;
        public const int JoinedSmallGroup = 100000006;
        public const int AttendedSunday = 100000007;
        public const int Transferred = 100000008;
        public const int Other = 100000009;

        public static string GetDisplayName(int resultCode)
        {
            return resultCode switch
            {
                PleaseSelect => "請選擇",
                EnthusiasticResponse => "熱情回應",
                EagerToKnowFaith => "渴慕認識信仰",
                NoContact => "沒聯絡上",
                ColdResponse => "反應冷淡",
                Considering => "考慮中，繼續跟進",
                JoinedSmallGroup => "入小組",
                AttendedSunday => "來主日",
                Transferred => "轉介",
                Other => "其他",
                _ => ""
            };
        }
    }

    /// <summary>
    /// 跟進下一步驟常數
    /// </summary>
    public static class FollowUpNextStep
    {
        public const int PleaseSelect = 100000000;
        public const int ContinueFollowUp = 100000001;
        public const int Transfer = 100000002;

        public static string GetDisplayName(int nextStepCode)
        {
            return nextStepCode switch
            {
                PleaseSelect => "請選擇",
                ContinueFollowUp => "繼續跟進",
                Transfer => "轉介",
                _ => "請選擇"
            };
        }
    }

    /// <summary>
    /// 新人關懷相關常數
    /// </summary>
    public static class CareConstants
    {
        /// <summary>
        /// 過去幾週內的出席統計週期
        /// </summary>
        public const int WeekPeriod = 8;

        /// <summary>
        /// 出席次數門檻（達到此次數可晉升為小組組員）
        /// </summary>
        public const int MinimumAttendanceThreshold = 4;

        /// <summary>
        /// 新朋友轉為未入組的週次上限
        /// </summary>
        public const int NewFriendToNotJoinedWeekLimit = 10;

        /// <summary>
        /// 未入組結案的週次上限
        /// </summary>
        public const int NotJoinedClosureWeekLimit = 18;

        /// <summary>
        /// 是否啟用委身類型自動轉換
        /// </summary>
        public const bool EnableAutoIdentityTransfer = false;
    }
}
