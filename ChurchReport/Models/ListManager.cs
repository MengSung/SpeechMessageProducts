using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class ListManager
    {
        public DateTime SundayPrayers { get; set; } // 小組日期
        public String LoginType { get; set; }
        public String LoginFullName { get; set; }
        public String ActiveListId { get; set; }

        // 新增新人時，選擇進入哪一個小組的清單
        public AssignSmallGroupList m_AssignSmallGroupList = new AssignSmallGroupList();

        public List<ListSmallGroupWeeklyReport> m_ListSmallGroupWeeklyReport { get; set; }


        public void SetupListManager(String FullName, String Account, String Password, DateTime aSelectDate, bool DisplayDateFlag)
        {
            LoginType = "小組長";
            LoginFullName = "跟隨者";

            m_ListSmallGroupWeeklyReport = new List<ListSmallGroupWeeklyReport>
            {
                new ListSmallGroupWeeklyReport
                {
                    ListEntityId = "001",
                    ListEntityName = "夢嵩連碧小組",
                    LoginType = "小組長",
                    SmallGroupLeaderFullName = "以利亞",
                    SundayPrayers = DateTime.Now,
                    SundayPeriod = "不斷地來愛主耶穌", 
                    m_SmallGroupDataList = new SmallGroupDataList
                    {
                        m_SmallGroupData = new SmallGroupData
                        {
                            Members = new List<Member>
                            {
                                new Member
                                {
                                     FullName = "胡夢嵩",
                                     Phone = "0910391931",
                                     Address = "桃園市楊梅區三民路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                                new Member
                                {
                                     FullName = "吳連碧",
                                     Phone = "0921834289",
                                     Address = "台北市大安區敦化北路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                            },
                            WeeklyReportData = "0000",
                            WeeklyReportAnalysis = "1111",
                        },
                        m_NewPersonFollowUpData = new SmallGroupData
                        {
                            Members = new List<Member>
                            {
                                new Member
                                {
                                     FullName = "張大通",
                                     Phone = "0965526987",
                                     Address = "桃園市八德區中正路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                                new Member
                                {
                                     FullName = "李曉春",
                                     Phone = "0956874563",
                                     Address = "台北市中正區中華路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                            }
                        }
                    },
                    m_WeeklyReportChart = new ChartDataList
                    {
                        m_ChartDataList = new List<ChartData>
                        {
                                new ChartData {
                                    WeeklyReportEntityId = "001",
                                    SundayDate = DateTime.Now.AddDays(-7).ToShortDateString(),
                                    SundayNumber = 8,
                                    SmallNumber =6
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "002",
                                    SundayDate = DateTime.Now.AddDays(-14).ToShortDateString(),
                                    SundayNumber = 9,
                                    SmallNumber =5
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "003",
                                    SundayDate = DateTime.Now.AddDays(-21).ToShortDateString(),
                                    SundayNumber = 7,
                                    SmallNumber =9
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "004",
                                    SundayDate = DateTime.Now.AddDays(-28).ToShortDateString(),
                                    SundayNumber = 10,
                                    SmallNumber =7
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "005",
                                    SundayDate = DateTime.Now.AddDays(-35).ToShortDateString(),
                                    SundayNumber = 11,
                                    SmallNumber =12
                                },

                        }
                    }
                },
                new ListSmallGroupWeeklyReport
                {
                    ListEntityId = "002",
                    ListEntityName = "逸凡小組",
                    LoginType = "小組長",
                    SmallGroupLeaderFullName = "胡逸凡",
                    SundayPrayers = DateTime.Now,
                    SundayPeriod = "主耶穌永遠與我們同在",
                    m_SmallGroupDataList = new SmallGroupDataList
                    {
                        m_SmallGroupData = new SmallGroupData
                        {
                            Members = new List<Member>
                            {
                                new Member
                                {
                                     FullName = "約書亞",
                                     Status= "小組長",
                                     Phone = "0910391931",
                                     Address = "桃園市楊梅區三民路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                                new Member
                                {
                                     FullName = "跟隨者",
                                     Status= "小組長",
                                     Phone = "0921834289",
                                     Address = "台北市大安區敦化北路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                            },
                            WeeklyReportData = "2222",
                            WeeklyReportAnalysis = "3333",
                        },
                        m_NewPersonFollowUpData = new SmallGroupData
                        {
                            Members = new List<Member>
                            {
                                new Member
                                {
                                     FullName = "火熱者",
                                     Phone = "0965526987",
                                     Address = "桃園市八德區中正路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                                new Member
                                {
                                     FullName = "以利亞",
                                     Phone = "0956874563",
                                     Address = "台北市中正區中華路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                            }
                        },
                    },
                    m_WeeklyReportChart = new ChartDataList
                    {
                        m_ChartDataList = new List<ChartData>
                        {
                                new ChartData {
                                    WeeklyReportEntityId = "001",
                                    SundayDate = DateTime.Now.AddDays(-7).ToShortDateString(),
                                    SundayNumber = 14,
                                    SmallNumber =16
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "002",
                                    SundayDate = DateTime.Now.AddDays(-14).ToShortDateString(),
                                    SundayNumber = 19,
                                    SmallNumber =15
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "003",
                                    SundayDate = DateTime.Now.AddDays(-21).ToShortDateString(),
                                    SundayNumber = 17,
                                    SmallNumber =19
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "004",
                                    SundayDate = DateTime.Now.AddDays(-28).ToShortDateString(),
                                    SundayNumber = 10,
                                    SmallNumber =17
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "005",
                                    SundayDate = DateTime.Now.AddDays(-35).ToShortDateString(),
                                    SundayNumber = 11,
                                    SmallNumber =12
                                },

                        }
                    }
                }
            };

        }
        public void SetupListManager()
        {
            LoginType = "小組長";
            LoginFullName = "跟隨者";
            SundayPrayers = DateTime.Now;

            m_ListSmallGroupWeeklyReport = new List<ListSmallGroupWeeklyReport>
            {
                new ListSmallGroupWeeklyReport
                {
                    ListEntityId = "001",
                    ListEntityName = "夢嵩連碧小組",
                    LoginType = "小組長",
                    SmallGroupLeaderFullName = "以利亞",
                    SundayPrayers = DateTime.Now,
                    SundayPeriod = "不斷地來愛主耶穌",
                    m_SmallGroupDataList = new SmallGroupDataList
                    {
                        m_SmallGroupData = new SmallGroupData
                        {
                            Members = new List<Member>
                            {
                                new Member
                                {
                                     PresentRecordId = "AAA",
                                     //Id = 1,
                                     FullName = "胡夢嵩",
                                     Phone = "0910391931",
                                     Address = "桃園市楊梅區三民路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                                new Member
                                {
                                     PresentRecordId = "BBB",
                                     FullName = "吳連碧",
                                     Phone = "0921834289",
                                     Address = "台北市大安區敦化北路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                            }
                        },
                        m_NewPersonFollowUpData = new SmallGroupData
                        {
                            Members = new List<Member>
                            {
                                new Member
                                {
                                     PresentRecordId = "CCC",
                                     FullName = "張大通",
                                     Phone = "0965526987",
                                     Address = "桃園市八德區中正路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                                new Member
                                {
                                     PresentRecordId = "DDD",
                                     FullName = "李曉春",
                                     Phone = "0956874563",
                                     Address = "台北市中正區中華路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                            }
                        }
                    },

                    WeeklyReportData = "AAA",
                    WeeklyReportAnalysis = "BBB",
                    m_WeeklyReportChart = new ChartDataList
                    {
                        m_ChartDataList = new List<ChartData>
                        {
                                new ChartData {
                                    WeeklyReportEntityId = "001",
                                    SundayDate = DateTime.Now.AddDays(-7).ToShortDateString(),
                                    SundayNumber = 8,
                                    SmallNumber =6
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "002",
                                    SundayDate = DateTime.Now.AddDays(-14).ToShortDateString(),
                                    SundayNumber = 9,
                                    SmallNumber =5
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "003",
                                    SundayDate = DateTime.Now.AddDays(-21).ToShortDateString(),
                                    SundayNumber = 7,
                                    SmallNumber =9
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "004",
                                    SundayDate = DateTime.Now.AddDays(-28).ToShortDateString(),
                                    SundayNumber = 10,
                                    SmallNumber =7
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "005",
                                    SundayDate = DateTime.Now.AddDays(-35).ToShortDateString(),
                                    SundayNumber = 11,
                                    SmallNumber =12
                                },

                        }
                    }
                },
                new ListSmallGroupWeeklyReport
                {
                    ListEntityId = "002",
                    ListEntityName = "逸凡小組",
                    LoginType = "小組長",
                    SmallGroupLeaderFullName = "胡逸凡",
                    SundayPrayers = DateTime.Now,
                    SundayPeriod = "主耶穌永遠與我們同在",
                    m_SmallGroupDataList = new SmallGroupDataList
                    {
                        m_SmallGroupData = new SmallGroupData
                        {
                            Members = new List<Member>
                            {
                                new Member
                                {
                                     PresentRecordId = "EEE",
                                     FullName = "約書亞",
                                     Status= "小組長",
                                     Phone = "0910391931",
                                     Address = "桃園市楊梅區三民路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                                new Member
                                {
                                     PresentRecordId = "FFF",
                                     FullName = "跟隨者",
                                     Status= "小組長",
                                     Phone = "0921834289",
                                     Address = "台北市大安區敦化北路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                            }
                        },
                        m_NewPersonFollowUpData = new SmallGroupData
                        {
                            Members = new List<Member>
                            {
                                new Member
                                {
                                     PresentRecordId = "HHH",
                                     FullName = "火熱者",
                                     Phone = "0965526987",
                                     Address = "桃園市八德區中正路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                                new Member
                                {
                                     PresentRecordId = "III",
                                     FullName = "以利亞",
                                     Phone = "0956874563",
                                     Address = "台北市中正區中華路",
                                     Sunday = true,
                                     SmallGroup = true,
                                },
                            }
                        }
                    },
                    WeeklyReportData = "CCC",
                    WeeklyReportAnalysis = "DDD",

                    m_WeeklyReportChart = new ChartDataList
                    {
                        m_ChartDataList = new List<ChartData>
                        {
                                new ChartData {
                                    WeeklyReportEntityId = "001",
                                    SundayDate = DateTime.Now.AddDays(-7).ToShortDateString(),
                                    SundayNumber = 14,
                                    SmallNumber =16
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "002",
                                    SundayDate = DateTime.Now.AddDays(-14).ToShortDateString(),
                                    SundayNumber = 19,
                                    SmallNumber =15
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "003",
                                    SundayDate = DateTime.Now.AddDays(-21).ToShortDateString(),
                                    SundayNumber = 17,
                                    SmallNumber =19
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "004",
                                    SundayDate = DateTime.Now.AddDays(-28).ToShortDateString(),
                                    SundayNumber = 10,
                                    SmallNumber =17
                                },
                                new ChartData {
                                    WeeklyReportEntityId = "005",
                                    SundayDate = DateTime.Now.AddDays(-35).ToShortDateString(),
                                    SundayNumber = 11,
                                    SmallNumber =12
                                },

                        }
                    }
                }
            };

        }
    }
}
