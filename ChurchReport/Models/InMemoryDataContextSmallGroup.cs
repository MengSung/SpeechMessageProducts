using ChurchReport.Tools;
using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Models
{
    public class InMemoryDataContextSmallGroup : IInMemoryDataContext
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

        // ========================================
        // ✅ Session Bleeding 修復：不再在建構函式中捕獲 Session
        // 改為每次存取時從 IHttpContextAccessor 取得當前的 Session
        // ========================================
        private readonly IHttpContextAccessor m_ContextAccessor;

        private readonly IPayment m_PamentService;
        private readonly IToolUtilityProvider _toolUtilityProvider;

        /// <summary>
        /// 取得當前 HTTP 請求的 Session（每次都從 HttpContextAccessor 取得最新值）
        /// 這是修復 Session Bleeding 的關鍵：不再使用建構時捕獲的 Session
        /// </summary>
        private ISession CurrentSession => m_ContextAccessor?.HttpContext?.Session;

        /// <summary>
        /// 安全地取得當前 Session ID
        /// 若 Session 不存在，返回空字串（避免 NullReferenceException）
        /// </summary>
        private string GetCurrentSessionId()
        {
            System.Diagnostics.Debug.WriteLine("[GetCurrentSessionId] 🔵 進入方法");

            var session = CurrentSession;
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 📌 CurrentSession 是否為 null: {session == null}");

            if (session == null)
            {
                System.Diagnostics.Debug.WriteLine("[GetCurrentSessionId] ❌ CurrentSession 為 null，拋出異常防止資料洩漏");
                throw new InvalidOperationException(
                    "Session 不可用，無法產生安全的快取 key。" +
                    "請確保在 HTTP 請求上下文中存取此屬性，且 Session 中介軟體已正確配置。");
            }

            var sessionId = session.Id;
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 📋 Session ID: {sessionId}");

            var boundUserId = session.GetString("_SessionRegeneratedFor") ?? string.Empty;
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 👤 BoundUserId: {(string.IsNullOrEmpty(boundUserId) ? "(empty)" : boundUserId)}");

            // ========================================
            // ✅ 指紋策略：優先使用已綁定的 Session 指紋
            // 
            // 已登入時使用 Session 指紋，確保同一使用者跨請求一致。
            // 未綁定時使用即時指紋，避免 Session ID 碰撞時資料混淆。
            // ========================================
            var storedFingerprint = session.GetString("_SessionFingerprint");
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🔐 StoredFingerprint 是否存在: {!string.IsNullOrEmpty(storedFingerprint)}");

            string currentRequestFingerprint = string.IsNullOrEmpty(storedFingerprint)
                ? GenerateCurrentRequestFingerprint()
                : storedFingerprint;
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🔐 CurrentRequestFingerprint (前16字): {(currentRequestFingerprint?.Substring(0, Math.Min(16, currentRequestFingerprint.Length)) ?? "(empty)")}...");

            var sessionCreatedTime = session.GetString("_SessionCreatedTime");
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] ⏱️  SessionCreatedTime 是否存在: {!string.IsNullOrEmpty(sessionCreatedTime)}");

            if (string.IsNullOrEmpty(sessionCreatedTime))
            {
                // 使用 Ticks + GUID 的組合確保絕對唯一性
                sessionCreatedTime = $"{DateTime.UtcNow.Ticks}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🆕 生成新的 SessionCreatedTime: {sessionCreatedTime}");

                try
                {
                    session.SetString("_SessionCreatedTime", sessionCreatedTime);
                    System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] ✅ 首次存取，已初始化 Session 時間戳: {sessionCreatedTime}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] ❌ 無法寫入 Session 時間戳 - Exception: {ex.GetType().Name}");
                    System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] ❌ 異常詳情: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] ❌ StackTrace: {ex.StackTrace}");
                    throw new InvalidOperationException(
                        "無法寫入 Session 時間戳，無法產生安全的快取 key。" +
                        "請確保 Session 中介軟體已正確配置且可寫入。", ex);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] ⏱️  使用既有的 SessionCreatedTime: {sessionCreatedTime}");
            }

            // 建構安全的快取 key
            var keyBuilder = new System.Text.StringBuilder(sessionId);
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🏗️  開始構建快取 Key，初始值: {sessionId}");

            if (!string.IsNullOrEmpty(boundUserId))
            {
                keyBuilder.Append('_').Append(boundUserId);
                System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🏗️  已添加 BoundUserId: {boundUserId}");
            }

            // 使用即時指紋作為 key 的一部分
            if (!string.IsNullOrEmpty(currentRequestFingerprint))
            {
                // 只取指紋的前 8 個字元，避免 key 過長
                var shortFingerprint = currentRequestFingerprint.Length > 8
                    ? currentRequestFingerprint.Substring(0, 8)
                    : currentRequestFingerprint;
                keyBuilder.Append('_').Append(shortFingerprint);
                System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🏗️  已添加短指紋: {shortFingerprint}");
            }

            if (!string.IsNullOrEmpty(sessionCreatedTime))
            {
                var shortTimestamp = sessionCreatedTime.Length > 10
                    ? sessionCreatedTime.Substring(sessionCreatedTime.Length - 10)
                    : sessionCreatedTime;
                keyBuilder.Append('_').Append(shortTimestamp);
                System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🏗️  已添加時間戳: {shortTimestamp}");
            }

            var finalKey = keyBuilder.ToString();
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] ✅ 最終快取 Key: {finalKey}");
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🟢 方法返回，Key 長度: {finalKey.Length}");

            return finalKey;
        }

        /// <summary>
        /// 生成當前請求的指紋（IP + UserAgent）
        /// 不依賴 Session 中儲存的值，確保即時隔離
        /// </summary>
        private string GenerateCurrentRequestFingerprint()
        {
            System.Diagnostics.Debug.WriteLine("[GenerateCurrentRequestFingerprint] 🔵 進入方法");

            try
            {
                var httpContext = m_ContextAccessor?.HttpContext;
                System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] 📌 HttpContext 是否為 null: {httpContext == null}");

                if (httpContext == null)
                {
                    System.Diagnostics.Debug.WriteLine("[GenerateCurrentRequestFingerprint] ⚠️  HttpContext 為 null，返回空字串");
                    return string.Empty;
                }

                var ip = "Unknown";
                try
                {
                    System.Diagnostics.Debug.WriteLine("[GenerateCurrentRequestFingerprint] 🌐 開始提取 IP 地址");

                    var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].ToString();
                    System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] 🌐 X-Forwarded-For Header: {(string.IsNullOrEmpty(forwardedFor) ? "(empty)" : forwardedFor)}");

                    if (!string.IsNullOrEmpty(forwardedFor))
                    {
                        var ips = forwardedFor.Split(',');
                        System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] 🌐 X-Forwarded-For IP 列表數量: {ips.Length}");

                        if (ips.Length > 0)
                        {
                            ip = ips[0].Trim();
                            System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] 🌐 使用 X-Forwarded-For 第一個 IP: {ip}");
                        }
                    }
                    else
                    {
                        ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                        System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] 🌐 使用 RemoteIpAddress: {ip}");
                    }
                }
                catch (Exception ipEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] ⚠️  提取 IP 時發生異常: {ipEx.GetType().Name} - {ipEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] ⚠️  IP 預設為: Unknown");
                }

                var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
                System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] 🖥️  User-Agent (前50字): {(string.IsNullOrEmpty(userAgent) ? "(empty)" : userAgent.Substring(0, Math.Min(50, userAgent.Length)))}...");

                var input = $"{ip}|{userAgent}";
                System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] 🔐 指紋輸入: {input}");

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                    var fingerprint = Convert.ToBase64String(bytes);
                    System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] ✅ 生成的指紋: {fingerprint}");
                    System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] 🟢 方法返回成功");

                    return fingerprint;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] ❌ 方法異常 - Exception 類型: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] ❌ 異常訊息: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] ❌ StackTrace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"[GenerateCurrentRequestFingerprint] ⚠️  返回空字串作為備用");

                return string.Empty;
            }
        }

        /// <summary>
        /// 安全地設定 Session 值（dirty flag）
        /// </summary>
        private void SetSessionDirtyFlag()
        {
            System.Diagnostics.Debug.WriteLine("[SetSessionDirtyFlag] 🔵 進入方法");

            var session = CurrentSession;
            System.Diagnostics.Debug.WriteLine($"[SetSessionDirtyFlag] 📌 CurrentSession 是否為 null: {session == null}");

            if (session != null)
            {
                try
                {
                    session.SetInt32("dirty", 1);
                    System.Diagnostics.Debug.WriteLine("[SetSessionDirtyFlag] ✅ 已成功設定 dirty flag = 1");
                    System.Diagnostics.Debug.WriteLine("[SetSessionDirtyFlag] 🟢 方法完成");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SetSessionDirtyFlag] ❌ 設定 dirty flag 時發生異常");
                    System.Diagnostics.Debug.WriteLine($"[SetSessionDirtyFlag] ❌ Exception 類型: {ex.GetType().Name}");
                    System.Diagnostics.Debug.WriteLine($"[SetSessionDirtyFlag] ❌ 異常訊息: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[SetSessionDirtyFlag] ❌ StackTrace: {ex.StackTrace}");

                    // 不拋出異常，只記錄警告
                    System.Diagnostics.Debug.WriteLine("[SetSessionDirtyFlag] ⚠️  由於異常，dirty flag 設定可能失敗");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[SetSessionDirtyFlag] ⚠️  CurrentSession 為 null，無法設定 dirty flag");
                System.Diagnostics.Debug.WriteLine("[SetSessionDirtyFlag] ⚠️  方法返回（不設定任何值）");
            }
        }

        #endregion
        #region 初始化
        public InMemoryDataContextSmallGroup(
            IHttpContextAccessor contextAccessor,
            IMemoryCache memoryCache,
            IPayment PamentService,
            IToolUtilityProvider toolUtilityProvider)
        {
            _memoryCache = memoryCache;

            // ========================================
            // ✅ Session Bleeding 修復：只保存 IHttpContextAccessor 參考
            // 不再在建構時捕獲 HttpContext 或 Session
            // 每次需要時透過 CurrentSession 屬性取得當前的 Session
            // ========================================
            m_ContextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));

            m_PamentService = PamentService;
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));

            System.Diagnostics.Debug.WriteLine("[InMemoryDataContext] ✅ 建構完成（Session Bleeding 修復版本）");
        }
        #endregion
        #region 多個組長處理區
        public ListManager ListManager
        {
            get
            {
                var key = GetCurrentSessionId() + "_ListManager";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_SmallGroupDataList";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_WeeklyReportData";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_NewPersonModel";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_PersonalInfomationModel";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_HappyGroupDataManager";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_ListManagementDataManager";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_EquipmentDataManager";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_FeeList";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_LineBindingViewModel";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_AppointmentsListManager";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_QpayManager";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_PollManager";

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

                    SetSessionDirtyFlag();
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
                var key = GetCurrentSessionId() + "_ToolUtilityClass";

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

                    SetSessionDirtyFlag();
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
