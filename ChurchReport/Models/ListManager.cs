using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class ListManager
    {
        public String ActiveListId { get; set; }
        public List<ListSmallGroupWeeklyReport> m_ListSmallGroupWeeklyReport { get; set; }

        public void SetupListManager(String FullName, String Account, String Password, DateTime aSelectDate, bool DisplayDateFlag)
        {
            m_ListSmallGroupWeeklyReport = new List<ListSmallGroupWeeklyReport>
            {
                new ListSmallGroupWeeklyReport
                {
                    ListEntityId = "001",
                    ListEntityName = "夢嵩連碧小組",
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
                            }
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
                    }
                },
                new ListSmallGroupWeeklyReport
                {
                    ListEntityId = "002",
                    ListEntityName = "逸凡小組",
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
                            }
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
                        }
                    }
                }
            };

        }
        public void SetupListManager()
        {
            m_ListSmallGroupWeeklyReport = new List<ListSmallGroupWeeklyReport>
            {
                new ListSmallGroupWeeklyReport
                {
                    ListEntityId = "001",
                    ListEntityName = "夢嵩連碧小組",
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
                    m_WeeklyReportData = new WeeklyReportData
                    {
                        m_WeeklyReportViewModel = new ViewModels.WeeklyReportViewModel
                        {
                            WeeklyReportData = "AAA",
                            WeeklyReportAnalysis = "BBB"
                        }
                    }
                },
                new ListSmallGroupWeeklyReport
                {
                    ListEntityId = "002",
                    ListEntityName = "逸凡小組",
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
                    m_WeeklyReportData = new WeeklyReportData
                    {
                        m_WeeklyReportViewModel = new ViewModels.WeeklyReportViewModel
                        {
                            WeeklyReportData = "CCC",
                            WeeklyReportAnalysis = "DDD"
                        }
                    }
                }
            };

        }
    }
}
