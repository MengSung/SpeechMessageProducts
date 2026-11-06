/*
 * 檔案整體說明
 *
 * 目的與定位：Factory 負責建立、管理及防護管理 ToolUtilityClass 的實例 遵守 SOLID 的單一職責原則 (Single Responsibility Principle)
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityFactory。
 * - 設定程序級的追蹤資源擁有者。必須在第一次 GetInstance() 之前呼叫。
 * - 設定 legacy 單例使用的 ambient Dataverse 操作代理。必須在第一次 GetInstance() 前由組合根呼叫一次。
 * - 將 legacy Factory 的 CRM 解析暫時綁定到呼叫端已建立的背景 DI scope。
 * - 獲得 legacy 程序級單一實例（Thread-Safe Double-Check Locking）。
 * 主要操作入口包含 SetConfiguration、SetTracer、SetAmbientService、BeginBackgroundScope、GetInstance、ResetInstance。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs、
 * ChurchReport.MemberInfo.Tests/Models/LegacyToolUtilityFactoryCollection.cs、
 * ChurchReport.MemberInfo.Tests/Models/ListManagerIntegratePublicationTests.cs、
 * ChurchReport.MemberInfo.Tests/Models/SmallGroupDataListSnapshotIsolationTests.cs、
 * ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs 追查目前工作樹中的合作／使用位置。
 */
using Microsoft.Extensions.Configuration;
using System;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace ToolUtilityNameSpace.Factory
{
    /// <summary>
    /// Factory 負責建立、管理及防護管理 ToolUtilityClass 的實例
    /// 遵守 SOLID 的單一職責原則 (Single Responsibility Principle)
    /// </summary>
    /// <remarks>
    /// 本工廠保留 legacy 程序級單例 API，但只保存不含 client、lease、scope 或 request
    /// 身分的 <see cref="AmbientGatewayOrganizationService"/>。每次 CRM 操作才解析目前
    /// request 的 Gateway；背景操作建立短命 scope 並立即釋放，因此單例不會跨 request
    /// 共享可變 Dataverse 狀態。待 session cache 持有者完成重構後可移除此過渡路徑。
    /// </remarks>
    public sealed class ToolUtilityFactory
    {
        private static readonly object _lock = new object();
        private static ToolUtilityClass _instance;
        private static volatile bool _isInitialized = false;
        private static IConfiguration _configuration;
        private static AmbientGatewayOrganizationService _ambientService;

        /// <summary>
        /// 程序級的追蹤資源擁有者，由組合根於啟動時設定一次。
        /// </summary>
        /// <remarks>
        /// 追蹤資源（FileStream / TraceListener）的生命週期等同整個 Worker Process，
        /// 因此由 DI 以 Singleton 建立後注入此處，而非由每個 ToolUtilityClass 自行建立。
        /// 本欄位僅供本工廠建構 ToolUtilityClass 時傳遞使用，不對外公開。
        /// </remarks>
        private static IToolUtilityTracer _tracer;

        // 私有建構函式防止外部建立實例
        private ToolUtilityFactory()
        {
        }

        /// <summary>
        /// 設定 IConfiguration 實例
        /// </summary>
        /// <param name="configuration">配置物件</param>
        public static void SetConfiguration(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// 設定程序級的追蹤資源擁有者。必須在第一次 <see cref="GetInstance()"/> 之前呼叫。
        /// </summary>
        /// <param name="tracer">由 DI 容器以 Singleton 建立並負責釋放的追蹤器。</param>
        /// <exception cref="ArgumentNullException">
        /// 未提供追蹤器時擲回；若允許為 null，將建立出無法輸出診斷的實例，
        /// 且錯誤要到執行期第一次追蹤時才會顯現，因此在此採取快速失敗。
        /// </exception>
        /// <remarks>
        /// 追蹤資源（FileStream、TraceListener）的生命週期等同整個 Worker Process，
        /// 不隨 ToolUtilityClass 的建立或釋放而變動。本方法只保存參照，不接管其釋放責任 ——
        /// 釋放由建立它的 DI 容器於應用程式關閉時負責。
        /// </remarks>
        public static void SetTracer(IToolUtilityTracer tracer)
        {
            _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
        }

        /// <summary>
        /// 設定 legacy 單例使用的 ambient Dataverse 操作代理。必須在第一次
        /// <see cref="GetInstance()"/> 前由組合根呼叫一次。
        /// </summary>
        /// <param name="ambientService">
        /// 不保存 request scope 或 raw client 的操作代理；它會在每次呼叫時解析目前 scope，
        /// 並在無 HTTP request 的背景工作建立與釋放短命 scope。
        /// </param>
        /// <exception cref="ArgumentNullException">未提供代理時擲回，避免單例回退為自行建立 raw client。</exception>
        /// <remarks>
        /// Factory 可以安全保存此代理，因為代理本身不保存 HttpContext、RequestServices、lease 或 client。
        /// 實際資源所有權仍完全由 Scoped Gateway 與 Singleton pool 管理，防止跨 request／使用者重用。
        /// </remarks>
        public static void SetAmbientService(AmbientGatewayOrganizationService ambientService)
        {
            _ambientService = ambientService ?? throw new ArgumentNullException(nameof(ambientService));
        }

        /// <summary>
        /// 將 legacy Factory 的 CRM 解析暫時綁定到呼叫端已建立的背景 DI scope。
        /// </summary>
        /// <param name="serviceProvider">
        /// 背景工作唯一擁有的 scope provider；必須在該 scope Dispose 前呼叫回傳值的
        /// <see cref="IDisposable.Dispose"/>，本方法不保存或釋放這個 provider。
        /// </param>
        /// <returns>離開時還原前一個 ambient 解析來源的可釋放範圍。</returns>
        /// <exception cref="InvalidOperationException">
        /// ambient gateway 尚未由組合根設定時擲回，避免背景工作無聲回退到不明的 request scope。
        /// </exception>
        /// <remarks>
        /// <para>
        /// Task.Run 會流動 ExecutionContext，因而可能帶入已結束 request 的 HttpContextAccessor。
        /// 此明確覆蓋必須包住整個背景 CRM 工作，讓 legacy <see cref="ToolUtilityClass"/> 一律從
        /// 新建 scope 解析 IOrganizationService。這既保留 DataverseTrace 的 AsyncLocal 關聯，
        /// 也阻止 request scope、Session 或 lease 被背景工作重新使用。
        /// </para>
        /// <para>
        /// 回傳 scope 只保有目前非同步流程的短期 provider 參考；using 結束後它會確定還原，
        /// 不會把背景 scope 提升成 Factory 靜態狀態或留給其他使用者／租戶。
        /// </para>
        /// </remarks>
        public static IDisposable BeginBackgroundScope(IServiceProvider serviceProvider)
        {
            if (_ambientService == null)
            {
                throw new InvalidOperationException("Ambient Dataverse 代理尚未設定，無法建立背景 scope 覆蓋。");
            }

            return _ambientService.BeginBackgroundScope(serviceProvider);
        }

        /// <summary>
        /// 獲得 legacy 程序級單一實例（Thread-Safe Double-Check Locking）。
        /// </summary>
        /// <returns>ToolUtilityClass 實例</returns>
        public static ToolUtilityClass GetInstance()
        {
            if (_configuration == null)
            {
                throw new InvalidOperationException("配置尚未設定。請先調用 SetConfiguration() 方法。");
            }

            if (_tracer == null)
            {
                throw new InvalidOperationException("追蹤器尚未設定。請先調用 SetTracer() 方法。");
            }

            if (_ambientService == null)
            {
                throw new InvalidOperationException("Ambient Dataverse 代理尚未設定。請先調用 SetAmbientService() 方法。");
            }

            if (!_isInitialized)
            {
                lock (_lock)
                {
                    if (!_isInitialized)
                    {
                        _instance = new ToolUtilityClass(_ambientService, _tracer, _configuration);
                        _isInitialized = true;
                    }
                }
            }
            return _instance;
        }

        /// <summary>
        /// 獲得 legacy 程序級單一實例，保留指定 DiscoveryServiceType 的既有簽章。
        /// DiscoveryServiceType 已不再用於自建連線；所有操作均透過 ambient gateway 執行。
        /// </summary>
        /// <param name="discoveryServiceType">服務類型</param>
        /// <returns>ToolUtilityClass 實例</returns>
        public static ToolUtilityClass GetInstance(string discoveryServiceType)
        {
            if (_configuration == null)
            {
                throw new InvalidOperationException("配置尚未設定。請先調用 SetConfiguration() 方法。");
            }

            if (_tracer == null)
            {
                throw new InvalidOperationException("追蹤器尚未設定。請先調用 SetTracer() 方法。");
            }

            if (_ambientService == null)
            {
                throw new InvalidOperationException("Ambient Dataverse 代理尚未設定。請先調用 SetAmbientService() 方法。");
            }

            if (!_isInitialized)
            {
                lock (_lock)
                {
                    if (!_isInitialized)
                    {
                        _instance = new ToolUtilityClass(_ambientService, _tracer, _configuration);
                        _isInitialized = true;
                    }
                }
            }
            return _instance;
        }

        /// <summary>
        /// 重設實例 (僅供測試使用，生產環境不建議呼叫)。
        /// 單例只釋放自身與已建立的 Facade；不釋放 ambient gateway、scope、pool 或 client，
        /// 因為那些資源都由其建立者的 DI 生命週期負責。
        /// </summary>
        internal static void ResetInstance()
        {
            lock (_lock)
            {
                if (_instance != null)
                {
                    _instance.Dispose();
                    _instance = null;
                }
                _isInitialized = false;
            }
        }

        /// <summary>
        /// 檢查是否已經初始化
        /// </summary>
        public static bool IsInitialized => _isInitialized;
    }
}
