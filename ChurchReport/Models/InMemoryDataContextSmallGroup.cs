using ChurchReport.Payments;
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
    /// <summary>
    /// 記憶體內資料上下文 - 小組管理專用
    /// 
    /// 此類別負責管理小組相關資料的記憶體快取和 Session 處理，
    /// 實現 IInMemoryDataContext 接口，提供各種資料管理器的快取存取。
    /// 
    /// 主要功能：
    /// - 使用 IMemoryCache 快取資料管理器實例，避免重複建立
    /// - 處理 Session 隔離，防止 Session Bleeding 問題
    /// - 提供安全的 Session ID 生成和指紋驗證
    /// - 支援多個組長和各種小組資料的管理
    /// 
    /// 快取策略：
    /// - 每個屬性使用 Session ID + 屬性名稱作為快取鍵
    /// - 快取過期時間：絕對 30 分鐘，滑動 30 分鐘
    /// - Session 變更時設定 dirty flag 標記
    /// 
    /// Session 安全：
    /// - 每次存取 Session 時從 IHttpContextAccessor 取得最新值
    /// - 使用 IP + User-Agent 生成請求指紋，防止資料混淆
    /// - 支援已登入使用者的 Session 綁定
    /// </summary>
    public class InMemoryDataContextSmallGroup : IInMemoryDataContext
    {
        #region 資料區

        /// <summary>
        /// 記憶體快取實例，用於快取各種資料管理器
        /// </summary>
        IMemoryCache _memoryCache;

        /// <summary>
        /// ToolUtilityClass 實例，用於 CRM 操作
        /// </summary>
        private ToolUtilityClass m_ToolUtilityClass;

        /// <summary>
        /// 組長管理器
        /// </summary>
        public ListManager m_ListManager;

        /// <summary>
        /// 小組資料列表管理器
        /// </summary>
        public SmallGroupDataList m_SmallGroupDataList;

        /// <summary>
        /// 週報資料管理器
        /// </summary>
        public WeeklyReportData m_WeeklyReportData;

        /// <summary>
        /// 新人模型管理器
        /// </summary>
        public NewPersonModel m_NewPersonModel;

        /// <summary>
        /// 個人資訊模型管理器
        /// </summary>
        public PersonalInfomationModel m_PersonalInfomationModel;

        /// <summary>
        /// 幸福小組資料管理器
        /// </summary>
        public HappyGroupDataManager m_HappyGroupDataManager;

        /// <summary>
        /// 名單管理資料管理器
        /// </summary>
        public ListManagementDataManager m_ListManagementDataManager;

        /// <summary>
        /// 裝備資料管理器
        /// </summary>
        public EquipmentDataManager m_EquipmentDataManager;

        /// <summary>
        /// 繳費列表管理器
        /// </summary>
        public FeeList m_FeeList;

        /// <summary>
        /// Line 綁定視圖模型
        /// </summary>
        public LineBindingViewModel m_LineBindingViewModel;

        /// <summary>
        /// 行事曆列表管理器
        /// </summary>
        public AppointmentsListManager m_AppointmentsListManager;

        /// <summary>
        /// 永豐金流奉獻管理器
        /// </summary>
        public QpayManager m_QpayManager;

        /// <summary>
        /// 課程問卷調查管理器
        /// </summary>
        public PollManager m_PollManager;

        // ========================================
        // ✅ Session Bleeding 修復：不再在建構函式中捕獲 Session
        // 改為每次存取時從 IHttpContextAccessor 取得當前的 Session
        // ========================================

        /// <summary>
        /// HTTP 上下文存取器，用於安全取得當前請求的 Session
        /// </summary>
        private readonly IHttpContextAccessor m_ContextAccessor;

        /// <summary>
        /// 付款服務實例
        /// </summary>
        private readonly QPayCreatePaymentGatewayAdapter m_QPayCreatePaymentGatewayAdapter;

        /// <summary>
        /// ToolUtility 提供者，用於依賴注入
        /// </summary>
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
                // 在偵錯或非 HTTP 請求上下文 (例如 VS 立即視窗、除錯評估) 中存取時，避免丟出例外導致 Visual Studio 顯示不安全的中止情形。
                // 改為回傳一個暫時性的唯一 key，並記錄警告。這能避免除錯時評估屬性引發中斷，但在正常請求流程中 Session 應可用。
                System.Diagnostics.Debug.WriteLine("[GetCurrentSessionId] ❌ CurrentSession 為 null，回傳暫時性快取 key 以避免在除錯時中斷流程");
                var tempKey = $"NOSESSION_{Environment.MachineName}_{Thread.CurrentThread.ManagedThreadId}_{DateTime.UtcNow.Ticks}";
                System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] ⚠️ 暫時性 Key: {tempKey}");
                return tempKey;
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

            /// <summary>
            /// 從 Session 中獲取已儲存的指紋
            /// 
            /// 指紋是用於識別請求來源的唯一標識符，
            /// 基於 IP 地址和 User-Agent 的 SHA256 雜湊。
            /// 如果存在已儲存的指紋，表示使用者已登入並綁定。
            /// </summary>
            var storedFingerprint = session.GetString("_SessionFingerprint");
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🔐 StoredFingerprint 是否存在: {!string.IsNullOrEmpty(storedFingerprint)}");
            // 注意事項：
            // - storedFingerprint 若存在代表應用程式在某次登入或綁定時，
            //   已將穩定的指紋寫入 Session，這樣可以讓同一使用者在未來請求
            //   中使用相同的快取 key（即使來源 IP 有所變動），提高可用性。
            // - 如果系統部署於反向代理或多節點環境，請確認 Forwarded headers
            //  （例如 X-Forwarded-For）有正確設定，否則動態指紋可能不穩定。

            /// <summary>
            /// 決定當前請求的指紋
            /// 
            /// 如果有已儲存的指紋（已登入使用者），優先使用它以確保一致性。
            /// 否則，動態生成新的指紋以防止未登入使用者的 Session 碰撞。
            /// 這是 Session 安全隔離的核心機制。
            /// </summary>
            string currentRequestFingerprint = string.IsNullOrEmpty(storedFingerprint)
                ? GenerateCurrentRequestFingerprint()
                : storedFingerprint;
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🔐 CurrentRequestFingerprint (前16字): {(currentRequestFingerprint?.Substring(0, Math.Min(16, currentRequestFingerprint.Length)) ?? "(empty)")}...");
            // 補充說明：
            // - GenerateCurrentRequestFingerprint() 會使用 IP + User-Agent 做 SHA256 雜湊並以 Base64 回傳，
            //   這會在匿名使用者之間提供較低的碰撞機率，但也會受到 User-Agent 偽造或 NAT/代理的影響。
            // - 對於已登入使用者，我們優先使用 storedFingerprint，以避免每次請求都生成不同的動態指紋，
            //   造成快取分裂（cache fragmentation）。

            /// <summary>
            /// 從 Session 中獲取 Session 建立時間戳
            /// 
            /// 時間戳用於進一步區分 Session，防止重複使用舊的 Session ID。
            /// 如果不存在，將在首次存取時初始化。
            /// </summary>
            var sessionCreatedTime = session.GetString("_SessionCreatedTime");
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] ⏱️  SessionCreatedTime 是否存在: {!string.IsNullOrEmpty(sessionCreatedTime)}");
            // 補充說明：
            // - sessionCreatedTime 用來補強 key 的唯一性；它由 Ticks + 短 GUID 構成，
            //   可在極端情況下降低 Session ID 與指紋組合碰撞的風險。
            // - 如果無法寫入 Session（例如 Session 中介軟體未啟用或權限問題），
            //   程式會拋出例外，因為無法保證快取 key 的安全性與唯一性。

            /// <summary>
            /// 初始化或驗證 Session 建立時間戳
            /// 
            /// 如果時間戳不存在，生成一個絕對唯一的時間戳（使用 UTC Ticks + GUID）。
            /// 這確保即使在高併發情況下，每個 Session 都有唯一的時間標識。
            /// 寫入失敗時拋出異常，因為無法產生安全的快取 key。
            /// </summary>
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

            // ========================================
            // 建構安全的快取 key
            // 
            // 快取 key 由多個部分組成，確保唯一性和安全性：
            // 1. Session ID：ASP.NET Core 提供的基礎 Session 識別符
            // 2. BoundUserId：已登入使用者的識別符（如果存在）
            // 3. 短指紋：請求來源的縮短指紋（前8字元）
            // 4. 短時間戳：Session 建立時間的縮短版本（後10字元）
            // ========================================

            /// <summary>
            /// 初始化快取 key 建構器，以 Session ID 為基礎
            /// 
            /// Session ID 是 ASP.NET Core 自動生成的唯一識別符，
            /// 但單獨使用可能會有碰撞風險，因此需要額外元件強化。
            /// </summary>
            var keyBuilder = new System.Text.StringBuilder(sessionId);
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🏗️  開始構建快取 Key，初始值: {sessionId}");

            /// <summary>
            /// 如果有已綁定的使用者 ID，加入到 key 中
            /// 
            /// 這確保已登入使用者的資料不會與其他使用者混淆，
            /// 即使他們有相同的 Session ID。
            /// </summary>
            if (!string.IsNullOrEmpty(boundUserId))
            {
                keyBuilder.Append('_').Append(boundUserId);
                System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🏗️  已添加 BoundUserId: {boundUserId}");
            }

            /// <summary>
            /// 加入請求指紋的縮短版本到 key 中
            /// 
            /// 只取前 8 個字元以避免 key 過長，同時保留足夠的唯一性。
            /// 這是防止 Session 碰撞和資料洩漏的核心安全措施。
            /// </summary>
            if (!string.IsNullOrEmpty(currentRequestFingerprint))
            {
                // 只取指紋的前 8 個字 元，避免 key 過長
                var shortFingerprint = currentRequestFingerprint.Length > 8
                    ? currentRequestFingerprint.Substring(0, 8)
                    : currentRequestFingerprint;
                // 補充說明：
                // - 只取前 8 個字元是為了控制 key 長度，但要注意 Base64 字元可能包含 '+' '/' '='，
                //   若需在外部系統（如檔案名、URL 等）使用，請先做安全編碼（例如 Base64Url 或 Hex）。
                // - 保留過短的片段會降低唯一性，若在高併發或大量匿名使用者環境，
                //   可考慮改為取更多字元或使用其他穩定標識。
                keyBuilder.Append('_').Append(shortFingerprint);
                System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🏗️  已添加短指紋: {shortFingerprint}");
            }

            /// <summary>
            /// 加入 Session 建立時間戳的縮短版本到 key 中
            /// 
            /// 取後 10 個字元（通常是 GUID 的部分），提供額外的唯一性。
            /// 這確保即使在極端情況下，key 仍然是唯一的。
            /// </summary>
            if (!string.IsNullOrEmpty(sessionCreatedTime))
            {
                var shortTimestamp = sessionCreatedTime.Length > 10
                    ? sessionCreatedTime.Substring(sessionCreatedTime.Length - 10)
                    : sessionCreatedTime;
                // 補充說明：
                // - 取時間戳的後 10 個字元是為了得到較為隨機且不容易重複的片段（通常包含 GUID 的部分），
                //   並且避免將整個長字串加入 key 造成過長。
                // - 此片段並非用來表達時間的可讀形式，只是用作增加唯一性的標識。
                keyBuilder.Append('_').Append(shortTimestamp);
                System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] 🏗️  已添加時間戳: {shortTimestamp}");
            }

            /// <summary>
            /// 生成最終的快取 key
            /// 
            /// 這個 key 現在包含了足夠的資訊來唯一識別一個使用者的 Session，
            /// 同時防止資料洩漏和碰撞。
            /// </summary>
            var finalKey = keyBuilder.ToString();
            System.Diagnostics.Debug.WriteLine($"[GetCurrentSessionId] ✅ 最終快取 Key: {finalKey}");
            // 補充說明：
            // - finalKey 的格式為：{SessionId}_{BoundUserId?}_{ShortFingerprint?}_{ShortTimestamp?}
            // - 這個 key 適用於記憶體快取（IMemoryCache）中作為索引。
            // - 請注意此 key 可能會包含特殊字元（來自 Base64），如果未來需要將其序列化到其他儲存或傳輸媒介，
            //   請先做安全字元處理。
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

        /// <summary>
        /// 建構函式 - 初始化記憶體資料上下文
        /// 
        /// 注入必要的依賴項：
        /// - IHttpContextAccessor: 用於安全存取 HTTP 上下文和 Session
        /// - IMemoryCache: 用於快取資料管理器實例
        /// - QPayCreatePaymentGatewayAdapter: QPay 建單 adapter
        /// - IToolUtilityProvider: ToolUtility 提供者
        /// 
        /// 注意：不再在建構時捕獲 Session，以避免 Session Bleeding
        /// </summary>
        /// <param name="contextAccessor">HTTP 上下文存取器</param>
        /// <param name="memoryCache">記憶體快取</param>
        /// <param name="PamentService">付款服務</param>
        /// <param name="toolUtilityProvider">ToolUtility 提供者</param>
        public InMemoryDataContextSmallGroup(
            IHttpContextAccessor contextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            QPayCreatePaymentGatewayAdapter qPayCreatePaymentGatewayAdapter = null)
        {
            _memoryCache = memoryCache;

            // ========================================
            // ✅ Session Bleeding 修復：只保存 IHttpContextAccessor 參考
            // 不再在建構時捕獲 HttpContext 或 Session
            // 每次需要時透過 CurrentSession 屬性取得當前的 Session
            // ========================================
            m_ContextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));

            m_QPayCreatePaymentGatewayAdapter = qPayCreatePaymentGatewayAdapter;
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));

            System.Diagnostics.Debug.WriteLine("[InMemoryDataContext] ✅ 建構完成（Session Bleeding 修復版本）");
        }

        #endregion

        #region 多個組長處理區

        /// <summary>
        /// 組長管理器屬性
        /// 
        /// 使用記憶體快取管理 ListManager 實例，
        /// 快取鍵為 Session ID + "_ListManager"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則建立新實例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// 設定小組資料
        /// 
        /// 根據提供的姓名、帳號、密碼和選擇日期設定小組資料，
        /// 並更新 SmallGroupDataList 的聯絡人 ID 字串。
        /// </summary>
        /// <param name="FullName">完整姓名</param>
        /// <param name="Account">帳號</param>
        /// <param name="Password">密碼</param>
        /// <param name="aSelectDate">選擇日期</param>
        /// <param name="DisplayDateFlag">顯示日期旗標</param>
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

        /// <summary>
        /// 小組資料列表管理器屬性
        /// 
        /// 使用記憶體快取管理 SmallGroupDataList 實例，
        /// 快取鍵為 Session ID + "_SmallGroupDataList"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則建立新實例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// 週報資料管理器屬性
        /// 
        /// 使用記憶體快取管理 WeeklyReportData 實例，
        /// 快取鍵為 Session ID + "_WeeklyReportData"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則建立新實例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// 新人模型管理器屬性
        /// 
        /// 使用記憶體快取管理 NewPersonModel 實例，
        /// 快取鍵為 Session ID + "_NewPersonModel"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則建立新實例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// 個人資訊模型管理器屬性
        /// 
        /// 使用記憶體快取管理 PersonalInfomationModel 實例，
        /// 快取鍵為 Session ID + "_PersonalInfomationModel"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則建立新實例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// 幸福小組資料管理器屬性
        /// 
        /// 使用記憶體快取管理 HappyGroupDataManager 實例，
        /// 快取鍵為 Session ID + "_HappyGroupDataManager"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則使用 DI 注入 ToolUtilityProvider 建立新實例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// 名單管理資料管理器屬性
        /// 
        /// 使用記憶體快取管理 ListManagementDataManager 實例，
        /// 快取鍵為 Session ID + "_ListManagementDataManager"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則建立新實例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// 裝備資料管理器屬性
        /// 
        /// 使用記憶體快取管理 EquipmentDataManager 實例，
        /// 快取鍵為 Session ID + "_EquipmentDataManager"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則使用 DI 注入 ToolUtilityProvider 建立新實例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// 繳費列表管理器屬性
        /// 
        /// 使用記憶體快取管理 FeeList 實例，
        /// 快取鍵為 Session ID + "_FeeList"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則使用 DI 注入 ToolUtilityProvider 建立新實例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// Line 綁定視圖模型屬性
        /// 
        /// 使用記憶體快取管理 LineBindingViewModel 實例，
        /// 快取鍵為 Session ID + "_LineBindingViewModel"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則建立新實例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// 行事曆列表管理器屬性
        /// 
        /// 使用記憶體快取管理 AppointmentsListManager 實例，
        /// 快取鍵為 Session ID + "_AppointmentsListManager"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則建立新實例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// 永豐金流奉獻管理器屬性
        /// 
        /// 使用記憶體快取管理 QpayManager 實例，
        /// 快取鍵為 Session ID + "_QpayManager"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則注入付款服務建立新實例並設定快取選項。
        /// </summary>
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

                    m_QpayManager = new QpayManager(m_QPayCreatePaymentGatewayAdapter);
                    _memoryCache.Set<QpayManager>(key, m_QpayManager, options);

                    SetSessionDirtyFlag();
                }

                return _memoryCache.Get<QpayManager>(key);
            }
        }

        #endregion

        #region 課程問卷調查處理區

        /// <summary>
        /// 課程問卷調查管理器屬性
        /// 
        /// 使用記憶體快取管理 PollManager 實例，
        /// 快取鍵為 Session ID + "_PollManager"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則建立新實例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// ToolUtilityClass 屬性
        /// 
        /// 使用記憶體快取管理 ToolUtilityClass 實例，
        /// 快取鍵為 Session ID + "_ToolUtilityClass"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        /// 
        /// 若快取不存在，則使用 Factory 模式取得 ToolUtilityClass 單例並設定快取選項。
        /// </summary>
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

        /// <summary>
        /// 儲存變更
        /// 
        /// 此方法目前為空實作，可用於將記憶體中的變更持久化到資料庫。
        /// </summary>
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
