// ============================================================================
// 檔案：ChurchReport/Services/DonationDynamicsAccessBootstrap.cs
// 目的：依 appsettings 的 DynamicsAccess（必要時對齊 CrmConnection）建立 Package 1 fee-read 路徑。
//
// 保姆級教學：
// 1. Package01FeeReadsEnabled=false：完全走舊 ToolUtility，行為不變。這是安全預設。
// 2. =true 且 ExecutionMode=Gateway：產品只打共用 Gateway Web Service。
// 3. =true 且 ExecutionMode=Embedded：在本產品行程內嵌同一套受控操作（適合 VS 本機除錯）。
// 4. DynamicsAccess 可以只寫開關；ProfileAlias / Web API root 可從 CrmConnection 推導。
// 5. 絕對不把 CrmConnection:Password 複製進 DynamicsAccess JSON。密碼只允許秘密參考名稱。
// 6. 本機 VS 只有 Windows Auth 可用「秘密名稱 -> CrmConnection 欄位」的行程內橋接；ADFS OAuth 永久禁止
//    人類帳密／password grant，只接受受控 bearer 或 refresh-token secret reference。
// 7. Gateway／Embedded bootstrap 由 ChurchReport 主 DI singleton 擁有 process-level ServiceProvider，
//    避免每次新建造成 memory/socket leak；host shutdown 後此 owner 為 terminal，不可重建 transport generation。
// 8. 檔案請維持 UTF-8（無 BOM），註解請寫繁體中文。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
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
using SpeechMessage.Dynamics.Embedded.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Gateway;
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
        /// 建立奉獻收費表單服務；若啟用 Package 1，會依 JSON 走 Gateway / Embedded。
        /// </summary>
        public static DonationDedicationFeeFormService CreateFeeFormService(
            ToolUtilityClass utility,
            IConfiguration configuration,
            IPackage01FeeReadClient? injectedFeeReadClient = null,
            IOptions<ProductDynamicsOptions>? injectedOptions = null)
        {
            ArgumentNullException.ThrowIfNull(utility);
            ArgumentNullException.ThrowIfNull(configuration);

            var enabled = IsPackage01Enabled(configuration);
            if (!enabled)
            {
                return new DonationDedicationFeeFormService(utility);
            }

            // 已由 DI 注入完成時（測試/正式 DI 路徑），直接使用。
            if (injectedFeeReadClient is not null)
            {
                var options = injectedOptions ?? Options.Create(BindOptions(configuration));
                return new DonationDedicationFeeFormService(
                    utility,
                    injectedFeeReadClient,
                    options,
                    package01FeeReadsEnabled: true);
            }

            var productOptions = BindOptions(configuration);
            var processHost = GetStartedProcessHost();
            IDynamicsOperationExecutor executor = productOptions.ExecutionMode switch
            {
                DynamicsExecutionMode.Gateway => CreateGatewayExecutor(productOptions, processHost),
                DynamicsExecutionMode.Embedded => CreateEmbeddedExecutor(productOptions, configuration, processHost),
                _ => throw new InvalidOperationException(
                    $"Unsupported DynamicsAccess:ExecutionMode '{productOptions.ExecutionMode}'.")
            };

            IPackage01FeeReadClient feeReadClient = new Package01FeeReadClient(
                executor,
                NullLogger<Package01FeeReadClient>.Instance);

            return new DonationDedicationFeeFormService(
                utility,
                feeReadClient,
                Options.Create(productOptions),
                package01FeeReadsEnabled: true);
        }

        /// <summary>
        /// 嘗試建立 Package 1 client（Gateway / Embedded）。
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
            var processHost = GetStartedProcessHost();
            IDynamicsOperationExecutor executor = productOptions.ExecutionMode switch
            {
                DynamicsExecutionMode.Gateway => CreateGatewayExecutor(productOptions, processHost),
                DynamicsExecutionMode.Embedded => CreateEmbeddedExecutor(productOptions, configuration, processHost),
                _ => throw new InvalidOperationException(
                    $"Unsupported DynamicsAccess:ExecutionMode '{productOptions.ExecutionMode}'.")
            };

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
        /// 綁定 DynamicsAccess，並用 CrmConnection 補齊缺漏的 ProfileAlias / Embedded Web API root。
        /// </summary>
        public static ProductDynamicsOptions BindOptions(IConfiguration configuration)
        {
            var options = new ProductDynamicsOptions();
            configuration.GetSection(ProductDynamicsOptions.SectionName).Bind(options);

            // 字串欄位再保險，避免 section bind 失敗時整段空白。
            options.ProfileAlias = FirstNonEmpty(
                options.ProfileAlias,
                configuration["DynamicsAccess:ProfileAlias"]) ?? string.Empty;

            var modeText = configuration["DynamicsAccess:ExecutionMode"];
            if (!string.IsNullOrWhiteSpace(modeText) &&
                Enum.TryParse<DynamicsExecutionMode>(modeText, ignoreCase: true, out var mode))
            {
                options.ExecutionMode = mode;
            }

            options.Gateway ??= new GatewayModeOptions();
            options.Gateway.Endpoint = FirstNonEmpty(
                options.Gateway.Endpoint,
                configuration["DynamicsAccess:Gateway:Endpoint"]) ?? string.Empty;
            options.Gateway.ApiPrefix = FirstNonEmpty(
                options.Gateway.ApiPrefix,
                configuration["DynamicsAccess:Gateway:ApiPrefix"],
                "/v1") ?? "/v1";

            options.Embedded ??= new EmbeddedModeOptions();
            options.Embedded.OrganizationWebApiBaseUri = FirstNonEmpty(
                options.Embedded.OrganizationWebApiBaseUri,
                configuration["DynamicsAccess:Embedded:OrganizationWebApiBaseUri"]) ?? string.Empty;
            options.Embedded.CeVersion = FirstNonEmpty(
                options.Embedded.CeVersion,
                configuration["DynamicsAccess:Embedded:CeVersion"],
                configuration["DynamicsAccess:CeVersion"],
                "8.2") ?? "8.2";
            options.Embedded.SecretReference = FirstNonEmpty(
                options.Embedded.SecretReference,
                configuration["DynamicsAccess:Embedded:SecretReference"]) ?? string.Empty;
            options.Embedded.CredentialSource = FirstNonEmpty(
                options.Embedded.CredentialSource,
                configuration["DynamicsAccess:Embedded:CredentialSource"],
                "HostIdentity") ?? "HostIdentity";
            options.Embedded.UserNameSecretName = FirstNonEmpty(
                options.Embedded.UserNameSecretName,
                configuration["DynamicsAccess:Embedded:UserNameSecretName"]);
            options.Embedded.PasswordSecretName = FirstNonEmpty(
                options.Embedded.PasswordSecretName,
                configuration["DynamicsAccess:Embedded:PasswordSecretName"]);
            options.Embedded.DomainSecretName = FirstNonEmpty(
                options.Embedded.DomainSecretName,
                configuration["DynamicsAccess:Embedded:DomainSecretName"]);
            options.Embedded.ManifestOrRegistrySource = FirstNonEmpty(
                options.Embedded.ManifestOrRegistrySource,
                configuration["DynamicsAccess:Embedded:ManifestOrRegistrySource"],
                "local-dev-manifest") ?? "local-dev-manifest";

            // IFD / ADFS OAuth 設定（jesus 需要 AdfsOAuth，不能只用 Windows NTLM）
            options.Embedded.AuthMode = FirstNonEmpty(
                options.Embedded.AuthMode,
                configuration["DynamicsAccess:Embedded:AuthMode"],
                "Windows") ?? "Windows";
            options.Embedded.AuthorityUri = FirstNonEmpty(
                options.Embedded.AuthorityUri,
                configuration["DynamicsAccess:Embedded:AuthorityUri"]);
            options.Embedded.ResourceUri = FirstNonEmpty(
                options.Embedded.ResourceUri,
                configuration["DynamicsAccess:Embedded:ResourceUri"]);
            options.Embedded.ClientId = FirstNonEmpty(
                options.Embedded.ClientId,
                configuration["DynamicsAccess:Embedded:ClientId"]);
            options.Embedded.ClientIdSecretName = FirstNonEmpty(
                options.Embedded.ClientIdSecretName,
                configuration["DynamicsAccess:Embedded:ClientIdSecretName"]);
            options.Embedded.ClientSecretName = FirstNonEmpty(
                options.Embedded.ClientSecretName,
                configuration["DynamicsAccess:Embedded:ClientSecretName"]);
            options.Embedded.CredentialReferenceName = FirstNonEmpty(
                options.Embedded.CredentialReferenceName,
                configuration["DynamicsAccess:Embedded:CredentialReferenceName"]);

            options.Embedded.RefreshTokenSecretName = FirstNonEmpty(
                options.Embedded.RefreshTokenSecretName,
                configuration["DynamicsAccess:Embedded:RefreshTokenSecretName"]);
            options.Embedded.RedirectUri = FirstNonEmpty(
                options.Embedded.RedirectUri,
                configuration["DynamicsAccess:Embedded:RedirectUri"]);

            // AllowLocalDevPasswordGrant 只保留為舊設定的 fail-closed migration trap；不在 bootstrap 自動打開，
            // 也不因 local-dev-manifest 或缺少 bearer 而回退。值若為 true，Embedded DI validation 會在建立
            // provider／handler／socket 前拒絕啟動。refresh token 只能由上述秘密參考解析，永不建立檔案路徑。
            // 關鍵：用 CrmConnection 對齊缺漏欄位（不複製密碼進 DynamicsAccess JSON）
            AlignFromCrmConnection(configuration, options);

            return options;
        }

        /// <summary>
        /// 若 DynamicsAccess 缺 ProfileAlias / Embedded Web API，就從 CrmConnection 推導。
        /// </summary>
        public static void AlignFromCrmConnection(IConfiguration configuration, ProductDynamicsOptions options)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(options);

            var organization = configuration["CrmConnection:Organization"];
            var serverUrl = configuration["CrmConnection:ServerUrl"];
            var ceVersion = FirstNonEmpty(
                options.Embedded?.CeVersion,
                configuration["DynamicsAccess:Embedded:CeVersion"],
                configuration["DynamicsAccess:CeVersion"],
                "8.2") ?? "8.2";
            var environmentSuffix = FirstNonEmpty(
                configuration["DynamicsAccess:EnvironmentSuffix"],
                "prod") ?? "prod";
            var secretReference = FirstNonEmpty(
                options.Embedded?.SecretReference,
                configuration["DynamicsAccess:Embedded:SecretReference"],
                configuration["DynamicsAccess:SecretReference"]);

            // 沒有 CrmConnection 也能運作，只要 DynamicsAccess 自己寫齊。
            if (string.IsNullOrWhiteSpace(organization) && string.IsNullOrWhiteSpace(serverUrl))
            {
                return;
            }

            if (!DynamicsProfileAlignment.TryAlignFromLegacyCrmConnection(
                    organization,
                    serverUrl,
                    ceVersion,
                    environmentSuffix,
                    secretReference,
                    out var aligned,
                    out var error))
            {
                // 不是每個 Package 1 都需要對齊成功；僅記錄，不在這裡硬 fail-closed。
                System.Diagnostics.Trace.WriteLine(
                    $"[DynamicsAccess] CrmConnection alignment skipped/failed: {error}");
                return;
            }

            if (string.IsNullOrWhiteSpace(options.ProfileAlias))
            {
                options.ProfileAlias = aligned.ProfileAlias;
            }

            options.Embedded ??= new EmbeddedModeOptions();
            if (string.IsNullOrWhiteSpace(options.Embedded.OrganizationWebApiBaseUri))
            {
                options.Embedded.OrganizationWebApiBaseUri = aligned.OrganizationWebApiBaseUri;
            }

            if (string.IsNullOrWhiteSpace(options.Embedded.CeVersion))
            {
                options.Embedded.CeVersion = aligned.CeVersion;
            }

            if (string.IsNullOrWhiteSpace(options.Embedded.SecretReference))
            {
                options.Embedded.SecretReference = aligned.SecretReference;
            }

            if (string.IsNullOrWhiteSpace(options.Embedded.ManifestOrRegistrySource))
            {
                options.Embedded.ManifestOrRegistrySource = "local-dev-manifest";
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

        private static IDynamicsOperationExecutor CreateEmbeddedExecutor(
            ProductDynamicsOptions productOptions,
            IConfiguration configuration,
            IDonationDynamicsAccessProcessHost processHost)
        {
            if (string.IsNullOrWhiteSpace(productOptions.ProfileAlias))
            {
                throw new InvalidOperationException(
                    "DynamicsAccess Embedded mode requires ProfileAlias when Package01FeeReadsEnabled=true.");
            }

            if (productOptions.Embedded is null ||
                string.IsNullOrWhiteSpace(productOptions.Embedded.OrganizationWebApiBaseUri) ||
                string.IsNullOrWhiteSpace(productOptions.Embedded.CeVersion) ||
                string.IsNullOrWhiteSpace(productOptions.Embedded.SecretReference) ||
                string.IsNullOrWhiteSpace(productOptions.Embedded.ManifestOrRegistrySource))
            {
                throw new InvalidOperationException(
                    "DynamicsAccess Embedded mode requires Embedded:OrganizationWebApiBaseUri, CeVersion, SecretReference, ManifestOrRegistrySource.");
            }

            var credentialSource = productOptions.Embedded.CredentialSource ?? "HostIdentity";
            var isWindowsAuth = !string.Equals(
                productOptions.Embedded.AuthMode,
                "AdfsOAuth",
                StringComparison.OrdinalIgnoreCase);
            if (isWindowsAuth &&
                string.Equals(credentialSource, "SecretReference", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(productOptions.Embedded.UserNameSecretName) ||
                 string.IsNullOrWhiteSpace(productOptions.Embedded.PasswordSecretName)))
            {
                throw new InvalidOperationException(
                    "DynamicsAccess Embedded SecretReference requires UserNameSecretName and PasswordSecretName.");
            }

            // cache key 必須包含 auth 維度，避免 Windows / AdfsOAuth 設定互相污染。
            var localSecrets = BuildLocalDevSecretMap(configuration, productOptions);

            // 舊版無界 ServiceProvider 快取已移除；此類別只保留單一 process generation，設定改變必須重啟並先 Dispose 舊世代，
            // 防止每個要求建立新的 handler、socket pool、timer 或 token cache 而造成資源與跨設定檔狀態洩漏。
            // 這層建立 DI 容器與 Embedded 執行器。
            // 產品只可 reference Embedded 專案，不可直接 reference WebApi。

            // 本機 local-dev：把秘密名稱橋接到 CrmConnection 值（不寫進 DynamicsAccess JSON）。

            return processHost.GetOrCreateEmbeddedExecutor(productOptions, localSecrets);
        }

        /// <summary>
        /// 本機 VS / local-dev-manifest 的 Windows Auth 專用：把秘密名稱對應到既有 CrmConnection 值。
        /// ADFS OAuth 一律回傳空 map，避免人類帳密進入 OAuth provider generation；Windows 路徑也只在目前
        /// process generation 記憶體保存，並由 process host Dispose 時整體釋放，不寫入 JSON、Session 或 static cache。
        /// </summary>
        private static IReadOnlyDictionary<string, string> BuildLocalDevSecretMap(
            IConfiguration configuration,
            ProductDynamicsOptions productOptions)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 只有 local-dev-manifest 才允許橋接 CrmConnection，避免正式環境誤用。
            var manifest = productOptions.Embedded?.ManifestOrRegistrySource;
            if (!string.Equals(manifest, "local-dev-manifest", StringComparison.OrdinalIgnoreCase))
            {
                return map;
            }

            if (string.Equals(
                    productOptions.Embedded?.AuthMode,
                    "AdfsOAuth",
                    StringComparison.OrdinalIgnoreCase))
            {
                return map;
            }

            var userName = configuration["CrmConnection:Username"];
            var password = configuration["CrmConnection:Password"];
            var domain = configuration["CrmConnection:Domain"];

            void Put(string? secretName, string? value)
            {
                if (string.IsNullOrWhiteSpace(secretName) || string.IsNullOrEmpty(value))
                {
                    return;
                }

                map[secretName.Trim()] = value;
            }

            Put(productOptions.Embedded?.UserNameSecretName, userName);
            Put(productOptions.Embedded?.PasswordSecretName, password);

            // Domain：若 Username 是 DOMAIN\user 且 Domain 空白，拆出 DOMAIN。
            if (string.IsNullOrWhiteSpace(domain) &&
                !string.IsNullOrWhiteSpace(userName) &&
                userName.Contains('\\'))
            {
                domain = userName.Split('\\', 2)[0];
            }

            Put(productOptions.Embedded?.DomainSecretName, domain);

            return map;
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
        /// 取得或建立目前唯一的 Embedded executor generation；local secret 值只在此 process 記憶體內
        /// 交給 Embedded provider，不能寫入 log、例外、static cache 或 product JSON。設定世代改變時同樣
        /// 必須先 Dispose，避免 CE 版本、credential 或 token state 交叉污染。
        /// </summary>
        /// <param name="options">已完成 legacy 對齊的 Embedded 產品設定。</param>
        /// <param name="localSecrets">local-dev manifest 使用的 process-memory secret bridge。</param>
        /// <returns>由本 process host 擁有、不得由呼叫者 Dispose 的正式 operation executor。</returns>
        IDynamicsOperationExecutor GetOrCreateEmbeddedExecutor(
            ProductDynamicsOptions options,
            IReadOnlyDictionary<string, string> localSecrets);
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
                    configured.ExecutionMode = DynamicsExecutionMode.Gateway;
                    configured.ProfileAlias = options.ProfileAlias;
                    configured.Gateway = options.Gateway is null
                        ? null
                        : new GatewayModeOptions
                        {
                            Endpoint = options.Gateway.Endpoint,
                            ApiPrefix = options.Gateway.ApiPrefix,
                            MaxResponseBytes = options.Gateway.MaxResponseBytes
                        };
                });
            });
        }

        /// <summary>
        /// 取得或建立 Embedded executor。generation digest 涵蓋所有 routing／authentication 維度與 local
        /// secret 值，但 digest 計算用的暫存 UTF-8 bytes 會立即清零；原始值不寫入例外或 log。
        /// 相同 process 不允許在未 Dispose 前切換 CE 版本或 credential，避免跨設定檔狀態洩漏。
        /// </summary>
        public IDynamicsOperationExecutor GetOrCreateEmbeddedExecutor(
            ProductDynamicsOptions options,
            IReadOnlyDictionary<string, string> localSecrets)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(localSecrets);
            var embedded = options.Embedded
                           ?? throw new InvalidOperationException(
                               "DynamicsAccess Embedded options are required.");
            var keyParts = new List<string?>
            {
                "embedded",
                options.ProfileAlias,
                embedded.OrganizationWebApiBaseUri,
                embedded.CeVersion,
                embedded.SecretReference,
                embedded.ManifestOrRegistrySource,
                embedded.CredentialSource,
                embedded.UserNameSecretName,
                embedded.PasswordSecretName,
                embedded.DomainSecretName,
                embedded.AuthMode,
                embedded.AuthorityUri,
                embedded.ResourceUri,
                embedded.ClientId,
                embedded.ClientIdSecretName,
                embedded.ClientSecretName,
                embedded.CredentialReferenceName,
                embedded.AllowLocalDevPasswordGrant.ToString(),
                embedded.RefreshTokenSecretName,
                embedded.RedirectUri
            };

            foreach (var secret in localSecrets.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                keyParts.Add(secret.Key);
                keyParts.Add(secret.Value);
            }

            var key = ComputeGenerationKey(keyParts.ToArray());
            return GetOrCreate(key, services =>
                services.AddSpeechMessageDynamicsEmbedded(options, localSecrets));
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
