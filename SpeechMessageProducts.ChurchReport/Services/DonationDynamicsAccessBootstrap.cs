// ============================================================================
// 檔案：ChurchReport/Services/DonationDynamicsAccessBootstrap.cs
// 目的：依 appsettings 的 DynamicsAccess（必要時對齊 CrmConnection）建立 Package 1 fee-read 路徑。
//
// 保母教學：
// 1. Package01FeeReadsEnabled=false：完全走舊 ToolUtility，行為不變。這是安全預設。
// 2. =true 且 ExecutionMode=Gateway：產品只打共用 Gateway Web Service。
// 3. =true 且 ExecutionMode=Embedded：在本產品程序內嵌同一套受控操作（方便 VS 本機除錯）。
// 4. DynamicsAccess 可以只寫開關；ProfileAlias / Web API root 可從 CrmConnection 推導。
// 5. 絕對不把 CrmConnection:Password 複製進 DynamicsAccess。密碼只允許秘密參考名稱。
// 6. Embedded bootstrap 會以 process-level 快取 ServiceProvider，避免每次建立造成 memory/socket leak。
// 7. 本檔為 UTF-8（無 BOM）+ 繁體中文保姆級註解。
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Net.Http;
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
    /// DynamicsAccess 啟動組裝器：Package 1 fee-read 第一消費者入口。
    /// </summary>
    public static class DonationDynamicsAccessBootstrap
    {
        // 保母提醒：
        // Embedded bootstrap 若每次 new ServiceProvider，會造成 handler/socket/timer 無法回收。
        // 因此以 ProfileAlias + WebApiRoot 當 key，做成 process-level 單例快取。
        // 這不是 per-user session pool；同一個部署設定只會有一份 host runtime。
        private static readonly ConcurrentDictionary<string, IServiceProvider> EmbeddedProviders =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 建立奉獻收費單表單服務；若啟用 Package 1，會依 JSON 切換 Gateway / Embedded。
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

            // 已由 DI 注入完整依賴時（測試/正式 DI 路徑），直接使用。
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
                DynamicsExecutionMode.Embedded => CreateEmbeddedExecutor(productOptions),
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
        /// 是否啟用 Package 1 fee-read 新路徑。
        /// </summary>
        /// <summary>
        /// 嘗試建立 Package 1 client（Gateway / Embedded）。
        /// 若未啟用或設定不足，回傳 null，呼叫端應走舊路徑。
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
                DynamicsExecutionMode.Embedded => CreateEmbeddedExecutor(productOptions),
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

            // 明確字串覆寫，避免 section bind 失敗時靜默空值。
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
                "9.1") ?? "9.1";
            options.Embedded.SecretReference = FirstNonEmpty(
                options.Embedded.SecretReference,
                configuration["DynamicsAccess:Embedded:SecretReference"]) ?? string.Empty;
            options.Embedded.ManifestOrRegistrySource = FirstNonEmpty(
                options.Embedded.ManifestOrRegistrySource,
                configuration["DynamicsAccess:Embedded:ManifestOrRegistrySource"],
                "local-dev-manifest") ?? "local-dev-manifest";

            // ---- 關鍵：用 CrmConnection 對齊缺漏欄位（不複製密碼）----
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
                "9.1") ?? "9.1";
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
                // 只有在 Package 1 需要用到對齊結果時才視為錯誤；此處先記錄，由後續驗證 fail-closed。
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

            // 注意：這裡使用 process-level 共用 HttpClient。
            // 這是「產品 -> Gateway」連線池，不是 per-user CRM session pool。
            var httpClient = GatewayHttpClientFactory.GetSharedClient(
                productOptions.Gateway.Endpoint,
                TimeSpan.FromSeconds(60));

            return new GatewayDynamicsOperationExecutor(
                httpClient,
                Options.Create(productOptions),
                NullLogger<GatewayDynamicsOperationExecutor>.Instance);
        }

        private static IDynamicsOperationExecutor CreateEmbeddedExecutor(ProductDynamicsOptions productOptions)
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

            var cacheKey =
                productOptions.ProfileAlias.Trim() + "|" +
                productOptions.Embedded.OrganizationWebApiBaseUri.Trim() + "|" +
                productOptions.Embedded.CeVersion.Trim();

            var provider = EmbeddedProviders.GetOrAdd(cacheKey, _ =>
            {
                // 用迷你 DI 容器組裝 Embedded 執行器。
                // 產品仍只 reference Embedded 專案，不直接 reference WebApi。
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSpeechMessageDynamicsEmbedded(productOptions);
                return services.BuildServiceProvider(validateScopes: true);
            });

            return provider.GetRequiredService<IDynamicsOperationExecutor>();
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
