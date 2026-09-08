// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/ListManager.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class ListManager
// 主要成員：SetupListManager、SetSelectDate、SetupOnlyOneListManager、GetDisplayViewType、SetupIntegrateData、SetupIntegrateDataDemo、GetMarkers、m_SelectDate、SchedulerView、DisplayNavigation
// 引用命名空間：ChurchReport.WebServiceConnector、Microsoft.AspNetCore.Mvc、Microsoft.Xrm.Sdk、Newtonsoft.Json、System、System.Collections.Generic、System.Linq、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.WebServiceConnector;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class ListManager
    {
        /// <summary>
        /// 同一 Session holder 內整合資料的同步根。由於現有 CRM SDK 入口是同步 API，
        /// 使用 instance lock 可在整個候選快照建立期間阻擋同一 holder 的第二個 writer，
        /// 且不引入需要跨 cache eviction 額外 Dispose 的 SemaphoreSlim。此鎖不在 static、
        /// Session 或跨使用者 registry 中，因此不會把不同使用者的 request 串在一起。
        /// </summary>
        private readonly object m_IntegratePublicationGate = new object();

        /// <summary>
        /// 測試用的區域候選建立器；正式建構式保持原有行為並建立新的 DownloadIntegrateData。
        /// 委派只在 gate 內暫時使用，不得保存 HttpContext、Session、CRM 連線或測試同步原語。
        /// </summary>
        private readonly Func<string, ListSmallGroupWeeklyReport> m_IntegrateCandidateFactory;

        /// <summary>
        /// 最近一次成功發布的完整隔離鍵。此值只存在於單一 Session 的 ListManager 生命週期，
        /// 不會進入程序級 cache key、log 或回應；密碼欄位只保留既有字串參考以辨識同一 holder
        /// 是否已換登入者，不建立額外明文副本，也不延長超過 ListManager 的保存時間。
        /// </summary>
        private IntegrateLoadKey? m_PublishedIntegrateLoadKey;

        /// <summary>
        /// 建立具有正式 CRM 載入行為的 ListManager。所有可變資料仍由此 instance 擁有，
        /// 不會透過 static 欄位或跨 request singleton 共用。
        /// </summary>
        public ListManager()
        {
            m_IntegrateCandidateFactory = BuildIntegrateCandidate;
        }

        /// <summary>
        /// 建立可注入候選建立器的 ListManager，僅供隔離測試驗證併發發布契約。
        /// </summary>
        /// <param name="candidateFactory">依小組 ID 建立獨立候選週報的函式。</param>
        internal ListManager(Func<string, ListSmallGroupWeeklyReport> candidateFactory)
        {
            m_IntegrateCandidateFactory = candidateFactory ?? throw new ArgumentNullException(nameof(candidateFactory));
        }

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

        // 新增新人時，選擇進入哪一個小組的清單 + 小家長或一人帶多個小組時，提供選擇點選進入觀看的Grid
        public MultiGroupList m_MultiGroupList = new MultiGroupList();

        // 個別小組長點名的畫面所需要的資料，就是整合型頁面所需的資料
        public ListSmallGroupWeeklyReport m_ListSmallGroupWeeklyReport = new ListSmallGroupWeeklyReport();// { get; set; }

        // 圓餅圖
        public MultiGroupChartDataList m_MultiGroupChartDataList = new MultiGroupChartDataList();

        DownloadListManager m_DownloadListManager = new DownloadListManager();

        public void SetupListManager(String Account, String Password, DateTime aSelectDate, IOrganizationService organizationService = null)
        {
            try
            {
                // 先把登入的帳號密碼存下來
                m_Account = Account;
                m_Password = Password;

                m_SelectDate = aSelectDate;

                m_DownloadListManager.GetListManager(Account, Password, aSelectDate, ref m_MultiGroupList, ref m_MultiGroupChartDataList, ref LoginType, ref UserType, ref LoginFullName, ref ActiveListId, organizationService);
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
            EnsureAndGetIntegrateDetachedRead(ListEntityId);
        }

        /// <summary>
        /// 以同一個 Session holder 的同步根建立並發布完整整合快照，再回傳呼叫端可任意修改的深複製。
        /// 快速路徑與 gate 內都重新檢查小組 ID 與 LoadFlag；候選建立或 row-key 驗證失敗時，
        /// 既有完整快照保持不變，避免半成品、舊小組資料或失敗資料覆蓋目前畫面。
        /// </summary>
        /// <param name="listEntityId">已由目前登入 scope 決定、且必須存在於可見小組清單的 ID。</param>
        /// <returns>不含 Session 可變集合與 CRM Entity 參考的 detached 週報。</returns>
        /// <exception cref="ArgumentException">小組 ID 空白或不存在於目前可見清單。</exception>
        /// <exception cref="InvalidOperationException">候選資料缺少完整 row key，或同一資料集有 exact duplicate row key。</exception>
        internal ListSmallGroupWeeklyReport EnsureAndGetIntegrateDetachedRead(string listEntityId)
        {
            if (string.IsNullOrWhiteSpace(listEntityId))
            {
                throw new ArgumentException("整合資料的小組 ID 不得為空白。", nameof(listEntityId));
            }

            lock (m_IntegratePublicationGate)
            {
                var record = m_MultiGroupList?.m_WeeklyReportRecordListData?
                    .FirstOrDefault(item => string.Equals(item.ListEntityId, listEntityId, StringComparison.Ordinal));
                if (record == null)
                {
                    throw new ArgumentException("要求的小組不在目前登入者的可見清單中。", nameof(listEntityId));
                }

                var requestedKey = new IntegrateLoadKey(
                    m_Account ?? string.Empty,
                    m_Password ?? string.Empty,
                    LoginType ?? string.Empty,
                    m_SelectDate,
                    listEntityId,
                    record.WeeklyReportEntityId ?? string.Empty);

                if (m_ListSmallGroupWeeklyReport == null ||
                    !m_ListSmallGroupWeeklyReport.LoadFlag ||
                    m_PublishedIntegrateLoadKey != requestedKey)
                {
                    var candidate = m_IntegrateCandidateFactory(listEntityId)
                        ?? throw new InvalidOperationException("整合資料 loader 不得回傳 null 候選。");
                    ValidateIntegrateCandidate(candidate, listEntityId);
                    m_ListSmallGroupWeeklyReport = candidate;
                    m_PublishedIntegrateLoadKey = requestedKey;
                    ActiveListId = listEntityId;
                }

                return m_ListSmallGroupWeeklyReport.CreateDetachedReadCopy();
            }
        }

        /// <summary>
        /// 以新的 loader 與新的候選週報執行同步 CRM 載入；候選在呼叫端驗證完成前不會放入共享欄位。
        /// 新 loader 的 mutable fields 僅存活於本次 gate 內，避免兩個小組或兩個 request 互相覆寫。
        /// </summary>
        private ListSmallGroupWeeklyReport BuildIntegrateCandidate(string listEntityId)
        {
            var record = m_MultiGroupList?.m_WeeklyReportRecordListData?
                .FirstOrDefault(item => string.Equals(item.ListEntityId, listEntityId, StringComparison.Ordinal));
            if (record == null)
            {
                throw new ArgumentException("要求的小組不在目前登入者的可見清單中。", nameof(listEntityId));
            }

            var candidate = new ListSmallGroupWeeklyReport
            {
                ListEntityId = listEntityId,
                LoginType = LoginType
            };
            var loader = new DownloadIntegrateData();
            loader.SetupIntegrateData(m_Account, m_Password, LoginType, m_SelectDate,
                listEntityId, record.WeeklyReportEntityId, ref candidate);
            return candidate;
        }

        /// <summary>
        /// 驗證候選已完成載入，並在每個前端資料集內拒絕重複的穩定 row key。
        /// FullName、電話與 ContactId 都不是唯一性依據，因為同名會友可能是不同 CRM 記錄。
        /// </summary>
        private static void ValidateIntegrateCandidate(ListSmallGroupWeeklyReport candidate, string expectedListEntityId)
        {
            if (!candidate.LoadFlag || !string.Equals(candidate.ListEntityId, expectedListEntityId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("整合資料尚未完成，或候選小組 scope 不一致。");
            }

            var dataList = candidate.m_SmallGroupDataList ?? throw new InvalidOperationException("整合資料缺少資料集合。");
            ValidateUniqueRowKeys(dataList.m_SmallGroupData?.Members, "小組成員");
            ValidateUniqueRowKeys(dataList.m_NewPersonFollowUpData?.Members, "新人跟進");
            ValidateUniqueRowKeys(dataList.m_AllMemeberData?.Members, "全部成員");
        }

        /// <summary>
        /// 只對非空 PresentRecordId 做 exact duplicate 檢查；空 key 直接拒絕發布，避免前端產生不穩定 row。
        /// </summary>
        private static void ValidateUniqueRowKeys(IEnumerable<Member> members, string dataSetName)
        {
            if (members == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in members)
            {
                if (member == null || string.IsNullOrWhiteSpace(member.PresentRecordId))
                {
                    throw new InvalidOperationException($"{dataSetName} 存在空白 PresentRecordId，拒絕發布不穩定資料。");
                }

                if (!seen.Add(member.PresentRecordId.Trim()))
                {
                    throw new InvalidOperationException(
                        $"{dataSetName} 發現重複 PresentRecordId '{member.PresentRecordId.Trim()}'，拒絕發布候選資料。");
                }
            }
        }

        /// <summary>
        /// 定義一份整合快照的完整 Session 內隔離邊界。任何登入者、登入憑證、角色、日期、
        /// 小組或週報變化都會強制建立新候選，禁止只憑 LoadFlag 或 ListEntityId 沿用舊資料。
        /// </summary>
        private readonly record struct IntegrateLoadKey(
            string Account,
            string Credential,
            string LoginType,
            DateTime SelectDate,
            string ListEntityId,
            string WeeklyReportEntityId);
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
