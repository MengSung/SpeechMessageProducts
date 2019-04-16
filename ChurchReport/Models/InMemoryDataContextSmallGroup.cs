using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using ToolUtilityNameSpace;

namespace ChurchReport.Models
{
    public class InMemoryDataContextSmallGroup
    {
        #region 資料區
        IHttpContextAccessor _contextAccessor;
        IMemoryCache _memoryCache;

        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        public ListManager m_ListManager = new ListManager();
        public SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();
        public WeeklyReportData m_WeeklyReportData = new WeeklyReportData();
        public NewPersonModel m_NewPersonModel = new NewPersonModel();
        public HappyGroupDataManager m_HappyGroupDataManager = new HappyGroupDataManager();
        public FeeList m_FeeList = new FeeList();
        public LineBindingViewModel m_LineBindingViewModel = new LineBindingViewModel();

        #endregion
        #region 初始化

        public InMemoryDataContextSmallGroup(IHttpContextAccessor contextAccessor, IMemoryCache memoryCache)
        {
            _contextAccessor = contextAccessor;
            _memoryCache = memoryCache;
        }
        #endregion
        #region 多個組長處理區

        public void SetupListManager(String ActiveListId, String FullName, String Account, String Password, DateTime aSelectDate, bool DisplayDateFlag)
        {
            try
            {
                m_ListManager.ActiveListId = ActiveListId;

                m_ListManager.SetupListManager();
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public void SetupListManager( String Account, String Password, DateTime aSelectDate, bool DisplayDateFlag)
        {
            try
            {
                // 設定多個組長處理資料
                //m_ListManager.SetupListManager( Account,  Password,  aSelectDate);

                m_ListManager.SetupListManager();

                //m_ListManager.SetupOnlyOneListManager();
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }

        public ListManager ListManager
        {
            get
            {
                var session = _contextAccessor.HttpContext.Session;
                var key = session.Id + "_ListManager";

                if (_memoryCache.Get(key) == null)
                {
                    _memoryCache.Set<ListManager>(key, m_ListManager, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpiration = DateTime.Now.AddMinutes(3),
                        SlidingExpiration = TimeSpan.FromMinutes(3)
                    });
                    session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<ListManager>(key);
            }
        }
        #endregion
        #region 小組長處理區

        public void SetupSmallGroupData(String FullName, String Account, String Password, DateTime aSelectDate, bool DisplayDateFlag)
        {
            try
            {
                String ContactIdString = m_ToolUtilityClass.RetrieveContactByAccountNumber(Account, Password);

                m_SmallGroupDataList.SetupContactIdString(ContactIdString);

                m_SmallGroupDataList.SetupSmallGroupData(FullName, Account, Password, DateTime.Now, true);

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }

        public void SetCacheData()
        {
            var session = _contextAccessor.HttpContext.Session;
            var key = session.Id + "_SmallGroupDataList";

            if (_memoryCache.Get(key) == null)
            {
                _memoryCache.Set<SmallGroupDataList>(key, m_SmallGroupDataList, new MemoryCacheEntryOptions
                {
                    AbsoluteExpiration = DateTime.Now.AddMinutes(3),
                    SlidingExpiration = TimeSpan.FromMinutes(3)
                });
                session.SetInt32("dirty", 1);
            }
        }

        public SmallGroupDataList GetCacheData()
        {
            var session = _contextAccessor.HttpContext.Session;
            var key = session.Id + "_SmallGroupDataList";

            SmallGroupDataList aSmallGroupDataList = new SmallGroupDataList();
            if (!_memoryCache.TryGetValue(key, out aSmallGroupDataList))
            {
                //Time = "Cache is expired or not available";
            }

            return aSmallGroupDataList;
        }

        public SmallGroupDataList SmallGroupDataList
        {
            get
            {
                var session = _contextAccessor.HttpContext.Session;
                var key = session.Id + "_SmallGroupDataList";

                if (_memoryCache.Get(key) == null)
                {
                    _memoryCache.Set<SmallGroupDataList>(key, m_SmallGroupDataList, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpiration = DateTime.Now.AddMinutes(3),
                        SlidingExpiration = TimeSpan.FromMinutes(3)
                    });
                    session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<SmallGroupDataList>(key);
            }
        }
        #endregion
        #region 週報處理區

        public void SetupWeeklyReport(String Account, String Password, DateTime SundayDate)
        {
            m_WeeklyReportData.SetupWeeklyReport(Account, Password, SundayDate);

        }

        public WeeklyReportData WeeklyReportData
        {
            get
            {
                var session = _contextAccessor.HttpContext.Session;
                var key = session.Id + "_WeeklyReportData";

                if (_memoryCache.Get(key) == null)
                {
                    _memoryCache.Set<WeeklyReportData>(key, m_WeeklyReportData, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpiration = DateTime.Now.AddMinutes(3),
                        SlidingExpiration = TimeSpan.FromMinutes(3)
                    });
                    session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<WeeklyReportData>(key);
            }
        }

        #endregion
        #region 新增新人處理區

        public NewPersonModel NewPersonModel
        {
            get
            {
                var session = _contextAccessor.HttpContext.Session;
                var key = session.Id + "_NewPersonModel";

                if (_memoryCache.Get(key) == null)
                {
                    _memoryCache.Set<NewPersonModel>(key, m_NewPersonModel, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpiration = DateTime.Now.AddMinutes(3),
                        SlidingExpiration = TimeSpan.FromMinutes(3)
                    });
                    session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<NewPersonModel>(key);
            }
        }

        #endregion
        #region 幸福小組處理區

        public void SetupHappyGroupData( String Account, String Password)
        {
            try
            {
                m_HappyGroupDataManager.SetupHappyGroupData(Account, Password);

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        public HappyGroupDataManager HappyGroupDataManager
        {
            get
            {
                var session = _contextAccessor.HttpContext.Session;
                var key = session.Id + "_HappyGroupDataManager";

                if (_memoryCache.Get(key) == null)
                {
                    _memoryCache.Set<HappyGroupDataManager>(key, m_HappyGroupDataManager, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpiration = DateTime.Now.AddMinutes(3),
                        SlidingExpiration = TimeSpan.FromMinutes(3)
                    });
                    session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<HappyGroupDataManager>(key);
            }
        }

        #endregion
        #region 繳費與報名處理區

        public void SetupFeeListAccountAndPassword(String FullName, String Account, String Password)
        {
            try
            {
                // 儲存登入者資訊
                m_FeeList.SetupLoginUserInfo(FullName, Account, Password);

                // 取得繳費及點名的資料
                //m_FeeList.SetupFeeDataList(Account, Password);

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public void SetupFeeList()
        {
            try
            {
                // 取得繳費及點名的資料
                m_FeeList.SetupFeeDataList();
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }

        public void SetFeeManagerCacheData()
        {
            var session = _contextAccessor.HttpContext.Session;
            var key = session.Id + "_FeeList";

            if (_memoryCache.Get(key) == null)
            {
                _memoryCache.Set<FeeList>(key, m_FeeList, new MemoryCacheEntryOptions
                {
                    AbsoluteExpiration = DateTime.Now.AddMinutes(3),
                    SlidingExpiration = TimeSpan.FromMinutes(3)
                });
                session.SetInt32("dirty", 1);
            }
        }

        public FeeList GetFeeManagerCacheData()
        {
            var session = _contextAccessor.HttpContext.Session;
            var key = session.Id + "_FeeList";

            FeeList aFeeList = new FeeList();
            if (!_memoryCache.TryGetValue(key, out aFeeList))
            {
                //Time = "Cache is expired or not available";
            }

            return aFeeList;
        }

        public FeeList FeeList
        {
            get
            {
                var session = _contextAccessor.HttpContext.Session;
                var key = session.Id + "_FeeList";

                if (_memoryCache.Get(key) == null)
                {
                    _memoryCache.Set<FeeList>(key, m_FeeList, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpiration = DateTime.Now.AddMinutes(3),
                        SlidingExpiration = TimeSpan.FromMinutes(3)
                    });
                    session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<FeeList>(key);
            }
        }
        #endregion
        #region Line 綁定處理區
        public LineBindingViewModel LineBindingViewModel
        {
            get
            {
                var session = _contextAccessor.HttpContext.Session;
                var key = session.Id + "_LineBindingViewModel";

                if (_memoryCache.Get(key) == null)
                {
                    _memoryCache.Set<LineBindingViewModel>(key, m_LineBindingViewModel, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpiration = DateTime.Now.AddMinutes(3),
                        SlidingExpiration = TimeSpan.FromMinutes(3)
                    });
                    session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<LineBindingViewModel>(key);
            }
        }

        #endregion
        #region 工具區
        public void SaveChanges()
        {
            //foreach (var employee in DiscipleLessons.Where(a => a.DiscipleLessonsId == 0))
            //{
            //    employee.ID = DiscipleLessons.Max(a => a.ID) + 1;
            //}
        }
        #endregion
    }
}
