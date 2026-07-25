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
// 6. 本機 VS（local-dev-manifest + SecretReference）可用「秘密名稱 -> CrmConnection 欄位」橋接，
//    讓 Web API 使用與 legacy SOAP 相同帳密，但 DynamicsAccess 仍只存名稱不存密碼。
// 7. Embedded bootstrap 使用 process-level 快取 ServiceProvider，避免每次新建造成 memory/socket leak。
// 8. 檔案請維持 UTF-8（無 BOM），註解請寫繁體中文。
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Embedded.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Gateway;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// DynamicsAccess 啟動組裝器：Package 1 fee-read 的第一道產品入口。
    /// </summary>
    public static class DonationDynamicsAccessBootstrap
    {
        // Embedded bootstrap 不可每次 new ServiceProvider，否則 handler/socket/timer 容易累積。
        // 以 ProfileAlias + WebApiRoot + CeVersion + CredentialSource 當 key，快取 process-level provider。
        // 這不是 per-user session pool；同一個產品行程只共用一份 host runtime。
        private static readonly ConcurrentDictionary<string, IServiceProvider> EmbeddedProviders =
            new(StringComparer.OrdinalIgnoreCase);

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
            IDynamicsOperationExecutor executor = productOptions.ExecutionMode switch
            {
                DynamicsExecutionMode.Gateway => CreateGatewayExecutor(productOptions),
                DynamicsExecutionMode.Embedded => CreateEmbeddedExecutor(productOptions, configuration),
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
            IDynamicsOperationExecutor executor = productOptions.ExecutionMode switch
            {
                DynamicsExecutionMode.Gateway => CreateGatewayExecutor(productOptions),
                DynamicsExecutionMode.Embedded => CreateEmbeddedExecutor(productOptions, configuration),
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

            var allowLocalDevRaw = FirstNonEmpty(
                configuration["DynamicsAccess:Embedded:AllowLocalDevPasswordGrant"]);
            if (!string.IsNullOrWhiteSpace(allowLocalDevRaw) &&
                (string.Equals(allowLocalDevRaw, "true", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(allowLocalDevRaw, "1", StringComparison.OrdinalIgnoreCase)))
            {
                options.Embedded.AllowLocalDevPasswordGrant = true;
            }
            else if (string.Equals(options.Embedded.AuthMode, "AdfsOAuth", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(options.Embedded.ManifestOrRegistrySource, "local-dev-manifest", StringComparison.OrdinalIgnoreCase) &&
                     string.IsNullOrWhiteSpace(options.Embedded.CredentialReferenceName))
            {
                // 本機 Tier A 預設：IFD 沒有預發 bearer 時，允許用 CrmConnection 橋接 password grant。
                options.Embedded.AllowLocalDevPasswordGrant = true;
            }

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

        private static IDynamicsOperationExecutor CreateGatewayExecutor(ProductDynamicsOptions productOptions)
        {
            if (productOptions.Gateway is null ||
                string.IsNullOrWhiteSpace(productOptions.Gateway.Endpoint) ||
                string.IsNullOrWhiteSpace(productOptions.ProfileAlias))
            {
                throw new InvalidOperationException(
                    "DynamicsAccess Gateway mode requires ProfileAlias and Gateway:Endpoint when Package01FeeReadsEnabled=true.");
            }

            // 重要：必須使用 process-level 共用 HttpClient。
            // 這是產品 -> Gateway 的連線池，不是 per-user CRM session pool。
            var httpClient = GatewayHttpClientFactory.GetSharedClient(
                productOptions.Gateway.Endpoint,
                TimeSpan.FromSeconds(60));

            return new GatewayDynamicsOperationExecutor(
                httpClient,
                Options.Create(productOptions),
                NullLogger<GatewayDynamicsOperationExecutor>.Instance);
        }

        private static IDynamicsOperationExecutor CreateEmbeddedExecutor(
            ProductDynamicsOptions productOptions,
            IConfiguration configuration)
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
            if (string.Equals(credentialSource, "SecretReference", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(productOptions.Embedded.UserNameSecretName) ||
                 string.IsNullOrWhiteSpace(productOptions.Embedded.PasswordSecretName)))
            {
                throw new InvalidOperationException(
                    "DynamicsAccess Embedded SecretReference requires UserNameSecretName and PasswordSecretName.");
            }

            // cache key 必須包含 auth 維度，避免 Windows / AdfsOAuth 設定互相污染。
            var cacheKey =
                productOptions.ProfileAlias.Trim() + "|" +
                productOptions.Embedded.OrganizationWebApiBaseUri.Trim() + "|" +
                productOptions.Embedded.CeVersion.Trim() + "|" +
                credentialSource.Trim() + "|" +
                (productOptions.Embedded.AuthMode ?? "Windows").Trim() + "|" +
                (productOptions.Embedded.AuthorityUri ?? string.Empty).Trim() + "|" +
                (productOptions.Embedded.ClientId ?? string.Empty).Trim() + "|" +
                (productOptions.Embedded.UserNameSecretName ?? string.Empty).Trim();

            var provider = EmbeddedProviders.GetOrAdd(cacheKey, _ =>
            {
                // 這層建立 DI 容器與 Embedded 執行器。
                // 產品只可 reference Embedded 專案，不可直接 reference WebApi。
                var services = new ServiceCollection();
                services.AddLogging();

                // 本機 local-dev：把秘密名稱橋接到 CrmConnection 值（不寫進 DynamicsAccess JSON）。
                var localSecrets = BuildLocalDevSecretMap(configuration, productOptions);
                services.AddSpeechMessageDynamicsEmbedded(productOptions, localSecrets);
                return services.BuildServiceProvider(validateScopes: true);
            });

            return provider.GetRequiredService<IDynamicsOperationExecutor>();
        }

        /// <summary>
        /// 本機 VS / local-dev-manifest 專用：把秘密名稱對應到既有 CrmConnection 值。
        /// 注意：這不會把密碼寫進 DynamicsAccess JSON；只在行程內記憶體解析。
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

            System.Diagnostics.Trace.WriteLine(
                $"[DynamicsAccess] local-dev secret bridge ready for secrets: {string.Join(",", map.Keys)}");

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
    }
}