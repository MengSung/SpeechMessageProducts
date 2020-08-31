using ChurchReport.WebServiceConnector;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class ListManager
    {
        public DateTime m_SelectDate { get; set; } // 小組日期
        public String LoginType; //{ get; set; }
        public String LoginFullName; //{ get; set; }
        public String ActiveListId; //{ get; set; }

        public String SchedulerView { get; set; } = "";
        public String DisplayNavigation { get; set; } = "";
        public String UserType = "";
        public String DedicationType = "";
        public String DedicationFlag = "";

        public String QrCodeId { get; set; } = "";
        //public String ListName { get; set; } // 小組名稱

        public String m_Account;
        public String m_Password;

        public bool InitialFlag = false;

        // 地圖需要的資料
        public List<MapData> m_Markers;

        // 新增新人時，選擇進入哪一個小組的清單 + 區長或一人帶多個小組時，提供選擇點選進入觀看的Grid
        public MultiGroupList m_MultiGroupList = new MultiGroupList();

        // 個別小組長點名的畫面所需要的資料，就是整合型頁面所需的資料
        public ListSmallGroupWeeklyReport m_ListSmallGroupWeeklyReport = new ListSmallGroupWeeklyReport();// { get; set; }

        // 圓餅圖
        public MultiGroupChartDataList m_MultiGroupChartDataList = new MultiGroupChartDataList();

        DownloadListManager m_DownloadListManager = new DownloadListManager();

        DownloadIntegrateData m_DownloadIntegrateData = new DownloadIntegrateData();

        public void SetupListManager(String Account, String Password, DateTime aSelectDate )
        {
            try
            {
                // 先把登入的帳號密碼存下來
                m_Account = Account;
                m_Password = Password;

                m_SelectDate = aSelectDate;

                m_DownloadListManager.GetListManager(Account, Password, aSelectDate, ref m_MultiGroupList, ref m_MultiGroupChartDataList, ref LoginType, ref UserType, ref LoginFullName, ref ActiveListId );
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetSelectDate( DateTime aSelectDate)
        {
            m_SelectDate = aSelectDate;
        }

        public void SetupListManager()
        {
            LoginType = "小組長";
            LoginFullName = "跟隨者";
            m_SelectDate = DateTime.Now;
            ActiveListId = "001";

            // 圖表需要的資料
            m_MultiGroupChartDataList = new MultiGroupChartDataList
            {
                m_MultiGroupChartDataList = new List<MultiGroupChartData>
                {
                    new MultiGroupChartData
                    {
                        ID = "001",
                        Name= "總人數",
                        Number = 45
                    },
                    new MultiGroupChartData
                    {
                        ID = "002",
                        Name= "主日人數",
                        Number = 30
                    },
                    new MultiGroupChartData
                    {
                        ID = "003",
                        Name= "小組人數",
                        Number = 25
                    }
                }
            };

            // 表格需要的資料
            m_MultiGroupList = new MultiGroupList
            {
                m_WeeklyReportRecordListData = new List<WeeklyReportRecord>
                {
                    new WeeklyReportRecord {
                        ListEntityId = "001",
                        Name = "夢嵩連碧小組",
                        TotalNumber ="8",
                        SundayNumber = "5",
                        SmallGroupNumber = "4",
                        SundayRate = "0.52",
                        SmallGroupRate = "0.98",
                        ReportContent = "很火熱"
                    },
                    new WeeklyReportRecord {
                        ListEntityId = "002",
                        Name = "永初雅慧小組",
                        TotalNumber ="12",
                        SundayNumber = "9",
                        SmallGroupNumber = "7",
                        SundayRate = "0.85",
                        SmallGroupRate = "0.74",
                        ReportContent = "很火熱"
                    },
                    new WeeklyReportRecord {
                        ListEntityId = "001",
                        Name = "萬全敏惠小組",
                        TotalNumber ="10",
                        SundayNumber = "8",
                        SmallGroupNumber = "9",
                        SundayRate = "0.63",
                        SmallGroupRate = "0.84",
                        ReportContent = "很火熱"
                    },
                    new WeeklyReportRecord {
                        ListEntityId = "002",
                        Name = "寶珠小組",
                        TotalNumber ="9",
                        SundayNumber = "5",
                        SmallGroupNumber = "7",
                        SundayRate = "0.95",
                        SmallGroupRate = "0.83",
                        ReportContent = "很火熱"
                    },
                    new WeeklyReportRecord {
                        ListEntityId = "001",
                        Name = "秋萍小組",
                        TotalNumber ="16",
                        SundayNumber = "12",
                        SmallGroupNumber = "13",
                        SundayRate = "0.87",
                        SmallGroupRate = "0.96",
                        ReportContent = "很火熱"
                    },
                }
            };

        }
        public void SetupOnlyOneListManager()
        {
            LoginType = "小組長";
            LoginFullName = "跟隨者";
            m_SelectDate = DateTime.Now;
            ActiveListId = "001";
            m_MultiGroupList = new MultiGroupList
            {
                m_WeeklyReportRecordListData = new List<WeeklyReportRecord>
                {
                    new WeeklyReportRecord {
                        ListEntityId = "001",
                        Name = "夢嵩連碧小組",
                        TotalNumber ="8",
                        SundayNumber = "5",
                        SmallGroupNumber = "4",
                        SundayRate = "0.52",
                        SmallGroupRate = "0.98",
                        ReportContent = "很火熱"
                    },
                }
            };
        }
        public String GetDisplayViewType()
        {
            //return m_MultiGroupChartDataList.m_MultiGroupChartDataList.Count > 1 ? "MultiGroupView" : "IntegrateView";
            if (m_MultiGroupList != null)
            {
                if (m_MultiGroupList.m_WeeklyReportRecordListData != null)
                {
                    return m_MultiGroupList.m_WeeklyReportRecordListData.Count > 1 ? "MultiGroupView" : "IntegrateView";
                }
                else
                {
                    return "IntegrateView";
                }
            }
            else
            {
                return "IntegrateView";
            }
        }
        public void SetupIntegrateData( String ListEntityId )
        {
            //WeeklyReportRecord aWeeklyReportRecord = m_MultiGroupList.m_WeeklyReportRecordListData.Where(e => e.ListEntityId == ListEntityId).FirstOrDefault();
            WeeklyReportRecord aWeeklyReportRecord = m_MultiGroupList.m_WeeklyReportRecordListData.FirstOrDefault(e => e.ListEntityId == ListEntityId);

            if ( aWeeklyReportRecord != null )
            {
                if (m_ListSmallGroupWeeklyReport == null)
                {
                    m_ListSmallGroupWeeklyReport = new ListSmallGroupWeeklyReport();

                    m_ListSmallGroupWeeklyReport.LoadFlag = true;
                }
                else
                { }

                m_ListSmallGroupWeeklyReport.ListEntityId = ListEntityId;
                m_ListSmallGroupWeeklyReport.LoginType = m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.LoginType = LoginType;
                m_DownloadIntegrateData.SetupIntegrateData( m_Account, m_Password, LoginType, this.m_SelectDate, ListEntityId, aWeeklyReportRecord.WeeklyReportEntityId, ref m_ListSmallGroupWeeklyReport);
            }
        }
        public void SetupIntegrateDataDemo(String ListEntityId)
        {
            switch (ListEntityId)
            {
                case "001":
                    {
                        m_ListSmallGroupWeeklyReport = new ListSmallGroupWeeklyReport
                        {
                            LoadFlag = true,
                            ListEntityId = "001",
                            ListEntityName = "夢嵩連碧小組",
                            LoginType = "小組長",
                            SmallGroupLeaderFullName = "以利亞",
                            SundayPrayers = DateTime.Now,
                            SundayPeriod = "不斷地來愛主耶穌",
                            m_SmallGroupDataList = new SmallGroupDataList()
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
                        };
                    }
                    return;
                case "002":
                    {
                        m_ListSmallGroupWeeklyReport = new ListSmallGroupWeeklyReport
                        {
                            LoadFlag = true,
                            ListEntityId = "002",
                            ListEntityName = "逸凡小組",
                            LoginType = "小組長",
                            SmallGroupLeaderFullName = "胡逸凡",
                            SundayPrayers = DateTime.Now,
                            SundayPeriod = "主耶穌永遠與我們同在",
                            m_SmallGroupDataList = new SmallGroupDataList()
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
                        };
                    }
                    return;
                default:
                    {
                        m_ListSmallGroupWeeklyReport = new ListSmallGroupWeeklyReport
                        {
                            LoadFlag = true,
                            ListEntityId = "001",
                            ListEntityName = "夢嵩連碧小組",
                            LoginType = "小組長",
                            SmallGroupLeaderFullName = "以利亞",
                            SundayPrayers = DateTime.Now,
                            SundayPeriod = "不斷地來愛主耶穌",
                            m_SmallGroupDataList = new SmallGroupDataList()
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
                        };
                    }
                    return;
            }

        }

        public List<MapData> GetMarkers()
        {
            if( m_Markers == null )
            {
                m_Markers = new List<MapData>();
            }
            else
            {
                m_Markers.Clear();
            }
            foreach( Member aMember in this.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members )
            {
                if (aMember.Address != null && aMember.Address != "" && aMember.Address != "null")
                {
                    //new MapData { location = aMember.Address, tooltip = new tooltip { text = aMember.FullName + ":" + aMember.Address, isShown = true } };
                    m_Markers.Add
                    (
                        new MapData { location = aMember.Address, tooltip = new tooltip { text = aMember.FullName + "，" + aMember.Address, isShown = true } }
                    );
                }
            }

            return m_Markers;

        }

    }
}
