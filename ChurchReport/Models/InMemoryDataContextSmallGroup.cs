using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Threading;
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
        public AppointmentsListManager m_AppointmentsListManager = new AppointmentsListManager();

        #endregion
        #region 初始化

        public InMemoryDataContextSmallGroup(IHttpContextAccessor contextAccessor, IMemoryCache memoryCache)
        {
            _contextAccessor = contextAccessor;
            _memoryCache = memoryCache;
        }
        #endregion
        #region 多個組長處理區
        public void SetupListManager(String Account, String Password, DateTime aSelectDate, bool DisplayDateFlag)
        {
            try
            {
                // 設定多個組長處理資料
                m_ListManager.SetupListManager(Account, Password, aSelectDate);

                //m_ListManager.SetupListManager();

                //m_ListManager.SetupOnlyOneListManager();
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public void SetSelectDate( DateTime aSelectDate)
        {
            try
            {
                // 設定多個組長處理資料
                m_ListManager.SetSelectDate(aSelectDate);

                //m_ListManager.SetupListManager();

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
                //if (!_memoryCache.TryGetValue(key, out m_ListManager))
                {
                        var options = new MemoryCacheEntryOptions();
                    options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration()
                    {
                        EvictionCallback = (subkey, subValue, reason, state) =>
                        {
                            // 這裡執行某一個動作
                            // ....
                            if (state != null)
                            {
                                var localCallbackInvoked = (ManualResetEvent)state;

                                localCallbackInvoked.Set();
                            }

                            //_memoryCache.Remove(key);

                        },
                    });
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(10));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(10));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    _memoryCache.Set<ListManager>(key, m_ListManager, options);

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
                //if (!_memoryCache.TryGetValue(key, out m_SmallGroupDataList))
                {
                    var options = new MemoryCacheEntryOptions();
                    options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration()
                    {
                        EvictionCallback = (subkey, subValue, reason, state) =>
                        {
                            // 這裡執行某一個動作
                            // ....
                            if (state != null)
                            {
                                var localCallbackInvoked = (ManualResetEvent)state;

                                localCallbackInvoked.Set();
                            }

                            //_memoryCache.Remove(key);

                        },
                    });
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(10));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(10));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    _memoryCache.Set<SmallGroupDataList>(key, m_SmallGroupDataList, options);

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
                //if (!_memoryCache.TryGetValue(key, out m_WeeklyReportData))
                {
                        var options = new MemoryCacheEntryOptions();
                    options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration()
                    {
                        EvictionCallback = (subkey, subValue, reason, state) =>
                        {
                            // 這裡執行某一個動作
                            // ....
                            if (state != null)
                            {
                                var localCallbackInvoked = (ManualResetEvent)state;

                                localCallbackInvoked.Set();
                            }

                            //_memoryCache.Remove(key);

                        },
                    });
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(10));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(10));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    _memoryCache.Set<WeeklyReportData>(key, m_WeeklyReportData, options);

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
                //if (!_memoryCache.TryGetValue(key, out m_NewPersonModel))
                {
                        var options = new MemoryCacheEntryOptions();
                    options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration()
                    {
                        EvictionCallback = (subkey, subValue, reason, state) =>
                        {
                            // 這裡執行某一個動作
                            // ....
                            if (state != null)
                            {
                                var localCallbackInvoked = (ManualResetEvent)state;

                                localCallbackInvoked.Set();
                            }

                            //_memoryCache.Remove(key);

                        },
                    });
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(10));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(10));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    _memoryCache.Set<NewPersonModel>(key, m_NewPersonModel, options);

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
                //if (!_memoryCache.TryGetValue(key, out m_HappyGroupDataManager))
                {
                        var options = new MemoryCacheEntryOptions();
                    options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration()
                    {
                        EvictionCallback = (subkey, subValue, reason, state) =>
                        {
                            // 這裡執行某一個動作
                            // ....
                            if (state != null)
                            {
                                var localCallbackInvoked = (ManualResetEvent)state;

                                localCallbackInvoked.Set();
                            }

                            //_memoryCache.Remove(key);

                        },
                    });
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(10));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(10));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    _memoryCache.Set<HappyGroupDataManager>(key, m_HappyGroupDataManager, options);

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
                //if (!_memoryCache.TryGetValue(key, out m_FeeList))
                {
                        var options = new MemoryCacheEntryOptions();
                    options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration()
                    {
                        EvictionCallback = (subkey, subValue, reason, state) =>
                        {
                            // 這裡執行某一個動作
                            // ....
                            if (state != null)
                            {
                                var localCallbackInvoked = (ManualResetEvent)state;

                                localCallbackInvoked.Set();
                            }

                            //_memoryCache.Remove(key);

                        },
                    });
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(10));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(10));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    _memoryCache.Set<FeeList>(key, m_FeeList, options);

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
                //if (!_memoryCache.TryGetValue(key, out m_LineBindingViewModel))
                {
                        var options = new MemoryCacheEntryOptions();
                    options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration()
                    {
                        EvictionCallback = (subkey, subValue, reason, state) =>
                        {
                            // 這裡執行某一個動作
                            // ....
                            if (state != null)
                            {
                                var localCallbackInvoked = (ManualResetEvent)state;

                                localCallbackInvoked.Set();
                            }

                            //_memoryCache.Remove(key);

                        },
                    });
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(10));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(10));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    _memoryCache.Set<LineBindingViewModel>(key, m_LineBindingViewModel, options);

                    session.SetInt32("dirty", 1);
                }
                return _memoryCache.Get<LineBindingViewModel>(key);
            }
        }

        #endregion
        #region 多個組長處理區

        public AppointmentsListManager AppointmentsListManager
        {
            get
            {
                var session = _contextAccessor.HttpContext.Session;
                var key = session.Id + "_AppointmentsListManager";

                if (_memoryCache.Get(key) == null)
                //if (!_memoryCache.TryGetValue(key, out m_AppointmentsListManager))
                {
                        var options = new MemoryCacheEntryOptions();
                    options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration()
                    {
                        EvictionCallback = (subkey, subValue, reason, state) =>
                        {
                            // 這裡執行某一個動作
                            // ....
                            if (state != null)
                            {
                                var localCallbackInvoked = (ManualResetEvent)state;

                                localCallbackInvoked.Set();
                            }

                            //_memoryCache.Remove(key);

                        },
                    });
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(10));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(10));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    _memoryCache.Set<AppointmentsListManager>(key, m_AppointmentsListManager, options);

                    session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<AppointmentsListManager>(key);
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
