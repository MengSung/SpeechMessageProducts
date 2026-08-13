// ============================================================================
// 檔案：ChurchReport/Services/DonationDynamicsAccessBootstrap.cs
// 目的：繫結產品可見的 Dynamics mode／ProfileAlias，並分別組成 Gateway fee-read 與 P4 Embedded WhoAmI executor。
//
// 安全與生命週期契約：
// 1. Package01FeeReadsEnabled=false 時保留既有 ToolUtility/Data8 業務路徑，且不建立 executor、provider 或 HTTP 資源。
// 2. Embedded 不讀取 Gateway endpoint；Package01 功能旗標維持 false 時，只有 host startup 的一次受控 WhoAmI
//    會建立 P4 composition root，既有收費清單仍不會切換或建立第二條業務 connector 路徑。
// 3. ProfileAlias 與 Gateway endpoint 只能由 DynamicsAccess 取得；不得由 CrmConnection、CRM URL 或 credential 推導。
// 4. 唯一 process host 擁有 Gateway 或 Embedded 的單一 ServiceProvider generation，host stop/DI disposal 以同一
//    terminal cleanup path 釋放資源；兩種 mode 不可在同一 host generation 混用。
// 5. static facade 只保存受主 DI lifecycle 管理的 host 參考，絕不保存 provider、session、credential 或 token。
// ============================================================================

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Connectors.Data8;
using SpeechMessage.Dynamics.ControlPlane.Guard;
using SpeechMessage.Dynamics.Embedded.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.Authentication;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Gateway;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using SpeechMessage.Dynamics.ProductClient.SpecialResources;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// DynamicsAccess 的舊呼叫點相容 facade 與純設定綁定入口。
    /// 此 static 類別不擁有 ServiceProvider、HttpClient、handler、timer 或 executor；真正的 process generation
    /// 由 ChurchReport 主 DI 註冊的 <see cref="IDonationDynamicsAccessProcessHost"/> singleton 唯一持有。
    /// hosted lifecycle 只在網站啟動期間發佈該 singleton 的非 owner 參考，讓尚未完成 DI 遷移的 manager／service
    /// 保持編譯相容；網站停止時先撤銷 facade，再由 singleton 完成確定性 Dispose，避免跨 host 世代洩漏。
    /// </summary>
    public static class DonationDynamicsAccessBootstrap
    {
        // 這個欄位只是舊 static callsite 的過渡路由，不是 provider／executor owner；它只在 hosted lifecycle
        // Start 與 Stop 間存在。使用 Interlocked 發佈可避免多 host／多測試競爭覆蓋彼此的 process generation。
        private static IDonationDynamicsAccessProcessHost? _compatibilityProcessHost;

        /// <summary>
        /// 建立奉獻收費表單服務；Package01 啟用時目前仍只走已完成 lifecycle 的 Gateway executor。
        /// </summary>
        public static DonationDedicationFeeFormService CreateFeeFormService(
            ToolUtilityClass utility,
            IConfiguration configuration,
            IPackage01FeeReadClient? injectedFeeReadClient = null,
            IOptions<ProductDynamicsOptions>? injectedOptions = null,
            LegacyToolUtilityDrainController? legacyDrainController = null)
        {
            ArgumentNullException.ThrowIfNull(utility);
            ArgumentNullException.ThrowIfNull(configuration);

            var enabled = IsPackage01Enabled(configuration);
            if (!enabled)
            {
                return new DonationDedicationFeeFormService(
                    utility,
                    package01FeeReadClient: null,
                    dynamicsAccess: null,
                    package01FeeReadsEnabled: false,
                    legacyDrainController);
            }

            // 已由 DI 注入完成時（測試/正式 DI 路徑），直接使用。
            if (injectedFeeReadClient is not null)
            {
                var options = injectedOptions ?? Options.Create(BindOptions(configuration));
                return new DonationDedicationFeeFormService(
                    utility,
                    injectedFeeReadClient,
                    options,
                    package01FeeReadsEnabled: true,
                    legacyDrainController);
            }

            var productOptions = BindOptions(configuration);
            EnsureGatewayOnly(productOptions);
            var processHost = GetStartedProcessHost();
            var executor = CreateGatewayExecutor(productOptions, processHost);

            IPackage01FeeReadClient feeReadClient = new Package01FeeReadClient(
                executor,
                NullLogger<Package01FeeReadClient>.Instance);

            return new DonationDedicationFeeFormService(
                utility,
                feeReadClient,
                Options.Create(productOptions),
                package01FeeReadsEnabled: true,
                legacyDrainController);
        }

        /// <summary>
        /// 嘗試建立 Package 1 client（已完成的 Gateway 路徑）。
        /// 若未啟用或設定不完整，回傳 null，呼叫端應走舊路徑。
        /// </summary>
        public static IPackage01FeeReadClient? TryCreatePackage01Client(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage01Enabled(configuration))
            {
                return null;
            }

            var productOptions = BindOptions(configuration);
            EnsureGatewayOnly(productOptions);
            var processHost = GetStartedProcessHost();
            var executor = CreateGatewayExecutor(productOptions, processHost);

            return new Package01FeeReadClient(
                executor,
                NullLogger<Package01FeeReadClient>.Instance);
        }

        public static bool IsPackage01Enabled(IConfiguration configuration)
        {
            var raw = configuration["DynamicsAccess:Package01FeeReadsEnabled"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 讀取 P7.4 fee-editor 唯讀 endpoint 的獨立 deployment-owned gate。此 capability 必須同時通過
        /// Package01 基礎 gate 與自己的 gate；任一缺失或 false 都在 controller 解析 browser locator、
        /// 讀取 session lesson snapshot、建立 ProductClient、process host、HTTP handler 或 Data8 pool 前
        /// 回傳 false。這使 rollback 可只關閉 fee-editor read，不會意外改變其他 Package01 consumer。
        /// </summary>
        /// <param name="configuration">只允許 deployment configuration；不得由 HTTP、Session 或 browser 值替代。</param>
        /// <returns>兩個明確 gate 都啟用時為 true；預設與缺值一律為 false。</returns>
        public static bool IsPackage01FeeEditorReadEnabled(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage01Enabled(configuration))
            {
                return false;
            }

            var raw = configuration["DynamicsAccess:Package01FeeEditorReadEnabled"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 判斷 P7.4 認獻單唯讀 ProductClient 是否可由 deployment 啟用。
        /// 此 sub-gate 必須依賴 Package01 基礎 gate；任一設定缺漏或為 false 時一律 fail closed，讓
        /// 舊有同步 ToolUtility 流程維持原狀，並確保 controller、Session、browser 或呼叫端資料無法
        /// 將自己提升為 Dynamics routing authority。此方法僅讀取組態字串，不建立 options、host、
        /// executor、HTTP handler、Data8 pool、credential graph、timer 或任何 outbound request。
        /// </summary>
        /// <param name="configuration">只允許 deployment-owned 組態；不得由 HTTP、Session、cookie 或表單值取代。</param>
        /// <returns>基礎與認獻單 sub-gate 都明確啟用時為 <see langword="true"/>；其他情況皆為 <see langword="false"/>。</returns>
        public static bool IsPackage01DedicationBookingReadEnabled(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage01Enabled(configuration))
            {
                return false;
            }

            var raw = configuration["DynamicsAccess:Package01DedicationBookingReadEnabled"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 依 deployment-owned gate 建立 P7.4 認獻單唯讀 typed client。
        /// gate=false 時在任何 options bind、ProcessHost 解析或 transport composition 前回傳
        /// <see langword="null"/>，因此 disabled state 不會配置 ServiceProvider、connection pool、
        /// socket、handler、token 或其他長生命週期資源。gate=true 時先驗證 deployment ProfileAlias，
        /// 即使 client 由 DI 注入也不得跳過此 profile/generation isolation boundary；注入 facade 只用於
        /// 已受 DI 控管的組態與測試，不能接受 caller 指定 endpoint、credential、connector 或 owner。
        /// 若需要正式 transport，唯一 resource owner 是既有 process host；本 helper 不建立 static provider，
        /// 也不擁有或 Dispose shared client，host 停止時仍由既有 lifecycle 統一 drain/dispose。
        /// </summary>
        /// <param name="configuration">deployment-owned DynamicsAccess 組態。</param>
        /// <param name="injectedClient">可選的 DI/測試 typed facade；不攜帶或覆寫 profile、endpoint、credential 或 request state。</param>
        /// <returns>未啟用時為 <see langword="null"/>；已啟用時為使用既有 host generation 的 stateless typed client。</returns>
        public static IPackage01DedicationBookingReadClient? TryCreatePackage01DedicationBookingReadClient(
            IConfiguration configuration,
            IPackage01DedicationBookingReadClient? injectedClient = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage01DedicationBookingReadEnabled(configuration))
            {
                return null;
            }

            var productOptions = BindOptions(configuration);
            EnsureNonEmptyProductProfile(productOptions, "Package01 dedication booking read");
            if (injectedClient is not null)
            {
                return injectedClient;
            }

            var processHost = GetStartedProcessHost();
            var executor = productOptions.ConnectionMode switch
            {
                ConnectionMode.Embedded => processHost.GetOrCreateEmbeddedExecutor(productOptions, configuration),
                ConnectionMode.DedicatedGateway or ConnectionMode.CentralGateway =>
                    processHost.GetOrCreateGatewayExecutor(productOptions),
                _ => throw new InvalidOperationException(
                    "Package01 dedication booking read requires a supported Dynamics connection mode.")
            };

            return new Package01DedicationBookingReadClient(
                executor,
                NullLogger<Package01DedicationBookingReadClient>.Instance);
        }

        /// <summary>
        /// 判斷認證聯絡人唯讀 capability 是否已由 deployment 明確開啟。
        /// 此開關獨立於 Package01／Package02，且缺值、空白或任何非 true／1 值皆為 false；方法只讀取設定字串，
        /// 不繫結 options、不解析 ProfileAlias、不取得 process host、不建立 ProductClient、HTTP handler、Data8 pool
        /// 或 credential graph。因此 rollback 只需關閉本開關，並能在登入、Session、claims 或任何 CE I/O 前確定停止。
        /// </summary>
        /// <param name="configuration">唯一可提供 deployment-owned gate 的設定；不得以 request、Session 或 browser 值取代。</param>
        /// <returns>只有明確 true／1 時為 <see langword="true"/>；其餘情況一律 fail closed。</returns>
        public static bool IsAuthenticationContactReadEnabled(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            var raw = configuration["DynamicsAccess:AuthenticationContactReadEnabled"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 依獨立 deployment gate 建立認證聯絡人唯讀 typed client。
        /// false gate 是第一個可執行決策，故不會碰觸 options、profile、process host、client 或 transport；true gate
        /// 則先驗證 deployment-owned ProfileAlias，即使使用 DI／測試注入 facade 也不能省略 profile/generation
        /// isolation boundary。方法不接入 AuthenticationController、不建立 legacy fallback，亦不擁有或 Dispose
        /// injected facade；可重用 executor 的唯一 owner 仍是既有 process host，停止時由其統一 drain/dispose。
        /// </summary>
        /// <param name="configuration">只含 deployment-owned gate 與 DynamicsAccess 設定的來源。</param>
        /// <param name="injectedClient">可選的已由 DI 或測試擁有的無狀態 facade；不得攜帶 request routing 或秘密。</param>
        /// <returns>gate=false 時為 null；gate=true 時為固定 deployment profile 所組成的 typed client。</returns>
        public static IAuthenticationContactReadClient? TryCreateAuthenticationContactReadClient(
            IConfiguration configuration,
            IAuthenticationContactReadClient? injectedClient = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsAuthenticationContactReadEnabled(configuration))
            {
                return null;
            }

            var productOptions = BindOptions(configuration);
            EnsureNonEmptyProductProfile(productOptions, "Authentication contact read");
            if (injectedClient is not null)
            {
                return injectedClient;
            }

            return new AuthenticationContactReadClient(
                CreateAuthenticationContactReadExecutor(productOptions, configuration),
                NullLogger<AuthenticationContactReadClient>.Instance);
        }

        /// <summary>
        /// 以已驗證的 deployment options 取得認證唯讀 capability 共用的 executor。
        /// 此 helper 只在 gate 已通過且 ProfileAlias 已驗證後呼叫；Embedded 與 Gateway 都重用 process host 的唯一
        /// generation，故不會為單次 login lookup 建立第二個 provider、handler、pool、credential graph 或長生命週期
        /// session。連線模式不是 caller input；未知模式立即 fail closed，且沒有 legacy 或另一 transport fallback。
        /// </summary>
        /// <param name="productOptions">已從 deployment configuration 繫結並完成非空 profile 驗證的產品設定。</param>
        /// <param name="configuration">只有 Embedded composition 必需的既有 deployment 設定來源。</param>
        /// <returns>由 process host 唯一擁有且不可由呼叫端 Dispose 的 operation executor。</returns>
        private static IDynamicsOperationExecutor CreateAuthenticationContactReadExecutor(
            ProductDynamicsOptions productOptions,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(productOptions);
            ArgumentNullException.ThrowIfNull(configuration);
            var processHost = GetStartedProcessHost();
            return productOptions.ConnectionMode switch
            {
                ConnectionMode.Embedded => processHost.GetOrCreateEmbeddedExecutor(productOptions, configuration),
                ConnectionMode.DedicatedGateway or ConnectionMode.CentralGateway =>
                    processHost.GetOrCreateGatewayExecutor(productOptions),
                _ => throw new InvalidOperationException(
                    "Authentication contact read requires a supported Dynamics connection mode.")
            };
        }

        /// <summary>
        /// 讀取 P7.2 contact basic-info 的獨立 consumer flag。預設為 false，故在 P7.4 之前不會建立
        /// ProductClient、provider、HTTP handler、Data8 pool 或任何 ChurchReport 寫入流量；此 flag 與
        /// Package01FeeReadsEnabled 分離，避免讀取與寫入能力意外形成同一個 rollout 邊界。
        /// </summary>
        public static bool IsPackage02ContactBasicInfoUpdatesEnabled(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            var raw = configuration["DynamicsAccess:Package02ContactBasicInfoUpdatesEnabled"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 嘗試建立 P7.2 contact basic-info typed client。flag=false 時在解析 process host 前立即回傳 null；
        /// flag=true 時借用既有主 DI process host 的單一 Gateway 或 Embedded executor，絕不另建 provider、
        /// handler、Data8 pool 或 credential graph。此 helper 只提供尚未接入 controller 的 composition 支援，
        /// 不會自行啟用 ChurchReport 流量；正式 consumer cutover 屬 P7.4。
        /// </summary>
        public static IPackage02ContactBasicInfoUpdateClient? TryCreatePackage02ContactBasicInfoClient(
            IConfiguration configuration,
            IPackage02ContactBasicInfoUpdateClient? injectedClient = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage02ContactBasicInfoUpdatesEnabled(configuration))
            {
                return null;
            }

            if (injectedClient is not null)
            {
                return injectedClient;
            }

            var executor = CreatePackage02Executor(configuration);

            return new Package02ContactBasicInfoUpdateClient(
                executor,
                NullLogger<Package02ContactBasicInfoUpdateClient>.Instance);
        }

        /// <summary>
        /// 讀取 P7.2 Slice B LINE profile／ungrouped aggregate 的獨立 consumer flag。預設 false，故 P7.4
        /// cutover 前不解析 process host、不建立 provider／HTTP handler／Data8 pool，也不送出 write、metadata、
        /// list、membership 或 aggregate operation。此 flag 不與 basic-info 或 Package01 共享 rollout 邊界。
        /// </summary>
        public static bool IsPackage02ContactProfileOperationsEnabled(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            var raw = configuration["DynamicsAccess:Package02ContactProfileOperationsEnabled"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 讀取 P7.4 ORG-CALL-00024 未分組承諾 aggregate 的獨立 consumer gate。此 gate 必須同時依賴
        /// Package02 base gate；任一缺失或 false 都在 controller 建立 typed client、process host、provider、
        /// HTTP handler、Data8 pool 或 outbound I/O 前回傳 false。它不影響同一 Package02 的 LINE write，
        /// 因此 deployment rollback 可只關閉 aggregate count，而不擴大或混合 mutation rollout 邊界。
        /// </summary>
        /// <param name="configuration">僅 deployment-owned configuration；不得由 HTTP、Session、browser 或 caller scalar 替代。</param>
        /// <returns>base 與 ORG-CALL-00024 sub-gate 皆為明確 true／1 時才為 true；其他一律 fail closed。</returns>
        public static bool IsPackage02UngroupedCommitmentReadEnabled(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage02ContactProfileOperationsEnabled(configuration))
            {
                return false;
            }

            var raw = configuration["DynamicsAccess:Package02UngroupedCommitmentReadEnabled"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 判斷 P7.4 ORG-CALL-00026 個人出席紀錄唯讀 capability 是否由部署端明確開啟。此 sub-gate 必須同時
        /// 依賴 Package02 base gate；任一 gate 缺失、空白或不是精確 true／1 時都 fail closed。此 predicate
        /// 只讀取 deployment configuration，不 bind options、不解析 ProfileAlias、不取得 process host、不建立
        /// ProductClient、provider、handler、Data8 pool、credential graph 或 outbound I/O，因此 rollback 只需關閉
        /// sub-gate 即可在 user/session hydration 和 transport 資源建立前停止，不會留下跨 request state。
        /// </summary>
        /// <param name="configuration">唯一可提供 deployment-owned gate 的設定來源；不得由 HTTP、Session 或 browser 取代。</param>
        /// <returns>Package02 base gate 與本 sub-gate 都是明確 true／1 時為 true；其餘一律為 false。</returns>
        public static bool IsPackage02MemberInfoPresentReadEnabled(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage02ContactProfileOperationsEnabled(configuration))
            {
                return false;
            }

            var raw = configuration["DynamicsAccess:Package02MemberInfoPresentReadEnabled"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 嘗試建立 P7.2 Slice B typed client。flag=false 時在 host resolution 前回傳 null；flag=true 時先驗證
        /// deployment-owned ProfileAlias，才可借用 injected facade 或 process host 的單一 Embedded／Gateway executor
        /// generation，不建立第二個 provider、pool、credential graph 或 session。injected facade 不是設定 authority，
        /// 因此不得繞過 profile 驗證；此 helper 尚未接入 controller，不會自行切換 ChurchReport 流量；P7.4 才擁有 cutover。
        /// </summary>
        /// <param name="configuration">deployment-owned DynamicsAccess 設定；不能來自 HTTP request。</param>
        /// <param name="injectedClient">測試或正式 DI 已擁有的 stateless typed client；helper 不 Dispose。</param>
        /// <returns>flag 關閉時 null；開啟時為注入 client 或共用 executor 上的新 stateless facade。</returns>
        public static IPackage02ContactProfileClient? TryCreatePackage02ContactProfileClient(
            IConfiguration configuration,
            IPackage02ContactProfileClient? injectedClient = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage02ContactProfileOperationsEnabled(configuration))
            {
                return null;
            }

            // ProfileAlias 是 deployment composition 的完整 isolation boundary 之一。即使 facade 由測試或 DI
            // 注入，也必須先驗證它，否則錯誤設定可能讓呼叫端在沒有可證實 profile/generation 的情況下取得
            // typed capability。這個純 options bind 不建立 host、provider、handler、pool 或 credential graph。
            var productOptions = BindOptions(configuration);
            EnsureNonEmptyProductProfile(productOptions, "Package02 contact profile operations");
            if (injectedClient is not null)
            {
                return injectedClient;
            }

            return new Package02ContactProfileClient(
                CreatePackage02Executor(productOptions, configuration),
                NullLogger<Package02ContactProfileClient>.Instance);
        }

        /// <summary>
        /// 嘗試建立只供 P7.4 ORG-CALL-00024 使用的未分組承諾 aggregate typed client。此 helper 同時驗證
        /// Package02 base/sub-gate 與 deployment-owned ProfileAlias；任何 gate 缺失或 profile 空白都在解析
        /// process host、provider、HTTP handler、Data8 pool、credential 或 outbound I/O 前停止。它不接受 caller
        /// profile、connector、owner、endpoint 或 credential，且 injected facade 的生命週期仍屬 DI/process host。
        /// </summary>
        /// <param name="configuration">唯一可提供 gate 與 profile 的 deployment-owned configuration。</param>
        /// <param name="injectedClient">測試或 composition 已擁有的 stateless facade；helper 絕不 Dispose 它。</param>
        /// <returns>gate=false 時為 null；gate=true 時為已先驗證 profile 的 Package02 typed client。</returns>
        public static IPackage02ContactProfileClient? TryCreatePackage02UngroupedCommitmentReadClient(
            IConfiguration configuration,
            IPackage02ContactProfileClient? injectedClient = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage02UngroupedCommitmentReadEnabled(configuration))
            {
                return null;
            }

            var productOptions = BindOptions(configuration);
            EnsureNonEmptyProductProfile(productOptions, "Package02 ungrouped commitment read operations");
            if (injectedClient is not null)
            {
                return injectedClient;
            }

            return new Package02ContactProfileClient(
                CreatePackage02Executor(configuration),
                NullLogger<Package02ContactProfileClient>.Instance);
        }

        /// <summary>
        /// 建立只供 ORG-CALL-00026 使用的獨立 present-record typed client。base/sub gate 為 false 時，此方法
        /// 必須在 options bind、ProfileAlias、host、provider、handler、pool、credential 與 outbound I/O 前回傳
        /// null；true 時則先驗證 deployment-owned ProfileAlias，連測試/DI injected facade 也不能繞過這個
        /// profile/generation isolation boundary。facade 與 executor 均不由本 helper Dispose，process host 是其
        /// 唯一 resource owner；本 helper 不自行接入流量、不 retry，也不提供 ToolUtility fallback。
        /// </summary>
        /// <param name="configuration">只含部署端 Package02 gate 與 DynamicsAccess 設定的來源，不能來自 request。</param>
        /// <param name="injectedClient">由受控 DI 或測試擁有的無狀態 read facade；只有 gate/profile 完整時才可借用。</param>
        /// <returns>gate 不完整時為 null；有效時為固定 deployment profile 的獨立 present-record client。</returns>
        public static IMemberInfoPresentRecordReadClient? TryCreatePackage02MemberInfoPresentReadClient(
            IConfiguration configuration,
            IMemberInfoPresentRecordReadClient? injectedClient = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage02MemberInfoPresentReadEnabled(configuration))
            {
                return null;
            }

            var productOptions = BindOptions(configuration);
            EnsureNonEmptyProductProfile(productOptions, "Package02 MemberInfo present read operations");
            if (injectedClient is not null)
            {
                return injectedClient;
            }

            return new MemberInfoPresentRecordReadClient(
                CreatePackage02Executor(productOptions, configuration),
                NullLogger<MemberInfoPresentRecordReadClient>.Instance);
        }

        /// <summary>
        /// 讀取 P7.4 Package03 特殊資源的獨立 consumer gate。預設 false；因此 controller 在 parse locator、
        /// session scope、typed client、process host、HTTP handler、Data8 pool 或任何 image I/O 前即可固定拒絕。
        /// 這個 gate 不依附 Package01／Package02，讓圖片讀取可獨立 rollback，且本方法不會變更 CE 或網站流量。
        /// </summary>
        /// <param name="configuration">僅 deployment-owned configuration；不得由 HTTP、Session 或 browser 值替代。</param>
        /// <returns>明確 true／1 時為 true；缺值、空白與其他值都 fail closed。</returns>
        public static bool IsPackage03SpecialResourcesEnabled(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            var raw = configuration["DynamicsAccess:Package03SpecialResourcesEnabled"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 嘗試建立 P7.4 Package03 圖片唯讀所需的 stateless typed client。gate=false 時必須在 options bind、
        /// host resolution、provider、handler、pool 與 credential graph 前回傳 null；gate=true 時則驗證非空
        /// deployment profile，並只借用主 DI process host 的 executor generation，絕不 per-request 建立第二個 owner。
        /// 此 helper 不接收 browser profile/workload，亦不 Dispose injected 或新 facade，資源仍由 process host 管理。
        /// </summary>
        /// <param name="configuration">deployment-owned DynamicsAccess 設定；不得由 request 值覆寫。</param>
        /// <param name="injectedClient">測試或正式 DI 已擁有的 typed client；只在 gate 與 profile 都有效時可使用。</param>
        /// <returns>gate=false 時 null；其他情況回傳借用既有 executor 的 stateless Package03 client。</returns>
        public static IPackage03SpecialResourceClient? TryCreatePackage03SpecialResourceClient(
            IConfiguration configuration,
            IPackage03SpecialResourceClient? injectedClient = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage03SpecialResourcesEnabled(configuration))
            {
                return null;
            }

            var productOptions = BindOptions(configuration);
            EnsureNonEmptyProductProfile(productOptions, "Package03 special-resource operations");
            if (injectedClient is not null)
            {
                return injectedClient;
            }

            return new Package03SpecialResourceClient(
                CreatePackage03Executor(productOptions, configuration),
                NullLogger<Package03SpecialResourceClient>.Instance);
        }

        /// <summary>
        /// 讀取 P7.4 MemberInfo 承諾類型 metadata 的獨立 Package03 consumer gate。base gate 只表示 Package03
        /// composition 可以被部署考慮；本 sub-gate 才是 ORG-CALL-00040 的可回復邊界，兩者都必須明確 true／1。
        /// 缺值、空白、僅開啟圖片 base gate 或任何其他文字都 fail closed，因此不會在 user/session hydration、
        /// process host、provider、handler、Data8 pool、metadata cache 或 outbound I/O 前意外建立 typed path。
        /// </summary>
        /// <param name="configuration">只含 deployment-owned 設定的來源；HTTP、Session、profile、locale 與 browser 值不得替代它。</param>
        /// <returns>base/sub gate 均有效時為 true；其餘情況一律為 false。</returns>
        public static bool IsPackage03MemberInfoCommitmentMetadataReadEnabled(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage03SpecialResourcesEnabled(configuration))
            {
                return false;
            }

            var raw = configuration["DynamicsAccess:Package03MemberInfoCommitmentMetadataReadEnabled"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 嘗試建立只供 P7.4 ORG-CALL-00040 使用的 Package03 metadata typed client。sub-gate 關閉時必須在
        /// options bind、host resolution、provider、handler、pool、metadata cache 與 credential graph 前回傳 null；
        /// 開啟時先驗證 non-empty deployment ProfileAlias，才可借用 process host 已擁有的 executor generation。
        /// 本 helper 不接受 caller profile/workload/target/locale，也不 Dispose injected 或 facade；所有可重用資源
        /// 仍由 Generic Host 的 process host 以 profile/generation 為界唯一擁有與釋放。
        /// </summary>
        /// <param name="configuration">唯一可提供 Package03 base/sub gate 與 deployment profile 的設定來源。</param>
        /// <param name="injectedClient">測試或受控 DI 已擁有的 stateless facade；僅在 gate/profile 完整時可借用。</param>
        /// <returns>gate 不完整時為 null；有效時回傳同一個 process-host executor 的 stateless Package03 client。</returns>
        public static IPackage03SpecialResourceClient? TryCreatePackage03MemberInfoCommitmentMetadataReadClient(
            IConfiguration configuration,
            IPackage03SpecialResourceClient? injectedClient = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage03MemberInfoCommitmentMetadataReadEnabled(configuration))
            {
                return null;
            }

            var productOptions = BindOptions(configuration);
            EnsureNonEmptyProductProfile(productOptions, "Package03 MemberInfo commitment metadata read operations");
            if (injectedClient is not null)
            {
                return injectedClient;
            }

            return new Package03SpecialResourceClient(
                CreatePackage03Executor(productOptions, configuration),
                NullLogger<Package03SpecialResourceClient>.Instance);
        }

        /// <summary>
        /// 為所有已實作但尚未 cutover 的 Package02 typed clients 選取同一 process generation。ConnectionMode 只由
        /// deployment configuration 決定；request 不能切換 Embedded／Dedicated／Central、Profile 或 connector。
        /// 回傳 executor 由 process host 唯一擁有，typed client 與 helper 均不得 Dispose。
        /// </summary>
        private static IDynamicsOperationExecutor CreatePackage02Executor(IConfiguration configuration)
        {
            var productOptions = BindOptions(configuration);
            return CreatePackage02Executor(productOptions, configuration);
        }

        /// <summary>
        /// 以已完成 deployment validation 的 Package02 options 取得 process-host executor。呼叫者負責在此方法前
        /// 驗證所屬 capability 的 profile 規則；本 overload 不重新 bind options，避免同一 composition path 在不同
        /// 時點讀到不一致設定。process host 仍是 provider、handler、Data8 pool、credential graph 與 generation
        /// cleanup 的唯一 owner；typed facade 只在當前呼叫中使用 executor，絕不保存 user、Session 或 profile state。
        /// </summary>
        /// <param name="productOptions">由 deployment configuration 產生且已通過呼叫者 profile 規則的不可變 options。</param>
        /// <param name="configuration">僅供 Embedded composition 讀取既有 deployment 設定；不能由 HTTP request 取代。</param>
        /// <returns>依固定 profile/generation 隔離、由 process host 唯一擁有的 executor。</returns>
        private static IDynamicsOperationExecutor CreatePackage02Executor(
            ProductDynamicsOptions productOptions,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(productOptions);
            ArgumentNullException.ThrowIfNull(configuration);
            var processHost = GetStartedProcessHost();
            return productOptions.ConnectionMode switch
            {
                ConnectionMode.Embedded => processHost.GetOrCreateEmbeddedExecutor(productOptions, configuration),
                ConnectionMode.DedicatedGateway or ConnectionMode.CentralGateway =>
                    processHost.GetOrCreateGatewayExecutor(productOptions),
                _ => throw new InvalidOperationException(
                    "Package02 contact operations require a supported Dynamics connection mode.")
            };
        }

        /// <summary>
        /// 為 Package03 特殊資源選取既有 process host 的唯一 executor generation。這個獨立 helper 不改變
        /// Package02 原有 composition 行為；Package03 在取得 host 前先確認非空 deployment profile，避免空白
        /// profile 觸發 provider、pool 或 credential graph 後才失敗。request 不能指定 mode、profile 或 connector。
        /// </summary>
        /// <param name="productOptions">已由 deployment configuration bind 的非秘密產品 options。</param>
        /// <param name="configuration">只由 Embedded composition 使用的 deployment configuration。</param>
        /// <returns>由主 process host 擁有且依 profile/generation 隔離的 executor。</returns>
        private static IDynamicsOperationExecutor CreatePackage03Executor(
            ProductDynamicsOptions productOptions,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(productOptions);
            ArgumentNullException.ThrowIfNull(configuration);
            EnsureNonEmptyProductProfile(productOptions, "Package03 special-resource operations");
            var processHost = GetStartedProcessHost();
            return productOptions.ConnectionMode switch
            {
                ConnectionMode.Embedded => processHost.GetOrCreateEmbeddedExecutor(productOptions, configuration),
                ConnectionMode.DedicatedGateway or ConnectionMode.CentralGateway =>
                    processHost.GetOrCreateGatewayExecutor(productOptions),
                _ => throw new InvalidOperationException(
                    "Package03 special-resource operations require a supported Dynamics connection mode.")
            };
        }

        /// <summary>
        /// 在取得 process host 前驗證 profile alias，避免空白設定導致 host、provider、pool 或 credential graph
        /// 已建立後才失敗。alias 只能由 deployment configuration 提供；本方法不記錄值、不猜測 legacy profile。
        /// </summary>
        /// <param name="productOptions">只含產品可見 connection mode 與 profile alias 的 options。</param>
        /// <param name="operationFamily">固定本機診斷分類，不含 caller 或秘密資料。</param>
        private static void EnsureNonEmptyProductProfile(
            ProductDynamicsOptions productOptions,
            string operationFamily)
        {
            if (string.IsNullOrWhiteSpace(productOptions.ProfileAlias))
            {
                throw new InvalidOperationException(
                    operationFamily + " require DynamicsAccess:ProfileAlias before client composition.");
            }
        }

        /// <summary>
        /// 繫結產品唯一可見的 mode、ProfileAlias 與可選 Gateway 設定。Embedded 不需要亦不使用 Gateway endpoint；
        /// CrmConnection、CRM endpoint、credential、token 與 secret-reference 均不會被複製到回傳 options。
        /// 實際 Gateway executor 啟用時仍由既有 validator 在任何 outbound request 前 fail closed。
        /// </summary>
        public static ProductDynamicsOptions BindOptions(IConfiguration configuration)
        {
            var options = new ProductDynamicsOptions
            {
                Gateway = new GatewayEndpointOptions()
            };

            // 字串欄位再保險，避免 section bind 失敗時整段空白。
            options.ProfileAlias = FirstNonEmpty(
                options.ProfileAlias,
                configuration["DynamicsAccess:ProfileAlias"]) ?? string.Empty;

            var modeText = configuration["DynamicsAccess:ConnectionMode"];
            if (!string.IsNullOrWhiteSpace(modeText) &&
                !Enum.TryParse<ConnectionMode>(modeText, ignoreCase: true, out _))
            {
                throw new InvalidOperationException(
                    $"Unsupported DynamicsAccess:ConnectionMode '{modeText}'. Expected a {nameof(ConnectionMode)} value.");
            }

            if (!string.IsNullOrWhiteSpace(modeText) &&
                Enum.TryParse<ConnectionMode>(modeText, ignoreCase: true, out var mode))
            {
                options.ConnectionMode = mode;
            }

            options.Gateway ??= new GatewayEndpointOptions();
            options.Gateway.Endpoint = FirstNonEmpty(
                options.Gateway.Endpoint,
                configuration["DynamicsAccess:Gateway:Endpoint"]) ?? string.Empty;
            options.Gateway.ApiPrefix = FirstNonEmpty(
                options.Gateway.ApiPrefix,
                configuration["DynamicsAccess:Gateway:ApiPrefix"],
                "/v1") ?? "/v1";

            // 這裡只做純設定繫結，不能因為 Embedded 沒有 Gateway endpoint 就拒絕。真正啟用 Package01
            // 時仍由各 mode 的 composition root 解析 executor；如此讀取設定不會建立 provider、HTTP handler、
            // connector、permit、timer 或秘密快取，也不會把 Embedded 降級為 Gateway fallback。
            return options;
        }

        /// <summary>
        /// Package01 啟用後 ChurchReport 僅允許 Gateway execution mode。此檢查位於 process host、ServiceProvider、
        /// HttpClient、handler 與任何 secret 解析之前，避免 Embedded 設定形成本機 transport 或既有 ToolUtility
        /// 路徑的 request fallback；legacy 業務仍由未啟用的功能旗標維持原有行為。
        /// </summary>
        /// <param name="productOptions">只含 Gateway 非秘密欄位的產品設定。</param>
        private static void EnsureGatewayOnly(ProductDynamicsOptions productOptions)
        {
            ArgumentNullException.ThrowIfNull(productOptions);
            if (productOptions.ConnectionMode is not (
                    ConnectionMode.DedicatedGateway or ConnectionMode.CentralGateway))
            {
                throw new InvalidOperationException(
                    "Package01FeeReadsEnabled requires DynamicsAccess:ConnectionMode=DedicatedGateway or CentralGateway.");
            }
        }
        private static IDynamicsOperationExecutor CreateGatewayExecutor(
            ProductDynamicsOptions productOptions,
            IDonationDynamicsAccessProcessHost processHost)
        {
            if (productOptions.Gateway is null ||
                string.IsNullOrWhiteSpace(productOptions.Gateway.Endpoint) ||
                string.IsNullOrWhiteSpace(productOptions.ProfileAlias))
            {
                throw new InvalidOperationException(
                    "DynamicsAccess Gateway mode requires ProfileAlias and Gateway:Endpoint when Package01FeeReadsEnabled=true.");
            }

            // 重要：舊 facade 只能借用主 DI singleton 的 process-level executor；不得在 static 類別另建 provider。
            // 這是產品 -> Gateway 的連線池，不是 per-user CRM session pool，亦不得保存 caller/session 身份。
            return processHost.GetOrCreateGatewayExecutor(productOptions);
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// 在 Generic Host 啟動時發佈主 DI singleton 給尚未完成建構式注入的舊呼叫點。
        /// 發佈只允許空值或同一物件重入；若另一個 host 尚未停止，立即 fail-closed，避免兩個 provider generation
        /// 透過 static facade 混用 endpoint、credential source、handler 或 token cache。
        /// </summary>
        /// <param name="processHost">由 ChurchReport 主 DI 建立並擁有的 process host。</param>
        internal static void AttachProcessHost(IDonationDynamicsAccessProcessHost processHost)
        {
            ArgumentNullException.ThrowIfNull(processHost);
            var current = Interlocked.CompareExchange(
                ref _compatibilityProcessHost,
                processHost,
                comparand: null);
            if (current is not null && !ReferenceEquals(current, processHost))
            {
                throw new InvalidOperationException(
                    "A different DynamicsAccess host is already started. Stop it before starting another host.");
            }
        }

        /// <summary>
        /// 在關機時只撤銷與指定 singleton 完全相同的 facade 參考；不 Dispose 資源，因為 cleanup 仍由
        /// <see cref="DonationDynamicsAccessBootstrapLifetime"/> 所持有的主 DI singleton 執行。
        /// 精確物件比較避免舊 host 的遲到 Stop 將新 host 的相容路由清空。
        /// </summary>
        /// <param name="processHost">目前正在停止、且預期已被發佈的主 DI singleton。</param>
        internal static void DetachProcessHost(IDonationDynamicsAccessProcessHost processHost)
        {
            ArgumentNullException.ThrowIfNull(processHost);
            Interlocked.CompareExchange(
                ref _compatibilityProcessHost,
                value: null,
                comparand: processHost);
        }

        /// <summary>
        /// 取得已由 hosted lifecycle 發佈的 process host；未啟動或關機已開始時立即 fail-closed，
        /// 不自行建立 fallback provider，避免舊 static 呼叫在 DI ownership 邊界之外產生第二個 HTTP pool。
        /// </summary>
        /// <returns>由 ChurchReport 主 DI 唯一擁有的 process host。</returns>
        /// <exception cref="InvalidOperationException">Generic Host 尚未 Start 或已開始 Stop。</exception>
        private static IDonationDynamicsAccessProcessHost GetStartedProcessHost()
        {
            return Volatile.Read(ref _compatibilityProcessHost)
                   ?? throw new InvalidOperationException(
                       "DynamicsAccess host has not started or is already stopping.");
        }
    }

    /// <summary>
    /// ChurchReport 行程內 Dynamics executor generation 的唯一可注入擁有權邊界。
    /// 實作必須把 Gateway／Embedded provider、HttpClient handler、timer、socket pool 與 token cache 的最長
    /// 存活範圍限制在 Generic Host lifetime，並以單一不可變 generation 防止不同 endpoint、profile、
    /// credential source 或 CE 版本共用 mutable transport state。此介面不接受 session／user identity，
    /// 因此不能被誤用為跨要求的身份或租戶 cache。
    /// </summary>
    public interface IDonationDynamicsAccessProcessHost : IAsyncDisposable
    {
        /// <summary>
        /// 在 host StartAsync 階段，以與 Dispose 共用的 lifecycle gate 發佈舊 static facade。若 disposal 已開始
        /// 則 fail-closed，不允許已 terminal owner 被重新發佈；此方法只發佈非 owner 參考，不建立 executor。
        /// </summary>
        void PublishCompatibilityFacade();

        /// <summary>
        /// 在 host StopAsync 階段撤銷舊 static facade。實作必須只清除精確相同的 owner，避免遲到的舊 host
        /// Stop 影響新的 host；此方法不 Dispose provider，cleanup 仍由 <see cref="IAsyncDisposable.DisposeAsync"/> 負責。
        /// </summary>
        void UnpublishCompatibilityFacade();

        /// <summary>
        /// 取得或建立目前唯一的 Gateway executor generation；相同設定重用同一 executor，設定變更則
        /// fail-closed 並要求 host restart／Dispose，避免在舊 handler 尚未 drain 時建立第二個連線池。
        /// </summary>
        /// <param name="options">已綁定、但仍會由正式 ProductClient options validator 驗證的產品設定。</param>
        /// <returns>由本 process host 擁有、不得由呼叫者 Dispose 的正式 operation executor。</returns>
        IDynamicsOperationExecutor GetOrCreateGatewayExecutor(ProductDynamicsOptions options);

        /// <summary>
        /// 取得或建立目前唯一的 Embedded Data8 executor generation。完整 <c>CrmConnection</c> 組態只可由
        /// 本 host composition boundary 讀取一次，用來映射受控 Profile、Organization Catalog 與 Data8 Factory；
        /// controller、request、session 與產品業務碼均只看到回傳的 SDK-free executor。相同的非秘密組態重用
        /// generation，變更則 fail-closed 並要求 host restart，防止舊 Pool／credential graph 與新設定混用。
        /// </summary>
        /// <param name="options">只含 ConnectionMode 與固定 ProfileAlias 的產品公開設定。</param>
        /// <param name="configuration">僅限 host 啟動 composition root 的設定來源，不可由 request 傳入。</param>
        /// <returns>由本 process host 唯一擁有、經 Shared Guard 與 ControlPlane 管線保護的 executor。</returns>
        IDynamicsOperationExecutor GetOrCreateEmbeddedExecutor(
            ProductDynamicsOptions options,
            IConfiguration configuration);


    }

    /// <summary>
    /// ChurchReport 主 DI 擁有的 Dynamics process host singleton。
    /// 內部只允許一個 provider／executor generation，使用短生命週期 monitor 序列化第一次建立、設定衝突與關機；
    /// 這項短而低頻的同步成本只發生在啟動或舊 facade 第一次解析，換取不會重複建立 handler、socket pool、
    /// timer、token cache 的記憶體與生命週期安全。DisposeAsync 與 GetOrCreate 共用同一 gate，確保關機時
    /// 沒有半建立或半釋放 generation，且多個 shutdown caller 會觀察到冪等、確定完成的 cleanup。
    /// </summary>
    public sealed class DonationDynamicsAccessProcessHost : IDonationDynamicsAccessProcessHost
    {
        private readonly object _lifecycleGate = new();
        private ServiceProvider? _provider;
        private IDynamicsOperationExecutor? _executor;
        private string? _generationKey;
        private Task? _disposeTask;
        private bool _disposeStarted;

        /// <summary>
        /// 建立尚未擁有 provider generation 的 process host。建構式不解析設定、不建立 ServiceProvider、
        /// HttpClient、handler、timer、socket 或 token cache；只有 feature flag 啟用後的正式 executor 解析
        /// 才會開始資源 ownership，之後由本物件的 <see cref="DisposeAsync"/> 唯一且 terminal 地釋放。
        /// </summary>
        public DonationDynamicsAccessProcessHost()
        {
        }

        /// <summary>
        /// 在 process lifecycle monitor 內發佈非 owner static facade。與 Dispose 使用同一把 lock 可封閉
        /// Start／shutdown 競爭：一旦 terminal flag 設定，任何遲到 Start 都只能收到 ObjectDisposedException，
        /// 不會把已釋放的 provider owner 再掛回 static 路由。
        /// </summary>
        public void PublishCompatibilityFacade()
        {
            lock (_lifecycleGate)
            {
                ObjectDisposedException.ThrowIf(_disposeStarted, this);
                DonationDynamicsAccessBootstrap.AttachProcessHost(this);
            }
        }

        /// <summary>
        /// 在 process lifecycle monitor 內撤銷精確相同的 static facade；此動作是冪等 no-op，且不等待
        /// provider cleanup，因此正常 Stop 可先封閉新 static 呼叫，再由 DisposeAsync 完成 transport 回收。
        /// </summary>
        public void UnpublishCompatibilityFacade()
        {
            lock (_lifecycleGate)
            {
                DonationDynamicsAccessBootstrap.DetachProcessHost(this);
            }
        }

        /// <summary>
        /// 取得或建立 Gateway executor。正式 ProductClient DI 擴充會建立唯一 HttpClientFactory-owned handler；
        /// 本方法不增加 caller identity header，也不直接送 HTTP。options 驗證在 executor 解析時執行，
        /// 因而無效 HTTPS endpoint、alias 或 API prefix 會在 host StartAsync preflight 階段 fail-closed。
        /// </summary>
        public IDynamicsOperationExecutor GetOrCreateGatewayExecutor(ProductDynamicsOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var key = ComputeGenerationKey(
                "gateway",
                options.ProfileAlias,
                options.Gateway?.Endpoint,
                options.Gateway?.ApiPrefix,
                options.Gateway?.MaxResponseBytes.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

            return GetOrCreate(key, services =>
            {
                services.AddSpeechMessageDynamicsGatewayProductClient(configured =>
                {
                    configured.ConnectionMode = options.ConnectionMode;
                    configured.ProfileAlias = options.ProfileAlias;
                    configured.Gateway = options.Gateway is null
                        ? null
                        : new GatewayEndpointOptions
                        {
                            Endpoint = options.Gateway.Endpoint,
                            ApiPrefix = options.Gateway.ApiPrefix,
                            MaxResponseBytes = options.Gateway.MaxResponseBytes
                        };
                });
            });
        }

        /// <summary>
        /// 取得或建立 Embedded Data8 generation。此方法只在 P4 的 host composition boundary 讀取既有
        /// <c>CrmConnection</c>，並將它映射為一個不可變 Profile/Catalog snapshot；產品 request 永遠不能攜帶
        /// OrganizationId、endpoint、ConnectorKind 或 credential。生成的 provider 由本 process host 唯一持有，
        /// provider DisposeAsync 會依 DI 反向順序先 drain/dispose <see cref="EmbeddedData8Runtime"/> 的 Pool/client，
        /// 再釋放 Admission manager 的 permit、CTS、renewal task 與 host slot，不留下 session 或 WCF resource。
        /// </summary>
        /// <param name="options">必須為 Embedded 的公開產品選項；Gateway 欄位在此完全不讀取。</param>
        /// <param name="configuration">只供本次啟動組裝的既有 CrmConnection 設定來源。</param>
        /// <returns>固定 alias 的 stateless EmbeddedHostAdapter。</returns>
        public IDynamicsOperationExecutor GetOrCreateEmbeddedExecutor(
            ProductDynamicsOptions options,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(configuration);
            if (options.ConnectionMode != ConnectionMode.Embedded)
            {
                throw new InvalidOperationException(
                    "Embedded Dynamics executor requires DynamicsAccess:ConnectionMode=Embedded.");
            }

            if (!CrmConnectionEmbeddedProfileMapper.TryCreate(
                    configuration,
                    options,
                    out var profiles,
                    out var catalog,
                    out var profileMappingError) ||
                !profiles.TryGetValue(options.ProfileAlias, out var profile) ||
                profile is null ||
                !catalog.TryGetValue(profile.OrganizationAlias, out var organization) ||
                organization is null)
            {
                throw new InvalidOperationException(
                    "Embedded Dynamics composition configuration is invalid: " + NormalizeEmbeddedConfigurationError(profileMappingError));
            }

            if (!CrmConnectionEmbeddedProfileMapper.TryCreateConnectionSettings(
                    configuration,
                    organization.ServiceUri,
                    out var connectionSettings,
                    out var connectionSettingsError) ||
                connectionSettings is null)
            {
                throw new InvalidOperationException(
                    "Embedded Dynamics connection credentials are unavailable: " + NormalizeEmbeddedConfigurationError(connectionSettingsError));
            }

            // Generation key 僅保存單向 SHA-256 digest。它涵蓋可影響 pool／admission 隔離的非秘密組態，
            // 不保存或記錄 CrmConnection password、帳號、endpoint、Organization GUID 或 profile 物件。
            var key = ComputeGenerationKey(
                "embedded",
                options.ProfileAlias,
                profile.OrganizationAlias,
                profile.CeVersion.ToString(),
                profile.ConnectorKind.ToString(),
                profile.CredentialReference,
                profile.Pool.MinSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                profile.Pool.MaxSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                profile.Pool.IdleTimeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                profile.Pool.AcquireTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                profile.Pool.HealthCheckOnAcquire.ToString(),
                profile.Operation.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                profile.Operation.MaxRetries.ToString(System.Globalization.CultureInfo.InvariantCulture),
                profile.Operation.RetryBaseDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                organization.OrganizationId.ToString("D"),
                organization.ServiceUri);

            return GetOrCreate(key, services =>
            {
                services.AddSingleton<EmbeddedData8Runtime>(serviceProvider => new EmbeddedData8Runtime(
                    profiles,
                    catalog,
                    options.ProfileAlias,
                    new OnPremiseData8ConnectorClientFactory(connectionSettings),
                    serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EmbeddedData8Runtime>>(),
                    serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()));
                services.AddSpeechMessageDynamicsEmbedded(
                    options,
                    _ => new RequestGuard(
                    [
                        OperationIds.RuntimeHealthWhoAmI,
                        OperationIds.MemberInfoContactUpdateBasicInfo,
                        OperationIds.MemberInfoContactUpdateLineProfile,
                        OperationIds.MemberInfoContactCountUngroupedCommitment,
                        OperationIds.PaymentsDedicationRetrieveByContact,
                        // 認證 lookup 仍保持 deployment gate=false 且未接入登入 consumer；此 allowlist 只讓未來經過
                        // 完整 authentication migration 的固定 operation 通過同一個 server-owned Guard，不能讓 request
                        // 選擇 entity、profile、credential、connector 或任意 CRM query。
                        OperationIds.AuthenticationContactRetrieveByAccount,
                        OperationIds.AuthenticationContactRetrieveByLineId
                    ]),
                    serviceProvider => serviceProvider.GetRequiredService<EmbeddedData8Runtime>().Executor);
            });
        }

        /// <summary>
        /// 確定性釋放目前 provider generation。多個 StopAsync／DI DisposeAsync caller 會取得同一個 cleanup task，
        /// 因此都觀察相同完成或失敗結果，且 provider 只 Dispose 一次。第一個 caller 在 monitor 內把 host 標成
        /// terminal、取走 owner 欄位並發佈 cleanup task；之後的 GetOrCreate 一律丟出 ObjectDisposedException，
        /// 避免 shutdown 開始後由遲到要求重建 handler、timer、socket 或 token cache。
        /// </summary>
        public ValueTask DisposeAsync()
        {
            lock (_lifecycleGate)
            {
                if (_disposeTask is null)
                {
                    _disposeStarted = true;
                    // Generic Host 啟動失敗時可能直接 Dispose DI singleton，而不先呼叫 hosted StopAsync；
                    // 同一 gate 內撤銷 facade，才能避免 concurrent Start 把已 terminal owner 重新發佈。
                    DonationDynamicsAccessBootstrap.DetachProcessHost(this);
                    var provider = _provider;
                    _provider = null;
                    _executor = null;
                    _generationKey = null;
                    _disposeTask = provider is null
                        ? Task.CompletedTask
                        : DisposeProviderAsync(provider);
                }

                return new ValueTask(_disposeTask);
            }
        }

        /// <summary>
        /// 在 lifecycle gate 內建立或重用唯一 generation。provider 只在全部服務註冊完成後才解析 executor；
        /// 解析失敗時先釋放尚未發佈的 provider，再把原始錯誤交回啟動流程，避免部分 handler graph 被遺留。
        /// </summary>
        private IDynamicsOperationExecutor GetOrCreate(
            string generationKey,
            Action<IServiceCollection> configureServices)
        {
            lock (_lifecycleGate)
            {
                ObjectDisposedException.ThrowIf(_disposeStarted, this);

                if (_provider is not null)
                {
                    if (!string.Equals(_generationKey, generationKey, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "DynamicsAccess process configuration changed. Restart the host to replace and drain the active generation.");
                    }

                    return _executor
                           ?? throw new InvalidOperationException(
                               "DynamicsAccess process generation has no executor.");
                }

                var services = new ServiceCollection();
                services.AddLogging();
                configureServices(services);

                var provider = services.BuildServiceProvider(validateScopes: true);
                try
                {
                    var executor = provider.GetRequiredService<IDynamicsOperationExecutor>();
                    _provider = provider;
                    _executor = executor;
                    _generationKey = generationKey;
                    return executor;
                }
                catch (Exception originalFailure)
                {
                    try
                    {
                        provider.Dispose();
                    }
                    catch (Exception cleanupFailure)
                    {
                        throw new AggregateException(
                            "DynamicsAccess provider initialization and rollback both failed.",
                            originalFailure,
                            cleanupFailure);
                    }

                    throw;
                }
            }
        }

        /// <summary>
        /// 等待 ServiceProvider 完成非同步 cleanup。此 helper 會把 provider DisposeAsync 在同步前段發生的例外
        /// 也封裝進共享 Task，確保所有併行 Dispose caller 觀察相同失敗，而不是只有第一個 owner 看見例外。
        /// </summary>
        private static async Task DisposeProviderAsync(ServiceProvider provider)
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 將 generation 欄位以長度前綴的 SHA-256 digest 正規化，避免簡單串接碰撞造成不同 profile／endpoint
        /// 誤判為同一世代。每個欄位的暫存 UTF-8 bytes 在加入 hash 後立即清零，降低 local secret 在額外
        /// managed buffer 中的停留時間；digest 只用於同 process 相等比較，不會記錄或跨程序持久化。
        /// </summary>
        private static string ComputeGenerationKey(params string?[] fields)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> length = stackalloc byte[4];

            foreach (var field in fields)
            {
                var bytes = Encoding.UTF8.GetBytes(field ?? string.Empty);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
                hash.AppendData(length);
                hash.AppendData(bytes);
                CryptographicOperations.ZeroMemory(bytes);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }

        /// <summary>
        /// 將 mapper 的內部失敗原因壓縮為固定安全分類，避免 host startup exception 回顯 Organization GUID、
        /// service URI、帳號、密碼或任何設定值。未知代碼一律視為 unavailable，維持 fail-closed。
        /// </summary>
        /// <param name="errorCode">composition mapper 回傳的內部分類。</param>
        /// <returns>可安全顯示或記錄的固定分類。</returns>
        private static string NormalizeEmbeddedConfigurationError(string? errorCode)
            => errorCode switch
            {
                "embedded.connection-mode-required" => "connection-mode-required",
                "embedded.profile-alias-mismatch" => "profile-alias-mismatch",
                "embedded.organization-id-invalid" => "organization-id-invalid",
                "embedded.ce-version-unsupported" => "ce-version-unsupported",
                "embedded.service-uri-invalid" => "service-uri-invalid",
                "embedded.pool-policy-invalid" => "pool-policy-invalid",
                "embedded.connection-credentials-invalid" => "connection-credentials-invalid",
                _ => "configuration-unavailable"
            };
    }

    /// <summary>
    /// 將主 DI singleton 發佈給舊 static facade，並在 host shutdown 時成為明確的 process generation
    /// cleanup 協調者。StartAsync 不解析 executor／provider／HttpClient，因此 feature flag=false 的網站啟動
    /// 仍是嚴格零資源；StopAsync 先撤銷新 static 呼叫，再等待 singleton Dispose，避免關機競爭建立新世代。
    /// DI container 之後可能再次呼叫 DisposeAsync，所以 process host 必須提供併行冪等保證。
    /// </summary>
    public sealed class DonationDynamicsAccessBootstrapLifetime : IHostedService
    {
        private readonly IDonationDynamicsAccessProcessHost _processHost;
        private int _started;

        /// <summary>
        /// 建立只持有主 DI singleton 參考的 hosted lifecycle；建構式不建立任何外部資源。
        /// </summary>
        /// <param name="processHost">ChurchReport 主 DI 唯一擁有的 Dynamics process host。</param>
        public DonationDynamicsAccessBootstrapLifetime(
            IDonationDynamicsAccessProcessHost processHost)
        {
            _processHost = processHost ?? throw new ArgumentNullException(nameof(processHost));
        }

        /// <summary>
        /// 發佈 legacy facade 路由。若 caller 已取消則在發佈前停止；重複 Start 為冪等 no-op，
        /// 不解析 executor 或啟動 HTTP，讓真正的 preflight 仍由獨立 hosted service 控制。
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _started, 1) == 0)
            {
                try
                {
                    _processHost.PublishCompatibilityFacade();
                }
                catch
                {
                    Volatile.Write(ref _started, 0);
                    throw;
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 先阻止舊 static 呼叫取得 owner，再等待 provider cleanup 完成。此方法刻意不把 host shutdown token
        /// 傳入 provider disposal，因為中途取消 cleanup 會遺留 handler、timer、socket 或 token cache；
        /// Stop 重入為 no-op，而 cleanup 例外會傳回 Generic Host，不能靜默視為成功。
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _started, 0) == 0)
            {
                return;
            }

            _processHost.UnpublishCompatibilityFacade();
            await _processHost.DisposeAsync().ConfigureAwait(false);
        }
    }
}
