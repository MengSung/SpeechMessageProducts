// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/InMemoryDataContextSmallGroup.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class InMemoryDataContextSmallGroup
// 主要成員：GetCurrentSessionId、GenerateCurrentRequestFingerprint、SetSessionDirtyFlag、SetupSmallGroupData、SaveChanges、ListManager、SmallGroupDataList、WeeklyReportData、NewPersonModel、PersonalInfomationModel
// 引用命名空間：ChurchReport.Payments、ChurchReport.Tools、ChurchReport.ViewModel、LineMessagingProcessor.Workflows、Microsoft.AspNetCore.Http、Microsoft.Extensions.Caching.Memory、System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Payments;
using ChurchReport.Diagnostics;
using ChurchReport.Tools;
using ChurchReport.ViewModel;
using LineMessagingProcessor.Workflows;
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
        /// ChurchReport 奉獻付款 UI 狀態管理器。
        /// 這是產品流程的主要入口，負責把 UI 狀態、CRM 資料與付款建立流程串起來。
        /// </summary>
        public DonationPaymentManager m_DonationPaymentManager;


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
        /// 建立付款用的產品層 adapter。
        /// 此處保留在 ChurchReport 內，是因為它銜接 Session、CRM 與舊奉獻頁面狀態；
        /// 可重用金流核心不應知道 InMemoryDataContext 或 ChurchReport 的頁面模型。
        /// </summary>
        private readonly IDonationPaymentCreateGatewayAdapter m_DonationPaymentCreateGatewayAdapter;

        /// <summary>
        /// 共用 LINE push workflow。這裡只保存 product-neutral 的 LINE 發送邊界，
        /// ChurchReport 的 CRM、奉獻、付款與頁面流程仍留在 ChurchReport。
        /// DonationPaymentManager 由本類別建立與快取，因此必須從這裡傳入 workflow，
        /// 才能避免付款與奉獻流程退回舊的 new PushUtility(client) 直呼 SDK 路徑。
        /// </summary>
        private readonly ILineNotificationWorkflow? _lineNotificationWorkflow;

        /// <summary>
        /// 共用 LINE reply-token workflow。Reply token 與 push notification 是不同 API 語意，
        /// 所以分開注入，讓未來 ASP.NET Core 產品也能重用同一套 reply adapter。
        /// </summary>
        private readonly ILineReplyWorkflow? _lineReplyWorkflow;

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
        /// 將既有 Session 除錯訊息送入 Debug 管線，但只在程序級診斷開關明確啟用時執行。
        /// </summary>
        /// <param name="message">由本檔案固定程式碼產生的診斷文字；不得帶入未遮罩的外部輸入。</param>
        /// <remarks>
        /// <para>
        /// 這個 helper 是本檔 51 個既有 <c>Debug.WriteLine</c> 呼叫的唯一出口。它只控制
        /// 診斷副作用，不包住或改寫 Session 存取、指紋雜湊、快取 key 組成、dirty flag 或
        /// 其他產品行為；因此關閉開關不會改變資料結果，也不會引入跨使用者狀態共享。
        /// </para>
        /// <para>
        /// 開關預設關閉時只做一次 volatile read，避免建立 writer、stream、task、timer 或
        /// cancellation registration。Debug listener 的 flush 與 Dispose 由程序級 Program
        /// owner 負責；本 request-scoped data context 不得擁有或釋放該 listener。
        /// <see cref="System.Diagnostics.ConditionalAttribute"/> 讓 Release 編譯器連同呼叫點的
        /// interpolated-string 參數評估一起移除，而不是只執行一個空方法；因此正式組態不會
        /// 為已編譯移除的 Session 診斷付出字串配置或 GC 成本。
        /// </para>
        /// </remarks>
        [System.Diagnostics.Conditional("DEBUG")]
        private static void WriteSessionDiagnostic(string message)
        {
#if DEBUG
            if (SessionDiagnosticsSwitch.Enabled)
            {
                System.Diagnostics.Debug.WriteLine(message);
            }
#endif
        }

        /// <summary>
        /// 嘗試從目前 HTTP request 即時取得 Session，建立只屬於該 Session 的快取 key。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 此方法不得使用建構時快取的 HttpContext 或 Session；每次存取均經由
        /// <see cref="CurrentSession"/> 取得當前 request，避免 scoped data context 因持有前一位
        /// 使用者的 Session 而跨 request 或跨使用者讀寫快取。產出的 key 組合 Session ID、已驗證的
        /// session bound user、請求指紋與 session-created timestamp，維持既有隔離語意不變。
        /// </para>
        /// <para>
        /// 沒有 Session 時會回傳 <see langword="false"/>，而不是產生含有 Ticks 的一次性 key。
        /// 呼叫端必須改用 data context 實例欄位持有的後備物件，且不得呼叫程序級
        /// <see cref="IMemoryCache"/>。這個後備物件的最長生命週期由目前 Scoped context 決定，
        /// scope 結束即失去唯一持有者，因此不會跨 request、使用者、profile 或 tenant 留存。
        /// </para>
        /// <para>
        /// 只有取得 Session 時才會寫入 Session timestamp 並組成 key。關閉診斷開關不會略過
        /// Session timestamp 寫入、例外傳播或 key 組成；也不會新增任何 cache、stream、listener
        /// 或背景工作。因診斷文字可包含 Session GUID、BoundUserId 與 key 片段，預設不能輸出。
        /// </para>
        /// </remarks>
        /// <param name="key">成功時為完整且已隔離的 Session 快取 key；失敗時為 <see langword="null"/>，禁止用於程序級快取。</param>
        /// <returns>目前是否有可安全用於程序級快取的 Session 隔離邊界。</returns>
        private bool TryGetSessionCacheKey(out string key)
        {
            WriteSessionDiagnostic("[TryGetSessionCacheKey] 🔵 進入方法");

            var session = CurrentSession;
            WriteSessionDiagnostic($"[TryGetSessionCacheKey] 📌 CurrentSession 是否為 null: {session == null}");

            if (session == null)
            {
                // 背景工作、非 HTTP 執行緒及除錯評估都可能沒有 Session。過去以 Ticks 產生唯一 key
                // 會使每次存取都寫入一筆無法命中的 30 分鐘快取，造成無界保留；此處刻意失敗關閉。
                // 呼叫端改以本 Scoped context 的欄位保存後備物件，scope 結束時自然回收，不會跨使用者共用。
                key = null;
                WriteSessionDiagnostic("[TryGetSessionCacheKey] ⚠️ CurrentSession 為 null，已改用實例層級後備物件，未寫入行程快取");
                return false;
            }

            var sessionId = session.Id;
            WriteSessionDiagnostic($"[TryGetSessionCacheKey] 📋 Session ID: {sessionId}");

            var boundUserId = session.GetString("_SessionRegeneratedFor") ?? string.Empty;
            WriteSessionDiagnostic($"[TryGetSessionCacheKey] 👤 BoundUserId: {(string.IsNullOrEmpty(boundUserId) ? "(empty)" : boundUserId)}");

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
            WriteSessionDiagnostic($"[TryGetSessionCacheKey] 🔐 StoredFingerprint 是否存在: {!string.IsNullOrEmpty(storedFingerprint)}");
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
            WriteSessionDiagnostic($"[TryGetSessionCacheKey] 🔐 CurrentRequestFingerprint (前16字): {(currentRequestFingerprint?.Substring(0, Math.Min(16, currentRequestFingerprint.Length)) ?? "(empty)")}...");
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
            WriteSessionDiagnostic($"[TryGetSessionCacheKey] ⏱️  SessionCreatedTime 是否存在: {!string.IsNullOrEmpty(sessionCreatedTime)}");
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
                WriteSessionDiagnostic($"[TryGetSessionCacheKey] 🆕 生成新的 SessionCreatedTime: {sessionCreatedTime}");

                try
                {
                    session.SetString("_SessionCreatedTime", sessionCreatedTime);
                    WriteSessionDiagnostic($"[TryGetSessionCacheKey] ✅ 首次存取，已初始化 Session 時間戳: {sessionCreatedTime}");
                }
                catch (Exception ex)
                {
                    WriteSessionDiagnostic($"[TryGetSessionCacheKey] ❌ 無法寫入 Session 時間戳 - Exception: {ex.GetType().Name}");
                    WriteSessionDiagnostic($"[TryGetSessionCacheKey] ❌ 異常詳情: {ex.Message}");
                    WriteSessionDiagnostic($"[TryGetSessionCacheKey] ❌ StackTrace: {ex.StackTrace}");
                    throw new InvalidOperationException(
                        "無法寫入 Session 時間戳，無法產生安全的快取 key。" +
                        "請確保 Session 中介軟體已正確配置且可寫入。", ex);
                }
            }
            else
            {
                WriteSessionDiagnostic($"[TryGetSessionCacheKey] ⏱️  使用既有的 SessionCreatedTime: {sessionCreatedTime}");
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
            WriteSessionDiagnostic($"[TryGetSessionCacheKey] 🏗️  開始構建快取 Key，初始值: {sessionId}");

            /// <summary>
            /// 如果有已綁定的使用者 ID，加入到 key 中
            ///
            /// 這確保已登入使用者的資料不會與其他使用者混淆，
            /// 即使他們有相同的 Session ID。
            /// </summary>
            if (!string.IsNullOrEmpty(boundUserId))
            {
                keyBuilder.Append('_').Append(boundUserId);
                WriteSessionDiagnostic($"[TryGetSessionCacheKey] 🏗️  已添加 BoundUserId: {boundUserId}");
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
                WriteSessionDiagnostic($"[TryGetSessionCacheKey] 🏗️  已添加短指紋: {shortFingerprint}");
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
                WriteSessionDiagnostic($"[TryGetSessionCacheKey] 🏗️  已添加時間戳: {shortTimestamp}");
            }

            /// <summary>
            /// 生成最終的快取 key
            ///
            /// 這個 key 現在包含了足夠的資訊來唯一識別一個使用者的 Session，
            /// 同時防止資料洩漏和碰撞。
            /// </summary>
            var finalKey = keyBuilder.ToString();
            WriteSessionDiagnostic($"[TryGetSessionCacheKey] ✅ 最終快取 Key: {finalKey}");
            // 補充說明：
            // - finalKey 的格式為：{SessionId}_{BoundUserId?}_{ShortFingerprint?}_{ShortTimestamp?}
            // - 這個 key 適用於記憶體快取（IMemoryCache）中作為索引。
            // - 請注意此 key 可能會包含特殊字元（來自 Base64），如果未來需要將其序列化到其他儲存或傳輸媒介，
            //   請先做安全字元處理。
            WriteSessionDiagnostic($"[TryGetSessionCacheKey] 🟢 方法返回，Key 長度: {finalKey.Length}");

            key = finalKey;
            return true;
        }

        /// <summary>
        /// 取得既有相容呼叫端使用的 Session key。
        /// </summary>
        /// <remarks>
        /// 新增的六個資料管理器 getter 必須先呼叫 <see cref="TryGetSessionCacheKey"/>，並在沒有
        /// Session 時完全避開 <see cref="IMemoryCache"/>。本包裝只保留給本階段範圍外的 legacy
        /// getter，以固定 <c>NOSESSION</c> 取代曾經每次不同的 Ticks key，避免既有呼叫端再產生
        /// 無界項目；它不是新程式碼可使用的 cache 授權。
        /// </remarks>
        /// <returns>有 Session 時的完整隔離 key；否則固定的相容字串 <c>NOSESSION</c>。</returns>
        private string GetCurrentSessionId()
        {
            return TryGetSessionCacheKey(out var key) ? key : "NOSESSION";
        }

        /// <summary>
        /// 依目前 HTTP request 的 forwarded IP 與 User-Agent 產生 Session 隔離用指紋。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 本方法只讀取當前 <see cref="IHttpContextAccessor.HttpContext"/>，不保存 IP、header、
        /// User-Agent、fingerprint 或任何 request 資料到 static/singleton 欄位。呼叫端只把雜湊結果
        /// 用於當前 Session 的快取 key，避免不同使用者在相同 Session ID 或代理環境下混用資料。
        /// </para>
        /// <para>
        /// X-Forwarded-For 與 User-Agent 的原始值屬敏感診斷資料；本次保留既有除錯能力但將所有輸出
        /// 交給預設關閉的 <see cref="WriteSessionDiagnostic"/>。IP 取得失敗仍沿用既有的 Unknown fallback，
        /// 外層例外仍回傳空字串，兩條行為路徑均沒有因診斷開關而改變。
        /// </para>
        /// </remarks>
        private string GenerateCurrentRequestFingerprint()
        {
            WriteSessionDiagnostic("[GenerateCurrentRequestFingerprint] 🔵 進入方法");

            try
            {
                var httpContext = m_ContextAccessor?.HttpContext;
                WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] 📌 HttpContext 是否為 null: {httpContext == null}");

                if (httpContext == null)
                {
                    WriteSessionDiagnostic("[GenerateCurrentRequestFingerprint] ⚠️  HttpContext 為 null，返回空字串");
                    return string.Empty;
                }

                var ip = "Unknown";
                try
                {
                    WriteSessionDiagnostic("[GenerateCurrentRequestFingerprint] 🌐 開始提取 IP 地址");

                    var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].ToString();
                    WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] 🌐 X-Forwarded-For Header: {(string.IsNullOrEmpty(forwardedFor) ? "(empty)" : forwardedFor)}");

                    if (!string.IsNullOrEmpty(forwardedFor))
                    {
                        var ips = forwardedFor.Split(',');
                        WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] 🌐 X-Forwarded-For IP 列表數量: {ips.Length}");

                        if (ips.Length > 0)
                        {
                            ip = ips[0].Trim();
                            WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] 🌐 使用 X-Forwarded-For 第一個 IP: {ip}");
                        }
                    }
                    else
                    {
                        ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                        WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] 🌐 使用 RemoteIpAddress: {ip}");
                    }
                }
                catch (Exception ipEx)
                {
                    WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] ⚠️  提取 IP 時發生異常: {ipEx.GetType().Name} - {ipEx.Message}");
                    WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] ⚠️  IP 預設為: Unknown");
                }

                var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
                WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] 🖥️  User-Agent (前50字): {(string.IsNullOrEmpty(userAgent) ? "(empty)" : userAgent.Substring(0, Math.Min(50, userAgent.Length)))}...");

                var input = $"{ip}|{userAgent}";
                WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] 🔐 指紋輸入: {input}");

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                    var fingerprint = Convert.ToBase64String(bytes);
                    WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] ✅ 生成的指紋: {fingerprint}");
                    WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] 🟢 方法返回成功");

                    return fingerprint;
                }
            }
            catch (Exception ex)
            {
                WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] ❌ 方法異常 - Exception 類型: {ex.GetType().Name}");
                WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] ❌ 異常訊息: {ex.Message}");
                WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] ❌ StackTrace: {ex.StackTrace}");
                WriteSessionDiagnostic($"[GenerateCurrentRequestFingerprint] ⚠️  返回空字串作為備用");

                return string.Empty;
            }
        }

        /// <summary>
        /// 在目前 Session 存在時設定 dirty flag，通知既有流程 Session 相關快取狀態已變更。
        /// </summary>
        /// <remarks>
        /// 此方法只透過 <see cref="CurrentSession"/> 取得當前 request 的 Session，絕不保留 Session
        /// 實體供下一個 request 使用。寫入失敗仍沿用既有「不拋出、讓主流程繼續」語意；本次只關閉
        /// 診斷副作用，沒有把失敗改為成功或改變 dirty 值。例外與 stack trace 可能含敏感內容，故預設
        /// 不寫入 Trace.log；需要除錯時必須由受信任的程序級開關明確啟用。
        /// </remarks>
        private void SetSessionDirtyFlag()
        {
            WriteSessionDiagnostic("[SetSessionDirtyFlag] 🔵 進入方法");

            var session = CurrentSession;
            WriteSessionDiagnostic($"[SetSessionDirtyFlag] 📌 CurrentSession 是否為 null: {session == null}");

            if (session != null)
            {
                try
                {
                    session.SetInt32("dirty", 1);
                    WriteSessionDiagnostic("[SetSessionDirtyFlag] ✅ 已成功設定 dirty flag = 1");
                    WriteSessionDiagnostic("[SetSessionDirtyFlag] 🟢 方法完成");
                }
                catch (Exception ex)
                {
                    WriteSessionDiagnostic($"[SetSessionDirtyFlag] ❌ 設定 dirty flag 時發生異常");
                    WriteSessionDiagnostic($"[SetSessionDirtyFlag] ❌ Exception 類型: {ex.GetType().Name}");
                    WriteSessionDiagnostic($"[SetSessionDirtyFlag] ❌ 異常訊息: {ex.Message}");
                    WriteSessionDiagnostic($"[SetSessionDirtyFlag] ❌ StackTrace: {ex.StackTrace}");

                    // 不拋出異常，只記錄警告
                    WriteSessionDiagnostic("[SetSessionDirtyFlag] ⚠️  由於異常，dirty flag 設定可能失敗");
                }
            }
            else
            {
                WriteSessionDiagnostic("[SetSessionDirtyFlag] ⚠️  CurrentSession 為 null，無法設定 dirty flag");
                WriteSessionDiagnostic("[SetSessionDirtyFlag] ⚠️  方法返回（不設定任何值）");
            }
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 建構小組資料的 request-scoped 記憶體上下文，保存服務依賴但不捕獲任何當前 Session。
        ///
        /// 注入必要的依賴項：
        /// - IHttpContextAccessor: 用於安全存取 HTTP 上下文和 Session
        /// - IMemoryCache: 用於快取資料管理器實例
        /// - IDonationPaymentCreateGatewayAdapter: ChurchReport 奉獻付款建單 adapter
        /// - IToolUtilityProvider: ToolUtility 提供者
        ///
        /// 注意：不再在建構時捕獲 Session，以避免 Session Bleeding
        /// </summary>
        /// <remarks>
        /// <para>
        /// <paramref name="contextAccessor"/> 可以是 singleton-safe accessor，但此型別只能在實際操作時
        /// 讀取其目前 HttpContext；不可在建構式存下 HttpContext、ISession、ClaimsPrincipal 或任何使用者
        /// 資料。這是避免 Session Leakage 的所有權邊界。快取 key 的隔離邏輯由
        /// <see cref="TryGetSessionCacheKey"/> 在每次存取時重新建立。
        /// </para>
        /// <para>
        /// 建構完成診斷本次會走預設關閉的 <see cref="WriteSessionDiagnostic"/>，所以一般請求不會因
        /// 建構 data context 而同步寫檔。開關不影響任何相依性指派、資源 Dispose 或產品資料流程。
        /// </para>
        /// </remarks>
        /// <param name="contextAccessor">可安全取得當前 request 的 HTTP 上下文 accessor；不可直接保存其 Session。</param>
        /// <param name="memoryCache">由 DI 管理的程序級快取；所有 key 必須保留完整 Session 隔離邊界。</param>
        /// <param name="toolUtilityProvider">由目前 scope 使用的 ToolUtility 提供者，不得提升為 singleton。</param>
        /// <param name="donationPaymentCreateGatewayAdapter">奉獻付款建立邊界；可為 null 以保持既有相容呼叫端。</param>
        /// <param name="lineNotificationWorkflow">可選的 LINE push workflow，不保存 reply token 或使用者身份。</param>
        /// <param name="lineReplyWorkflow">可選的 LINE reply workflow，僅由目前產品流程使用。</param>
        public InMemoryDataContextSmallGroup(
            IHttpContextAccessor contextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            IDonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter = null,
            ILineNotificationWorkflow? lineNotificationWorkflow = null,
            ILineReplyWorkflow? lineReplyWorkflow = null)
        {
            _memoryCache = memoryCache;

            // ========================================
            // ✅ Session Bleeding 修復：只保存 IHttpContextAccessor 參考
            // 不再在建構時捕獲 HttpContext 或 Session
            // 每次需要時透過 CurrentSession 屬性取得當前的 Session
            // ========================================
            m_ContextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));

            m_DonationPaymentCreateGatewayAdapter = donationPaymentCreateGatewayAdapter;
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
            _lineNotificationWorkflow = lineNotificationWorkflow;
            _lineReplyWorkflow = lineReplyWorkflow;

            WriteSessionDiagnostic("[InMemoryDataContext] ✅ 建構完成（Session Bleeding 修復版本）");
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
        /// <remarks>
        /// 有 Session 時才可使用程序級快取，完整 key 保留 Session、使用者、指紋與建立時間的隔離邊界。
        /// 無 Session 時回傳由目前 Scoped data context 唯一持有的後備物件，絕不寫入 <see cref="IMemoryCache"/>；
        /// 該物件最晚在 scope 結束後失去持有者，因此不會跨 request 或使用者留存。
        /// </remarks>
        public ListManager ListManager
        {
            get
            {
                // 無 Session 時不具備可安全分割程序級快取的隔離邊界；後備物件只由目前 Scoped
                // data context 持有，scope 結束即釋放，不會把背景或除錯狀態留給下一個 request。
                if (!TryGetSessionCacheKey(out var sessionKey))
                {
                    return m_ListManager ??= new ListManager();
                }

                var key = sessionKey + "_ListManager";

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
                String ContactIdString = ToolUtilityClass.RetrieveContactByAccountNumber(Account, Password);

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
        /// <remarks>
        /// 背景或除錯執行緒沒有 Session 時不可推定程序級快取的隔離鍵。此情況僅使用目前 data context 的
        /// 實例欄位，scope 是唯一的生命週期 owner，確保可變小組資料不會跨 request、profile 或 tenant 外洩。
        /// </remarks>
        public SmallGroupDataList SmallGroupDataList
        {
            get
            {
                // 沒有 Session 時禁止碰 IMemoryCache；此欄位是目前 context 的唯一後備 owner，
                // 因此不會跨 request、使用者、profile 或 tenant 殘留可變資料。
                if (!TryGetSessionCacheKey(out var sessionKey))
                {
                    return m_SmallGroupDataList ??= new SmallGroupDataList();
                }

                var key = sessionKey + "_SmallGroupDataList";

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
        /// <remarks>
        /// 無 Session 的週報資料屬暫時背景狀態，不能以一次性 key 提升為程序級物件圖。後備實例由目前
        /// Scoped context 持有至 scope 釋放為止，且不會寫入或讀取其他 request 的快取項目。
        /// </remarks>
        public WeeklyReportData WeeklyReportData
        {
            get
            {
                // 背景／非 HTTP 路徑沒有可驗證的 Session 隔離，改用實例欄位後備物件，
                // 避免以每次唯一 key 寫入 30 分鐘的程序級快取。
                if (!TryGetSessionCacheKey(out var sessionKey))
                {
                    return m_WeeklyReportData ??= new WeeklyReportData();
                }

                var key = sessionKey + "_WeeklyReportData";

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
        /// <remarks>
        /// 此 getter 只有驗證到 Session 隔離邊界時才使用 <see cref="IMemoryCache"/>。沒有 Session 時，
        /// 後備模型的最長生命週期限制在目前 Scoped context，避免背景處理延長新人資料或身份相關狀態的保留。
        /// </remarks>
        public NewPersonModel NewPersonModel
        {
            get
            {
                // 後備物件的生命週期不超過目前 Scoped context；無 Session 時絕不寫入
                // 程序級 cache，避免背景執行緒把資料帶到其他使用者。
                if (!TryGetSessionCacheKey(out var sessionKey))
                {
                    return m_NewPersonModel ??= new NewPersonModel();
                }

                var key = sessionKey + "_NewPersonModel";

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
        /// <remarks>
        /// 個人資料模型不可因缺少 Session 而共享程序級後備 key。無 Session 路徑只使用目前 context 的欄位，
        /// scope 結束即終止其可達性，防止個人資料在下一個 request、使用者或 tenant 重新出現。
        /// </remarks>
        public PersonalInfomationModel PersonalInfomationModel
        {
            get
            {
                // 沒有 Session 時以 context-local 後備欄位取代 cache；scope disposal 是唯一
                // 清理路徑，故不會產生跨 request 的 mutable profile 狀態。
                if (!TryGetSessionCacheKey(out var sessionKey))
                {
                    return m_PersonalInfomationModel ??= new PersonalInfomationModel();
                }

                var key = sessionKey + "_PersonalInfomationModel";

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
        /// <remarks>
        /// 沒有 Session 時仍以目前 scope 注入的 <see cref="IToolUtilityProvider"/> 建立後備實例，但不寫入
        /// <see cref="IMemoryCache"/>。provider 與後備資料同由目前 scope 擁有並在 scope 結束時釋放，避免
        /// 快取保存跨 request 的服務參考或使用者特定可變狀態。
        /// </remarks>
        public HappyGroupDataManager HappyGroupDataManager
        {
            get
            {
                // HappyGroupDataManager 仍必須由目前 scope 的 provider 建構；只有在沒有 Session
                // 時略過程序級 cache，保留 provider 的資源所有權與跨使用者隔離。
                if (!TryGetSessionCacheKey(out var sessionKey))
                {
                    return m_HappyGroupDataManager ??= new HappyGroupDataManager(_toolUtilityProvider);
                }

                var key = sessionKey + "_HappyGroupDataManager";

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

        #region 奉獻付款處理區

        /// <summary>
        /// ChurchReport 奉獻付款管理器屬性。
        ///
        /// 使用記憶體快取管理 DonationPaymentManager 實例，
        /// 快取鍵為 Session ID + "_DonationPaymentManager"，
        /// 快取過期：絕對 30 分鐘，滑動 30 分鐘。
        ///
        /// 若快取不存在，則注入中性的付款建單 adapter 建立新實例並設定快取選項。
        /// </summary>
        public DonationPaymentManager DonationPaymentManager
        {
            get
            {
                var key = GetCurrentSessionId() + "_DonationPaymentManager";

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

                    m_DonationPaymentManager = new DonationPaymentManager(
                        m_DonationPaymentCreateGatewayAdapter,
                        _lineNotificationWorkflow,
                        _lineReplyWorkflow);
                    _memoryCache.Set<DonationPaymentManager>(key, m_DonationPaymentManager, options);

                    SetSessionDirtyFlag();
                }

                return _memoryCache.Get<DonationPaymentManager>(key);
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
        /// ToolUtilityClass 屬性。
        ///
        /// 本 Run 維持既有 Factory 單例行為，但不再以 Session ID 為鍵放入程序級
        /// IMemoryCache。資料上下文不持有或釋放 ToolUtility；目前實例的最長生命週期
        /// 仍由 Factory 管理。Run 2 改為 Scoped 後，這裡沒有快取持有者可讓已釋放的
        /// CRM 連線跨 request 或跨使用者重用。
        /// </summary>
        public ToolUtilityClass ToolUtilityClass
        {
            get => ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
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
