using ChurchReport.Tools;
using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Threading;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Models
{
    public class InMemoryDataContextSmallGroup
    {
        #region 資料區
        IMemoryCache _memoryCache;

        private ToolUtilityClass m_ToolUtilityClass;
        public ListManager m_ListManager;
        public SmallGroupDataList m_SmallGroupDataList;
        public WeeklyReportData m_WeeklyReportData;
        public NewPersonModel m_NewPersonModel;
        public PersonalInfomationModel m_PersonalInfomationModel;
        public HappyGroupDataManager m_HappyGroupDataManager;
        public ListManagementDataManager m_ListManagementDataManager;
        public EquipmentDataManager m_EquipmentDataManager;
        public FeeList m_FeeList;
        public LineBindingViewModel m_LineBindingViewModel;
        public AppointmentsListManager m_AppointmentsListManager;
        public QpayManager m_QpayManager;
        public PollManager m_PollManager;

        private HttpContextAccessor m_ContextAccessor;
        private HttpContext m_HttpContext;
        private ISession m_Session;

        private readonly IPayment m_PamentService;
        private readonly IToolUtilityProvider _toolUtilityProvider;

        #endregion
        #region 初始化
        public InMemoryDataContextSmallGroup(
            IHttpContextAccessor contextAccessor, 
            IMemoryCache memoryCache, 
            IPayment PamentService,
            IToolUtilityProvider toolUtilityProvider)
        {
            _memoryCache = memoryCache;

            m_ContextAccessor = (HttpContextAccessor)contextAccessor;
            m_HttpContext = m_ContextAccessor.HttpContext;
            m_Session = m_ContextAccessor.HttpContext.Session;

            m_PamentService = PamentService;
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
        }
        #endregion
        #region 多個組長處理區
        public ListManager ListManager
        {
            get
            {
                var key = m_Session.Id + "_ListManager";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    m_ListManager = new ListManager();
                    _memoryCache.Set<ListManager>(key, m_ListManager, options);

                    m_Session.SetInt32("dirty", 1);
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

                SmallGroupDataList.SetupContactIdString(ContactIdString);

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public SmallGroupDataList SmallGroupDataList
        {
            get
            {
                var key = m_Session.Id + "_SmallGroupDataList";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    m_SmallGroupDataList = new SmallGroupDataList();

                    _memoryCache.Set<SmallGroupDataList>(key, m_SmallGroupDataList, options);

                    m_Session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<SmallGroupDataList>(key);
            }
        }
        #endregion
        #region 週報處理區
        public WeeklyReportData WeeklyReportData
        {
            get
            {
                var key = m_Session.Id + "_WeeklyReportData";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    m_WeeklyReportData = new WeeklyReportData();
                    _memoryCache.Set<WeeklyReportData>(key, m_WeeklyReportData, options);

                    m_Session.SetInt32("dirty", 1);
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
                var key = m_Session.Id + "_NewPersonModel";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    m_NewPersonModel = new NewPersonModel();
                    _memoryCache.Set<NewPersonModel>(key, m_NewPersonModel, options);

                    m_Session.SetInt32("dirty", 1);
                }
                return _memoryCache.Get<NewPersonModel>(key);
            }
        }

        #endregion
        #region 個人相關資料處理區
        public PersonalInfomationModel PersonalInfomationModel
        {
            get
            {
                var key = m_Session.Id + "_PersonalInfomationModel";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    m_PersonalInfomationModel = new PersonalInfomationModel();
                    _memoryCache.Set<PersonalInfomationModel>(key, m_PersonalInfomationModel, options);

                    m_Session.SetInt32("dirty", 1);
                }
                return _memoryCache.Get<PersonalInfomationModel>(key);
            }
        }

        #endregion
        #region 幸福小組處理區
        public HappyGroupDataManager HappyGroupDataManager
        {
            get
            {
                var key = m_Session.Id + "_HappyGroupDataManager";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    // 使用 DI 模式注入 ToolUtilityProvider
                    m_HappyGroupDataManager = new HappyGroupDataManager(_toolUtilityProvider);
                    _memoryCache.Set<HappyGroupDataManager>(key, m_HappyGroupDataManager, options);

                    m_Session.SetInt32("dirty", 1);
                }
                return _memoryCache.Get<HappyGroupDataManager>(key);
            }
        }

        #endregion
        #region 名單管理處理區
        public ListManagementDataManager ListManagementDataManager
        {
            get
            {
                var key = m_Session.Id + "_ListManagementDataManager";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    m_ListManagementDataManager = new ListManagementDataManager();
                    _memoryCache.Set<ListManagementDataManager>(key, m_ListManagementDataManager, options);

                    m_Session.SetInt32("dirty", 1);
                }
                return _memoryCache.Get<ListManagementDataManager>(key);
            }
        }

        #endregion
        #region 裝備情形處理區
        public EquipmentDataManager EquipmentDataManager
        {
            get
            {
                var key = m_Session.Id + "_EquipmentDataManager";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    // 使用 DI 模式注入 ToolUtilityProvider
                    m_EquipmentDataManager = new EquipmentDataManager(_toolUtilityProvider);
                    _memoryCache.Set<EquipmentDataManager>(key, m_EquipmentDataManager, options);

                    m_Session.SetInt32("dirty", 1);
                }
                return _memoryCache.Get<EquipmentDataManager>(key);
            }
        }

        #endregion
        #region 繳費與報名處理區

        public FeeList FeeList
        {
            get
            {
                var key = m_Session.Id + "_FeeList";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    // 使用 DI 模式注入 ToolUtilityProvider
                    m_FeeList = new FeeList(_toolUtilityProvider);
                    _memoryCache.Set<FeeList>(key, m_FeeList, options);

                    m_Session.SetInt32("dirty", 1);
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
                var key = m_Session.Id + "_LineBindingViewModel";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    m_LineBindingViewModel = new LineBindingViewModel();
                    _memoryCache.Set<LineBindingViewModel>(key, m_LineBindingViewModel, options);

                    m_Session.SetInt32("dirty", 1);
                }
                return _memoryCache.Get<LineBindingViewModel>(key);
            }
        }

        #endregion
        #region 行事曆處理區
        public AppointmentsListManager AppointmentsListManager
        {
            get
            {
                var key = m_Session.Id + "_AppointmentsListManager";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    m_AppointmentsListManager = new AppointmentsListManager();
                    _memoryCache.Set<AppointmentsListManager>(key, m_AppointmentsListManager, options);

                    m_Session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<AppointmentsListManager>(key);
            }
        }
        #endregion
        #region 永豐金流奉獻處理區
        public QpayManager QpayManager
        {
            get
            {
                var key = m_Session.Id + "_QpayManager";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    m_QpayManager = new QpayManager(m_PamentService);
                    _memoryCache.Set<QpayManager>(key, m_QpayManager, options);

                    m_Session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<QpayManager>(key);
            }
        }
        #endregion
        #region 課程問卷調查處理區
        public PollManager PollManager
        {
            get
            {
                var key = m_Session.Id + "_PollManager";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    m_PollManager = new PollManager();
                    _memoryCache.Set<PollManager>(key, m_PollManager, options);

                    m_Session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<PollManager>(key);
            }
        }
        #endregion
        #region 工具區

        public ToolUtilityClass ToolUtilityClass
        {
            get
            {
                var key = m_Session.Id + "_ToolUtilityClass";

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
                    options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
                    options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
                    //options.SetSize(1);
                    //options.Size = 1024;

                    // 使用 Factory 模式取得 ToolUtilityClass 單例
                    m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
                    _memoryCache.Set<ToolUtilityClass>(key, m_ToolUtilityClass, options);

                    m_Session.SetInt32("dirty", 1);
                }

                return _memoryCache.Get<ToolUtilityClass>(key);
            }
        }

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
